using Forelia.Api.Data;

namespace Forelia.Api.Reconciliations;

public sealed record ReconciliationRequest(Guid ArquivoOperacionalId, Guid ArquivoContabilId);
public sealed record ReconciliationResponse(Guid Id, Guid ArquivoOperacionalId, Guid ArquivoContabilId,
    string VersaoRegras, string Status, DateTime CriadaEm, DateTime? FinalizadaEm, string? CodigoErro,
    int TotalRegistrosOrigem, int TotalItens, int ParesConciliados, int ParesDivergentes,
    int SemRegistroContabil, int SemOperacaoFinanceira, decimal TaxaConciliacao)
{
    public static ReconciliationResponse From(Reconciliation x) => new(x.Id, x.OperationalFileId,
        x.AccountingFileId, x.RulesVersion, x.Status, x.CreatedAt, x.FinishedAt, x.ErrorCode,
        x.SourceRecords, x.MatchedPairs + x.DivergentPairs + x.MissingAccounting + x.MissingOperational,
        x.MatchedPairs, x.DivergentPairs, x.MissingAccounting, x.MissingOperational, x.ReconciliationRate);
}

public sealed record ReconciliationItemResponse(Guid Id, string IdOperacao, Guid? RegistroOperacionalId,
    Guid? RegistroContabilId, decimal? ValorOperacional, decimal? ValorContabil,
    DateOnly? DataOperacional, DateOnly? DataContabil, string Resultado, string[] Divergencias)
{
    public static ReconciliationItemResponse From(ReconciliationItem x) => new(x.Id, x.ExternalId,
        x.OperationalRecordId, x.AccountingRecordId, x.OperationalAmount, x.AccountingAmount,
        x.OperationalDate, x.AccountingDate, x.Result,
        new[] { x.ValueDivergence ? "VALOR" : null, x.DateDivergence ? "DATA" : null }
            .OfType<string>().ToArray());
}

public sealed record ReconciliationOutcome(int StatusCode, ReconciliationResponse? Execution = null,
    string? Code = null, string? Message = null);
