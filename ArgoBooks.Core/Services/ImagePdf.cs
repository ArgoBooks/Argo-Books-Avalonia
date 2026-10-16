using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArgoBooks.Core.Services;

/// <summary>A PDF with one picture filling each page, for scans and photos that something downstream only reads as a PDF.</summary>
public static class ImagePdf
{
    /// <summary>A JPEG and the size of the page it fills, in points.</summary>
    public sealed record Page(byte[] Jpeg, float WidthPoints, float HeightPoints);

    public static byte[] Create(IReadOnlyList<Page> pages)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document =>
        {
            foreach (var page in pages)
            {
                document.Page(p =>
                {
                    p.Size(new PageSize(page.WidthPoints, page.HeightPoints));
                    p.Margin(0);
                    p.Content().Image(page.Jpeg).FitArea();
                });
            }
        }).GeneratePdf();
    }
}
