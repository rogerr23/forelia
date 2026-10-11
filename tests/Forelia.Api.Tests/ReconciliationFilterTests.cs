using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Forelia.Api.Data;
using Forelia.Api.Imports;
using Forelia.Api.Reconciliations;
using Microsoft.Extensions.DependencyInjection;

namespace Forelia.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class ReconciliationFilterTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static int uploadSequence;

    [Fact]
    public async Task IdentifierUsesExactCaseSensitiveEqualityAndPreservesZeros()
    {
        var execution = await CreateAsync();
        Assert.Equal(new[] { "OP001" }, Ids(await QueryAsync(execution.Id, "idOperacao=OP001")));
        foreach (var key in new[] { "op001", "OP1", "OP00", "OP00%", "INEXISTENTE" })
        {
            var page = await QueryAsync(execution.Id, "idOperacao=" + Uri.EscapeDataString(key));
            Assert.Empty(Ids(page));
            Assert.Equal(0, page.GetProperty("total").GetInt32());
        }
    }

    [Fact]
    public async Task DateFiltersUseOperationalDateAndAccountingFallbackWithInclusiveBounds()
    {
        var execution = await CreateAsync();
        Assert.Equal(new[] { "OP003" }, Ids(await QueryAsync(execution.Id, "dataOperacao=2026-10-03")));
        // OP003 tem data contábil em 05/10, mas sua data de referência é a operacional (03/10).
        Assert.Equal(new[] { "OP005" }, Ids(await QueryAsync(execution.Id, "dataOperacao=2026-10-05")));
        Assert.Equal(new[] { "OP003", "OP004", "OP005" }, Ids(await QueryAsync(execution.Id, "dataInicial=2026-10-03&dataFinal=2026-10-05")));
        Assert.Equal(new[] { "OP001", "OP002" }, Ids(await QueryAsync(execution.Id, "dataFinal=2026-10-02")));
        Assert.Equal(new[] { "OP008", "OP009" }, Ids(await QueryAsync(execution.Id, "dataInicial=2026-10-08")));
        Assert.Equal(new[] { "OP009" }, Ids(await QueryAsync(execution.Id, "dataInicial=2026-10-09&dataFinal=2026-10-09")));
        Assert.Empty(Ids(await QueryAsync(execution.Id, "dataOperacao=2024-02-29")));
    }

    [Fact]
    public async Task AllProvidedFiltersAreCombinedWithAndWithoutChangingExecutionTotals()
    {
        var execution = await CreateAsync();
        var query = "idOperacao=OP004&resultado=DIVERGENTE&situacaoAnalise=PENDENTE&dataOperacao=2026-10-04&dataInicial=2026-10-03&dataFinal=2026-10-05";
        var page = await QueryAsync(execution.Id, query);
        Assert.Equal(new[] { "OP004" }, Ids(page));
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Empty(Ids(await QueryAsync(execution.Id, query.Replace("resultado=DIVERGENTE", "resultado=CONCILIADO"))));
        Assert.Empty(Ids(await QueryAsync(execution.Id, "dataOperacao=2026-10-03&dataInicial=2026-10-04&dataFinal=2026-10-05")));
        using var client = fixture.Factory.CreateClient();
        Assert.Equal(execution, await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{execution.Id}"));
        Assert.Equal(9, (await QueryAsync(execution.Id, "")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AnalysisFilterReflectsChangesAndExcludesMatchedItems()
    {
        var execution = await CreateAsync();
        Assert.Equal(new[] { "OP002", "OP003", "OP004", "OP005", "OP009" }, Ids(await QueryAsync(execution.Id, "situacaoAnalise=PENDENTE")));
        Assert.Empty(Ids(await QueryAsync(execution.Id, "resultado=CONCILIADO&situacaoAnalise=PENDENTE")));
        Assert.Equal(new[] { "OP005" }, Ids(await QueryAsync(execution.Id, "resultado=SEM_REGISTRO_CONTABIL")));
        Assert.Equal(new[] { "OP009" }, Ids(await QueryAsync(execution.Id, "resultado=SEM_OPERACAO_FINANCEIRA")));
        var page = await QueryAsync(execution.Id, "idOperacao=OP004");
        var itemId = page.GetProperty("itens")[0].GetProperty("id").GetGuid();
        using var client = fixture.Factory.CreateClient();
        using var patch = await client.PatchAsJsonAsync($"/api/conciliacoes/{execution.Id}/itens/{itemId}/analise", new { situacao = "ANALISADA" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(new[] { "OP004" }, Ids(await QueryAsync(execution.Id, "resultado=DIVERGENTE&situacaoAnalise=ANALISADA")));
        Assert.Equal(new[] { "OP002", "OP003", "OP005", "OP009" }, Ids(await QueryAsync(execution.Id, "situacaoAnalise=PENDENTE")));
        Assert.Empty(Ids(await QueryAsync(execution.Id, "situacaoAnalise=EM_ANALISE")));
    }

    [Fact]
    public async Task FiltersAreAppliedBeforeCountingAndPaginating()
    {
        var execution = await CreateAsync();
        var first = await QueryAsync(execution.Id, "resultado=DIVERGENTE&pagina=1&tamanhoPagina=2");
        var second = await QueryAsync(execution.Id, "resultado=DIVERGENTE&pagina=2&tamanhoPagina=2");
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(3, second.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "OP002", "OP003" }, Ids(first));
        Assert.Equal(new[] { "OP004" }, Ids(second));
        var afterLast = await QueryAsync(execution.Id, "resultado=DIVERGENTE&pagina=3&tamanhoPagina=2");
        Assert.Equal(3, afterLast.GetProperty("total").GetInt32());
        Assert.Empty(Ids(afterLast));
        var defaults = await QueryAsync(execution.Id, "resultado=CONCILIADO");
        Assert.Equal(20, defaults.GetProperty("tamanhoPagina").GetInt32());
        Assert.Equal(1, defaults.GetProperty("pagina").GetInt32());
        Assert.Equal(new[] { "OP001", "OP006", "OP007", "OP008" }, Ids(defaults));
    }

    [Fact]
    public async Task InvalidFiltersReturn400InsteadOfBeingSilentlyIgnored()
    {
        var execution = await CreateAsync();
        using var client = fixture.Factory.CreateClient();
        foreach (var query in new[]
        {
            "resultado=DESCONHECIDO", "resultado=divergente", "resultado=", "situacaoAnalise=CONCLUIDA",
            "situacaoAnalise=", "idOperacao=", "idOperacao=%20", "idOperacao=%00",
            "dataOperacao=2026-02-29", "dataOperacao=2026-10-01T00:00:00", "dataOperacao=2026-1-01",
            "dataInicial=01-10-2026", "dataFinal=2026-13-01", "dataFinal=", "dataInicial=2026-10-05&dataFinal=2026-10-03"
        })
        {
            using var response = await client.GetAsync($"/api/conciliacoes/{execution.Id}/itens?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("FILTRO_INVALIDO", body.GetProperty("codigo").GetString());
        }
        using var invalidPage = await client.GetAsync($"/api/conciliacoes/{execution.Id}/itens?resultado=DIVERGENTE&tamanhoPagina=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
    }

    [Fact]
    public async Task FiltersKeepExecutionIsolationAndOnlyExposeCompletedResults()
    {
        var execution = await CreateAsync();
        var other = await CreateAsync();
        var first = await QueryAsync(execution.Id, "idOperacao=OP004");
        var second = await QueryAsync(other.Id, "idOperacao=OP004");
        Assert.NotEqual(first.GetProperty("itens")[0].GetProperty("id").GetGuid(), second.GetProperty("itens")[0].GetProperty("id").GetGuid());
        Guid failedId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            var failed = new Reconciliation
            {
                OperationalFileId = execution.ArquivoOperacionalId, AccountingFileId = execution.ArquivoContabilId,
                Status = "FALHOU"
            };
            db.Reconciliations.Add(failed);
            await db.SaveChangesAsync();
            failedId = failed.Id;
        }
        using var client = fixture.Factory.CreateClient();
        using var failedQuery = await client.GetAsync($"/api/conciliacoes/{failedId}/itens?resultado=DIVERGENTE");
        Assert.Equal(HttpStatusCode.Conflict, failedQuery.StatusCode);
        using var missing = await client.GetAsync($"/api/conciliacoes/{Guid.NewGuid()}/itens?resultado=DIVERGENTE");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task<JsonElement> QueryAsync(Guid id, string query)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/conciliacoes/{id}/itens?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<ReconciliationResponse> CreateAsync()
    {
        using var client = fixture.Factory.CreateClient();
        async Task<Guid> ImportAsync(string source)
        {
            var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(CsvValidatorTests.Example($"01_base/{source}.csv"))
                + "\n" + new string(' ', Interlocked.Increment(ref uploadSequence)) + "\n");
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(bytes), "arquivo", "teste.csv");
            using var response = await client.PostAsync($"/api/importacoes/{source}", form);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ImportResponse>())!.Id;
        }
        var request = new ReconciliationRequest(await ImportAsync("operacional"), await ImportAsync("contabil"));
        using var created = await client.PostAsJsonAsync("/api/conciliacoes", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<ReconciliationResponse>())!;
    }

    private static string[] Ids(JsonElement page) => page.GetProperty("itens").EnumerateArray()
        .Select(x => x.GetProperty("idOperacao").GetString()!).ToArray();
}
