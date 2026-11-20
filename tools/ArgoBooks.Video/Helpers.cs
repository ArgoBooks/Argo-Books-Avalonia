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

    /// <summary>Prints every piece of text on screen with its position, so a take can be scripted from what the app really shows.</summary>
    public static void Dump(Window window, string title)
    {
        Console.WriteLine($"--- {title}");
        foreach (var block in window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && TextOf(t).Length > 0 && OnScreen(window, t)))
        {
            var at = CentreOf(window, block);
            Console.WriteLine($"{at.X,5:0} {at.Y,5:0}  {TextOf(block)}");
        }
    }

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

    /// <summary>Prints what the open company really holds, so an import can be checked against the totals of its files.</summary>
    public static void Audit(string title)
    {
        var data = App.CompanyManager!.CompanyData!;
        Console.WriteLine($"=== {title}: {data.Customers.Count} customers, {data.Suppliers.Count} suppliers, {data.Products.Count} products, " +
                          $"{data.Revenues.Count} revenues {data.Revenues.Sum(r => r.Total):0.00}, {data.Expenses.Count} expenses {data.Expenses.Sum(e => e.Total):0.00}, " +
                          $"{data.Invoices.Count} invoices {data.Invoices.Sum(i => i.Total):0.00}, owed {data.Invoices.Sum(i => i.Balance):0.00}");
    }

    /// <summary>Runs the app's background currency conversion now, rather than on its timer.</summary>
    public static Task FinishConversionsAsync() =>
        (Task)typeof(App).GetMethod("TryProcessPendingConversionsAsync", Any)!.Invoke(null, null)!;

    /// <summary>A view model the app shell owns, by the name of its property.</summary>
    public static object Shell(string property)
    {
        var shell = typeof(App).GetField("_appShellViewModel", Any)!.GetValue(null)!;
        return shell.GetType().GetProperty(property, Any)!.GetValue(shell)!;
    }

    public static bool IsOpen(object dialog) => (bool)dialog.GetType().GetProperty("IsOpen", Any)!.GetValue(dialog)!;

    public static void SetProperty(object target, string property, object value) => Set(target, property, value);

    /// <summary>Gives the window a file picker that answers at once, since a window with no desktop has none.</summary>
    public static void AnswerFilePickers(Window window, string saveAs, string? open = null)
    {
        var field = typeof(TopLevel).GetFields(Any).First(f => f.FieldType == typeof(IStorageProvider));
        field.SetValue(window, AnsweringStorage.Over(window.StorageProvider, saveAs, open));
    }

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
        return Wrap(path);
    }

    /// <summary>Without a desktop the window's storage provider finds nothing, so Avalonia's own file class is built directly.</summary>
    public static IStorageFile Wrap(string path)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile"))
            .First(t => t != null)!;
        return (IStorageFile)Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [new FileInfo(path)], null)!;
    }
}

/// <summary>
/// A file picker that picks by itself: saving always goes to the one path it was given, and opening picks the one file it
/// was given. It is a proxy because Avalonia does not let outside code implement the interface directly.
/// </summary>
internal class AnsweringStorage : DispatchProxy
{
    private IStorageProvider _inner = null!;
    private string _saveAs = "";
    private string? _open;

    public static IStorageProvider Over(IStorageProvider inner, string saveAs, string? open = null)
    {
        var proxy = Create<IStorageProvider, AnsweringStorage>();
        var self = (AnsweringStorage)(object)proxy;
        self._inner = inner;
        self._saveAs = saveAs;
        self._open = open;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IStorageProvider.OpenFilePickerAsync) && _open != null)
            return Task.FromResult<IReadOnlyList<IStorageFile>>([LocalFile.Wrap(_open)]);
        if (method.Name != nameof(IStorageProvider.SaveFilePickerAsync))
            return method.Invoke(_inner, args);
        File.WriteAllBytes(_saveAs, []);
        return Task.FromResult<IStorageFile?>(LocalFile.Wrap(_saveAs));
    }
}
