using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Forelia.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Forelia.Api.Reconciliations;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/conciliacoes/{id:guid}/exportacao", async (Guid id,
            [AsParameters] ReconciliationItemFilters filters, ForeliaDbContext database, CancellationToken cancellationToken) =>
        {
            if (filters.Validate() is { } error)
                return Results.BadRequest(new { codigo = "FILTRO_INVALIDO", mensagem = error });
            var status = await database.Reconciliations.Where(x => x.Id == id)
                .Select(x => x.Status).SingleOrDefaultAsync(cancellationToken);
            if (status is null) return Results.NotFound();
            if (status != "CONCLUIDA")
                return Results.Conflict(new { codigo = "RESULTADO_INDISPONIVEL", mensagem = "A execução ainda não possui resultados definitivos." });
            var query = filters.Apply(database.ReconciliationItems.AsNoTracking().Where(x => x.ReconciliationId == id))
                .OrderBy(x => x.ExternalId).ThenBy(x => x.Id);
            return Results.Stream(async stream =>
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
                await using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = ";", NewLine = "\r\n"
                });
                foreach (var header in new[] { "id_operacao", "valor_operacional", "valor_contabil", "data_operacional",
                    "data_contabil", "resultado", "divergencias", "situacao_analise" }) csv.WriteField(header);
                await csv.NextRecordAsync();
                await foreach (var item in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
                {
                    csv.WriteField(item.ExternalId);
                    csv.WriteField(item.OperationalAmount?.ToString("F2", CultureInfo.InvariantCulture));
                    csv.WriteField(item.AccountingAmount?.ToString("F2", CultureInfo.InvariantCulture));
                    csv.WriteField(item.OperationalDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    csv.WriteField(item.AccountingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    csv.WriteField(item.Result);
                    csv.WriteField(string.Join("|", ReconciliationItemResponse.From(item).Divergencias));
                    csv.WriteField(item.AnalysisStatus);
                    await csv.NextRecordAsync();
                }
            }, "text/csv; charset=utf-8", $"conciliacao-{id:D}.csv");
        }).WithTags("Relatórios").WithSummary("Exportar todos os itens filtrados de uma conciliação concluída")
            .WithDescription("CSV UTF-8 com BOM e separador ;. Mesmos filtros da consulta, sem paginação. Valores exatos com duas casas; ausências vazias; divergências múltiplas VALOR|DATA.")
            .Produces(200, contentType: "text/csv").Produces(400).Produces(404).Produces(409);
    }
}
