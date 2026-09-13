using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// "Your data" tab: the grouped menu (Money / People / Inventory / Rentals), mirroring the
/// prototype's Data hub. Each group lists its sections with a row count; tapping a section opens
/// its list (<see cref="DataSectionListViewModel"/>). The snapshot only carries Money/People/
/// Inventory rows today, so Rentals always shows an empty section until a later snapshot version
/// adds rental data.
/// </summary>
public partial class DataHubViewModel : ViewModelBase
{
    private readonly Action<string, string, IReadOnlyList<RowDto>> _onOpenSection;

    public ObservableCollection<DataGroupViewModel> Groups { get; } = new();

    public DataHubViewModel(Action<string, string, IReadOnlyList<RowDto>> onOpenSection)
    {
        _onOpenSection = onOpenSection;
        BuildGroups(null);
    }

    public void UpdateSnapshot(MobileSnapshot? snapshot) => BuildGroups(snapshot);

    private void BuildGroups(MobileSnapshot? snapshot)
    {
        Groups.Clear();

        Groups.Add(new DataGroupViewModel("Money", new[]
        {
            MakeSection("expenses", "Expenses", snapshot?.Expenses),
            MakeSection("revenue", "Revenue", snapshot?.Revenue),
            MakeSection("invoices", "Invoices", snapshot?.Invoices),
        }));

        Groups.Add(new DataGroupViewModel("People", new[]
        {
            MakeSection("customers", "Customers", snapshot?.Customers),
            MakeSection("suppliers", "Suppliers", snapshot?.Suppliers),
        }));

        Groups.Add(new DataGroupViewModel("Inventory", new[]
        {
            MakeSection("products", "Products", snapshot?.Products),
        }));

        Groups.Add(new DataGroupViewModel("Rentals", new[]
        {
            MakeSection("rentals", "Rental records", null),
        }));
    }

    private DataSectionSummaryViewModel MakeSection(string key, string label, IReadOnlyList<RowDto>? rows)
    {
        var list = rows ?? Array.Empty<RowDto>();
        return new DataSectionSummaryViewModel(key, label, list.Count, vm => _onOpenSection(vm.Key, vm.Label, list));
    }
}
