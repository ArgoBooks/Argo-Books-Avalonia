using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ArgoBooks.Controls;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

/// <summary>
/// Modal dialogs for creating, editing, and filtering invoices.
/// Invoice preview and editing are handled by InvoicePreviewControl using NativeWebView.
/// </summary>
public partial class InvoiceModals : UserControl
{
    public InvoiceModals()
    {
        InitializeComponent();

        if (this.FindControl<InvoicePreviewControl>("EditorPreview") is { } editorPreview)
            PaperEditorWiring.Attach(this, editorPreview);
    }

    // Flush any value the user just typed on the paper into the model before previewing/saving,
    // otherwise the re-render would drop the last edit (e.g. a rate that hasn't posted yet).
    private async void OnPreviewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
            await editorPreview.CommitPendingEditsAsync();
        vm.ShowEditorPreviewCommand.Execute(null);
    }

    private async void OnSaveAsDraftClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
            await editorPreview.CommitPendingEditsAsync();
        if (vm.SaveAsDraftCommand.CanExecute(null))
            vm.SaveAsDraftCommand.Execute(null);
    }

    // Same as the info dialogs below: the company modal draws under the native web view unless the paper steps aside first.
    private async void OnAddCompanyDetailsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not InvoiceModalsViewModel vm) return;
        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
            await editorPreview.CommitPendingEditsAsync();
        vm.EditCompanyDetailsCommand.Execute(null);
    }

    private async void OnDownloadPdfClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        var preview = this.FindControl<InvoicePreviewControl>(vm.IsViewOnly ? "ViewPreview" : "EditorPreview");
        if (preview == null) return;

        try
        {
            // Flushed first so a figure the user is still typing is in the model as well as on the paper.
            if (!vm.IsViewOnly)
                await preview.CommitPendingEditsAsync();

            await using var pdf = await preview.RenderPdfAsync();
            if (pdf == null)
            {
                // Linux has no inline web view, so there is nothing rendered to print from.
                App.AddNotification(
                    "Download unavailable".Translate(),
                    "Open the invoice in your browser and print it to PDF from there.".Translate(),
                    NotificationType.Warning);
                return;
            }

            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (window?.StorageProvider == null) return;

            var saved = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Invoice PDF".Translate(),
                SuggestedFileName = vm.PdfFileName,
                DefaultExtension = "pdf",
                FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }]
            });
            if (saved == null) return;

            await using var file = System.IO.File.Create(saved.Path.LocalPath);
            await pdf.CopyToAsync(file);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Invoice.DownloadPdf");
            App.AddNotification(
                "Could not save the PDF".Translate(),
                ex.Message,
                NotificationType.Warning);
        }
    }

    // The sidebar info dialogs hide and re-show the native WebView, which re-navigates the paper.
    private async void OnProcessingFeeInfoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
            await editorPreview.CommitPendingEditsAsync();
        vm.ShowProcessingFeeInfoCommand.Execute(null);
    }

    private async void OnRecurringInfoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
            await editorPreview.CommitPendingEditsAsync();
        vm.ShowRecurringInfoCommand.Execute(null);
    }
}
