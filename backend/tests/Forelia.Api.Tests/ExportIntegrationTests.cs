using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Forelia.Api.Data;
using Forelia.Api.Reconciliations;
using Microsoft.Extensions.DependencyInjection;

namespace Forelia.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class ExportIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture fixture = new();
    private const string Header = "id_operacao;valor_operacional;valor_contabil;data_operacional;data_contabil;resultado;divergencias;situacao_analise";
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task BaseReportPreservesBothDivergencesMissingSourcesAndExactValues()
    {
        var execution = await CreateAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/conciliacoes/{execution.Id}/exportacao");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal($"conciliacao-{execution.Id:D}.csv", response.Content.Headers.ContentDisposition.FileNameStar);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes.AsSpan(3));
        Assert.StartsWith(Header + "\r\n", text);
        var rows = Parse(bytes);
        Assert.Equal(9, rows.Count);
        Assert.Equal(Enumerable.Range(1, 9).Select(x => $"OP{x:000}"), rows.Select(x => x[0]));
        Assert.Equal(new[] { "OP004", "430.75", "400.00", "2026-10-04", "2026-10-07", "DIVERGENTE", "VALOR|DATA", "PENDENTE" }, rows[3]);
        Assert.Equal(new[] { "OP005", "120.00", "", "2026-10-05", "", "SEM_REGISTRO_CONTABIL", "", "PENDENTE" }, rows[4]);
        Assert.Equal("-75.50", rows[5][1]);
        Assert.Equal("0.00", rows[6][1]);
        Assert.Equal("", rows[0][7]);
        Assert.Equal(new[] { "OP009", "", "310.00", "", "2026-10-09", "SEM_OPERACAO_FINANCEIRA", "", "PENDENTE" }, rows[8]);
        Assert.Equal(execution, await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{execution.Id}"));
    }

    [Fact]
    public async Task ReportUsesAllFiltersAndExportsAllMatchesRegardlessOfPageParameters()
    {
        var execution = await CreateAsync();
        Assert.Equal(new[] { "OP002", "OP003", "OP004" },
            (await DownloadAsync(execution.Id, "resultado=DIVERGENTE&pagina=2&tamanhoPagina=1")).Select(x => x[0]));
        Assert.Equal(new[] { "OP004" }, (await DownloadAsync(execution.Id,
            "idOperacao=OP004&resultado=DIVERGENTE&situacaoAnalise=PENDENTE&dataOperacao=2026-10-04&dataInicial=2026-10-03&dataFinal=2026-10-05")).Select(x => x[0]));
        Assert.Equal(new[] { "OP003", "OP004", "OP005" }, (await DownloadAsync(execution.Id,
            "dataInicial=2026-10-03&dataFinal=2026-10-05")).Select(x => x[0]));
        Assert.Equal(new[] { "OP009" }, (await DownloadAsync(execution.Id, "dataOperacao=2026-10-09")).Select(x => x[0]));
        Assert.Empty(await DownloadAsync(execution.Id, "idOperacao=op004"));
    }

    [Fact]
    public async Task NoMatchesProducesOnlyHeaderAndExportsCurrentAnalysisWithoutObservations()
    {
        var execution = await CreateAsync();
        using var client = fixture.Factory.CreateClient();
        var page = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/conciliacoes/{execution.Id}/itens?idOperacao=OP004");
        var itemId = page.GetProperty("itens")[0].GetProperty("id").GetGuid();
        using var patch = await client.PatchAsJsonAsync($"/api/conciliacoes/{execution.Id}/itens/{itemId}/analise", new { situacao = "ANALISADA" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using var note = await client.PostAsJsonAsync($"/api/conciliacoes/{execution.Id}/itens/{itemId}/observacoes", new { texto = "Texto fora do relatório", analista = "teste" });
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        var rows = await DownloadAsync(execution.Id, "situacaoAnalise=ANALISADA");
        Assert.Equal("ANALISADA", Assert.Single(rows)[7]);
        Assert.Equal(8, rows[0].Length);
        using var empty = await client.GetAsync($"/api/conciliacoes/{execution.Id}/exportacao?idOperacao=INEXISTENTE");
        var bytes = await empty.Content.ReadAsByteArrayAsync();
        Assert.Equal(Header + "\r\n", Encoding.UTF8.GetString(bytes.AsSpan(3)));
    }

    [Fact]
    public async Task CsvEscapesSemicolonsQuotesUnicodeAndEmbeddedNewlinesWithoutChangingIdentifiers()
    {
        const string identifier = "0001; \"ação\"\nlinha";
        var content = "id_operacao;data_operacao;valor\r\n\"0001; \"\"ação\"\"\nlinha\";2026-10-01;10.25\r\n";
        var execution = await ReconcileAsync(await ImportAsync("operacional", Encoding.UTF8.GetBytes(content)),
            await ImportAsync("contabil", Encoding.UTF8.GetBytes(content)));
        var row = Assert.Single(await DownloadAsync(execution.Id));
        Assert.Equal(identifier, row[0]);
        Assert.Equal("10.25", row[1]);
        Assert.Equal("CONCILIADO", row[5]);
    }

    [Fact]
    public async Task HistoricalAndCorrectedReportsRemainSeparate()
    {
        var original = await CreateAsync();
        var corrected = await ReconcileAsync(original.ArquivoOperacionalId,
            await ImportAsync("contabil", CsvValidatorTests.Example("02_correcao/contabil_corrigido.csv")));
        var historical = await DownloadAsync(original.Id);
        var updated = await DownloadAsync(corrected.Id);
        Assert.Equal(9, historical.Count);
        Assert.Equal(8, updated.Count);
        Assert.Equal("VALOR|DATA", historical[3][6]);
        Assert.All(updated, row => Assert.Equal("CONCILIADO", row[5]));
    }

    [Fact]
    public async Task RejectsInvalidFiltersUnknownExecutionsAndEveryIncompleteStatus()
    {
        var execution = await CreateAsync();
        using var client = fixture.Factory.CreateClient();
        foreach (var query in new[] { "resultado=ERRADO", "dataInicial=2026-10-05&dataFinal=2026-10-03", "dataOperacao=2026-02-29" })
        {
            using var invalid = await client.GetAsync($"/api/conciliacoes/{execution.Id}/exportacao?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Null(invalid.Content.Headers.ContentDisposition);
            var body = await invalid.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal("FILTRO_INVALIDO", body.GetProperty("codigo").GetString());
        }
        using var missing = await client.GetAsync($"/api/conciliacoes/{Guid.NewGuid()}/exportacao");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var status in new[] { "PENDENTE", "PROCESSANDO", "FALHOU" })
        {
            var attempt = new Reconciliation { OperationalFileId = execution.ArquivoOperacionalId,
                AccountingFileId = execution.ArquivoContabilId, Status = status };
            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
                db.Reconciliations.Add(attempt);
                await db.SaveChangesAsync();
            }
            using var unavailable = await client.GetAsync($"/api/conciliacoes/{attempt.Id}/exportacao");
            Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
            Assert.Null(unavailable.Content.Headers.ContentDisposition);
        }
    }

    private async Task<List<string[]>> DownloadAsync(Guid id, string query = "")
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/conciliacoes/{id}/exportacao?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Parse(await response.Content.ReadAsByteArrayAsync());
    }

    private static List<string[]> Parse(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" });
        Assert.True(csv.Read());
        csv.ReadHeader();
        Assert.Equal(Header.Split(';'), csv.HeaderRecord);
        var rows = new List<string[]>();
        while (csv.Read()) rows.Add(Enumerable.Range(0, 8).Select(i => csv.GetField(i)!).ToArray());
        return rows;
    }

    private async Task<ReconciliationResponse> CreateAsync() => await ReconcileAsync(
        await ImportAsync("operacional", CsvValidatorTests.Example("01_base/operacional.csv")),
        await ImportAsync("contabil", CsvValidatorTests.Example("01_base/contabil.csv")));

    private async Task<ReconciliationResponse> ReconcileAsync(Guid operational, Guid accounting)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/conciliacoes", new ReconciliationRequest(operational, accounting));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReconciliationResponse>())!;
    }

    private async Task<Guid> ImportAsync(string source, byte[] content)
    {
        using var client = fixture.Factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "arquivo", "teste.csv");
        using var response = await client.PostAsync($"/api/importacoes/{source}", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
