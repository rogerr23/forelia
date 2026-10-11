using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Forelia.Api.Data;
using Forelia.Api.Imports;
using Forelia.Api.Reconciliations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forelia.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class ReconciliationIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static int uploadSequence;

    [Fact]
    public async Task PersistsBaseResultsAndReusesCompletedExecutionWhileCorrectionCreatesAnother()
    {
        var request = await PairAsync();
        using var client = fixture.Factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/conciliacoes", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = (await created.Content.ReadFromJsonAsync<ReconciliationResponse>())!;
        Assert.Equal("CONCLUIDA", original.Status);
        Assert.Equal(9, original.TotalItens);
        Assert.Equal(4, original.ParesConciliados);
        Assert.Equal(3, original.ParesDivergentes);
        Assert.Equal(44.44m, original.TaxaConciliacao);
        Assert.Equal($"/api/conciliacoes/{original.Id}", created.Headers.Location!.ToString());
        Assert.Equal(original, await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{original.Id}"));
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/conciliacoes/{original.Id}/itens?pagina=1&tamanhoPagina=100");
        Assert.Equal(9, page.GetProperty("total").GetInt32());
        var op4 = page.GetProperty("itens").EnumerateArray().Single(x => x.GetProperty("idOperacao").GetString() == "OP004");
        Assert.Equal(2, op4.GetProperty("divergencias").GetArrayLength());
        using var repeated = await client.PostAsJsonAsync("/api/conciliacoes", request);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(original.Id, (await repeated.Content.ReadFromJsonAsync<ReconciliationResponse>())!.Id);
        var correctedFile = await ImportAsync("contabil", "02_correcao/contabil_corrigido.csv");
        using var correction = await client.PostAsJsonAsync("/api/conciliacoes", request with { ArquivoContabilId = correctedFile });
        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        var corrected = (await correction.Content.ReadFromJsonAsync<ReconciliationResponse>())!;
        Assert.NotEqual(original.Id, corrected.Id);
        Assert.Equal(8, corrected.ParesConciliados);
        Assert.Equal(100m, corrected.TaxaConciliacao);
        Assert.Equal(original, await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{original.Id}"));
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
        Assert.Equal(2, await db.Reconciliations.CountAsync(x => x.OperationalFileId == request.ArquivoOperacionalId));
        Assert.Equal(17, await db.ReconciliationItems.CountAsync(x => x.ReconciliationId == original.Id || x.ReconciliationId == corrected.Id));
    }

    [Fact]
    public async Task ConcurrentRequestsCreateOnlyOneExecution()
    {
        var request = await PairAsync();
        using var client = fixture.Factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync("/api/conciliacoes", request)));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Equal(5, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
            var results = await Task.WhenAll(responses.Select(async x => (await x.Content.ReadFromJsonAsync<ReconciliationResponse>())!));
            Assert.Single(results.Select(x => x.Id).Distinct());
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            Assert.Equal(1, await db.Reconciliations.CountAsync(x => x.OperationalFileId == request.ArquivoOperacionalId));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task TechnicalFailurePreservesAttemptWithoutPartialItemsAndAllowsRetry()
    {
        var request = await PairAsync();
        await fixture.ExecuteAsync("""
            CREATE FUNCTION fail_test_reconciliation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."ExternalId" = 'OP004' THEN RAISE EXCEPTION 'Erro interno simulado'; END IF;
            RETURN NEW; END; $$;
            CREATE TRIGGER fail_test_reconciliation BEFORE INSERT ON item_conciliacao
            FOR EACH ROW EXECUTE FUNCTION fail_test_reconciliation();
            """);
        using var client = fixture.Factory.CreateClient();
        Guid failedId;
        try
        {
            using var failed = await client.PostAsJsonAsync("/api/conciliacoes", request);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.DoesNotContain("Erro interno simulado", await failed.Content.ReadAsStringAsync());
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            var execution = await db.Reconciliations.SingleAsync(x => x.OperationalFileId == request.ArquivoOperacionalId);
            failedId = execution.Id;
            Assert.Equal("FALHOU", execution.Status);
            Assert.Equal("FALHA_TECNICA", execution.ErrorCode);
            Assert.Equal(0, execution.SourceRecords);
            Assert.False(await db.ReconciliationItems.AnyAsync(x => x.ReconciliationId == execution.Id));
            using var items = await client.GetAsync($"/api/conciliacoes/{failedId}/itens");
            Assert.Equal(HttpStatusCode.Conflict, items.StatusCode);
        }
        finally
        {
            await fixture.ExecuteAsync("DROP TRIGGER fail_test_reconciliation ON item_conciliacao; DROP FUNCTION fail_test_reconciliation();");
        }
        using var retry = await client.PostAsJsonAsync("/api/conciliacoes", request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.NotEqual(failedId, (await retry.Content.ReadFromJsonAsync<ReconciliationResponse>())!.Id);
        var previous = await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{failedId}");
        Assert.Equal("FALHOU", previous!.Status);
    }

    [Fact]
    public async Task RejectsUnknownOrSwappedSourcesAndInvalidPagination()
    {
        var request = await PairAsync();
        using var client = fixture.Factory.CreateClient();
        using var missing = await client.PostAsJsonAsync("/api/conciliacoes", request with { ArquivoContabilId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var swapped = await client.PostAsJsonAsync("/api/conciliacoes", new ReconciliationRequest(request.ArquivoContabilId, request.ArquivoOperacionalId));
        Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        using var same = await client.PostAsJsonAsync("/api/conciliacoes", request with { ArquivoContabilId = request.ArquivoOperacionalId });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
        Assert.False(await db.Reconciliations.AnyAsync(x => x.OperationalFileId == request.ArquivoOperacionalId));
        foreach (var query in new[] { "pagina=0", "tamanhoPagina=101", "pagina=2147483647&tamanhoPagina=100" })
        {
            using var invalid = await client.GetAsync("/api/conciliacoes?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var nonexistent = await client.GetAsync($"/api/conciliacoes/{Guid.NewGuid()}/itens");
        Assert.Equal(HttpStatusCode.NotFound, nonexistent.StatusCode);
    }

    [Fact]
    public async Task RecoversInterruptedAttemptAndPaginatesHistoryAndResults()
    {
        var request = await PairAsync();
        var interrupted = new Reconciliation
        {
            OperationalFileId = request.ArquivoOperacionalId, AccountingFileId = request.ArquivoContabilId,
            Status = "PROCESSANDO"
        };
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            db.Reconciliations.Add(interrupted);
            await db.SaveChangesAsync();
        }
        using var client = fixture.Factory.CreateClient();
        using var retry = await client.PostAsJsonAsync("/api/conciliacoes", request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var execution = (await retry.Content.ReadFromJsonAsync<ReconciliationResponse>())!;
        var previous = await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{interrupted.Id}");
        Assert.Equal("FALHOU", previous!.Status);
        Assert.Equal("PROCESSAMENTO_INTERROMPIDO", previous.CodigoErro);
        var first = await client.GetFromJsonAsync<JsonElement>($"/api/conciliacoes/{execution.Id}/itens?pagina=1&tamanhoPagina=2");
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/conciliacoes/{execution.Id}/itens?pagina=2&tamanhoPagina=2");
        Assert.Equal(9, first.GetProperty("total").GetInt32());
        Assert.Equal(2, first.GetProperty("itens").GetArrayLength());
        Assert.Equal("OP001", first.GetProperty("itens")[0].GetProperty("idOperacao").GetString());
        Assert.Equal("OP003", second.GetProperty("itens")[0].GetProperty("idOperacao").GetString());
        var history = await client.GetFromJsonAsync<JsonElement>("/api/conciliacoes?tamanhoPagina=1");
        Assert.Equal(1, history.GetProperty("itens").GetArrayLength());
        Assert.True(history.GetProperty("total").GetInt32() >= 2);
    }

    private async Task<ReconciliationRequest> PairAsync() => new(
        await ImportAsync("operacional", "01_base/operacional.csv"),
        await ImportAsync("contabil", "01_base/contabil.csv"));

    private async Task<Guid> ImportAsync(string source, string path)
    {
        // Conteúdo distinto por teste, com os mesmos registros fictícios do exemplo.
        var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(CsvValidatorTests.Example(path))
            + "\n" + new string(' ', Interlocked.Increment(ref uploadSequence)) + "\n");
        using var client = fixture.Factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "arquivo", "teste.csv");
        using var response = await client.PostAsync($"/api/importacoes/{source}", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportResponse>())!.Id;
    }
}
