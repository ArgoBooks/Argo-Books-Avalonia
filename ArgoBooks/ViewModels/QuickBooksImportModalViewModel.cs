using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO.Compression;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the QuickBooks import modal. It collects the exported reports;
/// App.EventWiring hands each one to the spreadsheet importer.
/// </summary>
public partial class QuickBooksImportModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// QuickBooks Online and QuickBooks Desktop export through different menus,
    /// so the steps are written out twice and this picks which set is shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDesktopSelected))]
    private bool _isOnlineSelected = true;

    public bool IsDesktopSelected => !IsOnlineSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles))]
    [NotifyPropertyChangedFor(nameof(FileSummary))]
    private int _fileCount;

    public bool HasFiles => FileCount > 0;

    public string FileSummary => FileCount == 1
        ? "1 file ready".Translate()
        : "{0} files ready".TranslateFormat(FileCount);

    /// <summary>
    /// Everything the person has chosen so far. Several reports make up one
    /// import, so this is a list rather than a single path.
    /// </summary>
    public ObservableCollection<QuickBooksFileRow> Files { get; } = [];

    public QuickBooksImportModalViewModel()
    {
        Files.CollectionChanged += OnFilesChanged;
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => FileCount = Files.Count;

    /// <summary>Where zips were unpacked, so the copies can be removed once they are done with.</summary>
    private readonly List<string> _unpacked = [];

    private static readonly string[] ReportExtensions = [".xlsx", ".xls", ".csv"];

    public static bool IsAccepted(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || ReportExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Deletes the reports unpacked from zips. Call once the import has finished with them.</summary>
    public void DiscardUnpacked()
    {
        foreach (var folder in _unpacked)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception)
            {
                // Left for the system's temp cleanup.
            }
        }

        _unpacked.Clear();
    }

    [RelayCommand]
    private void Open()
    {
        DiscardUnpacked();
        Files.Clear();
        IsOnlineSelected = true;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ShowOnline() => IsOnlineSelected = true;

    [RelayCommand]
    private void ShowDesktop() => IsOnlineSelected = false;

    [RelayCommand]
    private void ChooseFiles() => FilesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Adds a chosen export, ignoring one that is already in the list. Picking the
    /// same folder twice is easy to do when the files were exported one at a time.
    /// </summary>
    public void AddFile(string path)
    {
        if (!IsAccepted(path)) return;

        // QuickBooks Online hands over its lists as one zip, so take the reports out of it.
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            AddZip(path);
            return;
        }

        if (Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Files.Add(new QuickBooksFileRow(System.IO.Path.GetFileName(path), Describe(path), path));
    }

    private void AddZip(string zipPath)
    {
        try
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ArgoBooks-quickbooks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            _unpacked.Add(folder);

            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                // Name only, so an entry cannot write outside the folder.
                var name = System.IO.Path.GetFileName(entry.FullName);
                if (!ReportExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var target = System.IO.Path.Combine(folder, name);
                entry.ExtractToFile(target, overwrite: true);
                AddFile(target);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            App.ErrorLogger?.LogWarning($"Could not open a QuickBooks zip: {ex.Message}", "Import");
        }
    }

    /// <summary>
    /// The line under a file name. CSV carries a caution, because QuickBooks reports
    /// keep their heading and subtotal rows in a CSV and those can be read as data.
    /// </summary>
    private static string Describe(string path)
    {
        var detail = string.Empty;

        try
        {
            var bytes = new FileInfo(path).Length;
            detail = bytes >= 1024 * 1024
                ? "{0} MB".TranslateFormat(Math.Round(bytes / 1024d / 1024d, 1))
                : "{0} KB".TranslateFormat(Math.Max(1, bytes / 1024));
        }
        catch (Exception)
        {
            // A size is decoration; a file we cannot stat can still be imported.
        }

        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var caution = "An Excel export of this report reads more reliably".Translate();
            detail = detail.Length > 0 ? $"{detail} · {caution}" : caution;
        }

        return detail;
    }

    [RelayCommand]
    private void RemoveFile(QuickBooksFileRow? row)
    {
        if (row is not null)
        {
            Files.Remove(row);
        }
    }

    [RelayCommand]
    private void Continue() => ImportRequested?.Invoke(this, EventArgs.Empty);

    #region Events

    /// <summary>Raised when the person asks to pick files.</summary>
    public event EventHandler? FilesRequested;

    /// <summary>Raised when the person is ready to import.</summary>
    public event EventHandler? ImportRequested;

    #endregion
}

/// <summary>One chosen export file, as the list shows it.</summary>
public sealed class QuickBooksFileRow(string name, string detail, string path)
{
    public string Name { get; } = name;

    public string Detail { get; } = detail;

    public string Path { get; } = path;
}
