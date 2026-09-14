using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Revenue page.
/// </summary>
public partial class RevenuePage : UserControl
{
    public RevenuePage()
    {
        InitializeComponent();
    }

    private void OnHeaderSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is RevenuePageViewModel viewModel && e.WidthChanged)
        {
            viewModel.ResponsiveHeader.HeaderWidth = e.NewSize.Width;
        }
    }

    private async void OnAiScanButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (App.ReceiptsModalsViewModel == null) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Receipt to Scan",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerTypes.AllSupportedTypes, FilePickerTypes.ImageFileType, FilePickerTypes.PdfFileType]
            });

            if (files.Count > 0)
            {
                var file = files[0];
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && App.ReceiptsModalsViewModel != null)
                {
                    await App.ReceiptsModalsViewModel.OpenScanModalAsync(path);
                }
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "OnAiScanButtonClick");
        }
    }

    private async void OnImportBankStatementClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            await App.OpenBankStatementImportAsync();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "OnImportBankStatementClick");
        }
    }
}
