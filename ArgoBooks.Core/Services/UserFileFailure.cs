using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Reports a file the user chose that could not be read. A damaged, password-protected, outdated
/// or moved file is a fact about that file, so it is a warning. Any other exception from reading it
/// is a fault in our reader and stays an error.
/// </summary>
public static class UserFileFailure
{
    // Matched by name so Core needs no reference to every library that can throw one.
    private static readonly HashSet<string> UnreadableFileExceptions =
    [
        "InvalidDataException",        // System.IO: a damaged zip, which every .xlsx and .argo is
        "FileFormatException",         // System.IO.Packaging: not a valid Office package
        "OpenXmlPackageException",     // DocumentFormat.OpenXml: not a valid Office package
        "EncryptedDocumentException",  // NPOI: a password-protected .xls
        "OldExcelFormatException",     // NPOI: Excel 95 or older
        "NotOLE2FileException",        // NPOI: not really an .xls, often a renamed HTML or CSV export
        "OfficeXmlFileException",      // NPOI: an .xlsx renamed to .xls
        "FileNotFoundException",       // the file was moved or deleted after it was chosen
        "DirectoryNotFoundException",
    ];

    /// <summary>True when <paramref name="ex"/> says the file itself cannot be read.</summary>
    public static bool IsUnreadableFile(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (UnreadableFileExceptions.Contains(e.GetType().Name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Files <paramref name="ex"/> as a warning with <paramref name="code"/> when the file is at fault,
    /// and through <c>LogError</c> otherwise, which also turns a locked or protected file into a warning of its own.
    /// </summary>
    public static void Report(IErrorLogger? errorLogger, Exception ex, string context, ErrorCategory category, string code)
    {
        if (errorLogger == null || ex is OperationCanceledException)
        {
            return;
        }

        if (IsUnreadableFile(ex))
        {
            ErrorLogger.MarkReported(ex);
            // The type name, not the message, so a path or file name in it is never uploaded.
            errorLogger.LogWarning($"{context} ({ex.GetType().Name})", context, category, code);
            return;
        }

        errorLogger.LogError(ex, category, context);
    }
}
