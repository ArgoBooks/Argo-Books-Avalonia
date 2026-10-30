using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ArgoBooks.Core.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace ArgoBooks.Video;

/// <summary>Finds things on screen the way a person would: by the words on them.</summary>
internal static class Ui
{
    public static string TextOf(TextBlock block) =>
        block.Text ?? (block.Inlines == null ? "" : string.Concat(block.Inlines.OfType<Run>().Select(r => r.Text)));

    /// <summary>The front-most visible piece of text that reads exactly this, or null.</summary>
    public static TextBlock? FindText(Window window, string text) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .LastOrDefault(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && TextOf(t) == text && OnScreen(window, t));

    public static TextBlock Text(Window window, string text) =>
        FindText(window, text) ?? throw new InvalidOperationException($"Nothing on screen says \"{text}\"");

    public static bool Shows(Window window, string text) => FindText(window, text) != null;

    public static Point CentreOf(Window window, Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("Not on screen");

    /// <summary>A point a little in from the left of a label, where a hand lands on a button or field.</summary>
    public static Point LeftOf(Window window, Visual visual, double inset = 0.3) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width * inset, visual.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("Not on screen");

    private static bool OnScreen(Window window, Visual visual)
    {
        var centre = visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window);
        return centre is { } p && new Rect(window.ClientSize).Contains(p);
    }
}

/// <summary>
/// Makes the sample company look like an ordinary business on camera. Only what is drawn changes;
/// nothing is saved.
/// </summary>
internal static class AppHooks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static void PresentAsBusiness(string name)
    {
        var main = typeof(App).GetProperty("MainWindowViewModel", Any)!.GetValue(null)!;
        Set(main, "SampleBannerDismissed", true);
        Set(main, "CurrentCompanyName", name);
        var shell = typeof(App).GetField("_appShellViewModel", Any)!.GetValue(null)!;
        shell.GetType().GetMethod("SetCompanyInfo", Any)!.Invoke(shell, [name, null, null]);
    }

    /// <summary>Hides the dashboard's sample company greeting and the sales tax reminder its data sets off.</summary>
    public static void HideSampleNotices(Window window)
    {
        foreach (var block in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (Ui.TextOf(block).StartsWith("You're exploring a sample company", StringComparison.Ordinal))
                block.Text = "Welcome back! Here is an overview of your business.";
        }
        foreach (var banner in window.GetVisualDescendants().OfType<Control>().Where(c => c.GetType().Name == "InfoBanner"))
        {
            if (banner.GetType().GetProperty("Message")?.GetValue(banner) is string message
                && message.Contains("sales tax", StringComparison.OrdinalIgnoreCase))
                banner.IsVisible = false;
        }
    }

    /// <summary>Puts the scan cache in front of the receipt scanner, so a retake replays the first real scan.</summary>
    public static void UseScanCache(string folder)
    {
        var vm = App.ReceiptsModalsViewModel!;
        var live = (IReceiptScannerService)vm.GetType().GetMethod("CreateScannerService", Any)!.Invoke(vm, null)!;
        vm.GetType().GetField("_scannerService", Any)!.SetValue(vm, new CachedScanner(live, folder));
    }

    /// <summary>The prompt that explains an import before its file is picked.</summary>
    public static object ImportPrompt()
    {
        var shell = typeof(App).GetField("_appShellViewModel", Any)!.GetValue(null)!;
        return shell.GetType().GetProperty("ImportFilePromptModalViewModel", Any)!.GetValue(shell)!;
    }

    /// <summary>Closes the prompt the way its Cancel does, so the import it was asked for steps back quietly.</summary>
    public static void CancelImportPrompt(object prompt) =>
        ((System.Windows.Input.ICommand)prompt.GetType().GetProperty("CancelCommand", Any)!.GetValue(prompt)!).Execute(null);

    private static void Set(object target, string property, object value) =>
        target.GetType().GetProperty(property, Any)!.SetValue(target, value);
}

/// <summary>
/// Scans for real the first time a receipt is seen and keeps the result, keyed by the image, so
/// retakes neither wait on nor use up the monthly scans.
/// </summary>
internal sealed class CachedScanner(IReceiptScannerService live, string folder) : IReceiptScannerService
{
    private sealed record Saved(long ElapsedMs, ReceiptScanResult Result);

    public bool IsConfigured => true;

    public Task<ReceiptScanResult> ScanReceiptAsync(byte[] imageData, string fileName, CancellationToken cancellationToken = default) =>
        ScanReceiptAsync(imageData, fileName, false, cancellationToken);

    public async Task<ReceiptScanResult> ScanReceiptAsync(byte[] imageData, string fileName, bool skipPreprocessing, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(imageData))[..16] + ".json");
        if (File.Exists(path))
        {
            var saved = JsonSerializer.Deserialize<Saved>(await File.ReadAllTextAsync(path, cancellationToken))!;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(saved.ElapsedMs, 4000)), cancellationToken);
            Console.WriteLine("scan: replayed from the cache");
            return saved.Result;
        }

        var clock = Stopwatch.StartNew();
        var result = await live.ScanReceiptAsync(imageData, fileName, skipPreprocessing, cancellationToken);
        Console.WriteLine($"scan: live, {clock.ElapsedMilliseconds} ms, success {result.IsSuccess}");
        if (result.IsSuccess)
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Saved(clock.ElapsedMilliseconds, result)), cancellationToken);
        return result;
    }
}

/// <summary>A file on disk in the shape a drag and drop delivers it.</summary>
internal static class LocalFile
{
    public static async Task<IStorageFile> OpenAsync(Window window, string path)
    {
        path = Path.GetFullPath(path);
        if (await window.StorageProvider.TryGetFileFromPathAsync(path) is { } file)
            return file;
        // Without a desktop the window's storage provider finds nothing, so Avalonia's own file class is built directly.
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile"))
            .First(t => t != null)!;
        return (IStorageFile)Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [new FileInfo(path)], null)!;
    }
}
