using ArgoBooks.Core.Models.BankMatching;

namespace ArgoBooks.Core.Services;

public interface IPdfStatementExtractor
{
    bool IsConfigured { get; }
    Task<StatementExtraction> ExtractAsync(byte[] pdfData, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>How the rows compare with the balances the statement prints: balanced, fixed, off or unchecked. The gap is the closing balance minus what the rows come to.</summary>
public sealed record StatementCheck(string Status, decimal? Gap);

/// <summary>The rows read from a statement, and the server's check of them, which is null when the server sent none.</summary>
public sealed record StatementExtraction(List<BankStatementLine> Lines, StatementCheck? Check);
