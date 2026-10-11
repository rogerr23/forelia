using Forelia.Api.Data;
using Forelia.Api.Reconciliations;
using Microsoft.EntityFrameworkCore;

namespace Forelia.Api.Dashboard;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dashboard", async (Guid? conciliacaoId, ForeliaDbContext database,
            CancellationToken cancellationToken) =>
        {
            var executions = database.Reconciliations.AsNoTracking();
            var selected = conciliacaoId.HasValue
                ? await executions.SingleOrDefaultAsync(x => x.Id == conciliacaoId.Value, cancellationToken)
                : await executions.Where(x => x.Status == "CONCLUIDA")
                    .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);

            if (conciliacaoId.HasValue && selected is null) return Results.NotFound();
            if (selected is not null && selected.Status != "CONCLUIDA")
                return Results.Conflict(new { codigo = "RESULTADO_INDISPONIVEL", mensagem = "A execução ainda não possui resultados definitivos." });

            var counts = await executions.GroupBy(x => x.Status)
                .Select(group => new { Status = group.Key, Total = group.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Total, cancellationToken);
            var recent = await executions.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Take(10).ToListAsync(cancellationToken);

            // Conta ocorrências de valor e data separadamente, sem duplicar os itens nos indicadores.
            var occurrences = selected is null ? null : await database.ReconciliationItems.AsNoTracking()
                .Where(x => x.ReconciliationId == selected.Id).GroupBy(x => x.ReconciliationId)
                .Select(group => new
                {
                    Value = group.Count(x => x.ValueDivergence),
                    Date = group.Count(x => x.DateDivergence),
                    MissingAccounting = group.Count(x => x.Result == "SEM_REGISTRO_CONTABIL"),
                    MissingOperational = group.Count(x => x.Result == "SEM_OPERACAO_FINANCEIRA")
                }).SingleOrDefaultAsync(cancellationToken);
            var indicators = selected is null
                ? new DashboardIndicators(0, 0, 0, 0, 0, 0, 0, 0)
                : new DashboardIndicators(selected.SourceRecords,
                    selected.MatchedPairs + selected.DivergentPairs + selected.MissingAccounting + selected.MissingOperational,
                    selected.MatchedPairs, selected.DivergentPairs, selected.MissingAccounting, selected.MissingOperational,
                    selected.DivergentPairs + selected.MissingAccounting + selected.MissingOperational, selected.ReconciliationRate);

            return Results.Ok(new DashboardResponse(
                new DashboardExecutionCounts(counts.Values.Sum(), counts.GetValueOrDefault("PENDENTE"),
                    counts.GetValueOrDefault("PROCESSANDO"), counts.GetValueOrDefault("CONCLUIDA"), counts.GetValueOrDefault("FALHOU")),
                selected is null ? null : ReconciliationResponse.From(selected), indicators,
                [new("VALOR", occurrences?.Value ?? 0), new("DATA", occurrences?.Date ?? 0),
                    new("SEM_REGISTRO_CONTABIL", occurrences?.MissingAccounting ?? 0),
                    new("SEM_OPERACAO_FINANCEIRA", occurrences?.MissingOperational ?? 0)],
                recent.Select(ReconciliationResponse.From).ToArray()));
        }).WithTags("Dashboard").WithSummary("Consultar indicadores por conciliação e histórico global")
            .WithDescription("Sem conciliacaoId: execução concluída mais recente. Indicadores e ocorrências pertencem somente à seleção; contagens de tentativas e histórico são globais.")
            .Produces<DashboardResponse>().Produces(400).Produces(404).Produces(409);
    }
}
