using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Forelia.Api.Data;
using Forelia.Api.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forelia.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class ImportIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task PersistsOriginalAndNormalizedRecordsAndReturnsMetadata()
    {
        var bytes = CsvValidatorTests.Example("04_normalizacao/operacional_com_espacos.csv");
        using var response = await UploadAsync("operacional", bytes, "espacos.csv");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportResponse>())!;
        Assert.Equal(4, result.TotalRegistros);
        Assert.Equal("OPERACIONAL", result.Fonte);
        Assert.Equal($"/api/importacoes/{result.Id}", response.Headers.Location!.ToString());
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
        var file = await db.ImportedFiles.Include(x => x.Records).SingleAsync(x => x.Id == result.Id);
        Assert.Equal(bytes, file.OriginalContent);
        Assert.Contains(file.Records, row => row.ExternalId == "OP000010" && row.Amount == 42.50m);
        using var client = fixture.Factory.CreateClient();
        var metadata = await client.GetFromJsonAsync<ImportResponse>($"/api/importacoes/{result.Id}");
        Assert.Equal(result, metadata);
    }

    [Theory]
    [InlineData("05_duplicidades/operacional_duplicados.csv", 2)]
    [InlineData("06_invalidos/contabil_estrutura_invalida.csv", 2)]
    [InlineData("06_invalidos/operacional_campos_invalidos.csv", 10)]
    public async Task RejectedExamplesPersistNothing(string path, int errorCount)
    {
        var before = await CountsAsync();
        using var response = await UploadAsync("operacional", CsvValidatorTests.Example(path));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(errorCount, body.GetProperty("erros").GetArrayLength());
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task BlocksSameBytesUnderAnotherNameButAllowsAnotherSourceAndCorrection()
    {
        var original = CsvValidatorTests.Example("01_base/contabil.csv");
        var copy = CsvValidatorTests.Example("03_reimportacao/contabil_mesmo_conteudo_outro_nome.csv");
        Assert.Equal(original, copy);
        using var first = await UploadAsync("contabil", original, "contabil.csv");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var id = (await first.Content.ReadFromJsonAsync<ImportResponse>())!.Id;
        var before = await CountsAsync();
        using var repeated = await UploadAsync("contabil", copy, "outro_nome.csv");
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        var body = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("importacaoExistenteId").GetGuid());
        Assert.Equal(before, await CountsAsync());
        using var anotherSource = await UploadAsync("operacional", copy);
        Assert.Equal(HttpStatusCode.Created, anotherSource.StatusCode);
        using var corrected = await UploadAsync("contabil", CsvValidatorTests.Example("02_correcao/contabil_corrigido.csv"));
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        Assert.NotEqual(id, (await corrected.Content.ReadFromJsonAsync<ImportResponse>())!.Id);
    }

    [Fact]
    public async Task ConcurrentImportsCreateOnlyOneFile()
    {
        var bytes = Csv($"CONCURRENT_{Guid.NewGuid():N};2026-10-01;42.50");
        var before = await CountsAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => UploadAsync("operacional", bytes)));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            var after = await CountsAsync();
            Assert.Equal((before.Files + 1, before.Records + 1), after);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task TechnicalFailureRollsBackTheEntireImportAndAllowsRetry()
    {
        var key = "FALHA_" + Guid.NewGuid().ToString("N");
        await fixture.ExecuteAsync($"""
            CREATE FUNCTION fail_test_import() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.identificador_externo = '{key}' THEN RAISE EXCEPTION 'Falha técnica simulada'; END IF;
            RETURN NEW; END; $$;
            CREATE TRIGGER fail_test_import BEFORE INSERT ON registro_financeiro
            FOR EACH ROW EXECUTE FUNCTION fail_test_import();
            """);
        var before = await CountsAsync();
        var bytes = Csv($"OK_{key};2026-10-01;1\n{key};2026-10-01;2");
        try
        {
            using var failed = await UploadAsync("operacional", bytes);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.DoesNotContain("Falha técnica simulada", await failed.Content.ReadAsStringAsync());
            Assert.Equal(before, await CountsAsync());
        }
        finally
        {
            await fixture.ExecuteAsync("DROP TRIGGER fail_test_import ON registro_financeiro; DROP FUNCTION fail_test_import();");
        }
        using var retry = await UploadAsync("operacional", bytes);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task EnforcesSizeLimitAndUploadContract()
    {
        var before = await CountsAsync();
        using var oversized = await UploadAsync("operacional", new byte[ImportContract.MaxFileBytes + 1]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var invalidSource = await UploadAsync("desconhecida", Csv("OP1;2026-10-01;1"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidSource.StatusCode);
        using var client = fixture.Factory.CreateClient();
        using var missing = await client.PostAsync("/api/importacoes/operacional", new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var wrongType = await client.PostAsync("/api/importacoes/operacional", new StringContent("not multipart"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task AcceptsFileExactlyAtLimitAndRejectsLargerRequest()
    {
        var bytes = new byte[ImportContract.MaxFileBytes];
        Array.Fill(bytes, (byte)' ');
        var csv = Csv($"LIMITE_{Guid.NewGuid():N};2026-10-01;42.50\n");
        csv.CopyTo(bytes, 0);
        using var allowed = await UploadAsync("operacional", bytes);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        using var tooLarge = await UploadAsync("operacional", new byte[ImportContract.MaxRequestBytes + 1]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
    }

    private async Task<HttpResponseMessage> UploadAsync(string source, byte[] content, string name = "teste.csv")
    {
        using var client = fixture.Factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "arquivo", name);
        return await client.PostAsync($"/api/importacoes/{source}", form);
    }

    private async Task<(int Files, int Records)> CountsAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
        return (await db.ImportedFiles.CountAsync(), await db.FinancialRecords.CountAsync());
    }

    private static byte[] Csv(string rows) => Encoding.UTF8.GetBytes("id_operacao;data_operacao;valor\n" + rows);
}
