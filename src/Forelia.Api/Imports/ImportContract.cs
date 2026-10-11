namespace Forelia.Api.Imports;

public static class ImportContract
{
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxRequestBytes = MaxFileBytes + 64 * 1024;
    public const string Version = "csv-v1";
    public const string ImporterVersion = "1";
}

public enum SourceType { OPERACIONAL, CONTABIL }

public sealed record ImportError(int? Linha, string? Campo, string Codigo, string Mensagem);
public sealed record CsvRecord(int Linha, string IdOperacao, DateOnly DataOperacao, decimal Valor);
public sealed record CsvValidation(IReadOnlyList<CsvRecord> Registros, IReadOnlyList<ImportError> Erros)
{
    public bool IsValid => Erros.Count == 0;
}

public sealed record ImportResponse(Guid Id, string Fonte, string NomeOriginal, string HashConteudo,
    long TamanhoBytes, int TotalRegistros, DateTime ImportadoEm, string VersaoLayout);

public sealed record ImportResult(ImportResponse? Importacao, IReadOnlyList<ImportError> Erros,
    Guid? ImportacaoExistenteId = null);
