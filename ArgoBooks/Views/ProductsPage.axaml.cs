using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Products page.
/// </summary>
public partial class ProductsPage : UserControl
{
    private KeyModifiers _lastPointerModifiers;

    public ProductsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // A checkbox's Click carries no modifier keys, so the press before it says whether Shift was held.
        AddHandler(PointerPressedEvent, (_, e) => _lastPointerModifiers = e.KeyModifiers, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnPageKeyDown);
    }

    private void OnRowCheckBoxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ProductDisplayItem item } && DataContext is ProductsPageViewModel vm)
            vm.Selection.OnRowChecked(item, _lastPointerModifiers.HasFlag(KeyModifiers.Shift));
    }

    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ProductsPageViewModel vm && vm.Selection.IsActive)
        {
            vm.Selection.IsActive = false;
            e.Handled = true;
        }
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        App.ProductsTutorialViewModel?.ShowIfFirstVisit();
    }
}
