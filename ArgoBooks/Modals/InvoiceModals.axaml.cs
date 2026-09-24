using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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

        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
        {
            editorPreview.InvoiceEdited += OnInvoiceEdited;
            editorPreview.ProductPicked += OnProductPicked;
            editorPreview.CreateProductRequested += OnCreateProductRequested;
            editorPreview.AddLineRequested += OnAddLineRequested;
            editorPreview.RemoveLineRequested += OnRemoveLineRequested;
            editorPreview.CustomerPicked += OnCustomerPicked;
            editorPreview.CreateCustomerRequested += OnCreateCustomerRequested;
            editorPreview.DateEdited += OnDateEdited;
            editorPreview.PickLogoRequested += OnPickLogoRequested;
            editorPreview.DeleteLogoRequested += OnDeleteLogoRequested;
            editorPreview.TotalsModeToggled += OnTotalsModeToggled;
        }
    }

    // Flush any value the user just typed on the paper into the model before an action re-renders the
    // paper. The paper-action commands below all trigger a full paper re-render (RegeneratePaper),
    // which rebuilds the WebView from the model; without flushing first, a value still sitting in the
    // DOM within the ~150ms input debounce (e.g. a rate typed right before clicking "+add line") would
    // be dropped. Same guard the Preview/Save/info-button handlers already use.
    private async Task CommitPaperEditsAsync()
    {
        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
            await editorPreview.CommitPendingEditsAsync();
    }

    private async void OnTotalsModeToggled(object? sender, string which)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.ToggleTotalsMode(which);
    }

    private async void OnDeleteLogoRequested(object? sender, EventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.DeleteLogoFromPaper();
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

    // The sidebar info messageboxes hide and re-show the native WebView, which re-navigates the
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

    private async void OnCustomerPicked(object? sender, string customerId)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.SelectCustomerFromPaper(customerId);
    }

    private async void OnCreateCustomerRequested(object? sender, EventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.CreateCustomerFromPaper();
    }

    private async void OnDateEdited(object? sender, (string Field, string Value) e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.SetDateFromPaper(e.Field, e.Value);
    }

    // Let the user pick a logo image from the invoice paper; embed it as base64 on the template.
    private async void OnPickLogoRequested(object? sender, EventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        // Flush pending edits while the WebView is still active, before the native picker takes focus.
        await CommitPaperEditsAsync();
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a logo",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.heic", "*.heif" }
                }
            }
        });

        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var raw = new MemoryStream();
            await stream.CopyToAsync(raw);

            // HEIC cannot be decoded by Skia, so it is converted rather than dropped.
            var decodable = ArgoBooks.Helpers.ImageFileLoader.TryMakeDecodable(raw.ToArray());
            if (decodable == null)
                return;

            vm.SetLogoFromPaper(DownscaleToBase64(decodable));
        }
        catch
        {
            // Ignore unreadable/oversized images; the user can pick another.
        }
    }

    // Downscale an uploaded logo so it renders crisply without bloating the invoice (and its base64).
    private const int MaxLogoDimension = 300;

    private static string DownscaleToBase64(byte[] imageBytes)
    {
        try
        {
            using var input = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(input);
            var size = bitmap.PixelSize;
            var longest = Math.Max(size.Width, size.Height);
            if (longest <= MaxLogoDimension)
                return Convert.ToBase64String(imageBytes);

            var scale = (double)MaxLogoDimension / longest;
            var target = new PixelSize(
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale)));

            using var scaled = bitmap.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
            using var output = new MemoryStream();
            scaled.Save(output, PngBitmapEncoderOptions.Default);
            return Convert.ToBase64String(output.ToArray());
        }
        catch
        {
            // If decoding/scaling fails, fall back to the original bytes.
            return Convert.ToBase64String(imageBytes);
        }
    }

    private async void OnAddLineRequested(object? sender, EventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.AddLineFromPaper();
    }

    private async void OnRemoveLineRequested(object? sender, int index)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.RemoveLineFromPaper(index);
    }

    // Route an edit made directly on the invoice paper back into the view-model.
    private void OnInvoiceEdited(object? sender, InvoiceEditEventArgs e)
    {
        if (DataContext is InvoiceModalsViewModel vm)
            vm.ApplyPaperEdit(e.Field, e.Index, e.Value);
    }

    private async void OnProductPicked(object? sender, ProductPickEventArgs e)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.SelectProductForLine(e.Index, e.ProductId);
    }

    private async void OnCreateProductRequested(object? sender, int index)
    {
        if (DataContext is not InvoiceModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.CreateProductForLine(index);
    }
}
