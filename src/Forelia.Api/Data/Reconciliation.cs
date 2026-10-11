namespace Forelia.Api.Data;

public sealed class Reconciliation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperationalFileId { get; set; }
    public Guid AccountingFileId { get; set; }
    public string RulesVersion { get; set; } = "conciliacao-v1";
    public string Status { get; set; } = "PENDENTE";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string? ErrorCode { get; set; }
    public int SourceRecords { get; set; }
    public int MatchedPairs { get; set; }
    public int DivergentPairs { get; set; }
    public int MissingAccounting { get; set; }
    public int MissingOperational { get; set; }
    public decimal ReconciliationRate { get; set; }
    public List<ReconciliationItem> Items { get; set; } = [];
}

public sealed class ReconciliationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReconciliationId { get; set; }
    public string ExternalId { get; set; } = "";
    public Guid? OperationalRecordId { get; set; }
    public Guid? AccountingRecordId { get; set; }
    public decimal? OperationalAmount { get; set; }
    public decimal? AccountingAmount { get; set; }
    public DateOnly? OperationalDate { get; set; }
    public DateOnly? AccountingDate { get; set; }
    public string Result { get; set; } = "";
    public bool ValueDivergence { get; set; }
    public bool DateDivergence { get; set; }
}
