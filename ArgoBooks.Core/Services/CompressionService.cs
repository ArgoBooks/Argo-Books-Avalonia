using System.Formats.Tar;
using System.IO.Compression;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for TAR archive creation/extraction and GZip compression.
/// </summary>
public class CompressionService
{
    /// <summary>
    /// Creates a TAR archive from a directory.
    /// </summary>
    /// <param name="sourceDirectory">Directory to archive.</param>
    /// <param name="includeBaseDirectory">Whether to include the base directory name in the archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory stream containing the TAR archive.</returns>
    public async Task<MemoryStream> CreateTarArchiveAsync(
        string sourceDirectory,
        bool includeBaseDirectory = true,
        CancellationToken cancellationToken = default)
    {
        var tarStream = new MemoryStream();

        // Use explicit disposal to ensure position is set after TarWriter finalizes
        var tarWriter = new TarWriter(tarStream, TarEntryFormat.Pax, leaveOpen: true);
        try
        {
            var basePath = includeBaseDirectory
                ? Path.GetDirectoryName(sourceDirectory) ?? sourceDirectory
                : sourceDirectory;

            await AddDirectoryToTarAsync(tarWriter, sourceDirectory, basePath, cancellationToken);
        }
        finally
        {
            await tarWriter.DisposeAsync();
        }

        tarStream.Position = 0;
        return tarStream;
    }

    private async Task AddDirectoryToTarAsync(
        TarWriter tarWriter,
        string directory,
        string basePath,
        CancellationToken cancellationToken)
    {
        // Add all files
        foreach (var filePath in Directory.GetFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Held open exclusively for as long as the company is, so it would fail the read below
            // as well as being meaningless inside a saved file.
            if (Path.GetFileName(filePath) == SecureTempDirectory.InUseFileName)
                continue;

            var entryName = Path.GetRelativePath(basePath, filePath).Replace('\\', '/');
            // PaxTarEntry does not own its DataStream, so dispose it ourselves. Use try/finally:
            // if WriteEntryAsync throws (cancellation, I/O error) the stream must still be closed,
            // otherwise the file handle leaks and can block deleting the temp directory afterwards.
            var dataStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
                {
                    DataStream = dataStream
                };

                await tarWriter.WriteEntryAsync(entry, cancellationToken);
            }
            finally
            {
                await dataStream.DisposeAsync();
            }
        }

        // Recursively add subdirectories
        foreach (var subDirectory in Directory.GetDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AddDirectoryToTarAsync(tarWriter, subDirectory, basePath, cancellationToken);
        }
    }

    /// <summary>
    /// Extracts a TAR archive to a directory.
    /// </summary>
    /// <param name="tarStream">Stream containing the TAR archive.</param>
    /// <param name="destinationDirectory">Directory to extract to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExtractTarArchiveAsync(
        Stream tarStream,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        // Ensure destination exists
        Directory.CreateDirectory(destinationDirectory);

        await using var tarReader = new TarReader(tarStream, leaveOpen: true);

        while (await tarReader.GetNextEntryAsync(true, cancellationToken) is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Sanitize entry name to prevent path traversal
            var entryName = SanitizeEntryName(entry.Name);
            if (string.IsNullOrEmpty(entryName))
                continue;

            var destinationPath = Path.Combine(destinationDirectory, entryName);

            // Ensure the destination is within the target directory
            var fullDestination = Path.GetFullPath(destinationPath);
            var fullTarget = Path.GetFullPath(destinationDirectory);
            if (!fullDestination.StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase))
                continue; // Skip entries that would escape the target directory

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(destinationPath);
                    break;

                case TarEntryType.RegularFile:
                    // Ensure parent directory exists
                    var parentDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(parentDir))
                        Directory.CreateDirectory(parentDir);

                    await entry.ExtractToFileAsync(destinationPath, overwrite: true, cancellationToken);
                    break;
            }
        }
    }

    /// <summary>
    /// Compresses data using GZip.
    /// </summary>
    /// <param name="inputStream">Stream to compress.</param>
    /// <param name="compressionLevel">Compression level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory stream containing compressed data.</returns>
    public async Task<MemoryStream> CompressGZipAsync(
        Stream inputStream,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        CancellationToken cancellationToken = default)
    {
        var compressedStream = new MemoryStream();

        await using (var gzipStream = new GZipStream(compressedStream, compressionLevel, leaveOpen: true))
        {
            await inputStream.CopyToAsync(gzipStream, cancellationToken);
        }

        compressedStream.Position = 0;
        return compressedStream;
    }

    /// <summary>
    /// Maximum decompressed size (4 GB) to prevent zip bomb attacks.
    /// Set high to accommodate company files with large numbers of receipt images.
    /// </summary>
    private const long MaxDecompressedSize = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Decompresses GZip data with a size limit to prevent zip bomb attacks.
    /// </summary>
    /// <param name="compressedStream">Stream containing compressed data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory stream containing decompressed data.</returns>
    /// <exception cref="InvalidDataException">Thrown if decompressed size exceeds the limit.</exception>
    public async Task<MemoryStream> DecompressGZipAsync(
        Stream compressedStream,
        CancellationToken cancellationToken = default)
    {
        var decompressedStream = new MemoryStream();

        await using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[81920];
            int bytesRead;
            long totalRead = 0;

            while ((bytesRead = await gzipStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > MaxDecompressedSize)
                {
                    throw new InvalidDataException(
                        $"Decompressed data exceeds the maximum allowed size of {MaxDecompressedSize / (1024 * 1024)} MB. The file may be corrupted.");
                }

                await decompressedStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }

        decompressedStream.Position = 0;
        return decompressedStream;
    }

    /// <summary>
    /// Extracts a GZip-compressed TAR archive to a directory, decompressing as it goes so the
    /// archive is never held in memory whole. Subject to the same size limit as
    /// <see cref="DecompressGZipAsync"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown if decompressed size exceeds the limit.</exception>
    public async Task ExtractGZipTarArchiveAsync(
        Stream compressedStream,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        await using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress, leaveOpen: true);
        await using var limitedStream = new SizeLimitedReadStream(gzipStream, MaxDecompressedSize);

        await ExtractTarArchiveAsync(limitedStream, destinationDirectory, cancellationToken);

        // The reader stops at the archive's end marker. Reading on to the end of the GZip data
        // checks its trailer, as decompressing it whole did.
        await limitedStream.CopyToAsync(Stream.Null, cancellationToken);
    }

    private sealed class SizeLimitedReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _totalRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _totalRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Counted(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Counted(inner.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Counted(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Counted(int bytesRead)
        {
            _totalRead += bytesRead;
            if (_totalRead > maxBytes)
            {
                throw new InvalidDataException(
                    $"Decompressed data exceeds the maximum allowed size of {maxBytes / (1024 * 1024)} MB. The file may be corrupted.");
            }
            return bytesRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Sanitizes a TAR entry name to prevent path traversal attacks.
    /// </summary>
    private static string SanitizeEntryName(string entryName)
    {
        // Remove leading slashes and dots
        entryName = entryName.TrimStart('/', '\\', '.');

        // Replace backslashes with forward slashes
        entryName = entryName.Replace('\\', '/');

        // Remove any ".." components
        var parts = entryName.Split('/');
        var sanitizedParts = parts.Where(p => p != ".." && p != ".").ToArray();

        return string.Join("/", sanitizedParts);
    }
}
