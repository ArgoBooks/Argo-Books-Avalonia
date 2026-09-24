using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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

        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
        {
            editorPreview.InvoiceEdited += OnQuoteEdited;
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

    // Flush whatever the user just typed on the paper into the model before anything re-renders it.
    // The paper actions below all rebuild the web view from the model; without this, a value still
    // sitting in the DOM inside the input debounce (a rate typed right before "+ add line") is lost.
    private async Task CommitPaperEditsAsync()
    {
        var editorPreview = this.FindControl<InvoicePreviewControl>("EditorPreview");
        if (editorPreview != null)
            await editorPreview.CommitPendingEditsAsync();
    }

    // Route an edit made directly on the quote paper back into the view model.
    private void OnQuoteEdited(object? sender, InvoiceEditEventArgs e)
    {
        if (DataContext is QuotesModalsViewModel vm)
            vm.ApplyPaperEdit(e.Field, e.Index, e.Value);
    }

    private async void OnTotalsModeToggled(object? sender, string which)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.ToggleTotalsMode(which);
    }

    private async void OnProductPicked(object? sender, ProductPickEventArgs e)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.SelectProductForLine(e.Index, e.ProductId);
    }

    private async void OnCreateProductRequested(object? sender, int index)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.CreateProductForLine(index);
    }

    private async void OnAddLineRequested(object? sender, EventArgs e)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.AddLineFromPaper();
    }

    private async void OnRemoveLineRequested(object? sender, int index)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.RemoveLineFromPaper(index);
    }

    private async void OnCustomerPicked(object? sender, string customerId)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.SelectCustomerFromPaper(customerId);
    }

    private async void OnCreateCustomerRequested(object? sender, EventArgs e)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.CreateCustomerFromPaper();
    }

    private async void OnDateEdited(object? sender, (string Field, string Value) e)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.SetDateFromPaper(e.Field, e.Value);
    }

    private async void OnDeleteLogoRequested(object? sender, EventArgs e)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        await CommitPaperEditsAsync();
        vm.DeleteLogoFromPaper();
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

    // Let the user pick a logo image from the quote paper; embed it as base64 on the templates,
    // which are shared with invoices, so the logo is a single company-wide choice.
    private async void OnPickLogoRequested(object? sender, EventArgs e)
    {
        if (DataContext is not QuotesModalsViewModel vm) return;
        // Flush pending edits while the web view is still active, before the picker takes focus.
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

    // Downscale an uploaded logo so it renders crisply without bloating the document (and its base64).
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
}
