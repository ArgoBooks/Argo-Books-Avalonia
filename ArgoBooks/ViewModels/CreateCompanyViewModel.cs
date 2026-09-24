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

    /// <summary>
    /// Asked here, rather than left to Settings, because it decides which sidebar sections the
    /// company opens with. See <see cref="Core.Models.IndustryFeatureDefaults"/>.
    /// </summary>
    [ObservableProperty]
    private string? _industry;

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

    /// <summary>
    /// Kept in the wizard because it is load-bearing well beyond the invoice header: tax labels,
    /// the accounting reports and the spreadsheet importer's parsing conventions all read it.
    /// Asking for it later would mean producing wrong output until someone thought to set it.
    ///
    /// The rest of the address, along with phone and email, is asked at the first invoice, which
    /// is the point where it means something to the person filling it in.
    /// </summary>
    [ObservableProperty]
    private string? _country;

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
        !string.IsNullOrEmpty(Industry) ||
        SelectedCurrency != "CAD - Canadian Dollar ($)" ||
        !string.IsNullOrEmpty(Country) ||
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

        var args = new CompanyCreatedEventArgs
        {
            CompanyName = CompanyName!,
            Industry = Industry,
            Country = Country,
            DefaultCurrency = CurrencyService.ParseCurrencyCode(SelectedCurrency),
            Password = EnablePassword ? Password : null,
            LogoPath = LogoPath
        };

        CompanyCreated?.Invoke(this, args);
    }

    /// <summary>
    /// Closes and clears the wizard once a file has been chosen. Called by the handler rather than
    /// by <see cref="CreateCompany"/>, whose save dialog can still be cancelled.
    /// </summary>
    public void CompleteCreation()
    {
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
        Industry = null;
        SelectedCurrency = "CAD - Canadian Dollar ($)";
        Country = null;
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
    public string? Industry { get; init; }
    public string? Country { get; init; }
    public string? DefaultCurrency { get; init; }
    public string? Password { get; init; }
    public string? LogoPath { get; init; }
}
