using ArgoBooks.Controls;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Company Creation Wizard.
/// </summary>
public partial class CreateCompanyViewModel : ViewModelBase
{
    #region Wizard State

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private int _currentStep = 1;

    private const int TotalSteps = 2;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;

    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep < TotalSteps;
    public bool IsLastStep => CurrentStep == TotalSteps;

    #endregion

    #region Step 1: Company Info

    [ObservableProperty]
    private string? _companyName;

    [ObservableProperty]
    private string? _businessType;

    [ObservableProperty]
    private string? _industry;

    public string[] BusinessTypes { get; } =
    [
        "Sole Proprietorship",
        "Partnership",
        "Corporation",
        "LLC",
        "Non-Profit",
        "Other"
    ];

    public string[] Industries { get; } = Core.Models.IndustryNames.All;

    [ObservableProperty]
    private string _selectedCurrency = "CAD - Canadian Dollar ($)";

    /// <summary>
    /// All available currencies.
    /// </summary>
    public IReadOnlyList<string> Currencies => Data.Currencies.All;

    /// <summary>
    /// Priority/common currencies shown at the top of the dropdown.
    /// </summary>
    public IReadOnlyList<string> PriorityCurrencies => Data.Currencies.Priority;

    #endregion

    #region Step 1: Contact Information

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
    private string? _provinceState;

    [ObservableProperty]
    private string? _address;

    [ObservableProperty]
    private string _email = "";

    #endregion

    #region Step 2: Security & Logo

    [ObservableProperty]
    private bool _enablePassword;

    [ObservableProperty]
    private string? _password;

    [ObservableProperty]
    private string? _confirmPassword;

    [ObservableProperty]
    private bool _hasLogo;

    [ObservableProperty]
    private Bitmap? _logoSource;

    [ObservableProperty]
    private string? _logoPath;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private bool _isConfirmPasswordVisible;

    [ObservableProperty]
    private bool _showPasswordStrength;

    [ObservableProperty]
    private int _passwordStrengthScore;

    [ObservableProperty]
    private string _passwordStrengthText = string.Empty;

    public string PasswordVisibilityIcon => IsPasswordVisible ? Icons.EyeOff : Icons.Eye;

    public string ConfirmPasswordVisibilityIcon => IsConfirmPasswordVisible ? Icons.EyeOff : Icons.Eye;

    /// <summary>
    /// Mask character for the password box, cleared while the password is revealed.
    ///
    /// Revealing is done by dropping the mask character rather than by setting
    /// RevealPassword, because Avalonia 12.0.5 treats any box with a mask character as a
    /// password box and silently disables Ctrl+Arrow word movement, Ctrl+Shift+Arrow
    /// selection and Ctrl+Backspace, regardless of RevealPassword. Clearing the character
    /// makes it an ordinary text box again, so those shortcuts work while it is revealed.
    /// </summary>
    public char PasswordMaskChar => IsPasswordVisible ? '\0' : '*';

    /// <inheritdoc cref="PasswordMaskChar" />
    public char ConfirmPasswordMaskChar => IsConfirmPasswordVisible ? '\0' : '*';

    public bool IsStrengthWeak => PasswordStrengthScore < 40;

    public bool IsStrengthFair => PasswordStrengthScore is >= 40 and < 70;

    public bool IsStrengthStrong => PasswordStrengthScore >= 70;

    public bool PasswordsMatch => Password == ConfirmPassword;

    public bool ShowPasswordError => EnablePassword && !string.IsNullOrEmpty(ConfirmPassword) && !PasswordsMatch;

    /// <summary>
    /// The unmet password requirement, or null when the password is acceptable.
    ///
    /// Holds back until the user has typed something, so the field doesn't greet them with
    /// an error before they have had a chance to enter anything.
    /// </summary>
    public string? PasswordRequirementError => EnablePassword && !string.IsNullOrEmpty(Password)
        ? Core.Security.PasswordValidator.GetValidationError(Password)
        : null;

    public bool ShowPasswordRequirementError => PasswordRequirementError != null;

