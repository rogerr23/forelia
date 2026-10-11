using Forelia.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Forelia.Api.Reconciliations;

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/conciliacoes/{conciliacaoId:guid}/itens/{itemId:guid}").WithTags("Análise e observações");
        group.MapGet("", async (Guid conciliacaoId, Guid itemId, ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            var (item, error) = await FindItemAsync(conciliacaoId, itemId, database, cancellationToken);
            return error ?? Results.Ok(ReconciliationItemResponse.From(item!));
        });
        group.MapPatch("/analise", async (Guid conciliacaoId, Guid itemId, AnalysisRequest request,
            ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            var status = request.Situacao?.Trim().ToUpperInvariant();
            if (status is not ("PENDENTE" or "EM_ANALISE" or "ANALISADA"))
                return Results.BadRequest(new { codigo = "SITUACAO_INVALIDA", mensagem = "Use PENDENTE, EM_ANALISE ou ANALISADA." });
            var (item, error) = await FindItemAsync(conciliacaoId, itemId, database, cancellationToken);
            if (error is not null) return error;
            if (item!.Result == "CONCILIADO") return NotApplicable();
            // Alterar somente a análise; valores, datas, resultado e totais permanecem intactos.
            await database.ReconciliationItems.Where(x => x.Id == itemId && x.ReconciliationId == conciliacaoId)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.AnalysisStatus, status), cancellationToken);
            item.AnalysisStatus = status;
            return Results.Ok(ReconciliationItemResponse.From(item));
        });
        group.MapPost("/observacoes", async (Guid conciliacaoId, Guid itemId, ObservationRequest request,
            ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Texto) || request.Texto.Length > 2000
                || request.Texto.Contains('\0') || string.IsNullOrWhiteSpace(request.Analista)
                || request.Analista.Length > 100 || request.Analista.Contains('\0'))
                return Results.BadRequest(new { codigo = "OBSERVACAO_INVALIDA",
                    mensagem = "Informe texto de até 2.000 caracteres e analista de até 100 caracteres; ambos são obrigatórios e não podem conter caracteres nulos." });
            var (item, error) = await FindItemAsync(conciliacaoId, itemId, database, cancellationToken);
            if (error is not null) return error;
            if (item!.Result == "CONCILIADO") return NotApplicable();
            var observation = new AnalystObservation
            {
                ReconciliationItemId = itemId, Text = request.Texto.Trim(), Analyst = request.Analista.Trim()
            };
            database.AnalystObservations.Add(observation);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/conciliacoes/{conciliacaoId}/itens/{itemId}/observacoes/{observation.Id}",
                ObservationResponse.From(observation));
        });
        group.MapGet("/observacoes", async (Guid conciliacaoId, Guid itemId, int? pagina, int? tamanhoPagina,
            ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            var page = pagina ?? 1;
            var size = tamanhoPagina ?? 20;
            if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue)
                return Results.BadRequest(new { codigo = "PAGINACAO_INVALIDA", mensagem = "Use página a partir de 1 e tamanho entre 1 e 100, dentro do intervalo permitido." });
            var (item, error) = await FindItemAsync(conciliacaoId, itemId, database, cancellationToken);
            if (error is not null) return error;
            if (item!.Result == "CONCILIADO") return NotApplicable();
            var query = database.AnalystObservations.AsNoTracking().Where(x => x.ReconciliationItemId == itemId);
            var total = await query.CountAsync(cancellationToken);
            var observations = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
            return Results.Ok(new { pagina = page, tamanhoPagina = size, total, itens = observations.Select(ObservationResponse.From) });
        });
        group.MapGet("/observacoes/{observacaoId:guid}", async (Guid conciliacaoId, Guid itemId, Guid observacaoId,
            ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            var (_, error) = await FindItemAsync(conciliacaoId, itemId, database, cancellationToken);
            if (error is not null) return error;
            var observation = await database.AnalystObservations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == observacaoId && x.ReconciliationItemId == itemId, cancellationToken);
            return observation is null ? Results.NotFound() : Results.Ok(ObservationResponse.From(observation));
        });
    }

    private static async Task<(ReconciliationItem? Item, IResult? Error)> FindItemAsync(Guid executionId, Guid itemId,
        ForeliaDbContext database, CancellationToken cancellationToken)
    {
        var status = await database.Reconciliations.Where(x => x.Id == executionId)
            .Select(x => x.Status).SingleOrDefaultAsync(cancellationToken);
        if (status is null) return (null, Results.NotFound());
        if (status != "CONCLUIDA") return (null, Results.Conflict(new { codigo = "RESULTADO_INDISPONIVEL",
            mensagem = "A execução ainda não possui resultados definitivos." }));
        var item = await database.ReconciliationItems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == itemId && x.ReconciliationId == executionId, cancellationToken);
        return item is null ? (null, Results.NotFound()) : (item, null);
    }

    private static IResult NotApplicable() => Results.Conflict(new { codigo = "ANALISE_NAO_APLICAVEL",
        mensagem = "A análise e as observações se aplicam a divergências e registros sem correspondência." });
}
