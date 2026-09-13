namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// One row in the Data hub's grouped menu (e.g. "Expenses" under the "Money" group), showing
/// the section's row count and navigating to its list when tapped.
/// </summary>
public partial class DataSectionSummaryViewModel : ObservableObject
{
    private readonly Action<DataSectionSummaryViewModel> _onOpen;

    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    private int _count;

    public DataSectionSummaryViewModel(string key, string label, int count, Action<DataSectionSummaryViewModel> onOpen)
    {
        Key = key;
        Label = label;
        _count = count;
        _onOpen = onOpen;
    }

    [RelayCommand]
    private void Open() => _onOpen(this);
}

/// <summary>A named group of sections in the Data hub (Money, People, Inventory, Rentals).</summary>
public sealed class DataGroupViewModel
{
    public string Name { get; }

    public ObservableCollection<DataSectionSummaryViewModel> Items { get; }

    public DataGroupViewModel(string name, IEnumerable<DataSectionSummaryViewModel> items)
    {
        Name = name;
        Items = new ObservableCollection<DataSectionSummaryViewModel>(items);
    }
}
