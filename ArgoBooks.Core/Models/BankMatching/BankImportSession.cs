using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models.BankMatching;

/// <summary>
/// A persisted bank statement import. Stored in the .argo file so that match
/// progress survives reload.
/// </summary>
public class BankImportSession : IRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("importedAt")]
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sourceFileName")]
    public string SourceFileName { get; set; } = string.Empty;

    [JsonPropertyName("lines")]
    public List<BankStatementLine> Lines { get; set; } = [];
}
