using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Forelia.Api.Data;
using Forelia.Api.Imports;
using Forelia.Api.Reconciliations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Forelia.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class AnalysisIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static int uploadSequence;

    [Fact]
    public async Task AnalysisCanBeClosedAndReopenedWithoutChangingFinancialResults()
    {
        var (execution, items, request) = await CreateAsync();
        Assert.Equal(5, items.Count(x => x.SituacaoAnalise == "PENDENTE"));
        Assert.Equal(4, items.Count(x => x.SituacaoAnalise is null));
        using var client = fixture.Factory.CreateClient();
        var item = items.Single(x => x.IdOperacao == "OP004");
        var financialSnapshot = item with { SituacaoAnalise = null };
        foreach (var status in new[] { "EM_ANALISE", "ANALISADA", "PENDENTE", "ANALISADA", "EM_ANALISE", "EM_ANALISE" })
        {
            using var response = await client.PatchAsJsonAsync(Path(execution.Id, item.Id) + "/analise", new { situacao = status });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = (await response.Content.ReadFromJsonAsync<ReconciliationItemResponse>())!;
            Assert.Equal(status, updated.SituacaoAnalise);
            Assert.Equal(FinancialJson(financialSnapshot), FinancialJson(updated));
            var fetched = await client.GetFromJsonAsync<ReconciliationItemResponse>(Path(execution.Id, item.Id));
            Assert.Equal(status, fetched!.SituacaoAnalise);
        }
        Assert.Equal(execution, await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{execution.Id}"));
        using var repeat = await client.PostAsJsonAsync("/api/conciliacoes", request);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(execution.Id, (await repeat.Content.ReadFromJsonAsync<ReconciliationResponse>())!.Id);
        Assert.Equal("EM_ANALISE", (await client.GetFromJsonAsync<ReconciliationItemResponse>(Path(execution.Id, item.Id)))!.SituacaoAnalise);
        foreach (var missing in items.Where(x => x.Resultado.StartsWith("SEM_")))
        {
            using var response = await client.PatchAsJsonAsync(Path(execution.Id, missing.Id) + "/analise", new { situacao = "ANALISADA" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(missing.Resultado, (await response.Content.ReadFromJsonAsync<ReconciliationItemResponse>())!.Resultado);
        }
    }

    [Fact]
    public async Task ObservationsAreAppendOnlyAndAvailableAfterAnalysisIsClosed()
    {
        var (execution, items, _) = await CreateAsync();
        var item = items.Single(x => x.IdOperacao == "OP004");
        var path = Path(execution.Id, item.Id);
        using var client = fixture.Factory.CreateClient();
        var before = DateTime.UtcNow;
        using var first = await client.PostAsJsonAsync(path + "/observacoes", new { texto = "Investigar valor e data", analista = "analista-teste" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var observation = (await first.Content.ReadFromJsonAsync<ObservationResponse>())!;
        Assert.Equal(item.Id, observation.ItemId);
        Assert.False(observation.IdentidadeAutenticada);
        Assert.Equal("Identidade não autenticada", observation.AvisoIdentidade);
        Assert.InRange(observation.CriadaEm, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, observation.CriadaEm.Kind);
        Assert.Equal(observation, await client.GetFromJsonAsync<ObservationResponse>(first.Headers.Location));
        using var closed = await client.PatchAsJsonAsync(path + "/analise", new { situacao = "ANALISADA" });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using var correction = await client.PostAsJsonAsync(path + "/observacoes", new { texto = "Complemento da investigação", analista = "outro-analista" });
        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        var second = (await correction.Content.ReadFromJsonAsync<ObservationResponse>())!;
        Assert.NotEqual(observation.Id, second.Id);
        var page = await client.GetFromJsonAsync<JsonElement>(path + "/observacoes?pagina=1&tamanhoPagina=1");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(observation.Id, page.GetProperty("itens")[0].GetProperty("id").GetGuid());
        var next = await client.GetFromJsonAsync<JsonElement>(path + "/observacoes?pagina=2&tamanhoPagina=1");
        Assert.Equal(second.Id, next.GetProperty("itens")[0].GetProperty("id").GetGuid());
        using var edit = await client.PutAsJsonAsync(first.Headers.Location, new { texto = "Alteração" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, edit.StatusCode);
        using var delete = await client.DeleteAsync(first.Headers.Location);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync($"UPDATE observacao_analista SET \"Text\" = 'Alteração' WHERE \"Id\" = '{observation.Id}'"));
        await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync($"DELETE FROM observacao_analista WHERE \"Id\" = '{observation.Id}'"));
        Assert.Equal(observation, await client.GetFromJsonAsync<ObservationResponse>(first.Headers.Location));
        Assert.Equal(execution, await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{execution.Id}"));
    }

    [Fact]
    public async Task RejectsInvalidInputsAndAnalysisOnMatchedItems()
    {
        var (execution, items, _) = await CreateAsync();
        using var client = fixture.Factory.CreateClient();
        var matched = items.First(x => x.Resultado == "CONCILIADO");
        var matchedPath = Path(execution.Id, matched.Id);
        using var cannotAnalyze = await client.PatchAsJsonAsync(matchedPath + "/analise", new { situacao = "ANALISADA" });
        Assert.Equal(HttpStatusCode.Conflict, cannotAnalyze.StatusCode);
        using var cannotObserve = await client.PostAsJsonAsync(matchedPath + "/observacoes", new { texto = "Texto", analista = "Teste" });
        Assert.Equal(HttpStatusCode.Conflict, cannotObserve.StatusCode);
        var path = Path(execution.Id, items.First(x => x.Resultado == "DIVERGENTE").Id);
        foreach (var status in new string?[] { null, "", " ", "CONCILIADO", "FALHOU", "1" })
        {
            using var invalid = await client.PatchAsJsonAsync(path + "/analise", new { situacao = status });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        foreach (var request in new[]
        {
            new ObservationRequest(null, "Teste"), new(" \t\n ", "Teste"), new("Texto", null),
            new("Texto", " \t "), new(new string('x', 2001), "Teste"), new("Texto", new string('x', 101)),
            new("Texto\0inválido", "Teste"), new("Texto", "Teste\0inválido")
        })
        {
            using var invalid = await client.PostAsJsonAsync(path + "/observacoes", request);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        var empty = await client.GetFromJsonAsync<JsonElement>(path + "/observacoes");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        using var maximum = await client.PostAsJsonAsync(path + "/observacoes", new ObservationRequest(new string('x', 2000), new string('x', 100)));
        Assert.Equal(HttpStatusCode.Created, maximum.StatusCode);
        using var invalidPage = await client.GetAsync(path + "/observacoes?pagina=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        Assert.Equal(execution, await client.GetFromJsonAsync<ReconciliationResponse>($"/api/conciliacoes/{execution.Id}"));
    }

    [Fact]
    public async Task ItemsAndObservationsCannotBeAccessedThroughAnotherExecution()
    {
        var (first, firstItems, _) = await CreateAsync();
        var (second, secondItems, _) = await CreateAsync();
        using var client = fixture.Factory.CreateClient();
        var firstItem = firstItems.First(x => x.Resultado == "DIVERGENTE");
        var secondItem = secondItems.First(x => x.Resultado == "DIVERGENTE");
        var wrong = Path(second.Id, firstItem.Id);
        using var get = await client.GetAsync(wrong);
        using var patch = await client.PatchAsJsonAsync(wrong + "/analise", new { situacao = "ANALISADA" });
        using var post = await client.PostAsJsonAsync(wrong + "/observacoes", new { texto = "Texto", analista = "Teste" });
        using var list = await client.GetAsync(wrong + "/observacoes");
        Assert.All(new[] { get, patch, post, list }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        using var created = await client.PostAsJsonAsync(Path(first.Id, firstItem.Id) + "/observacoes", new { texto = "Texto", analista = "Teste" });
        var observation = (await created.Content.ReadFromJsonAsync<ObservationResponse>())!;
        using var wrongObservation = await client.GetAsync(Path(second.Id, secondItem.Id) + $"/observacoes/{observation.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongObservation.StatusCode);
        using var missingExecution = await client.PatchAsJsonAsync(Path(Guid.NewGuid(), firstItem.Id) + "/analise", new { situacao = "ANALISADA" });
        Assert.Equal(HttpStatusCode.NotFound, missingExecution.StatusCode);
    }

    [Fact]
    public async Task FailedExecutionCannotReceiveAnalysisOrObservations()
    {
        var (execution, items, _) = await CreateAsync();
        Guid failedId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            var failed = new Reconciliation
            {
                OperationalFileId = execution.ArquivoOperacionalId, AccountingFileId = execution.ArquivoContabilId,
                Status = "FALHOU", ErrorCode = "FALHA_TECNICA"
            };
            db.Reconciliations.Add(failed);
            await db.SaveChangesAsync();
            failedId = failed.Id;
        }
        using var client = fixture.Factory.CreateClient();
        var path = Path(failedId, items.First().Id);
        using var patch = await client.PatchAsJsonAsync(path + "/analise", new { situacao = "ANALISADA" });
        using var post = await client.PostAsJsonAsync(path + "/observacoes", new { texto = "Texto", analista = "Teste" });
        Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
    }

    [Fact]
    public async Task ConcurrentObservationsAreAllPreserved()
    {
        var (execution, items, _) = await CreateAsync();
        var item = items.First(x => x.Resultado == "SEM_REGISTRO_CONTABIL");
        var path = Path(execution.Id, item.Id);
        using var client = fixture.Factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(1, 6).Select(index =>
            client.PostAsJsonAsync(path + "/observacoes", new { texto = $"Observação {index}", analista = "Teste" })));
        try { Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode)); }
        finally { foreach (var response in responses) response.Dispose(); }
        var page = await client.GetFromJsonAsync<JsonElement>(path + "/observacoes");
        Assert.Equal(6, page.GetProperty("total").GetInt32());
        Assert.Equal(6, page.GetProperty("itens").EnumerateArray().Select(x => x.GetProperty("texto").GetString()).Distinct().Count());
    }

    [Fact]
    public async Task MigrationInitializesExistingItemsWithoutChangingResults()
    {
        // Banco adicional isolado para verificar atualização de uma instalação anterior.
        var isolated = new PostgresFixture();
        await isolated.InitializeAsync();
        try
        {
            using var scope = isolated.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261011151852_AddReconciliations");
            var left = new ImportedFile { Source = SourceType.OPERACIONAL, OriginalContent = [1], SizeBytes = 1, RecordCount = 1, ContentHash = "left" };
            var right = new ImportedFile { Source = SourceType.CONTABIL, OriginalContent = [2], SizeBytes = 1, RecordCount = 1, ContentHash = "right" };
            db.ImportedFiles.AddRange(left, right);
            var execution = new Reconciliation { OperationalFileId = left.Id, AccountingFileId = right.Id, Status = "CONCLUIDA" };
            db.Reconciliations.Add(execution);
            await db.SaveChangesAsync();
            await isolated.ExecuteAsync($"""
                INSERT INTO item_conciliacao ("Id", "ReconciliationId", "ExternalId", "Result", "ValueDivergence", "DateDivergence")
                VALUES ('{Guid.NewGuid()}', '{execution.Id}', 'IGUAL', 'CONCILIADO', false, false),
                       ('{Guid.NewGuid()}', '{execution.Id}', 'DIFERENTE', 'DIVERGENTE', true, true),
                       ('{Guid.NewGuid()}', '{execution.Id}', 'SEM_CONTABIL', 'SEM_REGISTRO_CONTABIL', false, false),
                       ('{Guid.NewGuid()}', '{execution.Id}', 'SEM_OPERACIONAL', 'SEM_OPERACAO_FINANCEIRA', false, false);
                """);
            await migrator.MigrateAsync();
            var items = await db.ReconciliationItems.AsNoTracking().Where(x => x.ReconciliationId == execution.Id).ToListAsync();
            Assert.Null(items.Single(x => x.Result == "CONCILIADO").AnalysisStatus);
            Assert.All(items.Where(x => x.Result != "CONCILIADO"), x => Assert.Equal("PENDENTE", x.AnalysisStatus));
            Assert.True(items.Single(x => x.ExternalId == "DIFERENTE").ValueDivergence);
            Assert.True(items.Single(x => x.ExternalId == "DIFERENTE").DateDivergence);
        }
        finally { await isolated.DisposeAsync(); }
    }

    private async Task<(ReconciliationResponse Execution, List<ReconciliationItemResponse> Items, ReconciliationRequest Request)> CreateAsync()
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
        var execution = (await created.Content.ReadFromJsonAsync<ReconciliationResponse>())!;
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/conciliacoes/{execution.Id}/itens");
        var items = page.GetProperty("itens").Deserialize<List<ReconciliationItemResponse>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return (execution, items, request);
    }

    private static string Path(Guid executionId, Guid itemId) => $"/api/conciliacoes/{executionId}/itens/{itemId}";
    private static string FinancialJson(ReconciliationItemResponse item) => JsonSerializer.Serialize(item with { SituacaoAnalise = null });
}
