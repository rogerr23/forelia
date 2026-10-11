using Forelia.Api.Data;

namespace Forelia.Api.Reconciliations;

public static class ReconciliationEngine
{
    public const string RulesVersion = "conciliacao-v1";

    public static void Calculate(Reconciliation execution, IReadOnlyList<FinancialRecord> operational,
        IReadOnlyList<FinancialRecord> accounting)
    {
        var left = operational.ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
        var right = accounting.ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
        execution.Items = left.Keys.Union(right.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(key =>
            {
                left.TryGetValue(key, out var op);
                right.TryGetValue(key, out var acc);
                var valueDiff = op is not null && acc is not null && op.Amount != acc.Amount;
                var dateDiff = op is not null && acc is not null && op.OperationDate != acc.OperationDate;
                return new ReconciliationItem
                {
                    ReconciliationId = execution.Id, ExternalId = key,
                    OperationalRecordId = op?.Id, AccountingRecordId = acc?.Id,
                    OperationalAmount = op?.Amount, AccountingAmount = acc?.Amount,
                    OperationalDate = op?.OperationDate, AccountingDate = acc?.OperationDate,
                    ValueDivergence = valueDiff, DateDivergence = dateDiff,
                    AnalysisStatus = op is null || acc is null || valueDiff || dateDiff ? "PENDENTE" : null,
                    Result = op is null ? "SEM_OPERACAO_FINANCEIRA" : acc is null ? "SEM_REGISTRO_CONTABIL"
                        : valueDiff || dateDiff ? "DIVERGENTE" : "CONCILIADO"
                };
            }).ToList();
        execution.SourceRecords = operational.Count + accounting.Count;
        execution.MatchedPairs = execution.Items.Count(x => x.Result == "CONCILIADO");
        execution.DivergentPairs = execution.Items.Count(x => x.Result == "DIVERGENTE");
        execution.MissingAccounting = execution.Items.Count(x => x.Result == "SEM_REGISTRO_CONTABIL");
        execution.MissingOperational = execution.Items.Count(x => x.Result == "SEM_OPERACAO_FINANCEIRA");
        execution.ReconciliationRate = execution.Items.Count == 0 ? 0m
            : decimal.Round(100m * execution.MatchedPairs / execution.Items.Count, 2, MidpointRounding.AwayFromZero);
    }
}
