using Forelia.Api.Imports;

namespace Forelia.Api.Data;

public sealed class ImportedFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SourceType Source { get; set; }
    public string OriginalName { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public byte[] OriginalContent { get; set; } = [];
    public long SizeBytes { get; set; }
    public int RecordCount { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public string LayoutVersion { get; set; } = ImportContract.Version;
    public string ImporterVersion { get; set; } = ImportContract.ImporterVersion;
    public List<FinancialRecord> Records { get; set; } = [];
}

public sealed class FinancialRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public int LineNumber { get; set; }
    public string ExternalId { get; set; } = "";
    public DateOnly OperationDate { get; set; }
    public decimal Amount { get; set; }
}
