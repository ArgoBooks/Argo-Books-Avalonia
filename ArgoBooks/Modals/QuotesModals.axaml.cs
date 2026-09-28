using Avalonia.Controls;
using ArgoBooks.Controls;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

/// <summary>
/// Modal dialogs for writing, viewing, filtering and sending quotes.
/// The quote paper is the same document control the invoice editor uses.
/// ESC key handling is managed by ModalOverlay.
/// </summary>
public partial class QuotesModals : UserControl
{
    public QuotesModals()
    {
        InitializeComponent();

        if (this.FindControl<InvoicePreviewControl>("EditorPreview") is { } editorPreview)
            PaperEditorWiring.Attach(this, editorPreview);
    }

    private async Task CommitPaperEditsAsync()
    {
        if (this.FindControl<InvoicePreviewControl>("EditorPreview") is { } editorPreview)
            await editorPreview.CommitPendingEditsAsync();
    }

    // Flush the pending edit before previewing or saving, otherwise the re-render drops it.
    private async void OnPreviewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.ShowEditorPreviewCommand.Execute(null);
    }

    private async void OnSaveAsDraftClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        if (vm.SaveAsDraftCommand.CanExecute(null))
            vm.SaveAsDraftCommand.Execute(null);
    }
}
