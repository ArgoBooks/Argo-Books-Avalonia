using ArgoBooks.ViewModels.Dashboard;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArgoBooks.Services;

/// <summary>
/// Creates widget-specific config UI for the settings popup.
/// Returns null if the widget has no config options.
/// </summary>
public static class WidgetSettingsFactory
{
    public static Control? CreateConfigView(WidgetViewModelBase vm) => vm switch
    {
        QuickActionsWidgetViewModel => CreateQuickActionsConfig(),
        RecentTransactionsWidgetViewModel => CreateRowCountConfig("Rows to display"),
        ActiveRentalsWidgetViewModel => CreateActiveRentalsConfig(),
        TopCustomersWidgetViewModel => CreateTopCustomersConfig(),
        LowStockAlertsWidgetViewModel => CreateThresholdConfig(),
        UpcomingInvoicesWidgetViewModel => CreateDaysAheadConfig(),
        UnifiedChartWidgetViewModel chart when chart.IsDistribution => CreateChartConfig(chart),
        _ => null
    };

    private static Control CreateQuickActionsConfig()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Label("Visible Actions"));
        string[] actions =
        [
            "New Invoice:ShowNewInvoice", "New Expense:ShowNewExpense", "New Revenue:ShowNewRevenue",
            "Scan Receipt:ShowScanReceipt", "Import Bank Statement:ShowImportBankStatement",
            "New Customer:ShowNewCustomer", "New Supplier:ShowNewSupplier",
            "New Product:ShowNewProduct", "New Rental Item:ShowNewRentalItem",
            "New Rental Record:ShowNewRentalRecord", "New Category:ShowNewCategory",
            "New Location:ShowNewLocation", "New Purchase Order:ShowNewPurchaseOrder", "New Stock Adjustment:ShowNewStockAdjustment"
        ];
        foreach (var action in actions)
        {
            var parts = action.Split(':');
            panel.Children.Add(ToggleRow(parts[0], parts[1]));
        }
        return new ScrollViewer { Content = panel, MaxHeight = 220, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    private static Control CreateRowCountConfig(string label)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label(label));
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Bind(ComboBox.ItemsSourceProperty, new Avalonia.Data.Binding("RowCountOptions"));
        combo.Bind(ComboBox.SelectedItemProperty, new Avalonia.Data.Binding("RowCount"));
        panel.Children.Add(combo);
        return panel;
    }

    private static Control CreateActiveRentalsConfig()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("Rows to display"));
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Bind(ComboBox.ItemsSourceProperty, new Avalonia.Data.Binding("RowCountOptions"));
        combo.Bind(ComboBox.SelectedItemProperty, new Avalonia.Data.Binding("RowCount"));
        panel.Children.Add(combo);
        panel.Children.Add(ToggleRow("Overdue only", "OverdueOnly"));
        return panel;
    }

    private static Control CreateTopCustomersConfig()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("Number of customers"));
        var countCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        countCombo.Bind(ComboBox.ItemsSourceProperty, new Avalonia.Data.Binding("CountOptions"));
        countCombo.Bind(ComboBox.SelectedItemProperty, new Avalonia.Data.Binding("Count"));
        panel.Children.Add(countCombo);
        panel.Children.Add(Label("Sort by"));
        var sortCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        sortCombo.Bind(ComboBox.ItemsSourceProperty, new Avalonia.Data.Binding("SortByOptions"));
        sortCombo.Bind(ComboBox.SelectedItemProperty, new Avalonia.Data.Binding("SortBy"));
        panel.Children.Add(sortCombo);
        return panel;
    }

    private static Control CreateThresholdConfig()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("Stock threshold"));
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Bind(ComboBox.ItemsSourceProperty, new Avalonia.Data.Binding("ThresholdOptions"));
        combo.Bind(ComboBox.SelectedItemProperty, new Avalonia.Data.Binding("Threshold"));
        panel.Children.Add(combo);
        return panel;
    }

    private static Control CreateDaysAheadConfig()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("Days ahead"));
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Bind(ComboBox.ItemsSourceProperty, new Avalonia.Data.Binding("DaysAheadOptions"));
        combo.Bind(ComboBox.SelectedItemProperty, new Avalonia.Data.Binding("DaysAhead"));
        panel.Children.Add(combo);
        return panel;
    }

    private static Control CreateChartConfig(UnifiedChartWidgetViewModel chart)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("Chart style"));
        panel.Children.Add(Dropdown("ChartStyleOptions", "ChartStyle"));

        // Only the two charts that group by where somebody is can be asked for a smaller area.
        if (chart.IsGeographic)
        {
            panel.Children.Add(Label("Group by"));
            panel.Children.Add(Dropdown("GeoLevelOptions", "GeoLevelOption"));
        }

        return panel;
    }

    private static ComboBox Dropdown(string itemsPath, string selectedPath)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Bind(ComboBox.ItemsSourceProperty, new Avalonia.Data.Binding(itemsPath));
        combo.Bind(ComboBox.SelectedItemProperty, new Avalonia.Data.Binding(selectedPath));
        return combo;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = Avalonia.Media.FontWeight.Medium,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static Border ToggleRow(string text, string bindingPath)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12
        });
        var toggle = new ToggleSwitch { Margin = new Thickness(12, 0, 0, 0) };
        toggle.Bind(ToggleSwitch.IsCheckedProperty, new Avalonia.Data.Binding(bindingPath));
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return new Border
        {
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }
}
