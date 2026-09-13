using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ArgoBooks.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// Settings tab. Paired company label + last synced + manual refresh, the biometric app
/// lock toggle, and the Companies section: list every paired company with the
/// active one marked, switch between them, pair another, or unpair this phone from the active one.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private bool _isLoadingAppLockSetting;

    [ObservableProperty]
    private string _companyLabel = string.Empty;

    [ObservableProperty]
    private string _lastSyncedText = "Not synced yet";

    /// <summary>Biometric app lock on/off. Defaults to on; free for all users (no premium gate).</summary>
    [ObservableProperty]
    private bool _isAppLockEnabled = true;

    public ObservableCollection<CompanyRowViewModel> Companies { get; } = new();

    /// <summary>True while the "Unpair this phone" confirmation footer is showing.</summary>
    [ObservableProperty]
    private bool _isConfirmingUnpair;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _ = LoadAppLockSettingAsync();
        _ = LoadCompaniesAsync();
    }

    public void Update(string companyLabel, string lastSyncedText)
    {
        CompanyLabel = companyLabel;
        LastSyncedText = lastSyncedText;
        _ = LoadCompaniesAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await _shell.RefreshCommand.ExecuteAsync(null);

    private async Task LoadCompaniesAsync()
    {
        var all = await _shell.GetCompaniesAsync();
        var activeUid = await _shell.GetActiveCompanyUidAsync();

        Companies.Clear();
        foreach (var company in all)
        {
            var isActive = activeUid != null && activeUid == company.CompanyUid;
            Companies.Add(new CompanyRowViewModel(company.CompanyUid, company.CompanyLabel, isActive, SwitchCompanyAsync));
        }
    }

    private async Task SwitchCompanyAsync(string companyUid)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _shell.SwitchCompanyAsync(companyUid);
            await LoadCompaniesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void PairAnother() => _shell.OpenPairingFlow();

    /// <summary>First tap arms the confirmation footer; a second tap (ConfirmUnpairCommand)
    /// actually removes the active company. There's no dialog library wired up in this app, so
    /// the confirmation is an inline footer instead of a modal.</summary>
    [RelayCommand]
    private void RequestUnpair() => IsConfirmingUnpair = true;

    [RelayCommand]
    private void CancelUnpair() => IsConfirmingUnpair = false;

    [RelayCommand]
    private async Task ConfirmUnpairAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            IsConfirmingUnpair = false;
            var stillActive = await _shell.UnpairActiveCompanyAsync();
            if (stillActive)
            {
                await LoadCompaniesAsync();
            }
            // If false, RequestPairing already fired and the host is swapping to the pairing
            // screen; this SettingsViewModel instance is on its way out.
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAppLockSettingAsync()
    {
        _isLoadingAppLockSetting = true;
        try
        {
            IsAppLockEnabled = await AppLockSettings.IsEnabledAsync();
        }
        finally
        {
            _isLoadingAppLockSetting = false;
        }
    }

    partial void OnIsAppLockEnabledChanged(bool value)
    {
        // Skip the write-back while LoadAppLockSettingAsync is setting the initial value from
        // storage, so we don't redundantly re-save the value we just loaded.
        if (_isLoadingAppLockSetting)
        {
            return;
        }

        _ = AppLockSettings.SetEnabledAsync(value);
    }
}
