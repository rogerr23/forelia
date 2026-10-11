using Forelia.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Forelia.Api.Reconciliations;

public static class ReconciliationEndpoints
{
    public static void MapReconciliationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/conciliacoes", async (ReconciliationRequest request, ReconciliationService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ExecuteAsync(request, cancellationToken);
            return result.StatusCode switch
            {
                201 => Results.Created($"/api/conciliacoes/{result.Execution!.Id}", result.Execution),
                200 => Results.Ok(result.Execution),
                _ => Results.Json(new { codigo = result.Code, mensagem = result.Message }, statusCode: result.StatusCode)
            };
        });
        app.MapGet("/api/conciliacoes/{id:guid}", async (Guid id, ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            var execution = await database.Reconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return execution is null ? Results.NotFound() : Results.Ok(ReconciliationResponse.From(execution));
        });
        app.MapGet("/api/conciliacoes", async (int? pagina, int? tamanhoPagina, ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            var page = pagina ?? 1;
            var size = tamanhoPagina ?? 20;
            if (!ValidPage(page, size)) return InvalidPage();
            var total = await database.Reconciliations.CountAsync(cancellationToken);
            var executions = await database.Reconciliations.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
            return Results.Ok(new { pagina = page, tamanhoPagina = size, total, itens = executions.Select(ReconciliationResponse.From) });
        });
        app.MapGet("/api/conciliacoes/{id:guid}/itens", async (Guid id, int? pagina, int? tamanhoPagina,
            ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            var page = pagina ?? 1;
            var size = tamanhoPagina ?? 20;
            if (!ValidPage(page, size)) return InvalidPage();
            var status = await database.Reconciliations.Where(x => x.Id == id).Select(x => x.Status).SingleOrDefaultAsync(cancellationToken);
            if (status is null) return Results.NotFound();
            if (status != "CONCLUIDA") return Results.Conflict(new { codigo = "RESULTADO_INDISPONIVEL", mensagem = "A execução ainda não possui resultados definitivos." });
            var query = database.ReconciliationItems.AsNoTracking().Where(x => x.ReconciliationId == id);
            var total = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(x => x.ExternalId).ThenBy(x => x.Id)
                .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
            return Results.Ok(new { pagina = page, tamanhoPagina = size, total, itens = items.Select(ReconciliationItemResponse.From) });
        });
    }

    private static bool ValidPage(int page, int size) => page >= 1 && size is >= 1 and <= 100 && (long)(page - 1) * size <= int.MaxValue;
    private static IResult InvalidPage() => Results.BadRequest(new { codigo = "PAGINACAO_INVALIDA", mensagem = "Use página a partir de 1 e tamanho entre 1 e 100, dentro do intervalo suportado." });
}
