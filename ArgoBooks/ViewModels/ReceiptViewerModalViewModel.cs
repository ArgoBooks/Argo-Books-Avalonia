using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the receipt viewer modal.
///
/// Also shows generated documents that are not receipts, such as the Record of Employment
/// worksheet, through <see cref="ShowDocument"/>. They share everything that matters here (page
/// streaming, zoom, fullscreen, download) and differ only in where the bytes come from and
/// whether deleting makes sense, so a second viewer would have been the same code twice.
/// </summary>
public partial class ReceiptViewerModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _receiptId = string.Empty;

    /// <summary>
    /// Set when showing a generated document rather than a stored receipt. Held so Download can
    /// write the original PDF instead of a rendered page image.
    /// </summary>
    private byte[]? _documentBytes;

    private string _documentFileName = string.Empty;

    /// <summary>
    /// False for a generated document. There is nothing to delete: it is not stored anywhere,
    /// and it is rebuilt from the books every time it is opened.
    /// </summary>
    [ObservableProperty]
    private bool _canDelete = true;

    [ObservableProperty]
    private string _transactionType = string.Empty;

    /// <summary>Empty while the receipt has no linked transaction, which hides the switch action.</summary>
    public string SwitchTargetType => TransactionType switch
    {
        "Revenue" => "Expense",
        "Expense" => "Revenue",
        _ => string.Empty
    };

    public bool CanSwitchType => SwitchTargetType.Length > 0;

    partial void OnTransactionTypeChanged(string value)
    {
        OnPropertyChanged(nameof(SwitchTargetType));
        OnPropertyChanged(nameof(CanSwitchType));
    }

    /// <summary>
    /// The documents this viewer can page between, when it was opened on a set rather than a
    /// single file. Empty for a receipt or a one-off document.
    /// </summary>
    public ObservableCollection<ViewerDocument> Documents { get; } = new();

    /// <summary>True when there is a set to pick from, which is what shows the picker.</summary>
    public bool HasDocumentSet => Documents.Count > 1;

    /// <summary>
    /// The document on screen. Changing it renders that one and only that one, which is the
    /// whole point: a hundred stubs cost the same to open as one.
    /// </summary>
    [ObservableProperty]
    private ViewerDocument? _selectedDocument;

    partial void OnSelectedDocumentChanged(ViewerDocument? value)
    {
        if (value != null)
        {
            _ = ShowSelectedDocumentAsync(value);
        }
    }

    /// <summary>
    /// What to call the thing on screen while it loads or fails. The viewer shows generated
    /// documents as well as receipts, and a Record of Employment announcing itself as a
    /// receipt is just wrong.
    /// </summary>
    [ObservableProperty]
    private string _loadingMessage = "Loading receipt...";

    [ObservableProperty]
    private string _emptyMessage = "Receipt preview not available";

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _isLoadingPages;

    /// <summary>Resolved page image paths for the current receipt (one entry per PDF page).</summary>
    public ObservableCollection<string> ReceiptPages { get; } = new();

    /// <summary>True when there is nothing to show and no render in progress.</summary>
    public bool HasNoPages => ReceiptPages.Count == 0 && !IsLoadingPages;

    // Guards against a stale async render finishing after a newer Show / Close.
    private int _renderToken;

    // Parallels ReceiptPages, tracking each page's index so streamed pages stay in page order.
    private readonly List<int> _pageOrder = new();

    public ReceiptViewerModalViewModel()
    {
        ReceiptPages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoPages));
    }

    partial void OnIsLoadingPagesChanged(bool value) => OnPropertyChanged(nameof(HasNoPages));

    /// <summary>
    /// Shows the receipt viewer modal for the given receipt, resolving and rendering all of its
    /// pages asynchronously.
    /// </summary>
    /// <param name="receiptId">ID of the receipt to display.</param>
    /// <param name="title">Optional custom title.</param>
    public void Show(string receiptId, string? title = null)
    {
        ReceiptId = receiptId;
        TransactionType = App.CompanyManager?.CompanyData?.Receipts
            .FirstOrDefault(r => r.Id == receiptId && !string.IsNullOrEmpty(r.TransactionId))?.TransactionType ?? string.Empty;
        _documentBytes = null;
        ClearDocumentSet();
        _documentFileName = string.Empty;
        CanDelete = true;
        LoadingMessage = "Loading receipt...";
        EmptyMessage = "Receipt preview not available";
        Title = title ?? $"Receipt for {receiptId}";
        IsFullscreen = false;
        ReceiptPages.Clear();
        IsOpen = true;
        _ = LoadPagesAsync(receiptId);
    }

    /// <summary>
    /// Shows a generated PDF that is not a stored receipt, using the same page streaming, zoom
    /// and fullscreen as a receipt.
    /// </summary>
    /// <param name="title">Shown in the header.</param>
    /// <param name="pdfBytes">The document itself, kept so Download saves the PDF not a page.</param>
    /// <param name="fileName">Suggested name when saving, and the basis of the cache file name.</param>
    public void ShowDocument(string title, byte[] pdfBytes, string fileName)
    {
        ReceiptId = string.Empty;
        _documentBytes = pdfBytes;
        ClearDocumentSet();
        _documentFileName = fileName;
        CanDelete = false;
        LoadingMessage = "Loading document...";
        EmptyMessage = "Document preview not available";
        Title = title;
        IsFullscreen = false;
        ReceiptPages.Clear();
        IsOpen = true;
        _ = LoadDocumentPagesAsync(pdfBytes, fileName);
    }

    /// <summary>
    /// Shows a set of related documents with a picker, rendering only the one selected.
    ///
    /// Built for per-employee output: pay stubs now, and the T4, RL-1 and record of employment
    /// slips have the same shape. The alternative, one combined document, has to compose and
    /// rasterise every page before showing anything, which at a hundred employees is a long wait
    /// for a scroll bar nobody can navigate.
    /// </summary>
    /// <param name="title">Shown in the header, describing the set rather than one item.</param>
    /// <param name="documents">The set. A single item simply shows without a picker.</param>
    public void ShowDocumentSet(string title, IEnumerable<ViewerDocument> documents)
    {
        ReceiptId = string.Empty;
        _documentBytes = null;
        _documentFileName = string.Empty;
        CanDelete = false;
        LoadingMessage = "Loading document...";
        EmptyMessage = "Document preview not available";
        Title = title;
        IsFullscreen = false;
        ReceiptPages.Clear();

        Documents.Clear();
        foreach (ViewerDocument document in documents)
        {
            Documents.Add(document);
        }

        OnPropertyChanged(nameof(HasDocumentSet));

        IsOpen = true;

        // Assigning this renders it, through OnSelectedDocumentChanged.
        SelectedDocument = Documents.FirstOrDefault();
    }

    /// <summary>
    /// Renders one document from the set.
    ///
    /// The render token is taken before the bytes are produced, not after, so a slow render for
    /// someone the user has already clicked away from cannot paint its pages over the newer
    /// selection.
    /// </summary>
    private async Task ShowSelectedDocumentAsync(ViewerDocument document)
    {
        int token = ++_renderToken;

        _pageOrder.Clear();
        ReceiptPages.Clear();
        IsLoadingPages = true;

        try
        {
            byte[] bytes = await document.LoadAsync();

            if (token != _renderToken)
            {
                return;
            }

            // Held so the download button saves this document rather than a rendered page.
            _documentBytes = bytes;
            _documentFileName = document.FileName;

            var progress = new Progress<(int Index, string Path)>(page =>
            {
                if (token != _renderToken)
                    return;
                InsertPageOrdered(page.Index, page.Path);
            });

            await ReceiptPageRenderer.GetPagePathsAsync(document.FileName, bytes, progress);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Viewer.ShowDocument");
        }
        finally
        {
            if (token == _renderToken)
                IsLoadingPages = false;
        }
    }

    private async Task LoadDocumentPagesAsync(byte[] pdfBytes, string fileName)
    {
        var token = ++_renderToken;
        _pageOrder.Clear();

        IsLoadingPages = true;
        try
        {
            var progress = new Progress<(int Index, string Path)>(page =>
            {
                if (token != _renderToken)
                    return;
                InsertPageOrdered(page.Index, page.Path);
            });

            await ReceiptPageRenderer.GetPagePathsAsync(fileName, pdfBytes, progress);
        }
        finally
        {
            if (token == _renderToken)
                IsLoadingPages = false;
        }
    }

    private async Task LoadPagesAsync(string receiptId)
    {
        var token = ++_renderToken;
        _pageOrder.Clear();

        var receipt = App.CompanyManager?.CompanyData?.Receipts.FirstOrDefault(r => r.Id == receiptId);
        if (receipt == null || string.IsNullOrEmpty(receipt.FileData))
            return;

        IsLoadingPages = true;
        try
        {
            // Stream pages into the view as each finishes rendering. Progress callbacks run on the
            // UI thread (captured here); a stale render is ignored via the token check.
            var progress = new Progress<(int Index, string Path)>(page =>
            {
                if (token != _renderToken)
                    return;
                InsertPageOrdered(page.Index, page.Path);
            });

            await ReceiptPageRenderer.GetPagePathsAsync(receipt, progress);
        }
        finally
        {
            if (token == _renderToken)
                IsLoadingPages = false;
        }
    }

    // Inserts a streamed page so ReceiptPages stays sorted by page index regardless of arrival order.
    private void InsertPageOrdered(int index, string path)
    {
        var pos = _pageOrder.Count;
        for (var i = 0; i < _pageOrder.Count; i++)
        {
            if (_pageOrder[i] > index)
            {
                pos = i;
                break;
            }
        }
        _pageOrder.Insert(pos, index);
        ReceiptPages.Insert(pos, path);
    }

    [RelayCommand]
    private async Task SwitchType()
    {
        if (string.IsNullOrEmpty(ReceiptId)) return;

        if (await Services.ReceiptTypeSwitchService.SwitchAsync(ReceiptId))
        {
            TransactionType = SwitchTargetType;
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (string.IsNullOrEmpty(ReceiptId)) return;

        try
        {
            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null) return;

            var receipt = companyData.Receipts.FirstOrDefault(r => r.Id == ReceiptId);
            if (receipt == null) return;

            var dialog = App.ConfirmationDialog;
            if (dialog == null) return;

            var isLinked = !string.IsNullOrEmpty(receipt.TransactionId);
            var message = "Are you sure you want to delete this receipt?".Translate();
            if (isLinked)
            {
                message += "\n\n" + "This receipt is linked to a {0} transaction ({1}). The receipt will be removed from the transaction.".TranslateFormat(
                    receipt.TransactionType, receipt.TransactionId);
            }

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Receipt".Translate(),
                Message = message,
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;

            // Unlink from transaction if linked
            string? linkedTransactionId = null;
            string? linkedTransactionType = null;
            if (isLinked)
            {
                linkedTransactionId = receipt.TransactionId;
                linkedTransactionType = receipt.TransactionType;

                if (receipt.TransactionType == "Expense")
                {
                    var expense = companyData.Expenses.FirstOrDefault(e => e.Id == receipt.TransactionId);
                    if (expense != null) expense.ReceiptId = null;
                }
                else if (receipt.TransactionType == "Revenue")
                {
                    var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == receipt.TransactionId);
                    if (revenue != null) revenue.ReceiptId = null;
                }
            }

            companyData.Receipts.Remove(receipt);

            // Record undo/redo action
            var deletedReceipt = receipt;
            var action = new DelegateAction(
                $"Delete receipt {deletedReceipt.Id}",
                () =>
                {
                    companyData.Receipts.Add(deletedReceipt);
                    // Re-link transaction
                    if (linkedTransactionType == "Expense")
                    {
                        var expense = companyData.Expenses.FirstOrDefault(e => e.Id == linkedTransactionId);
                        if (expense != null) expense.ReceiptId = deletedReceipt.Id;
                    }
                    else if (linkedTransactionType == "Revenue")
                    {
                        var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == linkedTransactionId);
                        if (revenue != null) revenue.ReceiptId = deletedReceipt.Id;
                    }
                },
                () =>
                {
                    companyData.Receipts.Remove(deletedReceipt);
                    if (linkedTransactionType == "Expense")
                    {
                        var expense = companyData.Expenses.FirstOrDefault(e => e.Id == linkedTransactionId);
                        if (expense != null) expense.ReceiptId = null;
                    }
                    else if (linkedTransactionType == "Revenue")
                    {
                        var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == linkedTransactionId);
                        if (revenue != null) revenue.ReceiptId = null;
                    }
                });

            App.UndoRedoManager.RecordAction(action);
            App.CompanyManager?.MarkAsChanged();

            Close();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "ReceiptViewer.Delete");
        }
    }

    /// <summary>Saves the generated PDF itself, not a rendered page image.</summary>
    private async Task DownloadDocumentAsync()
    {
        byte[]? bytes = _documentBytes;
        if (bytes == null) return;

        try
        {
            var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel?.StorageProvider == null) return;

            var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save".Translate(),
                SuggestedFileName = _documentFileName,
                DefaultExtension = "pdf",
                FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }]
            });

            if (result == null) return;

            await File.WriteAllBytesAsync(result.Path.LocalPath, bytes);
            App.AddNotification("Success", "Saved successfully", NotificationType.Success);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "ReceiptViewer.DownloadDocument");
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        IsFullscreen = false;
        _renderToken++; // cancel any in-flight page render
        IsLoadingPages = false;
        ReceiptPages.Clear();
        _pageOrder.Clear();
        ReceiptId = string.Empty;
        Title = string.Empty;
        _documentBytes = null;
        _documentFileName = string.Empty;
        CanDelete = true;
        ClearDocumentSet();
    }

    /// <summary>
    /// Drops the set without re-rendering. The selection is cleared first and silently, because
    /// the change handler would otherwise try to render whatever it landed on next.
    /// </summary>
    private void ClearDocumentSet()
    {
        if (Documents.Count == 0 && SelectedDocument == null)
        {
            return;
        }

        _renderToken++;
        SelectedDocument = null;
        Documents.Clear();
        OnPropertyChanged(nameof(HasDocumentSet));
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    [RelayCommand]
    private async Task Download()
    {
        if (_documentBytes != null)
        {
            await DownloadDocumentAsync();
            return;
        }

        if (string.IsNullOrEmpty(ReceiptId)) return;

        try
        {
            // Always download the original receipt file (e.g. the source PDF), not a rendered page.
            var receipt = App.CompanyManager?.CompanyData?.Receipts
                .FirstOrDefault(r => r.Id == ReceiptId);
            if (receipt?.FileData == null) return;

            var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel?.StorageProvider == null) return;

            // Determine file extension from the original file name
            var sourceExtension = Path.GetExtension(receipt.FileName);
            if (string.IsNullOrEmpty(sourceExtension))
                sourceExtension = ".png";

            var filters = new[]
            {
                new FilePickerFileType("Receipt file") { Patterns = [$"*{sourceExtension}"] }
            };

            var suggestedName = $"Receipt_{ReceiptId}{sourceExtension}";

            var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Receipt",
                SuggestedFileName = suggestedName,
                FileTypeChoices = filters,
                DefaultExtension = sourceExtension.TrimStart('.')
            });

            if (result != null)
            {
                var destinationPath = result.Path.LocalPath;
                var bytes = Convert.FromBase64String(receipt.FileData);
                await File.WriteAllBytesAsync(destinationPath, bytes);
                App.AddNotification("Success", "Receipt saved successfully", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            await (App.ConfirmationDialog?.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Error",
                Message = $"Failed to save receipt: {ex.Message}",
                PrimaryButtonText = "OK",
                CancelButtonText = null
            }) ?? Task.CompletedTask);
        }
    }
}
