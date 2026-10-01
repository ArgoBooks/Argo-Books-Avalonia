using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the QuickBooks import modal.
///
/// User interface only for the moment. Choosing files and continuing raise
/// events that nothing listens to yet, so the modal can be opened and looked
/// at without an importer behind it.
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

    [RelayCommand]
    private void Open()
    {
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
        if (string.IsNullOrWhiteSpace(path)) return;

        if (Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Files.Add(new QuickBooksFileRow(System.IO.Path.GetFileName(path), Describe(path), path));
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

    /// <summary>Raised when the person asks to pick files. Not handled yet.</summary>
    public event EventHandler? FilesRequested;

    /// <summary>Raised when the person is ready to import. Not handled yet.</summary>
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
