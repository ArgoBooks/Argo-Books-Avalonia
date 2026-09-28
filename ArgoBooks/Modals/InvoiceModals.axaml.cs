using Avalonia.Controls;
using ArgoBooks.Controls;
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

    // The sidebar info dialogs hide and re-show the native WebView, which re-navigates the
    // paper. Flush any value the user just typed into the model first (while the WebView is still
    // active), otherwise the re-show reloads a stale paper and the edit is lost. The command then
    // rebuilds PreviewHtml from the flushed model while the WebView is hidden.
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
