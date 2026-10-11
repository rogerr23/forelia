using System.Net;
using System.Net.Http.Json;
using Forelia.Api.Dashboard;
using Forelia.Api.Data;
using Forelia.Api.Imports;
using Forelia.Api.Reconciliations;
using Microsoft.Extensions.DependencyInjection;

namespace Forelia.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class DashboardIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task EmptyDatabaseHasNoSelectedExecutionAndAllCountersAreZero()
    {
        var dashboard = await ReadAsync();
        Assert.Null(dashboard.ConciliacaoSelecionada);
        Assert.Equal(new DashboardExecutionCounts(0, 0, 0, 0, 0), dashboard.Execucoes);
        Assert.Equal(new DashboardIndicators(0, 0, 0, 0, 0, 0, 0, 0), dashboard.Indicadores);
        Assert.Empty(dashboard.ExecucoesRecentes);
        Assert.Equal(new[] { "VALOR", "DATA", "SEM_REGISTRO_CONTABIL", "SEM_OPERACAO_FINANCEIRA" },
            dashboard.DistribuicaoDivergencias.Select(x => x.Tipo));
        Assert.All(dashboard.DistribuicaoDivergencias, x => Assert.Equal(0, x.Ocorrencias));
    }

    [Fact]
    public async Task BaseScenarioCountsItemsAndOccurrencesSeparatelyAndIgnoresAnalysisChanges()
    {
        var execution = await CreateAsync();
        var dashboard = await ReadAsync(execution.Id);
        Assert.Equal(execution, dashboard.ConciliacaoSelecionada);
        Assert.Equal(new DashboardIndicators(16, 9, 4, 3, 1, 1, 5, 44.44m), dashboard.Indicadores);
        Assert.Equal(new[] { 2, 2, 1, 1 }, dashboard.DistribuicaoDivergencias.Select(x => x.Ocorrencias));
        Assert.Equal(6, dashboard.DistribuicaoDivergencias.Sum(x => x.Ocorrencias));
        using var client = fixture.Factory.CreateClient();
        var page = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/conciliacoes/{execution.Id}/itens?idOperacao=OP004");
        var item = page.GetProperty("itens")[0].GetProperty("id").GetGuid();
        using var updated = await client.PatchAsJsonAsync($"/api/conciliacoes/{execution.Id}/itens/{item}/analise", new { situacao = "ANALISADA" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = await ReadAsync(execution.Id);
        Assert.Equal(dashboard.Indicadores, after.Indicadores);
        Assert.Equal(dashboard.DistribuicaoDivergencias, after.DistribuicaoDivergencias);
    }

    [Fact]
    public async Task CorrectionSelectsLatestCompletedWithoutSummingOrCountingReusedExecution()
    {
        var original = await CreateAsync();
        var corrected = await CreateAsync("02_correcao/contabil_corrigido.csv");
        // Define a ordem explicitamente para não depender da resolução do relógio.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            (await db.Reconciliations.FindAsync(original.Id))!.CreatedAt = corrected.CriadaEm.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        using var client = fixture.Factory.CreateClient();
        using var reused = await client.PostAsJsonAsync("/api/conciliacoes",
            new ReconciliationRequest(corrected.ArquivoOperacionalId, corrected.ArquivoContabilId));
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        var dashboard = await ReadAsync();
        Assert.Equal(corrected.Id, dashboard.ConciliacaoSelecionada!.Id);
        Assert.Equal(new DashboardExecutionCounts(2, 0, 0, 2, 0), dashboard.Execucoes);
        Assert.Equal(new DashboardIndicators(16, 8, 8, 0, 0, 0, 0, 100), dashboard.Indicadores);
        Assert.All(dashboard.DistribuicaoDivergencias, x => Assert.Equal(0, x.Ocorrencias));
        Assert.Equal(new[] { corrected.Id, original.Id }, dashboard.ExecucoesRecentes.Select(x => x.Id));
        var historical = await ReadAsync(original.Id);
        Assert.Equal(9, historical.Indicadores.TotalItens);
        Assert.Equal(44.44m, historical.Indicadores.TaxaConciliacao);
        Assert.Equal(2, historical.Execucoes.Total);
    }

    [Fact]
    public async Task CountsEveryAttemptAndLimitsRecentListWithoutSelectingNewerIncompleteExecutions()
    {
        var execution = await CreateAsync();
        var attempts = new List<Reconciliation>();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            for (var i = 0; i < 12; i++)
            {
                var attempt = new Reconciliation
                {
                    OperationalFileId = execution.ArquivoOperacionalId, AccountingFileId = execution.ArquivoContabilId,
                    Status = new[] { "PENDENTE", "PROCESSANDO", "FALHOU" }[i % 3],
                    CreatedAt = execution.CriadaEm.AddMinutes(i + 1)
                };
                attempts.Add(attempt);
                db.Reconciliations.Add(attempt);
            }
            await db.SaveChangesAsync();
        }
        var dashboard = await ReadAsync();
        Assert.Equal(new DashboardExecutionCounts(13, 4, 4, 1, 4), dashboard.Execucoes);
        Assert.Equal(execution.Id, dashboard.ConciliacaoSelecionada!.Id);
        Assert.Equal(attempts.AsEnumerable().Reverse().Take(10).Select(x => x.Id),
            dashboard.ExecucoesRecentes.Select(x => x.Id));
        Assert.Equal(9, dashboard.Indicadores.TotalItens);
    }

    [Theory]
    [InlineData("PENDENTE")]
    [InlineData("PROCESSANDO")]
    [InlineData("FALHOU")]
    public async Task WithoutCompletedExecutionMetricsStayZeroAndExplicitSelectionReturns409(string status)
    {
        var operational = await ImportAsync("operacional", "01_base/operacional.csv");
        var accounting = await ImportAsync("contabil", "01_base/contabil.csv");
        var attempt = new Reconciliation { OperationalFileId = operational, AccountingFileId = accounting, Status = status };
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            db.Reconciliations.Add(attempt);
            await db.SaveChangesAsync();
        }
        var dashboard = await ReadAsync();
        Assert.Null(dashboard.ConciliacaoSelecionada);
        Assert.Equal(new DashboardIndicators(0, 0, 0, 0, 0, 0, 0, 0), dashboard.Indicadores);
        Assert.All(dashboard.DistribuicaoDivergencias, x => Assert.Equal(0, x.Ocorrencias));
        Assert.Equal(1, dashboard.Execucoes.Total);
        Assert.Equal(attempt.Id, Assert.Single(dashboard.ExecucoesRecentes).Id);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/dashboard?conciliacaoId={attempt.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("RESULTADO_INDISPONIVEL", body.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task MissingSelectionReturns404AndMalformedIdentifierReturns400()
    {
        using var client = fixture.Factory.CreateClient();
        using var missing = await client.GetAsync($"/api/dashboard?conciliacaoId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalid = await client.GetAsync("/api/dashboard?conciliacaoId=invalido");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private async Task<DashboardResponse> ReadAsync(Guid? id = null)
    {
        using var client = fixture.Factory.CreateClient();
        return (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard" + (id.HasValue ? $"?conciliacaoId={id}" : "")))!;
    }

    private async Task<ReconciliationResponse> CreateAsync(string accountingPath = "01_base/contabil.csv")
    {
        var request = new ReconciliationRequest(await ImportAsync("operacional", "01_base/operacional.csv"),
            await ImportAsync("contabil", accountingPath));
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/conciliacoes", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReconciliationResponse>())!;
    }

    private async Task<Guid> ImportAsync(string source, string path)
    {
        using var client = fixture.Factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(CsvValidatorTests.Example(path)), "arquivo", "teste.csv");
        using var response = await client.PostAsync($"/api/importacoes/{source}", form);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty(response.StatusCode == HttpStatusCode.Created ? "id" : "importacaoExistenteId").GetGuid();
    }
}
