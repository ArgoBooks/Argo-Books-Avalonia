using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.AI;

/// <summary>Kind of a structural (non-transaction) row in a mixed income/expense report.</summary>
public enum MixedRowKind
{
    IncomeSection,
    ExpenseSection,
    Category,
    Subtotal
}

/// <summary>A structural row the AI identified in a mixed report, by its row index.</summary>
public sealed record MixedRowMarker(int RowIndex, MixedRowKind Kind, string Text);

/// <summary>
/// Why the AI rescue pass judged a file impossible to import. The application maps each
/// code to vetted, user-facing copy; the AI never writes the message itself.
/// </summary>
public enum ImportRescueRejectionReason
{
    /// <summary>Aggregated totals/subtotals (e.g. a Profit and Loss report), not individual records.</summary>
    SummaryOrReport,

    /// <summary>Nothing in the file matches a data type Argo Books tracks.</summary>
    NotArgoData,

    /// <summary>Looks like records but the layout cannot be read as one record per row.</summary>
    UnsupportedStructure,

    /// <summary>No readable data rows at all.</summary>
    EmptyOrUnreadable,

    /// <summary>More rows than the rescue will attempt (see RescueMaxTotalRows); the user should split the file.</summary>
    TooLarge,

    /// <summary>
    /// The file would not open at all, so nothing was ever judged. Set by the reader, never by the AI.
    /// </summary>
    FileCouldNotBeOpened
}

/// <summary>Outcome of the whole-file rescue pass.</summary>
public enum ImportRescueOutcome
{
    Extracted,
    Rejected
}

/// <summary>
/// The AI's per-sheet decision: one of three mutually exclusive states: a single entity type to
/// extract into (<see cref="EntityType"/>), a rejection reason (<see cref="Reason"/>), or a mixed
/// income/expense report (<see cref="IsMixedIncomeExpense"/>).
/// </summary>
public sealed class RescueClassification
{
    public SpreadsheetSheetType? EntityType { get; init; }
    public ImportRescueRejectionReason? Reason { get; init; }

    /// <summary>
    /// True when the AI judged the sheet a mixed income+expense transaction report
    /// (e.g. a Profit and Loss Detail). When set, EntityType and Reason are both null.
    /// </summary>
    public bool IsMixedIncomeExpense { get; init; }
}

/// <summary>
/// Per-sheet rescue outcome: either extracted entities, or a rejection reason.
/// <see cref="Reason"/> is expected null exactly when <see cref="ProcessedData"/> holds entities.
/// </summary>
public sealed class RescueSheetResult
{
    public required string SheetName { get; init; }
    public List<LlmProcessedData> ProcessedData { get; init; } = [];
    public ImportRescueRejectionReason? Reason { get; init; }

    public int EntityCount => ProcessedData.Sum(d => d.Entities.Count);
}

/// <summary>Entities the rescue extracted for one sheet, ready for ImportProcessedEntities.</summary>
public sealed class RescueSheetExtraction
{
    public required string SheetName { get; init; }
    public required List<LlmProcessedData> ProcessedData { get; init; }
}

/// <summary>Aggregate result of a rescue pass across all sheets in a file.</summary>
public sealed class ImportRescueResult
{
    public required ImportRescueOutcome Outcome { get; init; }

    /// <summary>Populated when <see cref="Outcome"/> is <see cref="ImportRescueOutcome.Extracted"/>.</summary>
    public List<RescueSheetExtraction> Extractions { get; init; } = [];

    /// <summary>Meaningful only when <see cref="Outcome"/> is <see cref="ImportRescueOutcome.Rejected"/>.</summary>
    public ImportRescueRejectionReason ReasonCode { get; init; }

    /// <summary>
    /// Folds per-sheet rescue results into one outcome. If any sheet extracted entities the whole
    /// file is Extracted (empty/rejected sheets are dropped). Otherwise it is Rejected with the most
    /// common reason; a tie resolves to UnsupportedStructure, and no sheets at all to EmptyOrUnreadable.
    /// </summary>
    public static ImportRescueResult Aggregate(IReadOnlyList<RescueSheetResult> perSheet)
    {
        var extractions = perSheet
            .Where(s => s.EntityCount > 0)
            .Select(s => new RescueSheetExtraction { SheetName = s.SheetName, ProcessedData = s.ProcessedData })
            .ToList();

        if (extractions.Count > 0)
            return new ImportRescueResult { Outcome = ImportRescueOutcome.Extracted, Extractions = extractions };

        if (perSheet.Count == 0)
            return new ImportRescueResult
            {
                Outcome = ImportRescueOutcome.Rejected,
                ReasonCode = ImportRescueRejectionReason.EmptyOrUnreadable
            };

        // A sheet with no explicit reason (should not happen once extraction ran) defaults to UnsupportedStructure.
        var grouped = perSheet
            .Select(s => s.Reason ?? ImportRescueRejectionReason.UnsupportedStructure)
            .GroupBy(r => r)
            .Select(g => (Reason: g.Key, Count: g.Count()))
            .ToList();

        var maxCount = grouped.Max(g => g.Count);
        var top = grouped.Where(g => g.Count == maxCount).Select(g => g.Reason).ToList();
        var reasonCode = top.Count == 1 ? top[0] : ImportRescueRejectionReason.UnsupportedStructure;

        return new ImportRescueResult { Outcome = ImportRescueOutcome.Rejected, ReasonCode = reasonCode };
    }
}
