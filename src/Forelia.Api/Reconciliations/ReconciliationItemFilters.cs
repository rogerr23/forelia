using System.Globalization;
using Forelia.Api.Data;

namespace Forelia.Api.Reconciliations;

public sealed class ReconciliationItemFilters
{
    public string? IdOperacao { get; init; }
    public string? Resultado { get; init; }
    public string? SituacaoAnalise { get; init; }
    public string? DataOperacao { get; init; }
    public string? DataInicial { get; init; }
    public string? DataFinal { get; init; }

    private DateOnly? operationDate;
    private DateOnly? initialDate;
    private DateOnly? finalDate;

    public string? Validate()
    {
        if (IdOperacao is not null && (string.IsNullOrWhiteSpace(IdOperacao) || IdOperacao.Contains('\0')))
            return "Informe um idOperacao não vazio e sem caracteres nulos.";
        if (Resultado is not null && Resultado is not ("CONCILIADO" or "DIVERGENTE" or "SEM_REGISTRO_CONTABIL" or "SEM_OPERACAO_FINANCEIRA"))
            return "Use CONCILIADO, DIVERGENTE, SEM_REGISTRO_CONTABIL ou SEM_OPERACAO_FINANCEIRA em resultado.";
        if (SituacaoAnalise is not null && SituacaoAnalise is not ("PENDENTE" or "EM_ANALISE" or "ANALISADA"))
            return "Use PENDENTE, EM_ANALISE ou ANALISADA em situacaoAnalise.";
        if (!TryDate(DataOperacao, out operationDate) || !TryDate(DataInicial, out initialDate) || !TryDate(DataFinal, out finalDate))
            return "Use datas válidas no formato yyyy-MM-dd em dataOperacao, dataInicial e dataFinal.";
        if (initialDate.HasValue && finalDate.HasValue && initialDate > finalDate)
            return "dataInicial não pode ser posterior a dataFinal.";
        return null;
    }

    public IQueryable<ReconciliationItem> Apply(IQueryable<ReconciliationItem> query)
    {
        if (IdOperacao is not null) query = query.Where(x => x.ExternalId == IdOperacao);
        if (Resultado is not null) query = query.Where(x => x.Result == Resultado);
        if (SituacaoAnalise is not null) query = query.Where(x => x.AnalysisStatus == SituacaoAnalise);
        // A data contábil só é usada quando não existe data operacional.
        if (operationDate.HasValue) query = query.Where(x => (x.OperationalDate ?? x.AccountingDate) == operationDate);
        if (initialDate.HasValue) query = query.Where(x => (x.OperationalDate ?? x.AccountingDate) >= initialDate);
        if (finalDate.HasValue) query = query.Where(x => (x.OperationalDate ?? x.AccountingDate) <= finalDate);
        return query;
    }

    private static bool TryDate(string? text, out DateOnly? result)
    {
        result = null;
        if (text is null) return true;
        if (text.Length != 10 || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        result = date;
        return true;
    }
}