    #endregion

    #region Validation

    public bool IsStep1Valid => !string.IsNullOrWhiteSpace(CompanyName) && !string.IsNullOrWhiteSpace(Country);

    // Applies the same strength rules as Settings > Security, so a company file cannot be
    // created with a password that would be rejected if set later.
    public bool IsStep2Valid => !EnablePassword ||
                                (PasswordsMatch && Core.Security.PasswordValidator.IsValid(Password));

    public bool CanCreate => IsStep1Valid && IsStep2Valid;

    #endregion

    #region Change Detection

    public bool HasChanges =>
        !string.IsNullOrEmpty(CompanyName) ||
        !string.IsNullOrEmpty(BusinessType) ||
        !string.IsNullOrEmpty(Industry) ||
        SelectedCurrency != "CAD - Canadian Dollar ($)" ||
        !string.IsNullOrEmpty(PhoneNumber) ||
        SelectedPhoneCountry != null ||
        !string.IsNullOrEmpty(Country) ||
        !string.IsNullOrEmpty(City) ||
        !string.IsNullOrEmpty(ProvinceState) ||
        !string.IsNullOrEmpty(Address) ||
        !string.IsNullOrEmpty(Email) ||
        EnablePassword ||
        !string.IsNullOrEmpty(Password) ||
        !string.IsNullOrEmpty(ConfirmPassword) ||
        HasLogo;

    #endregion

    /// <summary>
    /// Event raised when a company is created.
    /// </summary>
    public event EventHandler<CompanyCreatedEventArgs>? CompanyCreated;

    #region Commands

