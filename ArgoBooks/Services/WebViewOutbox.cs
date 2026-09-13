using System.Text.Json;

namespace ArgoBooks.Services;

/// <summary>
/// A way for a page to answer C# on platforms where posting from JS does not arrive.
///
/// Avalonia.Controls.WebView 12.0.1 registers no WKScriptMessageHandler on macOS, so
/// window.webkit.messageHandlers is empty and every postMessage lands in its own catch. The
/// library does inject a bridge, but it is the Windows one, calling
/// window.chrome.webview.postMessage, and window.chrome does not exist there. InvokeScript
/// works in that direction, so the page queues what it would have posted and C# reads the
/// queue back on a timer.
///
/// The page decides which channel to use, once, at load: where a native bridge is present it
/// posts directly and never queues, so nothing is delivered twice and a fixed WebView needs no
/// change here.
/// </summary>
internal static class WebViewOutbox
{
    /// <summary>
    /// Defines window.__argoPost, which every page script should post through, plus
    /// window.__argoDrain for C# and window.__argoNative saying which channel was chosen.
    /// Safe to inject more than once.
    /// </summary>
    public const string Script = """
(function(){
    if (window.__argoPost) return;

    var handlers = (window.webkit && window.webkit.messageHandlers) ? window.webkit.messageHandlers : null;
    var native = !!(window.chrome && window.chrome.webview) || !!(handlers && Object.keys(handlers).length);

    window.__argoNative = native;
    window.__argoOutbox = [];

    window.__argoDrain = function() {
        var pending = window.__argoOutbox;
        window.__argoOutbox = [];
        return JSON.stringify(pending);
    };

    window.__argoPost = function(msg) {
        if (native) {
            try { window.chrome.webview.postMessage(msg); return; } catch(e) {}
            try { window.webkit.messageHandlers.webview.postMessage(msg); return; } catch(e) {}
        }
        // An edit or a zoom step is small, but a drain that stops running should still not
        // grow without bound. Dropping the oldest keeps it capped.
        if (window.__argoOutbox.length > 400) window.__argoOutbox.shift();
        window.__argoOutbox.push(msg);
    };
})();
""";

    /// <summary>Asks the page whether it chose the native bridge.</summary>
    public const string NativeProbe = "window.__argoNative === true";

    /// <summary>Takes everything queued since the last call.</summary>
    public const string Drain = "window.__argoDrain ? window.__argoDrain() : '[]'";

    /// <summary>
    /// Reads the JSON array the page hands back. InvokeScript returns the JS value already
    /// JSON encoded, and it has been observed handing back the array bare; a platform that
    /// quotes it one level deeper has to be read too, because getting that wrong yields zero
    /// messages and looks exactly like postMessage not arriving.
    ///
    /// Anything unreadable counts as "no messages" rather than throwing: a drain runs on a
    /// timer beside live work, and throwing would take down the thing it is reporting on.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            using var document = JsonDocument.Parse(raw);

            if (document.RootElement.ValueKind != JsonValueKind.String)
                return ReadStringArray(document.RootElement);

            var inner = document.RootElement.GetString();
            if (string.IsNullOrWhiteSpace(inner))
                return [];

            using var innerDocument = JsonDocument.Parse(inner);
            return ReadStringArray(innerDocument.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return [];

        var values = new List<string>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } value)
                values.Add(value);
        }

        return values;
    }
}
