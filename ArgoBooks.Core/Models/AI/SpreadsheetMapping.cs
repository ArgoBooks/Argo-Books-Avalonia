using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.AI;

/// <summary>
/// The processing tier for a sheet during AI import.
/// </summary>
public enum ProcessingTier
{
    /// <summary>
    /// Simple column renaming: headers are mapped 1:1 and processed deterministically.
    /// </summary>
    Tier1_Mapping,

    /// <summary>
    /// Complex transformation: rows are sent to the LLM in chunks for normalization.
    /// </summary>
    Tier2_LlmProcessing
}

/// <summary>
/// Result of LLM analysis of a spreadsheet file.
/// Contains per-sheet analysis with detected types, column mappings, and processing tier.
/// </summary>
public class SpreadsheetAnalysisResult
{
    /// <summary>
    /// Analysis results for each sheet/CSV in the file.
    /// </summary>
    public List<SheetAnalysis> Sheets { get; set; } = [];

    /// <summary>
    /// Set when part of the AI analysis failed and some sheets were skipped, so the user can be
    /// told their import is partial instead of silently missing sheets. Null when analysis was complete.
    /// </summary>
    public string? PartialAnalysisWarning { get; set; }

    /// <summary>
    /// The file name that was analyzed (for display purposes).
    /// </summary>
    public string FileName { get; set; } = string.Empty;
}

/// <summary>
/// Analysis result for a single worksheet or CSV file.
/// </summary>
public class SheetAnalysis
{
    /// <summary>
    /// Original sheet name from the source file.
    /// </summary>
    public string SourceSheetName { get; set; } = string.Empty;

    /// <summary>
    /// The detected Argo Books entity type for this sheet.
    /// </summary>
    public SpreadsheetSheetType DetectedType { get; set; }

    /// <summary>
    /// Confidence score (0.0-1.0) for the detected entity type.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Which processing tier the LLM recommends for this sheet.
    /// </summary>
    public ProcessingTier Tier { get; set; }

    /// <summary>
    /// Explanation of why Tier 2 was chosen (empty for Tier 1).
    /// </summary>
    public string TierReason { get; set; } = string.Empty;

    /// <summary>
    /// Column mappings from source columns to target columns (Tier 1 only).
    /// </summary>
    public List<ColumnMapping> ColumnMappings { get; set; } = [];

    /// <summary>
    /// Source columns that could not be mapped to any target column.
    /// </summary>
    public List<string> UnmappedSourceColumns { get; set; } = [];

    /// <summary>
    /// Target columns that have no corresponding source column.
    /// </summary>
    public List<string> UnmappedTargetColumns { get; set; } = [];

    /// <summary>
    /// Whether this sheet should be included in the import.
    /// </summary>
    public bool IsIncluded { get; set; } = true;

    /// <summary>
    /// Total number of data rows detected in this sheet.
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// When non-null, this sheet could not be matched to a supported Argo Books entity type
    /// and will be excluded from import. The value is a human-readable explanation.
    /// </summary>
    public string? UnsupportedReason { get; set; }
}

/// <summary>
/// Maps a single source column to a target column.
/// </summary>
public class ColumnMapping
{
    /// <summary>
    /// The column name in the source spreadsheet.
    /// </summary>
    public string SourceColumn { get; set; } = string.Empty;

    /// <summary>
    /// The target column name expected by Argo Books import.
    /// </summary>
    public string TargetColumn { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score (0.0-1.0) for this column mapping.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Optional hint about data transformation needed (e.g., "date format DD/MM/YYYY").
    /// </summary>
    public string? TransformHint { get; set; }
}

/// <summary>
/// Result of LLM Tier 2 processing for a chunk of rows.
/// </summary>
public class LlmProcessedData
{
    /// <summary>
    /// The entity type these processed entities represent.
    /// </summary>
    public SpreadsheetSheetType EntityType { get; set; }

    /// <summary>
    /// Raw JSON elements for each normalized entity.
    /// </summary>
    public List<JsonElement> Entities { get; set; } = [];

    /// <summary>
    /// Number of source rows that were processed to produce these entities.
    /// </summary>
    public int SourceRowsProcessed { get; set; }

    /// <summary>
    /// Warnings generated during processing.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Source rows (their cell text) that produced nothing because the AI call for them failed,
    /// so the import can report them instead of losing them.
    /// </summary>
    public List<string> FailedRows { get; set; } = [];
}