    [RelayCommand]
    private void Open()
    {
        Reset();
        IsOpen = true;

        // Here rather than at each caller, so every route in is counted.
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.CompanyCreateOpened);
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        await RequestCloseAsync();
    }

    public async void RequestClose()
    {
        try
        {
            await RequestCloseAsync();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.UI, "CreateCompany.RequestClose");
        }
    }

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
                    Message = "You have unsaved changes. Are you sure you want to close?".Translate(),
                    PrimaryButtonText = "Don't Save".Translate(),
                    CancelButtonText = "Cancel".Translate(),
                    IsPrimaryDestructive = true
                });

                switch (result)
                {
                    case ConfirmationResult.Primary:
                        IsOpen = false;
                        Reset();
                        return;
                    case ConfirmationResult.Cancel:
                    case ConfirmationResult.None:
                        return;
                }
            }
        }

        IsOpen = false;
        Reset();
    }

    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (CurrentStep >= TotalSteps)
            return;

        // Leaving step 1 (which holds the country and currency): warn, but allow,
        // when the chosen currency doesn't match the country.
        if (CurrentStep == 1)
        {
            var currencyCode = CurrencyService.ParseCurrencyCode(SelectedCurrency);
            if (!await CurrencyCountryMatcher.ConfirmIfMismatchAsync(Country, currencyCode))
                return;
        }

        CurrentStep++;
        UpdateStepProperties();
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            UpdateStepProperties();
        }
    }

    [RelayCommand]
    private void GoToStep(int step)
    {
        if (step >= 1 && step <= TotalSteps)
        {
            CurrentStep = step;
            UpdateStepProperties();
        }
    }

    [RelayCommand]
    private void BrowseLogo()
    {
        // This will be handled by the view to open file picker
        BrowseLogoRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveLogo()
    {
        LogoSource = null;
        LogoPath = null;
        HasLogo = false;
    }

    [RelayCommand]
    private void CreateCompany()
    {
        if (!CanCreate) return;

        PhoneNumberError = null;

        // Validate phone number completeness
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

        var args = new CompanyCreatedEventArgs
        {
            CompanyName = CompanyName!,
            BusinessType = BusinessType,
            Industry = Industry,
            Address = Address,
            City = City,
            ProvinceState = ProvinceState,
            Country = Country,
            PhoneNumber = fullPhone,
            Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
            DefaultCurrency = CurrencyService.ParseCurrencyCode(SelectedCurrency),
            Password = EnablePassword ? Password : null,
            LogoPath = LogoPath
        };

        CompanyCreated?.Invoke(this, args);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.CompanyCreated);
        IsOpen = false;
        Reset();
    }

    #endregion

    /// <summary>
    /// Event raised when browse logo is requested.
    /// </summary>
    public event EventHandler? BrowseLogoRequested;

    /// <summary>
    /// Sets the logo from file path.
    /// </summary>
    public void SetLogo(string path, Bitmap? bitmap)
    {
        LogoPath = path;
        LogoSource = bitmap;
        HasLogo = bitmap != null;
    }

    private void Reset()
    {
        CurrentStep = 1;
        CompanyName = null;
        BusinessType = null;
        Industry = null;
        SelectedCurrency = "CAD - Canadian Dollar ($)";
        PhoneNumber = "";
        PhoneNumberError = null;
        SelectedPhoneCountry = null;
        Country = null;
        City = null;
        ProvinceState = null;
        Address = null;
        Email = "";
        EnablePassword = false;
        Password = null;
        ConfirmPassword = null;
        // Don't leave a password revealed for whoever opens the wizard next.
        IsPasswordVisible = false;
        IsConfirmPasswordVisible = false;
        LogoSource = null;
        LogoPath = null;
        HasLogo = false;
        UpdateStepProperties();
    }

    private void UpdateStepProperties()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsLastStep));
    }

    partial void OnCompanyNameChanged(string? value)
    {
        OnPropertyChanged(nameof(IsStep1Valid));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnPasswordChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            ShowPasswordStrength = false;
            PasswordStrengthScore = 0;
            PasswordStrengthText = string.Empty;
        }
        else
        {
            ShowPasswordStrength = true;
            PasswordStrengthScore = Core.Security.PasswordValidator.GetStrengthScore(value);
            PasswordStrengthText = Core.Security.PasswordValidator.GetStrengthDescription(PasswordStrengthScore);
        }

        OnPropertyChanged(nameof(IsStrengthWeak));
        OnPropertyChanged(nameof(IsStrengthFair));
        OnPropertyChanged(nameof(IsStrengthStrong));
        OnPropertyChanged(nameof(PasswordsMatch));
        OnPropertyChanged(nameof(ShowPasswordError));
        OnPropertyChanged(nameof(PasswordRequirementError));
        OnPropertyChanged(nameof(ShowPasswordRequirementError));
        OnPropertyChanged(nameof(IsStep2Valid));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordVisibilityIcon));
        OnPropertyChanged(nameof(PasswordMaskChar));
    }

    partial void OnIsConfirmPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ConfirmPasswordVisibilityIcon));
        OnPropertyChanged(nameof(ConfirmPasswordMaskChar));
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private void ToggleConfirmPasswordVisibility() => IsConfirmPasswordVisible = !IsConfirmPasswordVisible;

    partial void OnConfirmPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordsMatch));
        OnPropertyChanged(nameof(ShowPasswordError));
        OnPropertyChanged(nameof(IsStep2Valid));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnCountryChanged(string? value)
    {
        OnPropertyChanged(nameof(IsStep1Valid));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnEnablePasswordChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordRequirementError));
        OnPropertyChanged(nameof(ShowPasswordRequirementError));
        OnPropertyChanged(nameof(ShowPasswordError));
        OnPropertyChanged(nameof(IsStep2Valid));
        OnPropertyChanged(nameof(CanCreate));
    }
}

/// <summary>
/// Event arguments for company creation.
/// </summary>
public class CompanyCreatedEventArgs : EventArgs
{
    public required string CompanyName { get; init; }
    public string? BusinessType { get; init; }
    public string? Industry { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Country { get; init; }
    public string? City { get; init; }
    public string? ProvinceState { get; init; }
    public string? Address { get; init; }
    public string? Email { get; init; }
    public string? DefaultCurrency { get; init; }
    public string? Password { get; init; }
    public string? LogoPath { get; init; }
}
