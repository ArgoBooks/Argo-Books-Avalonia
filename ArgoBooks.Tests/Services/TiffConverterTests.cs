using System.Text;
using System.Text.RegularExpressions;
using ArgoBooks.Core.Services;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class TiffConverterTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private enum PageKind { Colour, BlackAndWhite, Preview }

    /// <summary>
    /// A TIFF the way scanners write one: colour pages compressed with LZW, black and white pages
    /// with the fax compression office scanners use, and optionally a small preview entry.
    /// </summary>
    private string WriteTiff(params PageKind[] pages)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scan-{Guid.NewGuid():N}.tif");
        _files.Add(path);

        using var tiff = Tiff.Open(path, "w");
        foreach (var kind in pages)
        {
            var (width, height) = kind == PageKind.Preview ? (34, 44) : (340, 440);
            tiff.SetField(TiffTag.IMAGEWIDTH, width);
            tiff.SetField(TiffTag.IMAGELENGTH, height);
            tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tiff.SetField(TiffTag.ROWSPERSTRIP, height);
            tiff.SetField(TiffTag.XRESOLUTION, 100f);
            tiff.SetField(TiffTag.YRESOLUTION, 100f);
            tiff.SetField(TiffTag.RESOLUTIONUNIT, ResUnit.INCH);
            tiff.SetField(TiffTag.SUBFILETYPE, kind == PageKind.Preview ? FileType.REDUCEDIMAGE : FileType.PAGE);

            if (kind == PageKind.BlackAndWhite)
            {
                tiff.SetField(TiffTag.BITSPERSAMPLE, 1);
                tiff.SetField(TiffTag.SAMPLESPERPIXEL, 1);
                tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISWHITE);
                tiff.SetField(TiffTag.COMPRESSION, Compression.CCITTFAX4);
                var row = new byte[(width + 7) / 8];
                for (var y = 0; y < height; y++)
                {
                    // Black across the top half, white below.
                    Array.Fill(row, y < height / 2 ? (byte)0xFF : (byte)0x00);
                    tiff.WriteScanline(row, y);
                }
            }
            else
            {
                tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
                tiff.SetField(TiffTag.SAMPLESPERPIXEL, 3);
                tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
                tiff.SetField(TiffTag.COMPRESSION, Compression.LZW);
                var row = new byte[width * 3];
                for (var y = 0; y < height; y++)
                {
                    // Red on the left half, blue on the right.
                    for (var x = 0; x < width; x++)
                    {
                        row[x * 3] = (byte)(x < width / 2 ? 255 : 0);
                        row[x * 3 + 1] = 0;
                        row[x * 3 + 2] = (byte)(x < width / 2 ? 0 : 255);
                    }
                    tiff.WriteScanline(row, y);
                }
            }

            tiff.WriteDirectory();
        }

        return path;
    }

    private string Convert(string tiffPath)
    {
        var converted = TiffConverter.ConvertToTempFile(tiffPath);
        _files.Add(converted);
        return converted;
    }

    [Fact]
    public void AOnePageTiff_BecomesAJpegOfTheSamePicture()
    {
        var jpeg = Convert(WriteTiff(PageKind.Colour));

        Assert.Equal(".jpg", Path.GetExtension(jpeg));
        using var bitmap = SKBitmap.Decode(jpeg);
        Assert.Equal(340, bitmap.Width);
        Assert.Equal(440, bitmap.Height);
        var left = bitmap.GetPixel(40, 200);
        var right = bitmap.GetPixel(300, 200);
        Assert.True(left.Red > 200 && left.Blue < 60, $"left was {left}");
        Assert.True(right.Blue > 200 && right.Red < 60, $"right was {right}");
    }

    [Fact]
    public void ABlackAndWhiteFaxCompressedPage_IsReadTheRightWayUp()
    {
        var jpeg = Convert(WriteTiff(PageKind.BlackAndWhite));

        using var bitmap = SKBitmap.Decode(jpeg);
        Assert.True(bitmap.GetPixel(170, 50).Red < 60, "the top half should be black");
        Assert.True(bitmap.GetPixel(170, 390).Red > 200, "the bottom half should be white");
    }

    [Fact]
    public void AMultiPageTiff_BecomesAPdfWithAPagePerPage_LeavingOutThePreview()
    {
        var pdf = Convert(WriteTiff(PageKind.Colour, PageKind.Preview, PageKind.BlackAndWhite));

        Assert.Equal(".pdf", Path.GetExtension(pdf));
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
        Assert.StartsWith("%PDF", text);
        Assert.Equal("2", Regex.Match(text, @"/Type\s*/Pages\b.*?/Count\s+(\d+)", RegexOptions.Singleline).Groups[1].Value);
    }

    [Fact]
    public void TheConvertedFile_KeepsTheTiffsName()
    {
        var tiff = WriteTiff(PageKind.Colour);

        Assert.Equal(Path.GetFileNameWithoutExtension(tiff) + ".jpg", Path.GetFileName(Convert(tiff)));
    }

    [Fact]
    public void AFileThatIsNotATiff_IsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fake-{Guid.NewGuid():N}.tif");
        _files.Add(path);
        File.WriteAllText(path, "not a tiff");

        Assert.Throws<InvalidDataException>(() => TiffConverter.ConvertToTempFile(path));
    }

    [Theory]
    [InlineData("scan.tif", true)]
    [InlineData("scan.TIFF", true)]
    [InlineData("scan.jpg", false)]
    [InlineData(null, false)]
    public void TiffFiles_AreRecognisedByExtension(string? path, bool expected)
    {
        Assert.Equal(expected, TiffConverter.IsTiff(path));
    }
}
