using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArgoBooks.Core.Services;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class StatementImagesTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private string Write(string extension, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"statement-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        _files.Add(path);
        return path;
    }

    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.White);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    /// <summary>A JPEG stored on its side with an EXIF tag saying to turn it a quarter clockwise, as a phone saves an upright photo.</summary>
    private static byte[] SidewaysPhoneJpeg(int storedWidth, int storedHeight)
    {
        var jpeg = Jpeg(storedWidth, storedHeight);
        byte[] tiff = [0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, 0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        byte[] exif = [.. "Exif\0\0"u8.ToArray(), .. tiff];
        var length = exif.Length + 2;
        return [jpeg[0], jpeg[1], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. exif, .. jpeg[2..]];
    }

    private static string PdfText(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static (float Width, float Height) FirstPageSize(byte[] pdf)
    {
        var box = Regex.Match(PdfText(pdf), @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        Assert.True(box.Success, "the PDF should give its page size");
        return (float.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private static int PageCount(byte[] pdf) =>
        int.Parse(Regex.Match(PdfText(pdf), @"/Type\s*/Pages\b.*?/Count\s+(\d+)", RegexOptions.Singleline).Groups[1].Value);

    [Fact]
    public void APhoneStatementPhoto_BecomesOnePdfPage_TurnedUpright()
    {
        var pdf = StatementImages.ToPdf(Write(".jpg", SidewaysPhoneJpeg(storedWidth: 800, storedHeight: 600)));

        Assert.StartsWith("%PDF", PdfText(pdf));
        Assert.Equal(1, PageCount(pdf));
        var (width, height) = FirstPageSize(pdf);
        Assert.True(height > width, $"an upright statement is a portrait page, not {width} x {height}");
        Assert.True(height <= 792.5f && width <= 612.5f, $"the page fits Letter, not {width} x {height}");
    }

    [Fact]
    public void AMultiPageTiffScan_KeepsAPagePerPage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"statement-{Guid.NewGuid():N}.tif");
        _files.Add(path);
        using (var tiff = Tiff.Open(path, "w"))
        {
            for (var page = 0; page < 2; page++)
            {
                tiff.SetField(TiffTag.IMAGEWIDTH, 170);
                tiff.SetField(TiffTag.IMAGELENGTH, 220);
                tiff.SetField(TiffTag.BITSPERSAMPLE, 1);
                tiff.SetField(TiffTag.SAMPLESPERPIXEL, 1);
                tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISWHITE);
                tiff.SetField(TiffTag.COMPRESSION, Compression.CCITTFAX4);
                tiff.SetField(TiffTag.ROWSPERSTRIP, 220);
                tiff.SetField(TiffTag.XRESOLUTION, 20f);
                tiff.SetField(TiffTag.YRESOLUTION, 20f);
                tiff.SetField(TiffTag.RESOLUTIONUNIT, ResUnit.INCH);
                var row = new byte[(170 + 7) / 8];
                for (var y = 0; y < 220; y++)
                    tiff.WriteScanline(row, y);
                tiff.WriteDirectory();
            }
        }

        var pdf = StatementImages.ToPdf(path);

        Assert.Equal(2, PageCount(pdf));
        Assert.Equal((612f, 792f), FirstPageSize(pdf));
    }

    [Fact]
    public void AFileThatIsNotAPicture_IsRefused()
    {
        Assert.Throws<InvalidDataException>(() => StatementImages.ToPdf(Write(".jpg", "not a picture"u8.ToArray())));
    }

    [Theory]
    [InlineData("statement.jpg", true)]
    [InlineData("statement.HEIC", true)]
    [InlineData("statement.tiff", true)]
    [InlineData("statement.pdf", false)]
    [InlineData("statement.csv", false)]
    public void StatementPictures_AreRecognisedByExtension(string path, bool expected)
    {
        Assert.Equal(expected, StatementImages.IsImage(path));
    }
}
