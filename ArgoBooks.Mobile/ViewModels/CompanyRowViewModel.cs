namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// One row in a paired-company list (the company switcher page, or the Settings "Companies"
/// section): the company's label plus whether it's the currently active one. Tapping a row hands
/// its UID to the caller-supplied select callback, which owns the actual switch + refresh.
/// </summary>
public partial class CompanyRowViewModel : ObservableObject
{
    public string CompanyUid { get; }

    public string CompanyLabel { get; }

    [ObservableProperty]
    private bool _isActive;

    public IAsyncRelayCommand SelectCommand { get; }

    public CompanyRowViewModel(string companyUid, string companyLabel, bool isActive, Func<string, Task> onSelect)
    {
        CompanyUid = companyUid;
        CompanyLabel = companyLabel;
        _isActive = isActive;
        SelectCommand = new AsyncRelayCommand(() => onSelect(CompanyUid));
    }
}
