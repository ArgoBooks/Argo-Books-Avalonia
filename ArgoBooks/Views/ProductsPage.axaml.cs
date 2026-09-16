using Avalonia.Controls;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Products page.
/// </summary>
public partial class ProductsPage : UserControl
{
    public ProductsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        App.ProductsTutorialViewModel?.ShowIfFirstVisit();
    }
}
