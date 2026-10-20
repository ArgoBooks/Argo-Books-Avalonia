using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Expenses page.
/// </summary>
public partial class ExpensesPage : UserControl
{
    private KeyModifiers _lastPointerModifiers;

    public ExpensesPage()
    {
        InitializeComponent();

        // A checkbox's Click carries no modifier keys, so the press before it says whether Shift was held.
        AddHandler(PointerPressedEvent, (_, e) => _lastPointerModifiers = e.KeyModifiers, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnPageKeyDown);
    }

    private void OnRowCheckBoxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ExpenseDisplayItem item } && DataContext is ExpensesPageViewModel vm)
            vm.Selection.OnRowChecked(item, _lastPointerModifiers.HasFlag(KeyModifiers.Shift));
    }

    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ExpensesPageViewModel vm && vm.Selection.IsActive)
        {
            vm.Selection.IsActive = false;
            e.Handled = true;
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
                FileTypeFilter = FilePickerTypes.ReceiptFilters
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
