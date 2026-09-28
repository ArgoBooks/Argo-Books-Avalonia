using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Controls;

/// <summary>
/// Routes the editable paper's events to the editor behind it (the owner's DataContext), the same
/// way for invoices and quotes.
/// </summary>
internal static class PaperEditorWiring
{
    public static void Attach(Control owner, InvoicePreviewControl paper)
    {
        IPaperDocumentEditor? Editor() => owner.DataContext as IPaperDocumentEditor;

        // Every action below re-renders the paper from the model, so whatever the user just typed,
        // still sitting in the page inside the input debounce (a rate typed right before "+ add
        // line"), is flushed into the model first or it would be lost.
        async void Run(Action<IPaperDocumentEditor> action)
        {
            if (Editor() is not { } editor) return;
            await paper.CommitPendingEditsAsync();
            action(editor);
        }

        paper.InvoiceEdited += (_, e) => Editor()?.ApplyPaperEdit(e.Field, e.Index, e.Value);
        paper.TotalsModeToggled += (_, which) => Run(ed => ed.ToggleTotalsMode(which));
        paper.ProductPicked += (_, e) => Run(ed => ed.SelectProductForLine(e.Index, e.ProductId));
        paper.CreateProductRequested += (_, index) => Run(ed => ed.CreateProductForLine(index));
        paper.AddLineRequested += (_, _) => Run(ed => ed.AddLineFromPaper());
        paper.RemoveLineRequested += (_, index) => Run(ed => ed.RemoveLineFromPaper(index));
        paper.CustomerPicked += (_, customerId) => Run(ed => ed.SelectCustomerFromPaper(customerId));
        paper.CreateCustomerRequested += (_, _) => Run(ed => ed.CreateCustomerFromPaper());
        paper.DateEdited += (_, e) => Run(ed => ed.SetDateFromPaper(e.Field, e.Value));
        paper.DeleteLogoRequested += (_, _) => Run(ed => ed.DeleteLogoFromPaper());
        paper.PickLogoRequested += async (_, _) =>
        {
            if (Editor() is not { } editor) return;
            // Flushed while the web view is still active, before the picker takes focus.
            await paper.CommitPendingEditsAsync();
            await PickLogoAsync(owner, editor.SetLogoFromPaper);
        };
    }

    // The logo is embedded as base64 on the templates, which invoices and quotes share.
    private static async Task PickLogoAsync(Visual owner, Action<string> setLogo)
    {
        var top = TopLevel.GetTopLevel(owner);
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
            var decodable = Helpers.ImageFileLoader.TryMakeDecodable(raw.ToArray());
            if (decodable == null)
                return;

            setLogo(DownscaleToBase64(decodable));
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
