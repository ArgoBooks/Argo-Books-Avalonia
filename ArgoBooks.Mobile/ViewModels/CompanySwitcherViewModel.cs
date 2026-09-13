using ArgoBooks.Shared.Mobile;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// Company switcher page, pushed from the chip at the top of Dashboard/Data/Analytics (see
/// ShellViewModel.OpenCompanySwitcherCommand). Lists every paired company with the active one
/// marked, lets the user tap a different one to switch, and offers a "Pair another company"
/// action that hands off to the pairing flow. The actual store update + snapshot refresh (and
/// where to navigate afterward) is owned by the callbacks ShellViewModel supplies, so this class
/// stays a thin list/selection presenter.
/// </summary>
public partial class CompanySwitcherViewModel : ViewModelBase
{
    private readonly PairedCompanyStore _store;
    private readonly Func<string, Task> _onSelect;
    private readonly Action _onPairAnother;

    public ObservableCollection<CompanyRowViewModel> Companies { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    public CompanySwitcherViewModel(PairedCompanyStore store, Func<string, Task> onSelect, Action onPairAnother)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _onSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
        _onPairAnother = onPairAnother ?? throw new ArgumentNullException(nameof(onPairAnother));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var all = await _store.GetAllAsync();
        var active = await _store.GetActiveAsync();

        Companies.Clear();
        foreach (var company in all)
        {
            var isActive = active != null && active.CompanyUid == company.CompanyUid;
            Companies.Add(new CompanyRowViewModel(company.CompanyUid, company.CompanyLabel, isActive, SelectAsync));
        }
    }

    private async Task SelectAsync(string companyUid)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _onSelect(companyUid);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void PairAnother() => _onPairAnother();
}
