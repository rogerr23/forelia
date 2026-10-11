using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Forelia.Api.Data;
using Forelia.Api.Imports;
using Microsoft.EntityFrameworkCore;

namespace Forelia.Api.Reconciliations;

public sealed class ReconciliationService(ForeliaDbContext database, ILogger<ReconciliationService> logger)
{
    public async Task<ReconciliationOutcome> ExecuteAsync(ReconciliationRequest request, CancellationToken cancellationToken)
    {
        var files = await database.ImportedFiles.AsNoTracking()
            .Where(x => x.Id == request.ArquivoOperacionalId || x.Id == request.ArquivoContabilId)
            .Select(x => new { x.Id, x.Source }).ToListAsync(cancellationToken);
        if (!files.Any(x => x.Id == request.ArquivoOperacionalId) || !files.Any(x => x.Id == request.ArquivoContabilId))
            return new(404, Code: "ARQUIVO_NAO_ENCONTRADO", Message: "Selecione arquivos já importados.");
        if (!files.Any(x => x.Id == request.ArquivoOperacionalId && x.Source == SourceType.OPERACIONAL)
            || !files.Any(x => x.Id == request.ArquivoContabilId && x.Source == SourceType.CONTABIL))
            return new(400, Code: "FONTES_INVALIDAS", Message: "Selecione um arquivo operacional e um contábil, nas fontes corretas.");

        // Lock de sessão: serializa a combinação inclusive durante o registro de uma falha.
        // Os commits dos estados ficam independentes da transação dos resultados.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{request.ArquivoOperacionalId:N}/{request.ArquivoContabilId:N}/{ReconciliationEngine.RulesVersion}"));
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(hash);
        await database.Database.OpenConnectionAsync(cancellationToken);
        var locked = false;
        try
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({lockKey})", cancellationToken);
            locked = true;
            var equivalent = database.Reconciliations.Where(x => x.OperationalFileId == request.ArquivoOperacionalId
                && x.AccountingFileId == request.ArquivoContabilId && x.RulesVersion == ReconciliationEngine.RulesVersion);
            var existing = await equivalent.AsNoTracking().SingleOrDefaultAsync(x => x.Status == "CONCLUIDA", cancellationToken);
            if (existing is not null) return new(200, ReconciliationResponse.From(existing));

            // Com o lock adquirido, estados ativos anteriores pertencem a solicitações interrompidas.
            await equivalent.Where(x => x.Status == "PENDENTE" || x.Status == "PROCESSANDO")
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "FALHOU")
                    .SetProperty(x => x.ErrorCode, "PROCESSAMENTO_INTERROMPIDO")
                    .SetProperty(x => x.FinishedAt, DateTime.UtcNow), cancellationToken);
            var execution = new Reconciliation
            {
                OperationalFileId = request.ArquivoOperacionalId, AccountingFileId = request.ArquivoContabilId,
                RulesVersion = ReconciliationEngine.RulesVersion
            };
            database.Reconciliations.Add(execution);
            await database.SaveChangesAsync(cancellationToken);
            try
            {
                execution.Status = "PROCESSANDO";
                await database.SaveChangesAsync(cancellationToken);
                var records = await database.FinancialRecords.AsNoTracking()
                    .Where(x => x.FileId == request.ArquivoOperacionalId || x.FileId == request.ArquivoContabilId)
                    .ToListAsync(cancellationToken);
                ReconciliationEngine.Calculate(execution,
                    records.Where(x => x.FileId == request.ArquivoOperacionalId).ToList(),
                    records.Where(x => x.FileId == request.ArquivoContabilId).ToList());
                database.ReconciliationItems.AddRange(execution.Items);
                await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
                {
                    execution.Status = "CONCLUIDA";
                    execution.FinishedAt = DateTime.UtcNow;
                    await database.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                return new(201, ReconciliationResponse.From(execution));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha na conciliação {ReconciliationId}", execution.Id);
                database.ChangeTracker.Clear();
                // A transação dos resultados já foi descartada; não expor resultados parciais.
                try
                {
                    await database.Reconciliations.Where(x => x.Id == execution.Id && x.Status != "CONCLUIDA")
                        .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "FALHOU")
                            .SetProperty(x => x.ErrorCode, "FALHA_TECNICA")
                            .SetProperty(x => x.FinishedAt, DateTime.UtcNow), CancellationToken.None);
                }
                catch (Exception persistenceException)
                {
                    logger.LogError(persistenceException, "Não foi possível registrar a falha da conciliação {ReconciliationId}", execution.Id);
                }
                if (exception is OperationCanceledException) throw;
                return new(500, Code: "FALHA_TECNICA", Message: $"Não foi possível concluir a conciliação {execution.Id}. Uma nova tentativa é permitida.");
            }
        }
        finally
        {
            try
            {
                if (locked) await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({lockKey})", CancellationToken.None);
            }
            finally { await database.Database.CloseConnectionAsync(); }
        }
    }
}
