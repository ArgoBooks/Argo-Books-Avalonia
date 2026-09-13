using ArgoBooks.Controls;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Edit Company modal.
/// </summary>
public partial class EditCompanyModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _companyName = "";

    [ObservableProperty]
    private string? _businessType;

    [ObservableProperty]
    private string? _industry;

    [ObservableProperty]
    private Bitmap? _logoSource;

    [ObservableProperty]
    private bool _hasLogo;

    [ObservableProperty]
    private string? _logoPath;

    [ObservableProperty]
    private string _phoneNumber = "";

    [ObservableProperty]
    private string? _phoneNumberError;

    [ObservableProperty]
    private CountryDialCode? _selectedPhoneCountry;

    [ObservableProperty]
    private string? _country;

    [ObservableProperty]
    private string? _city;

    [ObservableProperty]
    private string? _address;

    [ObservableProperty]
    private string? _provinceState;

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _selectedCurrency = "USD - US Dollar ($)";

    // Currency change error state
    [ObservableProperty]
    private bool _hasCurrencyError;

    [ObservableProperty]
    private string _currencyErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isSavingCurrency;

    [ObservableProperty]
    private int _currencyLoadingProgress;

    /// <summary>
    /// True when loading exchange rates for the first time (not during retry).
    /// </summary>
    public bool ShowCurrencyLoading => IsSavingCurrency && !HasCurrencyError;

    private CancellationTokenSource? _currencyLoadingCts;

    /// <summary>
    /// Priority/common currencies shown at the top of the dropdown.
    /// </summary>
    public IReadOnlyList<string> PriorityCurrencies => Data.Currencies.Priority;

    /// <summary>
    /// All available currencies.
    /// </summary>
    public IReadOnlyList<string> Currencies => Data.Currencies.All;

    /// <summary>
    /// Available business types (shared data source).
    /// </summary>
    public static string[] BusinessTypes { get; } =
    [
        "Sole Proprietorship",
        "Partnership",
        "Corporation",
        "LLC",
        "Non-Profit",
        "Other"
    ];

    /// <summary>
    /// Available industries (shared data source).
    /// </summary>
    public static string[] Industries { get; } = Core.Models.IndustryNames.All;

    // Store original values for detecting changes
    private string _originalCompanyName = "";
    private string? _originalBusinessType;
    private string? _originalIndustry;
    private Bitmap? _originalLogo;
    private string _originalPhoneNumber = "";
    private CountryDialCode? _originalSelectedPhoneCountry;
    private string? _originalCountry;
    private string? _originalCity;
    private string? _originalAddress;
    private string? _originalProvinceState;
    private string _originalEmail = "";
    private string _originalCurrency = "USD - US Dollar ($)";
    private string? _pendingCurrencyCode;

    /// <summary>
    /// Whether any changes have been made.
    /// </summary>
    public bool HasChanges =>
        !StrEq(CompanyName, _originalCompanyName) ||
        !StrEq(BusinessType, _originalBusinessType) ||
        !StrEq(Industry, _originalIndustry) ||
        LogoSource != _originalLogo ||
        !StrEq(PhoneNumber, _originalPhoneNumber) ||
        SelectedPhoneCountry != _originalSelectedPhoneCountry ||
        !StrEq(Country, _originalCountry) ||
        !StrEq(City, _originalCity) ||
        !StrEq(Address, _originalAddress) ||
        !StrEq(ProvinceState, _originalProvinceState) ||
        !StrEq(Email, _originalEmail) ||
        !StrEq(SelectedCurrency, _originalCurrency);

    // Treats null and empty as equal: clearing a field that was originally empty (null)
    // leaves a "" behind, which must NOT count as a change. A non-empty value vs empty
    // is still detected as a real change.
    private static bool StrEq(string? a, string? b) =>
        string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) : string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>
    /// Whether the form is valid for saving.
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(CompanyName) && !string.IsNullOrWhiteSpace(Country);

    /// <summary>
    /// Default constructor.
    /// </summary>
    public EditCompanyModalViewModel()
    {
    }

    /// <summary>
    /// Opens the modal with the current company info.
    /// </summary>
    public void Open(
        string companyName,
        string? businessType = null,
        string? industry = null,
        Bitmap? logo = null,
        string? phone = null,
        string? country = null,
        string? city = null,
        string? address = null,
        string? provinceState = null,
        string? email = null,
        string? currencyCode = null)
    {
        _originalCompanyName = companyName;
        _originalBusinessType = businessType;
        _originalIndustry = industry;
        _originalLogo = logo;

        CompanyName = companyName;
        BusinessType = businessType;
        Industry = industry;
        LogoSource = logo;
        HasLogo = logo != null;
        LogoPath = null;

        // Load currency
        var currencyDisplayString = CurrencyService.GetDisplayString(currencyCode ?? "CAD");
        _originalCurrency = currencyDisplayString;
        SelectedCurrency = currencyDisplayString;
        HasCurrencyError = false;
        CurrencyErrorMessage = string.Empty;

        // Parse the phone number to extract country code and local number
        _originalPhoneNumber = "";
        _originalSelectedPhoneCountry = null;
        PhoneNumber = "";
        PhoneNumberError = null;
        SelectedPhoneCountry = null;

        if (!string.IsNullOrWhiteSpace(phone))
        {
            // Try to parse the phone number - format is typically "+1 5551000000"
            var dialCodes = PhoneInput.AllDialCodes;
            CountryDialCode? matchedDialCode;

            // Find all dial codes that match the phone prefix
            var matchingCodes = dialCodes
                .Where(d => phone.StartsWith(d.DialCode))
                .OrderByDescending(d => d.DialCode.Length)
                .ToList();

            if (matchingCodes.Count > 0)
            {
                // If the company country is set and multiple codes share the same dial code,
                // prefer the one matching the company country
                matchedDialCode = matchingCodes.FirstOrDefault(d =>
                    !string.IsNullOrEmpty(country) &&
                    d.Name.Equals(country, StringComparison.OrdinalIgnoreCase))
                    ?? matchingCodes[0];

                var remaining = phone[matchedDialCode.DialCode.Length..].Trim();
                _originalSelectedPhoneCountry = matchedDialCode;
                _originalPhoneNumber = remaining;
                SelectedPhoneCountry = matchedDialCode;
                PhoneNumber = remaining;
            }
            else
            {
                // No dial code prefix found, extract raw digits and default to US
                var digits = new string(phone.Where(char.IsDigit).ToArray());
                _originalPhoneNumber = digits;
                PhoneNumber = digits;
                var usDialCode = dialCodes.FirstOrDefault(c => c.Code == "US");
                _originalSelectedPhoneCountry = usDialCode;
                SelectedPhoneCountry = usDialCode;
            }
        }

        _originalCountry = country;
        _originalCity = city;
        _originalAddress = address;
        _originalProvinceState = provinceState;
        _originalEmail = email ?? "";
        Country = country;
        City = city;
        Address = address;
        ProvinceState = provinceState;
        Email = email ?? "";

        IsOpen = true;

        // Some TwoWay-bound controls (e.g., ComboBox, CountryInput, PhoneInput) may
        // write back a normalised or coerced value after the binding settles.
        // Re-snapshot all originals once the UI thread has processed those updates so
        // that HasChanges is false immediately after Open(), while still detecting any
        // genuine change the user makes afterwards.
        Dispatcher.UIThread.Post(SnapshotOriginals, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Re-captures the current field values as the baseline for change detection.
    /// Called once after Open() via a deferred Dispatcher post so that any
    /// TwoWay binding write-backs from controls (ComboBox normalisation, CountryInput,
    /// PhoneInput) have already settled before we take the snapshot.
    /// </summary>
    private void SnapshotOriginals()
    {
        _originalCompanyName = CompanyName;
        _originalBusinessType = BusinessType;
        _originalIndustry = Industry;
        _originalLogo = LogoSource;
        _originalPhoneNumber = PhoneNumber;
        _originalSelectedPhoneCountry = SelectedPhoneCountry;
        _originalCountry = Country;
        _originalCity = City;
        _originalAddress = Address;
        _originalProvinceState = ProvinceState;
        _originalEmail = Email;
        _originalCurrency = SelectedCurrency;
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>
    /// Opens the modal.
    /// </summary>
    [RelayCommand]
    private void OpenModal()
    {
        IsOpen = true;
    }

    /// <summary>
    /// Closes the modal without saving.
    /// </summary>
    [RelayCommand]
    private async Task CloseAsync()
    {
        await RequestCloseAsync();
    }

    /// <summary>
    /// Requests to close the modal, showing unsaved changes dialog if needed.
    /// </summary>
    public async void RequestClose()
    {
        try
        {
            await RequestCloseAsync();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "EditCompanyModal.RequestClose");
        }
    }

    /// <summary>
    /// Requests to close the modal, showing unsaved changes dialog if needed.
    /// </summary>
    private async Task RequestCloseAsync()
    {
        if (HasChanges)
        {
            var dialog = App.ConfirmationDialog;
            if (dialog != null)
            {
                var result = await dialog.ShowAsync(new ConfirmationDialogOptions
                {
                    Title = "Unsaved Changes".Translate(),
                    Message = "You have unsaved changes to your company information. Do you want to save them before closing?".Translate(),
                    PrimaryButtonText = "Save".Translate(),
                    SecondaryButtonText = "Don't Save".Translate(),
                    CancelButtonText = "Cancel".Translate()
                });

                switch (result)
                {
                    case ConfirmationResult.Primary:
                        // Save and close
                        await SaveAsync();
                        return;
                    case ConfirmationResult.Secondary:
                        // Don't save, just close
                        IsOpen = false;
                        return;
                    case ConfirmationResult.Cancel:
                    case ConfirmationResult.None:
                        // Stay open
                        return;
                }
            }
        }

        IsOpen = false;
    }

    [RelayCommand]
    private void CancelCurrencyLoading()
    {
        _currencyLoadingCts?.Cancel();
    }

    [RelayCommand]
    private void DismissCurrencyError()
    {
        HasCurrencyError = false;
        CurrencyErrorMessage = string.Empty;
        // Revert the currency selection since the user cancelled
        if (_pendingCurrencyCode != null)
        {
            SelectedCurrency = _originalCurrency;
            _pendingCurrencyCode = null;
        }
    }

    [RelayCommand]
    private async Task RetryCurrencySaveAsync()
    {
        if (_pendingCurrencyCode == null)
        {
            HasCurrencyError = false;
            CurrencyErrorMessage = string.Empty;
            await SaveAsync();
            return;
        }

        // Keep the modal open but switch to loading state
        CurrencyErrorMessage = string.Empty;
        _currencyLoadingCts?.Cancel();
        _currencyLoadingCts = new CancellationTokenSource();
        CurrencyLoadingProgress = 0;
        IsSavingCurrency = true;

        bool success;
        try
        {
            // Run the preload and a minimum display time in parallel so the spinner is always visible
            var preloadTask = PreloadExchangeRatesForCurrencyAsync(_pendingCurrencyCode, _currencyLoadingCts.Token);
            var minimumDisplayTask = Task.Delay(1000, _currencyLoadingCts.Token);
            await Task.WhenAll(preloadTask, minimumDisplayTask);
            success = preloadTask.Result;
        }
        catch (OperationCanceledException)
        {
            IsSavingCurrency = false;
            HasCurrencyError = false;
            SelectedCurrency = _originalCurrency;
            _pendingCurrencyCode = null;
            return;
        }
        finally
        {
            _currencyLoadingCts?.Dispose();
            _currencyLoadingCts = null;
        }

        IsSavingCurrency = false;

        if (!success)
        {
            // Still failing, error state is already set by PreloadExchangeRatesForCurrencyAsync
            return;
        }

        // Rates loaded successfully, dismiss error and proceed with save
        HasCurrencyError = false;
        CurrencyErrorMessage = string.Empty;
        _pendingCurrencyCode = null;
        await SaveAsync(skipCurrencyValidation: true);
    }

    /// <summary>
    /// Saves the changes and closes the modal.
    /// </summary>
    [RelayCommand]
    private Task SaveAsync() => SaveAsync(skipCurrencyValidation: false);

    private async Task SaveAsync(bool skipCurrencyValidation)
    {
        if (!CanSave) return;

        // Warn (but allow) when the chosen currency doesn't match the country. Only
        // when the user actually changed the country or currency this session, and not
        // on the rate-retry path (skipCurrencyValidation), which was already confirmed.
        if (!skipCurrencyValidation &&
            (Country != _originalCountry || SelectedCurrency != _originalCurrency))
        {
            var selectedCode = CurrencyService.ParseCurrencyCode(SelectedCurrency);
            if (!await CurrencyCountryMatcher.ConfirmIfMismatchAsync(Country, selectedCode))
                return;
        }

        var currencyChanged = SelectedCurrency != _originalCurrency;

        // If currency changed and we haven't already validated rates via retry
        if (currencyChanged && !skipCurrencyValidation)
        {
            var dialog = App.ConfirmationDialog;
            if (dialog != null)
            {
                var result = await dialog.ShowAsync(new ConfirmationDialogOptions
                {
                    Title = "Change Default Currency".Translate(),
                    Message = "Changing the default currency will update how all amounts are displayed throughout the app. Existing transactions will be converted using historical exchange rates. This may result in small rounding differences. You can change it back any time.".Translate(),
                    PrimaryButtonText = "Change Currency".Translate(),
                    CancelButtonText = "Cancel".Translate(),
                    IsPrimaryDestructive = false
                });

                if (result != ConfirmationResult.Primary)
                    return;
            }

            // Preload exchange rates for the new currency
            var newCurrencyCode = CurrencyService.ParseCurrencyCode(SelectedCurrency);
            _currencyLoadingCts?.Cancel();
            _currencyLoadingCts = new CancellationTokenSource();
            CurrencyLoadingProgress = 0;
            IsSavingCurrency = true;

            bool success;
            try
            {
                success = await PreloadExchangeRatesForCurrencyAsync(newCurrencyCode, _currencyLoadingCts.Token);
            }
            catch (OperationCanceledException)
            {
                IsSavingCurrency = false;
                SelectedCurrency = _originalCurrency;
                return;
            }
            finally
            {
                _currencyLoadingCts?.Dispose();
                _currencyLoadingCts = null;
            }

            IsSavingCurrency = false;

            if (!success)
            {
                // Store the pending currency so retry can pick it up
                _pendingCurrencyCode = newCurrencyCode;
                return;
            }
            _pendingCurrencyCode = null;
        }

        // Validate phone number completeness
        PhoneNumberError = null;
        if (!string.IsNullOrWhiteSpace(PhoneNumber) && SelectedPhoneCountry != null)
        {
            var digits = new string(PhoneNumber.Where(char.IsDigit).ToArray());
            var expectedDigits = SelectedPhoneCountry.PhoneFormat.Count(c => c == 'X');
            if (digits.Length > 0 && digits.Length < expectedDigits)
            {
                PhoneNumberError = "Please enter a complete phone number.".Translate();
                return;
            }
        }

        // Build the full phone number with country code
        string? fullPhone = null;
        if (!string.IsNullOrWhiteSpace(PhoneNumber))
        {
            var dialCode = SelectedPhoneCountry?.DialCode ?? "";
            fullPhone = string.IsNullOrEmpty(dialCode) ? PhoneNumber : $"{dialCode} {PhoneNumber}";
        }

        // If currency changed, save it to company settings and notify
        if (currencyChanged)
        {
            var newCurrencyCode = CurrencyService.ParseCurrencyCode(SelectedCurrency);
            var settings = App.CompanyManager?.CompanyData?.Settings;
            if (settings != null)
            {
                settings.Localization.Currency = newCurrencyCode;
            }
            _originalCurrency = SelectedCurrency;

            CurrencyService.NotifyCurrencyChanged();
        }

        CompanySaved?.Invoke(this, new CompanyEditedEventArgs
        {
            CompanyName = CompanyName,
            BusinessType = BusinessType,
            Industry = Industry,
            LogoSource = LogoSource,
            LogoPath = LogoPath,
            Phone = fullPhone,
            Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
            Country = Country,
            City = City,
            Address = Address,
            ProvinceState = ProvinceState
        });

        IsOpen = false;
    }

    /// <summary>
    /// Preloads exchange rates for the selected currency.
    /// Returns true if successful, false if exchange rates could not be fetched.
    /// </summary>
    private async Task<bool> PreloadExchangeRatesForCurrencyAsync(string currencyCode, CancellationToken cancellationToken = default)
    {
        // Skip if USD (no conversion needed)
        if (string.Equals(currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
            return true;

        CurrencyLoadingProgress = 0;

        var exchangeService = ExchangeRateService.Instance;
        if (exchangeService == null)
        {
            HasCurrencyError = true;
            CurrencyErrorMessage = "Exchange rate service is not available. Please restart the application.".Translate();
            return false;
        }

        // Check if there are any transactions that need conversion
        var companyData = App.CompanyManager?.CompanyData;
        var hasTransactions = companyData != null &&
            (companyData.Expenses.Any() || companyData.Revenues.Any());

        CurrencyLoadingProgress = 10;
        cancellationToken.ThrowIfCancellationRequested();

        // Check connectivity first
        var connectivityService = new ConnectivityService();
        var hasInternet = await connectivityService.IsInternetAvailableAsync();

        if (!hasInternet)
        {
            if (hasTransactions)
            {
                HasCurrencyError = true;
                CurrencyErrorMessage = "No internet connection. Exchange rates are required to convert existing transactions.".Translate();
                return false;
            }
            // No transactions, allow the change without rates
            return true;
        }

        CurrencyLoadingProgress = 20;
        cancellationToken.ThrowIfCancellationRequested();

        // Try to get exchange rate for today
        var today = DateTime.Today;
        var rate = await exchangeService.GetExchangeRateAsync(currencyCode, "USD", today, fetchIfMissing: true, cancellationToken: cancellationToken);

        if (rate <= 0)
        {
            if (hasTransactions)
            {
                HasCurrencyError = true;
                CurrencyErrorMessage = "Unable to fetch exchange rates. Please check your connection and try again.".Translate();
                return false;
            }
            // No transactions, allow the change even if rate fetch failed
            return true;
        }

        CurrencyLoadingProgress = 30;
        cancellationToken.ThrowIfCancellationRequested();

        // Preload exchange rates for all transaction dates so conversions are ready
        try
        {
            var dates = new List<DateTime> { today };
            if (companyData != null)
            {
                dates.AddRange(companyData.Expenses.Select(e => e.Date));
                dates.AddRange(companyData.Revenues.Select(r => r.Date));
                dates.AddRange(companyData.Invoices.Select(i => i.IssueDate));
                dates.AddRange(companyData.Payments.Select(p => p.Date));
                dates.AddRange(companyData.PurchaseOrders.Select(o => o.OrderDate));
            }
            // Map PreloadRatesAsync progress (0-100) into our 30-100 range
            var progress = new Progress<int>(p => CurrencyLoadingProgress = 30 + (p * 70 / 100));
            await exchangeService.PreloadRatesAsync(dates, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Preloading additional dates failed, but we have today's rate so it's OK
        }

        CurrencyLoadingProgress = 100;
        return true;
    }

    /// <summary>
    /// Opens the logo file picker.
    /// </summary>
    [RelayCommand]
    private void BrowseLogo()
    {
        BrowseLogoRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes the current logo.
    /// </summary>
    [RelayCommand]
    private void RemoveLogo()
    {
        LogoSource = null;
        HasLogo = false;
        LogoPath = null;
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>
    /// Sets the logo from a file.
    /// </summary>
    public void SetLogo(string path, Bitmap bitmap)
    {
        LogoPath = path;
        LogoSource = bitmap;
        HasLogo = true;
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnCompanyNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnBusinessTypeChanged(string? value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnIndustryChanged(string? value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnPhoneNumberChanged(string value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnSelectedPhoneCountryChanged(CountryDialCode? value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnCountryChanged(string? value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasChanges));
    }
    partial void OnCityChanged(string? value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnAddressChanged(string? value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnProvinceStateChanged(string? value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnEmailChanged(string value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnSelectedCurrencyChanged(string value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnIsSavingCurrencyChanged(bool value) => OnPropertyChanged(nameof(ShowCurrencyLoading));
    partial void OnHasCurrencyErrorChanged(bool value) => OnPropertyChanged(nameof(ShowCurrencyLoading));

    /// <summary>
    /// Event raised when the company is saved.
    /// </summary>
    public event EventHandler<CompanyEditedEventArgs>? CompanySaved;

    /// <summary>
    /// Event raised when the logo browse button is clicked.
    /// </summary>
    public event EventHandler? BrowseLogoRequested;
}

/// <summary>
/// Event args for when company is edited.
/// </summary>
public class CompanyEditedEventArgs : EventArgs
{
    public string CompanyName { get; set; } = "";
    public string? BusinessType { get; set; }
    public string? Industry { get; set; }
    public Bitmap? LogoSource { get; set; }
    public string? LogoPath { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? ProvinceState { get; set; }
}
