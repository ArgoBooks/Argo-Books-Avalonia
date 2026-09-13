using System.Collections.ObjectModel;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Dashboard;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels.Dashboard;

public record LowStockItem(string Name, decimal CurrentStock, int Threshold)
{
    public string CurrentStockText => StockUnits.Format(CurrentStock);
}

public partial class LowStockAlertsWidgetViewModel : WidgetViewModelBase
{
    public override WidgetType WidgetType => WidgetType.LowStockAlerts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlerts))]
    [NotifyPropertyChangedFor(nameof(HasNoAlerts))]
    private ObservableCollection<LowStockItem> _items = [];

    public bool HasAlerts => Items.Count > 0;
    public bool HasNoAlerts => Items.Count == 0;

    public override bool HasConfig => true;

    [ObservableProperty]
    private int _threshold = 10;

    public int[] ThresholdOptions { get; } = [5, 10, 15, 20, 25, 50, 100];

    partial void OnThresholdChanged(int value) => LoadData();

    public override void Initialize(Dictionary<string, string> config)
    {
        ApplyConfig(config);
    }

    public override void ApplyConfig(Dictionary<string, string> config)
    {
        if (config.TryGetValue("Threshold", out var thresholdStr) && int.TryParse(thresholdStr, out var threshold))
            Threshold = threshold;
    }

    public override Dictionary<string, string> GetConfig()
    {
        return new Dictionary<string, string>
        {
            ["Threshold"] = Threshold.ToString()
        };
    }

    public override void LoadData()
    {
        var data = CompanyManager?.CompanyData;
        if (data == null) return;

        LoadLowStockItems(data);
    }

    private void LoadLowStockItems(CompanyData data)
    {
        var lowStockItems = data.Inventory
            .Where(inv => inv.InStock > 0 && inv.InStock <= Threshold)
            .OrderBy(inv => inv.InStock)
            .Select(inv =>
            {
                var product = data.GetProduct(inv.ProductId);
                var name = product?.Name ?? "Unknown Item";
                return new LowStockItem(name, inv.InStock, Threshold);
            })
            .ToList();

        Items = new ObservableCollection<LowStockItem>(lowStockItems);
    }
}
