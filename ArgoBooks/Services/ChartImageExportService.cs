using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ArgoBooks.Controls;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.SKCharts;
using SkiaSharp;

namespace ArgoBooks.Services;

/// <summary>
/// Shared service for exporting charts as images.
/// </summary>
public static class ChartImageExportService
{
    /// <summary>
    /// Default width for exported charts when bounds are invalid.
    /// </summary>
    private const int DefaultExportWidth = 800;

    /// <summary>
    /// Default height for exported charts when bounds are invalid.
    /// </summary>
    private const int DefaultExportHeight = 400;

    /// <summary>
    /// The background to paint behind an exported chart, matching the app's chart card for the
    /// current theme (Themes/LightTheme.axaml and DarkTheme.axaml SurfaceColor). Exporting a
    /// transparent background made a light-theme chart look like a broken dark one, because image
    /// viewers render the transparent area as a dark canvas; a solid surface color matches the app.
    /// </summary>
    private static SKColor GetExportBackground()
        => ThemeService.Instance.IsDarkTheme ? SKColor.Parse("#1F2937") : SKColors.White;

    /// <summary>
    /// Encodes a rendered chart image onto a solid, theme-matching background and writes it to disk.
    /// LiveCharts' <c>SKChart.Background</c> is not reliably honored by <c>SaveImage</c> (it produced a
    /// transparent PNG that looked dark in image viewers), so we composite the chart onto an explicit
    /// background canvas to guarantee the backdrop matches the app.
    /// </summary>
    private static void SaveOnSolidBackground(SKImage chartImage, int width, int height, string filePath, SKEncodedImageFormat format)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(GetExportBackground());
        surface.Canvas.DrawImage(chartImage, 0, 0);
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(format, 100);
        File.WriteAllBytes(filePath, data.ToArray());
    }

    /// <summary>
    /// Saves a CartesianChart as an image file with a file picker dialog.
    /// </summary>
    /// <param name="topLevel">The top-level control for the file picker.</param>
    /// <param name="chart">The chart to save.</param>
    /// <param name="suggestedFileName">Suggested file name (without extension).</param>
    /// <returns>True if saved successfully, false if cancelled or failed.</returns>
    public static async Task<bool> SaveChartAsImageAsync(TopLevel topLevel, CartesianChart chart, string suggestedFileName)
    {
        var filePath = await ShowSaveFilePickerAsync(topLevel, suggestedFileName);
        if (filePath == null) return false;

        try
        {
            var format = GetImageFormat(filePath);
            var (width, height) = GetValidDimensions(chart.Bounds);
            var skChart = new SKCartesianChart(chart)
            {
                Width = width,
                Height = height,
                Background = SKColors.Transparent
            };
            using var chartImage = skChart.GetImage();
            SaveOnSolidBackground(chartImage, width, height, filePath, format);
            return true;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Export, "Failed to save chart image");
            await ShowSaveErrorDialog(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Saves a PieChart as an image file with a file picker dialog.
    /// Includes the sibling PieChartLegend in the exported image when present.
    /// </summary>
    public static async Task<bool> SaveChartAsImageAsync(TopLevel topLevel, PieChart chart, string suggestedFileName)
    {
        var filePath = await ShowSaveFilePickerAsync(topLevel, suggestedFileName);
        if (filePath == null) return false;

        try
        {
            var format = GetImageFormat(filePath);
            var (chartWidth, chartHeight) = GetValidDimensions(chart.Bounds);
            var skChart = new SKPieChart(chart)
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = SKColors.Transparent
            };

            using var chartImage = skChart.GetImage();

            // Include the sibling PieChartLegend when present
            var legend = FindSiblingLegend(chart);
            if (legend?.Items is { Count: > 0 } legendItems)
            {
                using var composited = ComposePieChartWithLegend(chartImage, legendItems, chartWidth, chartHeight);
                using var data = composited.Encode(format, 100);
                File.WriteAllBytes(filePath, data.ToArray());
            }
            else
            {
                SaveOnSolidBackground(chartImage, chartWidth, chartHeight, filePath, format);
            }

            return true;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Export, "Failed to save chart image");
            await ShowSaveErrorDialog(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Saves a GeoMap as an image file with a file picker dialog.
    /// </summary>
    public static async Task<bool> SaveChartAsImageAsync(TopLevel topLevel, GeoMap chart, string suggestedFileName)
    {
        var filePath = await ShowSaveFilePickerAsync(topLevel, suggestedFileName);
        if (filePath == null) return false;

        try
        {
            var format = GetImageFormat(filePath);
            var (width, height) = GetValidDimensions(chart.Bounds);

            // Build SKGeoMap manually instead of using the copy constructor,
            // which does not reliably copy the series data.
            var skChart = new SKGeoMap
            {
                Width = width,
                Height = height,
                Background = SKColors.Transparent,
                Series = chart.Series,
                MapProjection = chart.MapProjection
            };
            using var chartImage = skChart.GetImage();
            SaveOnSolidBackground(chartImage, width, height, filePath, format);
            return true;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Export, "Failed to save chart image");
            await ShowSaveErrorDialog(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Saves any supported chart type as an image file.
    /// </summary>
    public static async Task<bool> SaveChartAsImageAsync(TopLevel topLevel, Control chart, string suggestedFileName)
    {
        return chart switch
        {
            CartesianChart cartesianChart => await SaveChartAsImageAsync(topLevel, cartesianChart, suggestedFileName),
            PieChart pieChart => await SaveChartAsImageAsync(topLevel, pieChart, suggestedFileName),
            GeoMap geoMap => await SaveChartAsImageAsync(topLevel, geoMap, suggestedFileName),
            _ => false
        };
    }

    /// <summary>
    /// Creates a file-safe name from a chart title.
    /// </summary>
    /// <summary>
    /// Characters no common desktop platform accepts in a file name: the Windows reserved set,
    /// which is a superset of what macOS and Linux reject, plus the control characters.
    /// </summary>
    private static readonly char[] InvalidFileNameChars =
        [.. "<>:\"/\\|?*", .. Enumerable.Range(0, 32).Select(c => (char)c)];

    public static string CreateSafeFileName(string chartName)
    {
        // A fixed set, not Path.GetInvalidFileNameChars(): that returns only '/' and NUL on
        // macOS and Linux, so a chart called "Q1: Revenue" exported there kept its colon and
        // produced a file Windows cannot open. Exports get shared between machines, so the
        // name has to be legal everywhere rather than merely legal where it was written.
        var safeName = string.Join("_", chartName.Split(InvalidFileNameChars));
        safeName = safeName.Replace(" ", "_");
        return $"{safeName}_{DateTime.Now:yyyy-MM-dd}";
    }

    /// <summary>
    /// Gets valid dimensions for chart export, using defaults if bounds are invalid.
    /// </summary>
    /// <param name="bounds">The chart's bounds.</param>
    /// <returns>A tuple of (width, height) with valid dimensions.</returns>
    private static (int Width, int Height) GetValidDimensions(Rect bounds)
    {
        var width = (int)bounds.Width;
        var height = (int)bounds.Height;

        // Use defaults if dimensions are invalid (zero, negative, or unreasonably small)
        if (width < 100 || double.IsNaN(bounds.Width) || double.IsInfinity(bounds.Width))
            width = DefaultExportWidth;

        if (height < 50 || double.IsNaN(bounds.Height) || double.IsInfinity(bounds.Height))
            height = DefaultExportHeight;

        return (width, height);
    }

    /// <summary>
    /// Shows the save file picker dialog for image export.
    /// </summary>
    private static async Task<string?> ShowSaveFilePickerAsync(TopLevel topLevel, string suggestedFileName)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Chart as Image",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG Image") { Patterns = ["*.png"] },
                new FilePickerFileType("JPEG Image") { Patterns = ["*.jpg", "*.jpeg"] }
            ]
        });

        return file?.Path.LocalPath;
    }

    /// <summary>
    /// Gets the image format based on file extension.
    /// </summary>
    private static SKEncodedImageFormat GetImageFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            _ => SKEncodedImageFormat.Png
        };
    }

    /// <summary>
    /// Finds the PieChartLegend sibling of a PieChart within their shared parent Grid.
    /// </summary>
    private static PieChartLegend? FindSiblingLegend(PieChart chart)
    {
        if (chart.Parent is not Grid parentGrid) return null;

        foreach (var child in parentGrid.Children)
        {
            if (child is PieChartLegend legend)
                return legend;
        }

        return null;
    }

    /// <summary>
    /// Composites a rendered pie chart image with legend items drawn via SkiaSharp,
    /// producing a single image with the chart on the left and legend on the right.
    /// </summary>
    private static SKImage ComposePieChartWithLegend(
        SKImage chartImage,
        ObservableCollection<PieLegendItem> items,
        int chartWidth,
        int chartHeight)
    {
        const int legendPadding = 16;
        const int itemHeight = 26;
        const int circleRadius = 6;
        const int circleTextGap = 10;
        const int labelPercentGap = 16;
        const float fontSize = 13f;

        using var typeface = Core.Services.PlatformTypefaces.Default;
        using var font = new SKFont(typeface, fontSize);
        // Legend text follows the theme so it stays readable on the solid export background.
        var isDark = ThemeService.Instance.IsDarkTheme;
        using var labelPaint = new SKPaint { Color = isDark ? new SKColor(0xF9, 0xFA, 0xFB) : new SKColor(0x33, 0x33, 0x33), IsAntialias = true };
        using var percentPaint = new SKPaint { Color = isDark ? new SKColor(0x9C, 0xA3, 0xAF) : new SKColor(0x88, 0x88, 0x88), IsAntialias = true };

        // Measure text to calculate legend width
        float maxLabelWidth = 0;
        float maxPercentWidth = 0;
        foreach (var item in items)
        {
            maxLabelWidth = Math.Max(maxLabelWidth, font.MeasureText(item.Label, labelPaint));
            maxPercentWidth = Math.Max(maxPercentWidth, font.MeasureText(item.FormattedPercentage, percentPaint));
        }

        var legendContentWidth = circleRadius * 2 + circleTextGap + maxLabelWidth + labelPercentGap + maxPercentWidth;
        var legendWidth = (int)(legendContentWidth + legendPadding * 2);
        var totalWidth = chartWidth + legendWidth;
        var legendTotalHeight = items.Count * itemHeight;
        var totalHeight = Math.Max(chartHeight, legendTotalHeight + legendPadding * 2);

        using var surface = SKSurface.Create(new SKImageInfo(totalWidth, totalHeight));
        var canvas = surface.Canvas;
        canvas.Clear(GetExportBackground());

        // Draw chart on the left, centered vertically
        var chartY = (totalHeight - chartHeight) / 2f;
        canvas.DrawImage(chartImage, 0, chartY);

        // Draw legend items on the right, centered vertically
        var legendX = chartWidth + legendPadding;
        var itemY = (totalHeight - legendTotalHeight) / 2f;

        foreach (var item in items)
        {
            var centerY = itemY + itemHeight / 2f;

            // Colored circle
            using var circlePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColor.TryParse(item.ColorHex, out var color) ? color : SKColors.Gray,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawCircle(legendX + circleRadius, centerY, circleRadius, circlePaint);

            // Label text
            var textX = legendX + circleRadius * 2 + circleTextGap;
            var textBaseline = centerY + fontSize / 3f;
            canvas.DrawText(item.Label, textX, textBaseline, font, labelPaint);

            // Percentage text
            var percentX = legendX + circleRadius * 2 + circleTextGap + maxLabelWidth + labelPercentGap;
            canvas.DrawText(item.FormattedPercentage, percentX, textBaseline, font, percentPaint);

            itemY += itemHeight;
        }

        return surface.Snapshot();
    }

    /// <summary>
    /// Shows an error dialog when saving fails.
    /// </summary>
    private static async Task ShowSaveErrorDialog(string errorMessage)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog != null)
        {
            await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Save Failed".Translate(),
                Message = "Failed to save the chart image: {0}".TranslateFormat(errorMessage),
                PrimaryButtonText = "OK".Translate(),
                SecondaryButtonText = null,
                CancelButtonText = null
            });
        }
    }
}
