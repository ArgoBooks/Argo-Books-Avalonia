using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The fallback channel the renderer page uses to answer.
///
/// Avalonia.Controls.WebView 12.0.1 registers no WKScriptMessageHandler on macOS, so nothing
/// the page posts is delivered and every PDF preview timed out. InvokeScript works in that
/// direction, so the page queues what it would have posted and C# reads the queue back.
///
/// InvokeScript hands back the JS return value already JSON encoded, and the exact shape it
/// arrives in is the part worth pinning: it was observed returning the array as bare JSON,
/// but a platform that wraps it one level deeper would silently produce zero messages and
/// look exactly like the bug this replaced.
/// </summary>
public class PdfOutboxTests
{
    [Fact]
    public void ParseOutbox_WithBareJsonArray_ReadsEveryMessage()
    {
        var messages = WebViewOutbox.Parse(
            """["pdfjs-ready","render-page:0:data:image/jpeg;base64,AAAA","render-done:1"]""");

        Assert.Equal(
            ["pdfjs-ready", "render-page:0:data:image/jpeg;base64,AAAA", "render-done:1"],
            messages);
    }

    [Fact]
    public void ParseOutbox_WithArrayWrappedInAJsonString_ReadsEveryMessage()
    {
        // The same payload one level deeper, as a platform quoting the return value gives it.
        var messages = WebViewOutbox.Parse("\"[\\\"pdfjs-ready\\\",\\\"render-done:2\\\"]\"");

        Assert.Equal(["pdfjs-ready", "render-done:2"], messages);
    }

    [Fact]
    public void ParseOutbox_WithEmptyQueue_ReturnsNothing()
    {
        Assert.Empty(WebViewOutbox.Parse("[]"));
        Assert.Empty(WebViewOutbox.Parse("\"[]\""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("undefined")]
    [InlineData("{\"not\":\"an array\"}")]
    [InlineData("[1, 2, 3]")]
    public void ParseOutbox_WithAnythingUnusable_ReturnsNothingRatherThanThrowing(string? raw)
    {
        // A drain runs on a timer while a render is in flight. Throwing here would take out
        // the render that was about to succeed, so anything unreadable counts as "no messages".
        Assert.Empty(WebViewOutbox.Parse(raw));
    }

    [Fact]
    public void ParseOutbox_WithMixedElementTypes_KeepsOnlyTheStrings()
    {
        Assert.Equal(["render-done:0"], WebViewOutbox.Parse("""[null, 7, "render-done:0", {}]"""));
    }

    [Fact]
    public void RendererHtml_QueuesWhatItPostsFromTheStart()
    {
        var html = PdfThumbnailService.RendererHtml;

        // pdf.js reports ready during load, long before C# could switch the queue on, so a
        // queue that started disabled would miss the one message the handshake waits for.
        Assert.Contains("window.__outboxEnabled = true;", html);
        Assert.Contains("window.__drainOutbox", html);

        // Readiness is also exposed as state, because the post announcing it cannot be
        // waited for by anything that starts listening afterwards.
        Assert.Contains("window.__pdfjsReady = true;", html);
    }
}
