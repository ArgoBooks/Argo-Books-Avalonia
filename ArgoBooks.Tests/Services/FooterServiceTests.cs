using System.Text;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the FooterService class.
/// </summary>
public class FooterServiceTests
{
    private readonly FooterService _footerService = new();

    #region WriteFooter / ReadFooter Roundtrip Tests

    [Fact]
    public async Task WriteFooterAsync_ReadFooterFromStreamAsync_RoundTrip_RestoresFooter()
    {
        var footer = new FileFooter
        {
            Version = "2.0.0",
            IsEncrypted = false,
            CompanyName = "Test Company",
            Accountants = ["Alice", "Bob"]
        };

        using var stream = new MemoryStream();

        // Write some content first, then the footer
        var content = "some file content"u8.ToArray();
        await stream.WriteAsync(content);

        await _footerService.WriteFooterAsync(stream, footer);

        // Reset stream to beginning for reading
        stream.Position = 0;

        var readFooter = await _footerService.ReadFooterFromStreamAsync(stream);

        Assert.NotNull(readFooter);
        Assert.Equal("2.0.0", readFooter.Version);
        Assert.False(readFooter.IsEncrypted);
        Assert.Equal("Test Company", readFooter.CompanyName);
        Assert.Equal(2, readFooter.Accountants.Count);
        Assert.Equal("Alice", readFooter.Accountants[0]);
        Assert.Equal("Bob", readFooter.Accountants[1]);
    }

    [Fact]
    public async Task WriteFooterAsync_ReadFooterFromStreamAsync_EncryptedFooter_RoundTrip()
    {
        var footer = new FileFooter
        {
            Version = "1.0.0",
            IsEncrypted = true,
            Salt = Convert.ToBase64String(new byte[16]),
            PasswordHash = Convert.ToBase64String(new byte[32]),
            Iv = Convert.ToBase64String(new byte[12]),
            CompanyName = "Encrypted Corp",
            BiometricEnabled = true
        };

        using var stream = new MemoryStream();
        var content = new byte[100];
        await stream.WriteAsync(content);

        await _footerService.WriteFooterAsync(stream, footer);
        stream.Position = 0;

        var readFooter = await _footerService.ReadFooterFromStreamAsync(stream);

        Assert.NotNull(readFooter);
        Assert.True(readFooter.IsEncrypted);
        Assert.NotNull(readFooter.Salt);
        Assert.NotNull(readFooter.PasswordHash);
        Assert.NotNull(readFooter.Iv);
        Assert.Equal("Encrypted Corp", readFooter.CompanyName);
        Assert.True(readFooter.BiometricEnabled);
    }

    [Fact]
    public async Task WriteFooterAsync_MinimalFooter_RoundTrip()
    {
        var footer = new FileFooter();

        using var stream = new MemoryStream();
        var content = new byte[50];
        await stream.WriteAsync(content);

        await _footerService.WriteFooterAsync(stream, footer);
        stream.Position = 0;

        var readFooter = await _footerService.ReadFooterFromStreamAsync(stream);

        Assert.NotNull(readFooter);
        Assert.Equal("1.0.0", readFooter.Version);
        Assert.False(readFooter.IsEncrypted);
    }

    #endregion

    #region GetContentLength Tests

    [Fact]
    public async Task GetContentLengthFromStreamAsync_ReturnsCorrectContentLength()
    {
        var contentBytes = Encoding.UTF8.GetBytes("Hello, this is the content portion!");
        var footer = new FileFooter { CompanyName = "Content Test" };

        using var stream = new MemoryStream();
        await stream.WriteAsync(contentBytes);

        var contentEndPosition = stream.Position;
        await _footerService.WriteFooterAsync(stream, footer);

        stream.Position = 0;

        var contentLength = await _footerService.GetContentLengthFromStreamAsync(stream);

        Assert.Equal(contentEndPosition, contentLength);
    }

    [Fact]
    public async Task GetContentLengthFromStreamAsync_EmptyContent_ReturnsZero()
    {
        var footer = new FileFooter { CompanyName = "Empty Content" };

        using var stream = new MemoryStream();
        // Write footer directly without any preceding content
        await _footerService.WriteFooterAsync(stream, footer);
        stream.Position = 0;

        var contentLength = await _footerService.GetContentLengthFromStreamAsync(stream);

        Assert.Equal(0L, contentLength);
    }

    [Fact]
    public async Task GetContentLengthFromStreamAsync_LargeContent_ReturnsCorrectLength()
    {
        var largeContent = new byte[10000];
        new Random(42).NextBytes(largeContent);
        var footer = new FileFooter { CompanyName = "Large Content" };

        using var stream = new MemoryStream();
        await stream.WriteAsync(largeContent);
        await _footerService.WriteFooterAsync(stream, footer);
        stream.Position = 0;

        var contentLength = await _footerService.GetContentLengthFromStreamAsync(stream);

        Assert.Equal(10000L, contentLength);
    }

    #endregion

    #region Invalid File Handling Tests

    [Fact]
    public async Task ReadFooterFromStreamAsync_TooSmallStream_ReturnsNull()
    {
        using var stream = new MemoryStream(new byte[10]);

        var footer = await _footerService.ReadFooterFromStreamAsync(stream);

        Assert.Null(footer);
    }

    [Fact]
    public async Task ReadFooterFromStreamAsync_NoMagicBytes_ReturnsNull()
    {
        // Create a stream with enough data but no ARGO magic bytes
        using var stream = new MemoryStream(new byte[100]);

        var footer = await _footerService.ReadFooterFromStreamAsync(stream);

        Assert.Null(footer);
    }

    [Fact]
    public async Task ReadFooterFromStreamAsync_NonSeekableStream_ThrowsArgumentException()
    {
        await using var nonSeekable = new NonSeekableStream();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _footerService.ReadFooterFromStreamAsync(nonSeekable));
    }

    [Fact]
    public async Task GetContentLengthFromStreamAsync_InvalidStream_ReturnsNegativeOne()
    {
        using var stream = new MemoryStream(new byte[10]);

        var contentLength = await _footerService.GetContentLengthFromStreamAsync(stream);

        Assert.Equal(-1L, contentLength);
    }

    [Fact]
    public async Task GetContentLengthFromStreamAsync_NoMagicBytes_ReturnsNegativeOne()
    {
        using var stream = new MemoryStream(new byte[100]);

        var contentLength = await _footerService.GetContentLengthFromStreamAsync(stream);

        Assert.Equal(-1L, contentLength);
    }

    [Fact]
    public async Task ReadFooterFromStreamAsync_CorruptedFooterJson_ReturnsNull()
    {
        // Manually create a stream with correct magic bytes but invalid JSON
        using var stream = new MemoryStream();

        // Write some content
        var content = new byte[50];
        await stream.WriteAsync(content);

        // Write corrupt "footer JSON"
        var corruptJson = Encoding.UTF8.GetBytes("{{{not valid json");
        await stream.WriteAsync(corruptJson);

        // Write footer length
        var lengthBytes = BitConverter.GetBytes(corruptJson.Length);
        await stream.WriteAsync(lengthBytes);

        // Write magic bytes "ARGO"
        await stream.WriteAsync(FileFormatConstants.MagicBytes);

        stream.Position = 0;

        var footer = await _footerService.ReadFooterFromStreamAsync(stream);

        Assert.Null(footer);
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// A stream wrapper that does not support seeking.
    /// </summary>
    private class NonSeekableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
