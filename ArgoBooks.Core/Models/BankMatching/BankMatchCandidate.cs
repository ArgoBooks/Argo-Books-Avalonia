using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.BankMatching;

/// <summary>
/// A candidate match between a bank statement line and a book record. Transient:
/// produced by the matching engine and surfaced in the UI, never persisted.
/// </summary>
public class BankMatchCandidate
{
    /// <summary>Id of the bank statement line this candidate is for.</summary>
    public string LineId { get; set; } = string.Empty;

    public BookRecordType RecordType { get; set; }

    /// <summary>The kind of record as the matching screens name it.</summary>
    public string RecordTypeName => RecordType == BookRecordType.SalesTaxFiling ? "Sales tax" : RecordType.ToString();
    public string RecordId { get; set; } = string.Empty;

    /// <summary>Display description of the candidate book record.</summary>
    public string RecordDescription { get; set; } = string.Empty;
    public DateTime RecordDate { get; set; }

    /// <summary>Signed amount of the candidate record (bank convention).</summary>
    public decimal RecordAmount { get; set; }

    /// <summary>Match confidence in the range 0..1.</summary>
    public double Confidence { get; set; }

    public MatchReason Reason { get; set; }

    /// <summary>True when the candidate is strong enough to auto-confirm without user review.</summary>
    public bool IsAutoMatch { get; set; }
}
