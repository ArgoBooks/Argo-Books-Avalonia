using System.Text;
using ArgoBooks.Core.Models;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for reading and writing .argo file footers.
///
/// Footer structure (at end of file):
/// [Footer JSON bytes][Footer length (4 bytes, little-endian)][Magic bytes "ARGO"]
/// </summary>
public class FooterService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Reads the footer from an .argo file.
    /// </summary>
    /// <param name="filePath">Path to the .argo file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file footer, or null if invalid.</returns>
    public async Task<FileFooter?> ReadFooterAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var fileStream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

        return await ReadFooterFromStreamAsync(fileStream, cancellationToken);
    }

    /// <summary>
    /// Reads the footer from a stream.
    /// </summary>
    /// <param name="stream">Stream to read from (must be seekable).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file footer, or null if invalid.</returns>
    public async Task<FileFooter?> ReadFooterFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        // Minimum footer size: magic (4) + length (4) + minimal JSON (~20)
        if (stream.Length < 28)
            return null;

        // Read magic bytes and length from end of file
        stream.Seek(-8, SeekOrigin.End);
        var trailer = new byte[8];
        await stream.ReadExactlyAsync(trailer, cancellationToken);

        // Verify magic bytes
        var magic = trailer[4..8];
        if (!magic.SequenceEqual(FileFormatConstants.MagicBytes))
            return null;

        // Read footer length (little-endian)
        var footerLength = BitConverter.ToInt32(trailer, 0);
        if (footerLength <= 0 || footerLength > stream.Length - 8)
            return null;

        // Read footer JSON
        stream.Seek(-(8 + footerLength), SeekOrigin.End);
        var footerBytes = new byte[footerLength];
        await stream.ReadExactlyAsync(footerBytes, cancellationToken);

        try
        {
            var footerJson = Encoding.UTF8.GetString(footerBytes);
            return JsonSerializer.Deserialize<FileFooter>(footerJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a footer to a stream.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    /// <param name="footer">Footer to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteFooterAsync(Stream stream, FileFooter footer, CancellationToken cancellationToken = default)
    {
        var footerJson = JsonSerializer.Serialize(footer, JsonOptions);
        var footerBytes = Encoding.UTF8.GetBytes(footerJson);

        // Write footer JSON
        await stream.WriteAsync(footerBytes, cancellationToken);

        // Write footer length (little-endian)
        var lengthBytes = BitConverter.GetBytes(footerBytes.Length);
        await stream.WriteAsync(lengthBytes, cancellationToken);

        await stream.WriteAsync(FileFormatConstants.MagicBytes, cancellationToken);
    }

    /// <summary>
    /// Gets the content length from a stream (excluding footer).
    /// </summary>
    /// <param name="stream">Stream to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Content length in bytes, or -1 if invalid.</returns>
    public async Task<long> GetContentLengthFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek || stream.Length < 28)
            return -1;

        // Read length from trailer
        stream.Seek(-8, SeekOrigin.End);
        var trailer = new byte[8];
        await stream.ReadExactlyAsync(trailer, cancellationToken);

        // Verify magic bytes
        var magic = trailer[4..8];
        if (!magic.SequenceEqual(FileFormatConstants.MagicBytes))
            return -1;

        var footerLength = BitConverter.ToInt32(trailer, 0);
        if (footerLength <= 0 || footerLength > stream.Length - 8)
            return -1;

        // Content length = total - footer - length bytes - magic bytes
        return stream.Length - footerLength - 8;
    }

    /// <summary>
    /// Reads the content portion of a file (excluding footer).
    /// </summary>
    /// <param name="filePath">Path to the .argo file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory stream containing the content.</returns>
    public async Task<MemoryStream> ReadContentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var fileStream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

        var contentLength = await GetContentLengthFromStreamAsync(fileStream, cancellationToken);
        if (contentLength < 0)
            throw new InvalidDataException("Invalid file format.");

        // Read content
        fileStream.Seek(0, SeekOrigin.Begin);
        var contentStream = new MemoryStream();

        // Copy only the content portion
        var buffer = new byte[81920];
        var remaining = contentLength;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
                break;
            await contentStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }

        contentStream.Position = 0;
        return contentStream;
    }
}
