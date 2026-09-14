using ArgoBooks.ViewModels;
using Avalonia.Controls;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Stock Levels page.
/// </summary>
public partial class StockLevelsPage : UserControl
{
    public StockLevelsPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles the header size changed event for responsive layout.
    /// </summary>
    private void OnHeaderSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is StockLevelsPageViewModel viewModel && e.WidthChanged)
        {
            viewModel.ResponsiveHeader.HeaderWidth = e.NewSize.Width;
        }
    }
}
