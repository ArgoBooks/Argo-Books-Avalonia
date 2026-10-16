namespace ArgoBooks.Core.Services;

/// <summary>The content type for a file the app stores or uploads, worked out from its extension.</summary>
public static class MimeTypes
{
    public const string Fallback = "application/octet-stream";

    /// <summary>The content type for the file's extension, or <see cref="Fallback"/> for one not listed.</summary>
    public static string For(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            _ => Fallback
        };
}
