using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using ArgoBooks.Core;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Integrations;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Integrations;
using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Core.Validation;
using ArgoBooks.Data;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Sync;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Settings modal.
/// </summary>
public partial class SettingsModalViewModel : ViewModelBase
{
    // Store original values for reverting on cancel
    private string _originalTheme;
    private string _originalAccentColor;
    private string _originalLanguage = "English";
    private string _originalDateFormat = "MM/DD/YYYY";
    private TimeZoneItem _originalTimeZone = TimeZones.FindById("UTC");
    private string _originalTimeFormat = "12h";
    private int _originalMaxPieSlices = 6;
    private bool _originalLowStockAlert = true;
    private bool _originalOutOfStockAlert = true;
    private bool _originalInvoiceOverdue = true;
    private bool _originalRentalOverdue = true;
    private bool _originalUnsavedChangesReminder = true;
    private int _originalUnsavedChangesReminderMinutes = 5;

    // Flag to prevent firing LanguageChanged when loading from settings
    private bool _isLoadingLanguage;

    // Flag to indicate if language download is in progress
    [ObservableProperty]
    private bool _isDownloadingLanguage;

    /// <summary>
    /// Event raised when language changes.
    /// </summary>
    public event EventHandler<LanguageSettingsChangedEventArgs>? LanguageSettingsChanged;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private int _selectedTabIndex;

    #region General Settings

    [ObservableProperty]
    private string _selectedLanguage = "English";

    /// <summary>
    /// Called when language selection changes.
    /// </summary>
    partial void OnSelectedLanguageChanged(string value)
    {
        // Don't fire event when loading from company settings
        if (_isLoadingLanguage || string.IsNullOrEmpty(value)) return;

        // Fire event to notify language change is requested
        // The actual language service update happens on save
        LanguageSettingsChanged?.Invoke(this, new LanguageSettingsChangedEventArgs(value, false));
    }

    /// <summary>
    /// Sets the language value without triggering the change event.
    /// Used when syncing UI with company settings on load.
    /// </summary>
    public void SetLanguageWithoutNotify(string value)
    {
        _isLoadingLanguage = true;
        SelectedLanguage = value;
        _isLoadingLanguage = false;
    }

    [ObservableProperty]
    private string _selectedDateFormat = "MM/DD/YYYY";

    [ObservableProperty]
    private TimeZoneItem _selectedTimeZone = TimeZones.FindById("UTC");

    [ObservableProperty]
    private string _selectedTimeFormat = "12h";

    /// <summary>
    /// Whether the 12-hour time format is selected.
    /// </summary>
    public bool Is12HourFormat => SelectedTimeFormat == "12h";

    /// <summary>
    /// Whether the 24-hour time format is selected.
    /// </summary>
    public bool Is24HourFormat => SelectedTimeFormat == "24h";

    partial void OnSelectedTimeFormatChanged(string value)
    {
        OnPropertyChanged(nameof(Is12HourFormat));
        OnPropertyChanged(nameof(Is24HourFormat));
    }

    /// <summary>
    /// Selects the time format.
    /// </summary>
    [RelayCommand]
    private void SelectTimeFormat(string format)
    {
        if (!string.IsNullOrEmpty(format))
        {
            SelectedTimeFormat = format;
        }
    }

    [ObservableProperty]
    private int _telemetryEventCount;

    [ObservableProperty]
    private int _telemetryPendingCount;

    [ObservableProperty]
    private bool _isExportingTelemetry;

    [ObservableProperty]
    private bool _isDeletingTelemetry;

    [ObservableProperty]
    private int _maxPieSlices = 6;

    /// <summary>
    /// Available options for max pie slices.
    /// </summary>
    public int[] MaxPieSlicesOptions { get; } = [4, 5, 6, 7, 8, 10, 12];

    /// <summary>
    /// Priority/common languages shown at the top of the dropdown.
    /// </summary>
    public IReadOnlyList<string> PriorityLanguages => Data.Languages.Priority;

    /// <summary>
    /// All available languages.
    /// </summary>
    public IReadOnlyList<string> Languages => Data.Languages.All;

    public ObservableCollection<string> DateFormats { get; } =
    [
        "MM/DD/YYYY",
        "DD/MM/YYYY",
        "YYYY-MM-DD",
        "MMM D, YYYY"
    ];

    /// <summary>
    /// All available timezone options from the system.
    /// </summary>
    public IReadOnlyList<TimeZoneItem> AllTimeZones => TimeZones.All;

    /// <summary>
    /// Priority timezone options shown at the top of the dropdown.
    /// </summary>
    public IReadOnlyList<TimeZoneItem> PriorityTimeZones => TimeZones.Priority;

    #endregion

    #region Notification Settings

    [ObservableProperty]
    private bool _lowStockAlert = true;

    [ObservableProperty]
    private bool _outOfStockAlert = true;

    [ObservableProperty]
    private bool _invoiceOverdue = true;

    [ObservableProperty]
    private bool _rentalOverdue = true;

    [ObservableProperty]
    private bool _unsavedChangesReminder = true;

    [ObservableProperty]
    private int _unsavedChangesReminderMinutes = 5;

    /// <summary>
    /// Available options for the unsaved changes reminder minutes.
    /// </summary>
    public int[] ReminderMinuteOptions { get; } = [5, 10, 15, 30, 45, 60];

    #endregion

    #region Appearance Settings

    [ObservableProperty]
    private string _selectedTheme;

    [ObservableProperty]
    private string _selectedAccentColor;

    public ObservableCollection<string> Themes { get; } = new(ThemeModeExtensions.GetAllDisplayNames());

    public ObservableCollection<AccentColorItem> AccentColors { get; } =
    [
        new("Blue", AppColors.Primary),
        new("Green", AppColors.Emerald),
        new("Purple", AppColors.Violet),
        new("Pink", AppColors.Pink),
        new("Orange", AppColors.Orange),
        new("Teal", AppColors.Teal)
    ];

    #endregion

    #region Security Settings

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditPaymentReminders))]
    [NotifyPropertyChangedFor(nameof(ShowConnectRequirementsHint))]
    private bool _isSampleCompany;

    [ObservableProperty]
    private bool _hasPremium; // Whether user has Premium plan

    [ObservableProperty]
    private bool _biometricLoginEnabled;

    [ObservableProperty]
    private string _selectedAutoLock = "5 minutes";

    [ObservableProperty]
    private bool _hasPassword;

    /// <summary>
    /// Whether biometric login can be enabled (requires Premium plan AND password).
    /// </summary>
    public bool CanEnableBiometricLogin => HasPremium && HasPassword;

    /// <summary>
    /// Whether the user needs to set a password before enabling biometric login.
    /// Shows when user has Premium plan but no password.
    /// </summary>
    public bool NeedsPasswordForBiometricLogin => HasPremium && !HasPassword;

    /// <summary>
    /// Whether the user needs to set a password before enabling Auto-Lock.
    /// </summary>
    public bool NeedsPasswordForAutoLock => !HasPassword;

    /// <summary>
    /// Event raised when biometric login setting changes (after successful authentication).
    /// </summary>
    public event EventHandler<BiometricLoginEventArgs>? BiometricLoginChanged;

    /// <summary>
    /// Event raised to request biometric login authentication before enabling.
    /// The handler should authenticate and call OnBiometricAuthResult with the result.
    /// </summary>
    public event EventHandler? BiometricAuthRequested;

    // Flag to prevent recursive updates when setting biometric login programmatically
    private bool _isUpdatingBiometricLogin;

    /// <summary>
    /// Event raised when user wants to upgrade their plan.
    /// </summary>
    public event EventHandler? UpgradeRequested;

    [ObservableProperty]
    private bool _isAddPasswordModalOpen;

    /// <summary>
    /// Event raised when a password should be added to the company file.
    /// </summary>
    public event EventHandler<PasswordChangeEventArgs>? AddPasswordRequested;

    /// <summary>
    /// Event raised when the password should be changed.
    /// </summary>
    public event EventHandler<PasswordChangeEventArgs>? ChangePasswordRequested;

    /// <summary>
    /// Event raised when the password should be removed.
    /// </summary>
    public event EventHandler<PasswordChangeEventArgs>? RemovePasswordRequested;

    /// <summary>
    /// Event raised when a password textbox should be focused (e.g., after error).
    /// </summary>
    public event EventHandler? FocusPasswordRequested;

    [ObservableProperty]
    private bool _isChangePasswordModalOpen;

    [ObservableProperty]
    private bool _isRemovePasswordModalOpen;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private int _passwordStrengthScore;

    [ObservableProperty]
    private string _passwordStrengthText = string.Empty;

    [ObservableProperty]
    private bool _showPasswordStrength;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string? _passwordError;

    [ObservableProperty]
    private bool _isNewPasswordVisible;

    [ObservableProperty]
    private bool _isConfirmPasswordVisible;

    [ObservableProperty]
    private bool _isCurrentPasswordVisible;

    public string NewPasswordVisibilityIcon => IsNewPasswordVisible ? Icons.EyeOff : Icons.Eye;
    public string ConfirmPasswordVisibilityIcon => IsConfirmPasswordVisible ? Icons.EyeOff : Icons.Eye;
    public string CurrentPasswordVisibilityIcon => IsCurrentPasswordVisible ? Icons.EyeOff : Icons.Eye;

    /// <summary>
    /// Mask characters for the password boxes, cleared while a password is revealed.
    ///
    /// Revealing is done by dropping the mask character rather than by setting
    /// RevealPassword, because Avalonia 12.0.5 treats any box with a mask character as a
    /// password box and silently disables Ctrl+Arrow word movement, Ctrl+Shift+Arrow
    /// selection and Ctrl+Backspace, regardless of RevealPassword. Clearing the character
    /// makes it an ordinary text box again, so those shortcuts work while it is revealed.
    /// </summary>
    public char NewPasswordMaskChar => IsNewPasswordVisible ? '\0' : '*';

    /// <inheritdoc cref="NewPasswordMaskChar" />
    public char ConfirmPasswordMaskChar => IsConfirmPasswordVisible ? '\0' : '*';

    /// <inheritdoc cref="NewPasswordMaskChar" />
    public char CurrentPasswordMaskChar => IsCurrentPasswordVisible ? '\0' : '*';

    /// <summary>
    /// Width in pixels for the strength bar, scaled to fit the password modal content area.
    /// </summary>
    public double PasswordStrengthBarWidth => PasswordStrengthScore / 100.0 * 290;

    /// <summary>
    /// Whether the password strength is weak (red).
    /// </summary>
    public bool IsStrengthWeak => PasswordStrengthScore < 40;

    /// <summary>
    /// Whether the password strength is fair (yellow/warning).
    /// </summary>
    public bool IsStrengthFair => PasswordStrengthScore is >= 40 and < 70;

    /// <summary>
    /// Whether the password strength is strong (green).
    /// </summary>
    public bool IsStrengthStrong => PasswordStrengthScore >= 70;

    partial void OnNewPasswordChanged(string value)
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
        OnPropertyChanged(nameof(PasswordStrengthBarWidth));
        OnPropertyChanged(nameof(IsStrengthWeak));
        OnPropertyChanged(nameof(IsStrengthFair));
        OnPropertyChanged(nameof(IsStrengthStrong));
    }

    partial void OnIsNewPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(NewPasswordVisibilityIcon));
        OnPropertyChanged(nameof(NewPasswordMaskChar));
    }

    partial void OnIsConfirmPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ConfirmPasswordVisibilityIcon));
        OnPropertyChanged(nameof(ConfirmPasswordMaskChar));
    }

    partial void OnIsCurrentPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentPasswordVisibilityIcon));
        OnPropertyChanged(nameof(CurrentPasswordMaskChar));
    }

    // Flag to prevent firing AutoLockSettingsChanged when syncing UI with company settings
    private bool _isLoadingAutoLock;

    /// <summary>
    /// Called when HasPassword changes - notify dependent properties.
    /// </summary>
    partial void OnHasPasswordChanged(bool value)
    {
        // Notify biometric login and Auto-Lock computed properties
        OnPropertyChanged(nameof(CanEnableBiometricLogin));
        OnPropertyChanged(nameof(NeedsPasswordForBiometricLogin));
        OnPropertyChanged(nameof(NeedsPasswordForAutoLock));

        // Disable biometric login if password is removed
        if (!value && BiometricLoginEnabled)
        {
            BiometricLoginEnabled = false;
        }
    }

    /// <summary>
    /// Called when HasPremium changes - notify biometric login properties.
    /// </summary>
    partial void OnHasPremiumChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEnableBiometricLogin));
        OnPropertyChanged(nameof(NeedsPasswordForBiometricLogin));
    }

    /// <summary>
    /// Called when biometric login setting changes.
    /// </summary>
    partial void OnBiometricLoginEnabledChanged(bool value)
    {
        // Skip if we're programmatically updating (e.g., after auth result)
        if (_isUpdatingBiometricLogin) return;

        if (value)
        {
            // User is trying to enable - request authentication first
            BiometricAuthRequested?.Invoke(this, EventArgs.Empty);
            // The actual enabling will happen in OnBiometricAuthResult
        }
        else
        {
            // Disabling doesn't require authentication
            BiometricLoginChanged?.Invoke(this, new BiometricLoginEventArgs(false));
        }
    }

    /// <summary>
    /// Called after biometric login authentication attempt.
    /// </summary>
    /// <param name="success">Whether authentication was successful.</param>
    public void OnBiometricAuthResult(bool success)
    {
        _isUpdatingBiometricLogin = true;
        if (success)
        {
            // Authentication succeeded - keep enabled and fire event
            BiometricLoginChanged?.Invoke(this, new BiometricLoginEventArgs(true));
        }
        else
        {
            // Authentication failed or cancelled - revert the toggle
            BiometricLoginEnabled = false;
        }
        _isUpdatingBiometricLogin = false;
    }

    /// <summary>
    /// Sets biometric login enabled state without triggering authentication.
    /// Used when loading settings from company file.
    /// </summary>
    public void SetBiometricLoginWithoutAuth(bool enabled)
    {
        _isUpdatingBiometricLogin = true;
        BiometricLoginEnabled = enabled;
        _isUpdatingBiometricLogin = false;
    }

    /// <summary>
    /// Called when auto-lock setting changes.
    /// </summary>
    partial void OnSelectedAutoLockChanged(string value)
    {
        // Don't fire event when loading from company settings
        if (_isLoadingAutoLock) return;

        AutoLockSettingsChanged?.Invoke(this, new AutoLockSettingsEventArgs(value));
    }

    /// <summary>
    /// Sets the auto-lock value without triggering the change event.
    /// Used when syncing UI with company settings on load.
    /// </summary>
    public void SetAutoLockWithoutNotify(string value)
    {
        _isLoadingAutoLock = true;
        SelectedAutoLock = value;
        _isLoadingAutoLock = false;
    }

    /// <summary>
    /// Event raised when auto-lock settings change.
    /// </summary>
    public event EventHandler<AutoLockSettingsEventArgs>? AutoLockSettingsChanged;

    public ObservableCollection<string> AutoLockOptions { get; } =
    [
        "Never",
        "5 minutes",
        "15 minutes",
        "30 minutes",
        "1 hour"
    ];

    #endregion

    #region Payment Portal Settings

    /// <summary>
    /// Whether the user has authenticated to modify portal settings in this session.
    /// Reset when the settings modal is reopened.
    /// </summary>
    private bool _isPortalAuthenticated;

    // Flag to suppress auth check when loading portal settings from company data
    private bool _isLoadingPortalSettings;

    /// <summary>
    /// Event raised when portal authentication is required before making changes.
    /// The handler should verify the user's identity (password or biometric) and call
    /// <see cref="OnPortalAuthResult"/> with the result.
    /// </summary>
    public event Func<Task<bool>>? PortalAuthenticationRequested;

    /// <summary>
    /// Ensures the user has authenticated to modify portal settings.
    /// On first interaction, prompts for password/biometric. Once authenticated,
    /// subsequent changes are allowed without re-prompting.
    /// </summary>
    /// <returns>True if authenticated, false if cancelled or no password is set.</returns>
    private async Task<bool> EnsurePortalAuthenticatedAsync()
    {
        if (_isPortalAuthenticated) return true;
        if (!HasPassword) return true; // No password set, no auth needed

        if (PortalAuthenticationRequested != null)
        {
            var result = await PortalAuthenticationRequested.Invoke();
            if (result)
            {
                _isPortalAuthenticated = true;
                return true;
            }
            return false;
        }

        // No handler wired up, allow by default
        return true;
    }

    /// <summary>
    /// Called by App.axaml.cs to set the portal authentication result.
    /// </summary>
    public void OnPortalAuthResult(bool success)
    {
        _isPortalAuthenticated = success;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPortalCompanyName))]
    [NotifyPropertyChangedFor(nameof(CanConnectProvider))]
    [NotifyPropertyChangedFor(nameof(ShowConnectRequirementsHint))]
    private string _portalCompanyName = string.Empty;

    private CancellationTokenSource? _portalCompanyNameCts;

    /// <summary>
    /// Called when PortalCompanyName changes, sends the updated name to the portal server.
    /// </summary>
    partial void OnPortalCompanyNameChanged(string value)
    {
        if (_isLoadingPortalSettings) return;
        if (!HasPassword || _isPortalAuthenticated)
        {
            ScheduleCompanyNameUpdate(value);
            return;
        }

        // Revert and request auth
        _ = RevertAndAuthPortalCompanyNameAsync(value);
    }

    private async Task RevertAndAuthPortalCompanyNameAsync(string attemptedValue)
    {
        _isLoadingPortalSettings = true;
        PortalCompanyName = string.Empty;
        _isLoadingPortalSettings = false;

        if (await EnsurePortalAuthenticatedAsync())
        {
            _isLoadingPortalSettings = true;
            PortalCompanyName = attemptedValue;
            _isLoadingPortalSettings = false;
            ScheduleCompanyNameUpdate(attemptedValue);
        }
    }

    private void ScheduleCompanyNameUpdate(string value)
    {
        // Debounce: cancel and dispose any pending update, then schedule a new one
        var previousCts = _portalCompanyNameCts;
        if (previousCts != null)
        {
            previousCts.Cancel();
            previousCts.Dispose();
        }
        _portalCompanyNameCts = new CancellationTokenSource();
        var token = _portalCompanyNameCts.Token;

        _ = UpdatePortalCompanyNameAsync(value, token);
    }

    private async Task UpdatePortalCompanyNameAsync(string name, CancellationToken cancellationToken)
    {
        // Debounce: wait 600ms before sending the request
        try { await Task.Delay(600, cancellationToken); }
        catch (TaskCanceledException) { return; }

        // A blank name is only allowed while no payment provider is connected
        if (string.IsNullOrWhiteSpace(name) && IsPortalCompanyNameRequired) return;

        var portalService = App.PaymentPortalService;
        if (portalService == null || !PortalSettings.IsConfigured) return;

        try
        {
            await portalService.UpdateCompanyNameAsync(name.Trim(), cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // Debounce cancelled, ignore
        }
        catch
        {
            // Silently fail, user can retry
        }
    }

    [ObservableProperty]
    private bool _portalNotifyOnPayment = true;

    partial void OnHasPortalLogoChanged(bool value) => OnPropertyChanged(nameof(PortalLogoButtonText));

    /// <summary>
    /// Called when PortalNotifyOnPayment changes, requires auth if password is enabled.
    /// </summary>
    partial void OnPortalNotifyOnPaymentChanged(bool value)
    {
        if (_isLoadingPortalSettings) return;
        if (!HasPassword || _isPortalAuthenticated) return;

        // Revert and request auth
        _ = RevertAndAuthPortalNotifyAsync(value);
    }

    private async Task RevertAndAuthPortalNotifyAsync(bool attemptedValue)
    {
        // Revert to opposite while we authenticate
        _isLoadingPortalSettings = true;
        PortalNotifyOnPayment = !attemptedValue;
        _isLoadingPortalSettings = false;

        if (await EnsurePortalAuthenticatedAsync())
        {
            _isLoadingPortalSettings = true;
            PortalNotifyOnPayment = attemptedValue;
            _isLoadingPortalSettings = false;
        }
    }

    /// <summary>
    /// Whether the server emails the owner when a customer pays. Separate from
    /// <see cref="PortalNotifyOnPayment"/>, which is only the in-app popup.
    /// </summary>
    [ObservableProperty]
    private bool _portalEmailOwnerOnPayment = true;

    /// <summary>
    /// Whether the server chases unpaid invoices at 3, 7 and 14 days past due.
    /// Opt-in: turning this on emails real customers.
    /// </summary>
    [ObservableProperty]
    private bool _portalSendPaymentReminders;

    /// <summary>
    /// Whether the owner's email is verified server-side. The server refuses to
    /// send payment notifications without it, so the toggle is disabled and
    /// explained rather than silently doing nothing.
    /// </summary>
    [ObservableProperty]
    private bool _portalOwnerEmailVerified;

    /// <summary>
    /// When reminders were switched on. Only invoices due after this are ever
    /// chased, which the UI explains so nobody expects old invoices to go out.
    /// </summary>
    [ObservableProperty]
    private DateTime? _portalRemindersEnabledAt;

    partial void OnPortalEmailOwnerOnPaymentChanged(bool value)
    {
        if (_isLoadingPortalSettings) return;
        if (HasPassword && !_isPortalAuthenticated)
        {
            _ = RevertAndAuthPortalEmailOwnerAsync(value);
            return;
        }

        // Nothing is pushed here on purpose. These sit among the other
        // Notifications toggles, which all apply on Save, so pushing on toggle
        // would make Close-without-saving silently keep the change and skip the
        // unsaved-changes prompt. SaveAsync sends them.
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private async Task RevertAndAuthPortalEmailOwnerAsync(bool attemptedValue)
    {
        _isLoadingPortalSettings = true;
        PortalEmailOwnerOnPayment = !attemptedValue;
        _isLoadingPortalSettings = false;

        if (await EnsurePortalAuthenticatedAsync())
        {
            _isLoadingPortalSettings = true;
            PortalEmailOwnerOnPayment = attemptedValue;
            _isLoadingPortalSettings = false;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    partial void OnPortalSendPaymentRemindersChanged(bool value)
    {
        if (_isLoadingPortalSettings) return;
        if (HasPassword && !_isPortalAuthenticated)
        {
            _ = RevertAndAuthPortalRemindersAsync(value);
            return;
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private async Task RevertAndAuthPortalRemindersAsync(bool attemptedValue)
    {
        _isLoadingPortalSettings = true;
        PortalSendPaymentReminders = !attemptedValue;
        _isLoadingPortalSettings = false;

        if (await EnsurePortalAuthenticatedAsync())
        {
            _isLoadingPortalSettings = true;
            PortalSendPaymentReminders = attemptedValue;
            _isLoadingPortalSettings = false;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    /// <summary>
    /// Sends both preference toggles to the server and adopts whatever it echoes
    /// back, which includes the reminder cutoff it just armed. Called from Save.
    /// </summary>
    private async Task PushPortalPreferencesAsync()
    {
        var portalService = App.PaymentPortalService;
        if (portalService == null || !PortalSettings.IsConfigured) return;

        var companyData = App.CompanyManager?.CompanyData;

        try
        {
            var result = await portalService.UpdatePreferencesAsync(
                PortalSendPaymentReminders, PortalEmailOwnerOnPayment);
            if (result.Success && result.Preferences != null
                && ReferenceEquals(App.CompanyManager?.CompanyData, companyData))
            {
                ApplyPortalPreferences(result.Preferences);
                SavePortalPreferences();
            }
        }
        catch
        {
            // Best-effort. RefreshProviderStatusAsync reconciles against the
            // server on the next open, so a dropped push self-heals.
        }
    }

    /// <summary>
    /// Copies server-reported preferences onto the ViewModel. Fenced with
    /// <c>_isLoadingPortalSettings</c> so adopting them does not re-trigger the
    /// change handlers. Also re-baselines the originals, since state that came
    /// from the server is by definition not an unsaved local edit.
    /// </summary>
    private void ApplyPortalPreferences(PortalPreferences preferences)
    {
        _isLoadingPortalSettings = true;
        PortalSendPaymentReminders = preferences.SendPaymentReminders;
        PortalEmailOwnerOnPayment = preferences.EmailOwnerOnPayment;
        PortalRemindersEnabledAt = preferences.RemindersEnabledAt;
        PortalOwnerEmailVerified = preferences.OwnerEmailVerified;
        _isLoadingPortalSettings = false;

        _originalPortalSendPaymentReminders = PortalSendPaymentReminders;
        _originalPortalEmailOwnerOnPayment = PortalEmailOwnerOnPayment;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSyncIntervalNumeric))]
    private string _portalSyncInterval = "5";

    /// <summary>
    /// Called when PortalSyncInterval changes, requires auth if password is enabled.
    /// </summary>
    partial void OnPortalSyncIntervalChanged(string value)
    {
        if (_isLoadingPortalSettings) return;
        if (!HasPassword || _isPortalAuthenticated) return;

        _ = RevertAndAuthPortalSyncIntervalAsync(value);
    }

    private string? _previousSyncInterval;

    private async Task RevertAndAuthPortalSyncIntervalAsync(string attemptedValue)
    {
        var previousValue = _previousSyncInterval ?? "5";

        _isLoadingPortalSettings = true;
        PortalSyncInterval = previousValue;
        _isLoadingPortalSettings = false;

        if (await EnsurePortalAuthenticatedAsync())
        {
            _isLoadingPortalSettings = true;
            PortalSyncInterval = attemptedValue;
            _isLoadingPortalSettings = false;
        }
    }

    public bool IsSyncIntervalNumeric => PortalSyncInterval != "Manual";

    [ObservableProperty]
    private bool _stripeConnected;

    [ObservableProperty]
    private string? _stripeEmail;

    /// <summary>
    /// The company's owner email, bound by the Portal Settings UI as a
    /// read-only display next to a "Change…" button. Mutated only via the
    /// 4-step EmailChangeModal flow (locked to in-place edits).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompanyEmail))]
    [NotifyPropertyChangedFor(nameof(CanConnectProvider))]
    [NotifyPropertyChangedFor(nameof(ShowConnectRequirementsHint))]
    private string? _companyEmail;

    [ObservableProperty]
    private bool _paypalConnected;

    [ObservableProperty]
    private string? _paypalEmail;

    [ObservableProperty]
    private bool _squareConnected;

    [ObservableProperty]
    private string? _squareEmail;

    /// <summary>
    /// Whether at least one payment provider is connected.
    /// </summary>
    public bool IsAnyPaymentProviderConnected => StripeConnected || PaypalConnected || SquareConnected;

    /// <summary>
    /// The portal company name is required while a payment provider is connected.
    /// </summary>
    public bool IsPortalCompanyNameRequired => IsAnyPaymentProviderConnected;

    /// <summary>
    /// Whether the overdue reminders toggle can be used. Needs a connected provider,
    /// since the reminder sends the customer to the portal to pay.
    /// </summary>
    public bool CanEditPaymentReminders => IsAnyPaymentProviderConnected && !IsSampleCompany;

    private void NotifyProviderConnectionChanged()
    {
        OnPropertyChanged(nameof(IsAnyPaymentProviderConnected));
        OnPropertyChanged(nameof(IsPortalCompanyNameRequired));
        OnPropertyChanged(nameof(CanEditPaymentReminders));
    }

    partial void OnStripeConnectedChanged(bool value) => NotifyProviderConnectionChanged();

    partial void OnPaypalConnectedChanged(bool value) => NotifyProviderConnectionChanged();

    partial void OnSquareConnectedChanged(bool value) => NotifyProviderConnectionChanged();

    /// <summary>True once the customer-facing company name has been entered.</summary>
    public bool HasPortalCompanyName => !string.IsNullOrWhiteSpace(PortalCompanyName);

    /// <summary>True once the owner email has been set (and verified) for this company.</summary>
    public bool HasCompanyEmail => !string.IsNullOrWhiteSpace(CompanyEmail);

    /// <summary>
    /// A payment provider requires both the company name and the owner email first: the name so
    /// the portal and receipts carry a real business name, the email so refund verification and
    /// account recovery have somewhere to go. True only when both are set and no connect is in
    /// flight.
    /// </summary>
    public bool CanConnectProvider => HasPortalCompanyName && HasCompanyEmail && !IsConnectingProvider;

    /// <summary>
    /// Prompt the user to finish the required fields while either is still missing. Never
    /// shown for the sample company, where the fields it points at are disabled anyway.
    /// </summary>
    public bool ShowConnectRequirementsHint =>
        !IsSampleCompany && (!HasPortalCompanyName || !HasCompanyEmail);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnectProvider))]
    private bool _isConnectingProvider;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _portalLogoSource;

    [ObservableProperty]
    private bool _hasPortalLogo;

    public string PortalLogoButtonText => HasPortalLogo ? "Change".Translate() : "Upload".Translate();

    [ObservableProperty]
    private bool _isUploadingPortalLogo;

    public string[] SyncIntervalOptions { get; } = ["Manual", "1", "2", "5", "10", "15", "30"];

    [RelayCommand]
    private async Task ConnectStripeAsync()
    {
        if (!CanConnectProvider) return;
        if (!await EnsurePortalAuthenticatedAsync()) return;
        await ConnectProviderAsync("stripe");
    }

    [RelayCommand]
    private async Task ConnectPaypalAsync()
    {
        if (!await EnsurePortalAuthenticatedAsync()) return;
        await ConnectProviderAsync("paypal");
    }

    [RelayCommand]
    private async Task ConnectSquareAsync()
    {
        if (!CanConnectProvider) return;
        if (!await EnsurePortalAuthenticatedAsync()) return;
        await ConnectProviderAsync("square");
    }

    [RelayCommand]
    private async Task DisconnectStripeAsync()
    {
        if (!await EnsurePortalAuthenticatedAsync()) return;
        await DisconnectProviderAsync("stripe");
    }

    [RelayCommand]
    private async Task DisconnectPaypalAsync()
    {
        if (!await EnsurePortalAuthenticatedAsync()) return;
        await DisconnectProviderAsync("paypal");
    }

    [RelayCommand]
    private async Task DisconnectSquareAsync()
    {
        if (!await EnsurePortalAuthenticatedAsync()) return;
        await DisconnectProviderAsync("square");
    }

    /// <summary>
    /// Event raised when the portal logo browse button is clicked.
    /// Handled in App.axaml.cs to open the file picker.
    /// </summary>
    public event EventHandler? BrowsePortalLogoRequested;

    [RelayCommand]
    private async Task BrowsePortalLogoAsync()
    {
        if (!await EnsurePortalAuthenticatedAsync()) return;
        BrowsePortalLogoRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Uploads the selected file as the portal logo.
    /// Called from App.axaml.cs after the file picker returns.
    /// </summary>
    public async Task UploadPortalLogoFromFileAsync(string filePath)
    {
        var portalService = App.PaymentPortalService;
        if (portalService == null) return;

        // If no API key exists, try to auto-register first
        if (!PortalSettings.IsConfigured)
        {
            var registered = await TryRegisterPortalAsync(portalService);
            if (!registered) return;
        }

        IsUploadingPortalLogo = true;
        try
        {
            var result = await portalService.UploadCompanyLogoAsync(filePath);
            if (result.Success)
            {
                try
                {
                    PortalLogoSource = new Avalonia.Media.Imaging.Bitmap(filePath);
                    HasPortalLogo = true;
                }
                catch
                {
                    // File may not be a valid image for Avalonia, but upload succeeded
                    HasPortalLogo = !string.IsNullOrEmpty(result.LogoUrl);
                }
            }
            else
            {
                await ShowErrorDialogAsync("Upload Failed".Translate(),
                    (result.Message ?? "Failed to upload logo.").Translate());
            }
        }
        catch
        {
            await ShowErrorDialogAsync("Error".Translate(),
                "Failed to upload logo. Please check your internet connection.".Translate());
        }
        finally
        {
            IsUploadingPortalLogo = false;
        }
    }

    [RelayCommand]
    private async Task RemovePortalLogoAsync()
    {
        if (!await EnsurePortalAuthenticatedAsync()) return;

        var portalService = App.PaymentPortalService;
        if (portalService == null) return;

        // If no API key exists, try to auto-register first
        if (!PortalSettings.IsConfigured)
        {
            var registered = await TryRegisterPortalAsync(portalService);
            if (!registered) return;
        }

        IsUploadingPortalLogo = true;
        try
        {
            var result = await portalService.DeleteCompanyLogoAsync();
            if (result.Success)
            {
                PortalLogoSource = null;
                HasPortalLogo = false;
            }
            else if (ConnectivityMessage.IsConnectivityMessage(result.Message))
            {
                await App.ShowConnectivityErrorAsync(result.Message);
            }
            else
            {
                await ShowErrorDialogAsync("Couldn't Remove Logo".Translate(),
                    (result.Message ?? "Failed to remove logo.").Translate());
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or System.Net.Sockets.SocketException)
        {
            await App.ShowConnectivityErrorAsync();
        }
        catch
        {
            // A non-network failure shouldn't be reported as a connection problem.
            await ShowErrorDialogAsync("Couldn't Remove Logo".Translate(), "Failed to remove logo.".Translate());
        }
        finally
        {
            IsUploadingPortalLogo = false;
        }
    }

    /// <summary>
    /// Loads the portal logo from its URL on the server.
    /// </summary>
    private async Task LoadPortalLogoFromUrlAsync(string? logoUrl)
    {
        if (string.IsNullOrEmpty(logoUrl))
        {
            PortalLogoSource = null;
            HasPortalLogo = false;
            return;
        }

        // If we already have the image loaded (e.g., from a recent upload), keep it
        if (PortalLogoSource != null)
        {
            HasPortalLogo = true;
            return;
        }

        try
        {
            using var httpClient = new HttpClient();
            var imageBytes = await httpClient.GetByteArrayAsync(logoUrl);
            using var stream = new MemoryStream(imageBytes);
            PortalLogoSource = new Avalonia.Media.Imaging.Bitmap(stream);
            HasPortalLogo = true;
        }
        catch
        {
            // Download failed, don't show an empty gray box
            PortalLogoSource = null;
            HasPortalLogo = false;
        }
    }

    private async Task ConnectProviderAsync(string provider)
    {
        var portalService = App.PaymentPortalService;
        if (portalService == null) return;

        // If no API key exists, try to auto-register first
        if (!PortalSettings.IsConfigured)
        {
            var registered = await TryRegisterPortalAsync(portalService);
            if (!registered) return;
        }

        IsConnectingProvider = true;
        try
        {
            var response = await portalService.InitiateConnectAsync(provider);
            if (response.Success && !string.IsNullOrEmpty(response.AuthUrl)
                && Uri.TryCreate(response.AuthUrl, UriKind.Absolute, out var authUri)
                && (authUri.Scheme == "https" || authUri.Scheme == "http"))
            {
                // Open OAuth URL in default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = response.AuthUrl,
                    UseShellExecute = true
                });

                // Poll for status updates so the UI refreshes when the user completes OAuth
                _ = PollForProviderConnectionAsync(provider);
            }
            else
            {
                var message = !string.IsNullOrEmpty(response.Message)
                    ? response.Message
                    : $"Could not connect to {provider}. The payment portal server may be unavailable.";
                await ShowErrorDialogAsync("Connection Failed".Translate(), message.Translate());
            }
        }
        catch
        {
            await ShowErrorDialogAsync("Error".Translate(),
                "Failed to connect payment provider. Please check your internet connection.".Translate());
        }
        finally
        {
            IsConnectingProvider = false;
        }
    }

    /// <summary>
    /// Attempts to register the company with the portal.
    /// Premium users authenticate with license key; free users with device ID only.
    /// Returns true if registration succeeded (or was already done), false otherwise.
    /// </summary>
    private async Task<bool> TryRegisterPortalAsync(PaymentPortalService portalService)
    {
        var licenseService = App.LicenseService;
        var licenseKey = licenseService?.GetLicenseKey() ?? "";
        var deviceId = licenseService?.GetDeviceId() ?? "";

        if (string.IsNullOrEmpty(deviceId))
        {
            await ShowErrorDialogAsync(
                "Registration Failed".Translate(),
                "Could not identify this device. Please try again.".Translate());
            return false;
        }

        IsConnectingProvider = true;
        try
        {
            var companyData = App.CompanyManager?.CompanyData;
            var companyName = companyData?.Settings.Company.Name ?? "My Company";
            var ownerEmail = companyData?.Settings.Company.Email;

            var result = await portalService.RegisterCompanyAsync(licenseKey, deviceId, companyName, ownerEmail);
            if (result.Success && !string.IsNullOrEmpty(result.ApiKey))
            {
                // Persist the API key in the per-company .argo file
                var portalSettings = companyData?.Settings.PaymentPortal;
                if (portalSettings != null)
                {
                    portalSettings.PersistedApiKey = result.ApiKey;
                    PortalSettings.ActivateApiKey(portalSettings);
                }

                // If the server says verification is required, open the
                // verify-email modal so the user can enter the 6-digit code
                // emailed to them. Until they do, refund endpoints will return
                // 412 (email_not_verified).
                if (result.EmailVerificationRequired)
                {
                    ShowEmailVerificationModal(ownerEmail);
                }
                return true;
            }

            var message = result.Message ?? "Registration failed. Please check your license key.";
            await ShowErrorDialogAsync("Registration Failed".Translate(), message.Translate());
            return false;
        }
        catch
        {
            await ShowErrorDialogAsync("Error".Translate(),
                "Failed to register with the payment portal. Please check your internet connection.".Translate());
            return false;
        }
        finally
        {
            IsConnectingProvider = false;
        }
    }

    private async Task DisconnectProviderAsync(string provider)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog != null)
        {
            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Disconnect Provider".Translate(),
                Message = "Are you sure you want to disconnect this payment provider? Customers will no longer be able to pay using this method.".Translate(),
                PrimaryButtonText = "Disconnect".Translate(),
                CancelButtonText = "Cancel".Translate()
            });

            if (result != ConfirmationResult.Primary) return;
        }

        var portalService = App.PaymentPortalService;
        if (portalService == null) return;

        try
        {
            var response = await portalService.DisconnectProviderAsync(provider);
            if (response.Success)
            {
                // Use the server's authoritative connected provider state from the response
                if (response.ConnectedProviders != null)
                {
                    StripeConnected = response.ConnectedProviders.StripeConnected;
                    StripeEmail = response.ConnectedProviders.StripeEmail;
                    PaypalConnected = response.ConnectedProviders.PaypalConnected;
                    PaypalEmail = response.ConnectedProviders.PaypalEmail;
                    SquareConnected = response.ConnectedProviders.SquareConnected;
                    SquareEmail = response.ConnectedProviders.SquareEmail;
                }
                else
                {
                    // Fallback: clear the specific provider if response didn't include full state
                    switch (provider)
                    {
                        case "stripe":
                            StripeConnected = false;
                            StripeEmail = null;
                            break;
                        case "paypal":
                            PaypalConnected = false;
                            PaypalEmail = null;
                            break;
                        case "square":
                            SquareConnected = false;
                            SquareEmail = null;
                            break;
                    }
                }

                // Persist changes to local settings immediately
                SaveConnectedAccounts();

                // Notify invoice views and other subscribers that provider state changed
                PaymentProviderService.NotifyProvidersChanged();
            }
            else if (ConnectivityMessage.IsConnectivityMessage(response.Message))
            {
                // The service reports connectivity problems via Message rather than throwing.
                await App.ShowConnectivityErrorAsync(response.Message);
            }
            else
            {
                await ShowErrorDialogAsync("Couldn't Disconnect".Translate(),
                    (response.Message ?? "Failed to disconnect provider. Please try again.").Translate());
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or System.Net.Sockets.SocketException)
        {
            await App.ShowConnectivityErrorAsync();
        }
        catch
        {
            // A non-network failure shouldn't be reported as a connection problem.
            await ShowErrorDialogAsync("Couldn't Disconnect".Translate(), "Failed to disconnect provider. Please try again.".Translate());
        }
    }

    private static async Task ShowErrorDialogAsync(string title, string message)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog != null)
        {
            await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = title,
                Message = message,
                PrimaryButtonText = "OK".Translate(),
                CancelButtonText = null
            });
        }
    }

    private void LoadPortalSettings()
    {
        var settings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
        if (settings == null) return;

        // Cancel any pending company name update from a previous company
        var previousCts = _portalCompanyNameCts;
        if (previousCts != null)
        {
            previousCts.Cancel();
            previousCts.Dispose();
            _portalCompanyNameCts = null;
        }

        _isLoadingPortalSettings = true;
        PortalCompanyName = settings.CompanyName ?? string.Empty;
        PortalNotifyOnPayment = settings.NotifyOnPayment;
        // Cached values only. RefreshProviderStatusAsync below overwrites these
        // from the server, which is authoritative because the reminder cron and
        // the payment webhooks read its copy while this app is closed.
        PortalEmailOwnerOnPayment = settings.EmailOwnerOnPayment;
        PortalSendPaymentReminders = settings.SendPaymentReminders;
        PortalRemindersEnabledAt = settings.RemindersEnabledAt;
        PortalOwnerEmailVerified = false;
        PortalSyncInterval = settings.AutoSyncIntervalMinutes == 0
            ? "Manual"
            : settings.AutoSyncIntervalMinutes.ToString();
        _previousSyncInterval = PortalSyncInterval;
        _isLoadingPortalSettings = false;

        // Reset all portal UI state so stale data from a previous company doesn't leak
        PortalLogoSource = null;
        HasPortalLogo = false;
        StripeConnected = settings.ConnectedAccounts.StripeConnected;
        StripeEmail = settings.ConnectedAccounts.StripeEmail;
        PaypalConnected = settings.ConnectedAccounts.PaypalConnected;
        PaypalEmail = settings.ConnectedAccounts.PaypalEmail;
        SquareConnected = settings.ConnectedAccounts.SquareConnected;
        SquareEmail = settings.ConnectedAccounts.SquareEmail;
        CompanyEmail = App.CompanyManager?.CompanyData?.Settings.Company.Email;

        LoadStripeIntegrationState();
        LoadArgoApiState();

        // Fetch fresh provider status from the server in the background
        _ = RefreshProviderStatusAsync();
    }

    internal async Task RefreshProviderStatusAsync()
    {
        var portalService = App.PaymentPortalService;
        if (portalService == null) return;

        // The key lives in the company file and is copied into the process cache on company
        // open. If that copy is missing this returned early and never asked the server, which
        // left the tab claiming nothing was connected and the owner email unverified until
        // the company was closed and reopened. Re-prime from the file rather than trusting
        // the open path to have done it.
        if (!PortalSettings.IsConfigured)
        {
            PortalSettings.ActivateApiKey(App.CompanyManager?.CompanyData?.Settings.PaymentPortal);
            if (!PortalSettings.IsConfigured) return;

            App.ErrorLogger?.LogWarning(
                "Portal API key was missing from the process cache and was re-primed from the company file.",
                nameof(SettingsModalViewModel), ErrorCategory.Api,
                "PortalKeyNotActivated");
        }

        // The reply can take up to the client timeout, and describes the company whose key asked.
        // If another company has been opened since, it must not be written into that one.
        var companyData = App.CompanyManager?.CompanyData;
        bool CompanyChanged() => !ReferenceEquals(App.CompanyManager?.CompanyData, companyData);

        try
        {
            var status = await portalService.CheckStatusAsync();
            if (CompanyChanged()) return;
            if (status.Success && status.ConnectedProviders != null)
            {
                // Connection is real once the server has a merchant/account ID stored;
                // emails are informational and may be blank (Stripe Express, Square
                // locations without a business_email, etc.).
                StripeConnected = status.ConnectedProviders.StripeConnected;
                StripeEmail = status.ConnectedProviders.StripeEmail;
                PaypalConnected = status.ConnectedProviders.PaypalConnected;
                PaypalEmail = status.ConnectedProviders.PaypalEmail;
                SquareConnected = status.ConnectedProviders.SquareConnected;
                SquareEmail = status.ConnectedProviders.SquareEmail;
                SaveConnectedAccounts();

                // Persist PortalUrl so other pages (e.g. Invoices) see it immediately
                if (!string.IsNullOrEmpty(status.PortalUrl))
                {
                    var portalSettings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
                    if (portalSettings != null)
                        portalSettings.PortalUrl = status.PortalUrl;
                }

                // Notify invoice views and other subscribers that provider state changed
                PaymentProviderService.NotifyProvidersChanged();
            }

            // Load portal company name and logo from server
            if (status.Success)
            {
                // Server wins for the email preferences: it is what the cron and
                // the webhooks actually read. This is also what restores them
                // after a reinstall or on a second machine. Null when talking to
                // a server that predates the preferences block, in which case
                // the cached local values stand.
                if (status.Preferences != null)
                {
                    ApplyPortalPreferences(status.Preferences);
                    SavePortalPreferences();
                }

                // Mirror the authoritative owner email onto this device so a
                // server-side change (e.g. the revert link) is reflected here.
                await ReconcilePortalEmailFromStatusAsync(status);
                if (CompanyChanged()) return;

                if (!string.IsNullOrEmpty(status.Company?.Name))
                {
                    _isLoadingPortalSettings = true;
                    PortalCompanyName = status.Company.Name;
                    _isLoadingPortalSettings = false;
                }
                await LoadPortalLogoFromUrlAsync(status.Company?.LogoUrl);
            }
        }
        catch
        {
            // Silently fail, local cached values are still shown
        }
    }

    private async Task PollForProviderConnectionAsync(string provider)
    {
        var portalService = App.PaymentPortalService;
        if (portalService == null) return;

        // Stop once another company is open: the Connect was for this one, and its replies
        // must not be written into the other.
        var companyData = App.CompanyManager?.CompanyData;
        bool CompanyChanged() => !ReferenceEquals(App.CompanyManager?.CompanyData, companyData);

        // Poll every 3 seconds for up to 5 minutes
        const int intervalMs = 3000;
        const int maxAttempts = 100;

        for (var i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(intervalMs);
            if (CompanyChanged()) return;

            try
            {
                var status = await portalService.CheckStatusAsync();
                if (CompanyChanged()) return;
                if (status.Success && status.ConnectedProviders != null)
                {
                    var connected = provider switch
                    {
                        "stripe" => status.ConnectedProviders.StripeConnected,
                        "paypal" => status.ConnectedProviders.PaypalConnected,
                        "square" => status.ConnectedProviders.SquareConnected,
                        _ => false
                    };

                    // A merchant/account ID in the DB is what makes the connection
                    // real. Emails are informational and may be blank (Stripe Express
                    // accounts, Square locations without a business_email, etc.).
                    if (connected)
                    {
                        // Dispatch property updates to the UI thread to ensure bindings refresh
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (CompanyChanged()) return;
                            StripeConnected = status.ConnectedProviders.StripeConnected;
                            StripeEmail = status.ConnectedProviders.StripeEmail;
                            PaypalConnected = status.ConnectedProviders.PaypalConnected;
                            PaypalEmail = status.ConnectedProviders.PaypalEmail;
                            SquareConnected = status.ConnectedProviders.SquareConnected;
                            SquareEmail = status.ConnectedProviders.SquareEmail;
                            SaveConnectedAccounts();

                            // Persist PortalUrl so other pages (e.g. Invoices) see it immediately
                            if (!string.IsNullOrEmpty(status.PortalUrl))
                            {
                                var portalSettings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
                                if (portalSettings != null)
                                    portalSettings.PortalUrl = status.PortalUrl;
                            }

                            // Notify invoice views and other subscribers that provider state changed
                            PaymentProviderService.NotifyProvidersChanged();
                        });
                        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.PaymentProviderConnected, provider);
                        return;
                    }
                }
            }
            catch
            {
                // Ignore transient errors and keep polling
            }
        }
    }

    private void SavePortalSettings()
    {
        var settings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
        if (settings == null) return;

        // Kept locally because the name has to be entered before a provider can be
        // connected, so until then there is no portal record holding it.
        settings.CompanyName = PortalCompanyName;
        settings.NotifyOnPayment = PortalNotifyOnPayment;
        settings.AutoSyncIntervalMinutes = PortalSyncInterval == "Manual"
            ? 0
            : int.TryParse(PortalSyncInterval, out var mins) ? mins : 5;

        // Track the saved sync interval for revert-on-auth
        _previousSyncInterval = PortalSyncInterval;

        SavePortalPreferences();
        SaveConnectedAccounts();
    }

    // These two write only what the server reports, so a server reply can store it without
    // also committing the other portal fields, which the user may be editing and which apply on Save.
    private void SavePortalPreferences()
    {
        var settings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
        if (settings == null) return;

        settings.EmailOwnerOnPayment = PortalEmailOwnerOnPayment;
        settings.SendPaymentReminders = PortalSendPaymentReminders;
        settings.RemindersEnabledAt = PortalRemindersEnabledAt;
    }

    private void SaveConnectedAccounts()
    {
        var settings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
        if (settings == null) return;

        settings.ConnectedAccounts.StripeConnected = StripeConnected;
        settings.ConnectedAccounts.StripeEmail = StripeEmail;
        settings.ConnectedAccounts.PaypalConnected = PaypalConnected;
        settings.ConnectedAccounts.PaypalEmail = PaypalEmail;
        settings.ConnectedAccounts.SquareConnected = SquareConnected;
        settings.ConnectedAccounts.SquareEmail = SquareEmail;
    }

    #endregion

    #region Stripe data integration

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnectStripeIntegration))]
    private string _stripeKeyInput = string.Empty;

    [ObservableProperty]
    private bool _stripeIntegrationConnected;

    [ObservableProperty]
    private string? _stripeIntegrationAccountLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnectStripeIntegration))]
    private bool _isValidatingStripe;

    [ObservableProperty]
    private string? _stripeIntegrationError;

    [ObservableProperty]
    private bool _isSyncingStripe;

    [ObservableProperty]
    private string _stripeSyncStatus = string.Empty;

    [ObservableProperty]
    private string? _stripeLastSyncedDisplay;

    public bool CanConnectStripeIntegration
        => !IsValidatingStripe && !string.IsNullOrWhiteSpace(StripeKeyInput);

    /// <summary>
    /// Testable core: validates the pasted key and, on success, writes it to
    /// <paramref name="target"/> and mirrors connection state onto the VM.
    /// </summary>
    public async Task<bool> TryConnectStripeAsync(StripeApiClient client, StripeIntegrationSettings target)
    {
        IsValidatingStripe = true;
        StripeIntegrationError = null;
        var key = StripeKeyInput.Trim();
        try
        {
            var result = await client.ValidateKeyAsync(key);
            if (!result.Ok)
            {
                StripeIntegrationError = result.ErrorMessage;
                return false;
            }

            target.ApiKey = key;
            target.Connected = true;
            target.AccountLabel = result.AccountLabel;

            StripeIntegrationConnected = true;
            StripeIntegrationAccountLabel = result.AccountLabel;
            StripeKeyInput = string.Empty; // don't keep the raw key in the input box
            return true;
        }
        finally
        {
            IsValidatingStripe = false;
        }
    }

    [RelayCommand]
    private async Task ConnectStripeIntegrationAsync()
    {
        if (!CanConnectStripeIntegration) return;
        var stripe = App.CompanyManager?.CompanyData?.Settings.Integrations.Stripe;
        if (stripe == null || App.SharedHttpClient == null) return;

        var client = new StripeApiClient(App.SharedHttpClient);
        if (await TryConnectStripeAsync(client, stripe))
            App.CompanyManager?.MarkAsChanged();
    }

    [RelayCommand]
    private void DisconnectStripeIntegration()
    {
        var stripe = App.CompanyManager?.CompanyData?.Settings.Integrations.Stripe;
        if (stripe == null) return;

        stripe.ApiKey = null;
        stripe.Connected = false;
        stripe.AccountLabel = null;
        stripe.LastSyncCursor = null;
        stripe.LastSyncTime = null;

        StripeIntegrationConnected = false;
        StripeIntegrationAccountLabel = null;
        StripeIntegrationError = null;
        StripeLastSyncedDisplay = null;
        App.CompanyManager?.MarkAsChanged();
    }

    [RelayCommand]
    private void OpenStripeGuide()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                // The documentation page, not the marketing page. Someone who has
                // already opened Settings has decided to connect it and wants the
                // steps, not the pitch.
                FileName = "https://argorobots.com/documentation/pages/integrations/stripe-integration.php",
                UseShellExecute = true
            });
        }
        catch { /* best-effort: opening the browser is non-critical */ }
    }

    /// <summary>Loads Stripe integration display state from company settings. Call from the settings-load path.</summary>
    private void LoadStripeIntegrationState()
    {
        var stripe = App.CompanyManager?.CompanyData?.Settings.Integrations.Stripe;
        StripeIntegrationConnected = stripe?.Connected ?? false;
        StripeIntegrationAccountLabel = stripe?.AccountLabel;
        StripeKeyInput = string.Empty;
        StripeIntegrationError = null;
        if (stripe != null)
            RefreshStripeLastSynced(stripe);
    }

    [RelayCommand]
    private async Task SyncStripeIntegrationAsync()
    {
        if (IsSyncingStripe) return;
        var data = App.CompanyManager?.CompanyData;
        var stripe = data?.Settings.Integrations.Stripe;
        if (data == null || stripe == null || !stripe.Connected || App.SharedHttpClient == null) return;

        IsSyncingStripe = true;
        StripeSyncStatus = "Checking Stripe for new activity...".Translate();
        try
        {
            var svc = new StripeSyncService(new StripeApiClient(App.SharedHttpClient));
            var preview = await svc.PreviewAsync(data);
            if (!preview.HasActivity)
            {
                // A message box rather than a notification: this runs from the
                // settings modal, which sits on top of where notifications
                // appear, so the user would never see it.
                await App.ShowInfoMessageBoxAsync("Stripe".Translate(), "You're already up to date.".Translate());
                return;
            }

            if (App.ConfirmationDialog == null) return; // never import without a review step
            var confirmed = await App.ConfirmationDialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Import from Stripe".Translate(),
                Message = "Import your Stripe activity: {0} in sales and {1} in fees?"
                    .TranslateFormat(preview.TotalRevenue.ToString("C2"), preview.TotalFees.ToString("C2")),
                PrimaryButtonText = "Import".Translate(),
                CancelButtonText = "Cancel".Translate()
            }) == ConfirmationResult.Primary;
            if (!confirmed) return;

            var creation = await svc.ImportPreviewAsync(data, preview, SyncProgress(v => StripeSyncStatus = v));
            if (creation.AnyCreated)
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    "Import from Stripe".Translate(),
                    () => { creation.Undo(data); App.CompanyManager?.MarkAsChanged(); },
                    () => { creation.Redo(data); App.CompanyManager?.MarkAsChanged(); }));
            App.CompanyManager?.MarkAsChanged();
            RefreshStripeLastSynced(stripe);
            await App.ShowInfoMessageBoxAsync("Stripe".Translate(),
                "Imported {0} sales and {1} expense entries from Stripe.".TranslateFormat(creation.RevenuesCreated, creation.ExpensesCreated));
        }
        catch (Exception ex)
        {
            // Never let a Stripe/network error crash the app; surface it instead.
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Stripe sync failed");
            await App.ShowWarningMessageBoxAsync("Stripe".Translate(),
                "Sync failed: {0}".TranslateFormat(ex.Message));
        }
        finally
        {
            IsSyncingStripe = false;
            StripeSyncStatus = string.Empty;
        }
    }

    private void RefreshStripeLastSynced(StripeIntegrationSettings stripe)
        => StripeLastSyncedDisplay = stripe.LastSyncTime is { } t ? "Last synced {0}".TranslateFormat(t.ToString("MMM d, yyyy h:mm tt")) : null;

    #endregion

    #region Argo Books API

    [ObservableProperty]
    private bool _argoApiEnabled;

    [ObservableProperty]
    private bool _isEnablingArgoApi;

    [ObservableProperty]
    private bool _isSyncingArgoApi;

    /// <summary>
    /// What the sync is doing, shown beside the button. A disabled button was the
    /// only sign anything was happening, and an import can spend fifteen seconds
    /// fetching exchange rates before it writes a row.
    /// </summary>
    [ObservableProperty]
    private string _argoApiSyncStatus = string.Empty;

    [ObservableProperty]
    private string? _argoApiError;

    [ObservableProperty]
    private string? _argoApiLastSyncedDisplay;

    [ObservableProperty]
    private string? _argoApiPendingSummary;

    [ObservableProperty]
    private string _newArgoApiKeyLabel = string.Empty;

    /// <summary>The base URL a developer points their integration at.</summary>
    public string ArgoApiBaseUrl => $"{ApiConfig.BaseUrl}/v1";

    public ObservableCollection<ArgoApiKeyRow> ArgoApiKeys { get; } = [];

    /// <summary>True when the merchant has not created a key for anyone yet.</summary>
    [ObservableProperty]
    private bool _hasNoArgoApiKeys = true;

    /// <summary>Reads display state from company settings. Called on the settings-load path.</summary>
    private void LoadArgoApiState()
    {
        var api = App.CompanyManager?.CompanyData?.Settings.Integrations.ArgoApi;
        ArgoApiEnabled = api?.Enabled ?? false;
        ArgoApiError = null;
        NewArgoApiKeyLabel = string.Empty;
        ArgoApiPendingSummary = null;
        ArgoApiKeys.Clear();
        HasNoArgoApiKeys = true;

        RefreshArgoApiLastSynced(api);

        if (ArgoApiEnabled)
            _ = RefreshArgoApiKeysAsync();
    }

    /// <summary>
    /// Turn the API on for this company and mint the key the desktop uses itself.
    ///
    /// The desktop's key is separate from any key handed to a developer, so
    /// revoking a developer never locks the app out of its own review queue.
    /// </summary>
    [RelayCommand]
    private async Task EnableArgoApiAsync()
    {
        if (IsEnablingArgoApi) return;
        var data = App.CompanyManager?.CompanyData;
        if (data == null || App.SharedHttpClient == null) return;

        IsEnablingArgoApi = true;
        ArgoApiError = null;
        try
        {
            var api = data.Settings.Integrations.ArgoApi;

            // Reuse the mobile sync identifier when there is one, so both features
            // name the same company on the server rather than inventing two.
            api.CompanyUid ??= data.Settings.MobileSync.CompanyUid ?? Guid.NewGuid().ToString("N");

            var client = new ArgoApiClient(App.SharedHttpClient);
            var licenseKey = LicenseAuthHelper.GetLicenseKey() ?? string.Empty;
            var deviceId = LicenseAuthHelper.GetDeviceId() ?? string.Empty;

            // Registering the company is idempotent; minting a key is not.
            // Turning the feature on twice used to leave a spare key behind on
            // every press, so the key is only made when there is not one already.
            api.AccountId = await client.EnsureAccountAsync(
                api.CompanyUid!, data.Settings.Company.Name, licenseKey, deviceId);

            if (string.IsNullOrEmpty(api.DesktopKey))
            {
                api.DesktopKey = await client.CreateDesktopKeyAsync(api.CompanyUid!, licenseKey, deviceId);
            }

            api.Enabled = true;

            ArgoApiEnabled = true;
            App.CompanyManager?.MarkAsChanged();
            await RefreshArgoApiKeysAsync();
        }
        catch (Exception ex)
        {
            ArgoApiError = ex.Message;
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Enabling the Argo Books API failed");
        }
        finally
        {
            IsEnablingArgoApi = false;
        }
    }

    /// <summary>
    /// Stop the API for this company: revoke every key, then forget it locally.
    ///
    /// Objects already pushed stay on the server. Deleting them would destroy a
    /// developer's data on a click the merchant may have meant as "pause", and
    /// re-enabling brings the queue back exactly as it was.
    /// </summary>
    [RelayCommand]
    private async Task DisableArgoApiAsync()
    {
        var data = App.CompanyManager?.CompanyData;
        var api = data?.Settings.Integrations.ArgoApi;
        if (data == null || api == null || App.SharedHttpClient == null) return;

        if (App.ConfirmationDialog != null)
        {
            var confirmed = await App.ConfirmationDialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Turn off the Argo Books API".Translate(),
                Message = "Every key will stop working immediately and any integration using them will fail. Data already sent stays on the server and comes back if you turn this on again.".Translate(),
                PrimaryButtonText = "Turn off".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            }) == ConfirmationResult.Primary;
            if (!confirmed) return;
        }

        try
        {
            var client = new ArgoApiClient(App.SharedHttpClient);
            foreach (var row in ArgoApiKeys.Where(k => !k.IsRevoked).ToList())
            {
                await client.RevokeKeyAsync(
                    api.CompanyUid ?? string.Empty, row.Id,
                    LicenseAuthHelper.GetLicenseKey() ?? string.Empty,
                    LicenseAuthHelper.GetDeviceId() ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            // Local state is still cleared below: the merchant asked to turn it
            // off, and leaving it half-on because the network hiccuped is worse
            // than a key that outlives the setting. Surfaced so they can retry.
            ArgoApiError = "Some keys could not be revoked: {0}".TranslateFormat(ex.Message);
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Revoking Argo Books API keys failed");
        }

        api.Enabled = false;
        api.DesktopKey = null;

        ArgoApiEnabled = false;
        ArgoApiKeys.Clear();
        HasNoArgoApiKeys = true;
        ArgoApiPendingSummary = null;
        App.CompanyManager?.MarkAsChanged();
    }

    [RelayCommand]
    private async Task RefreshArgoApiKeysAsync()
    {
        var api = App.CompanyManager?.CompanyData?.Settings.Integrations.ArgoApi;
        if (api?.CompanyUid == null || App.SharedHttpClient == null) return;

        try
        {
            var client = new ArgoApiClient(App.SharedHttpClient);
            using var doc = await client.ListKeysAsync(
                api.CompanyUid,
                LicenseAuthHelper.GetLicenseKey() ?? string.Empty,
                LicenseAuthHelper.GetDeviceId() ?? string.Empty);

            ArgoApiKeys.Clear();
            if (!doc.RootElement.TryGetProperty("keys", out var keys)) return;

            // The key this app minted for itself is not one the merchant hands
            // out, and revoking it would cut the app off from its own review
            // queue with no obvious way back.
            var ownHint = ArgoApiKeyHint(api.DesktopKey);
            var strays = new List<string>();

            foreach (var k in keys.EnumerateArray())
            {
                var hint = k.GetProperty("hint").GetString() ?? string.Empty;
                var label = k.GetProperty("label").GetString() ?? string.Empty;
                var revoked = k.GetProperty("revoked_at").ValueKind != JsonValueKind.Null;

                // A revoked key can never be used again, so listing it is
                // clutter that reads like something still needing attention.
                if (revoked)
                {
                    continue;
                }

                if (label == ArgoApiClient.DesktopKeyLabel)
                {
                    // Ours. Anything with our label that is NOT the key we hold
                    // is a leftover from the days when enabling twice minted a
                    // spare. Nothing can use it, and it occupies one of the ten
                    // key slots, so retire it.
                    if (ownHint == null || hint != ownHint)
                    {
                        strays.Add(k.GetProperty("id").GetString() ?? string.Empty);
                    }
                    continue;
                }

                var lastUsed = k.GetProperty("last_used_at").GetString();
                ArgoApiKeys.Add(new ArgoApiKeyRow
                {
                    Id = k.GetProperty("id").GetString() ?? string.Empty,
                    Hint = hint,
                    Label = k.GetProperty("label").GetString() ?? string.Empty,
                    Scopes = string.Join(", ", k.GetProperty("scopes").EnumerateArray().Select(s => s.GetString())),
                    LastUsedDisplay = lastUsed == null
                        ? "Never used".Translate()
                        : "Last used {0}".TranslateFormat(lastUsed),
                    IsRevoked = k.GetProperty("revoked_at").ValueKind != JsonValueKind.Null
                });
            }

            HasNoArgoApiKeys = ArgoApiKeys.Count == 0;

            foreach (var strayId in strays.Where(id => !string.IsNullOrEmpty(id)))
            {
                try
                {
                    await client.RevokeKeyAsync(
                        api.CompanyUid,
                        strayId,
                        LicenseAuthHelper.GetLicenseKey() ?? string.Empty,
                        LicenseAuthHelper.GetDeviceId() ?? string.Empty);
                }
                catch (Exception ex)
                {
                    // Tidying is not worth surfacing or failing the panel over.
                    App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Retiring a leftover desktop key failed");
                }
            }
        }
        catch (Exception ex)
        {
            ArgoApiError = ex.Message;
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Listing Argo Books API keys failed");
        }
    }

    /// <summary>
    /// The display hint the server stores for a secret: first seven characters,
    /// an ellipsis, last four. Mirrors api_key_hint() on the server, and is the
    /// only way to recognise our own key in a list that never returns secrets.
    /// </summary>
    private static string? ArgoApiKeyHint(string? secret)
        => string.IsNullOrEmpty(secret) || secret!.Length < 11
            ? null
            : secret[..7] + "..." + secret[^4..];

    /// <summary>
    /// True when another key already answers to this name.
    ///
    /// The name is the only thing telling two keys apart in this list; the secret is
    /// never shown again and the hint is a dozen characters of hex. Two keys called
    /// "Shopify" and one of them needing to be deleted is a coin flip.
    /// </summary>
    private bool ArgoApiKeyNameTaken(string label, string? exceptId = null)
        => ArgoApiKeys.Any(k => k.Id != exceptId
                                && string.Equals(k.Label.Trim(), label, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private async Task CreateArgoApiKeyAsync()
    {
        var api = App.CompanyManager?.CompanyData?.Settings.Integrations.ArgoApi;
        if (api?.CompanyUid == null || App.SharedHttpClient == null) return;

        var label = string.IsNullOrWhiteSpace(NewArgoApiKeyLabel) ? "Untitled key" : NewArgoApiKeyLabel.Trim();

        if (ArgoApiKeyNameTaken(label))
        {
            await App.ShowWarningMessageBoxAsync("Argo Books API".Translate(),
                "There is already a key called \"{0}\". Give this one a different name.".TranslateFormat(label));
            return;
        }

        ArgoApiError = null;
        try
        {
            var client = new ArgoApiClient(App.SharedHttpClient);
            var (secret, _) = await client.CreateDeveloperKeyAsync(
                api.CompanyUid,
                label,
                allowWrite: true,
                LicenseAuthHelper.GetLicenseKey() ?? string.Empty,
                LicenseAuthHelper.GetDeviceId() ?? string.Empty);

            NewArgoApiKeyLabel = string.Empty;
            await RefreshArgoApiKeysAsync();

            // Straight onto the clipboard, then shown. This is the only moment the
            // secret exists outside the server's hash of it, and a message box is not
            // selectable, so telling someone to retype it would not be a plan.
            var copied = await App.CopyToClipboardAsync(secret);
            var advice = copied
                ? "Copied to your clipboard. This is the only time it can be shown, so paste it somewhere safe before closing this.".Translate()
                : "This is the only time it can be shown, so copy it somewhere safe before closing this.".Translate();

            await App.ShowInfoMessageBoxAsync(
                "Your new API key".Translate(),
                secret + Environment.NewLine + Environment.NewLine + advice);
        }
        catch (Exception ex)
        {
            ArgoApiError = ex.Message;
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Creating an Argo Books API key failed");
        }
    }

    /// <summary>
    /// Called "delete" throughout the UI, because from the merchant's side that
    /// is what happens: the key stops working and leaves the list. The server
    /// still only stamps it revoked, so its history survives for anyone looking
    /// into why an integration stopped working.
    /// </summary>
    [RelayCommand]
    private async Task DeleteArgoApiKeyAsync(ArgoApiKeyRow? row)
    {
        var api = App.CompanyManager?.CompanyData?.Settings.Integrations.ArgoApi;
        if (row == null || api?.CompanyUid == null || App.SharedHttpClient == null) return;

        if (App.ConfirmationDialog != null)
        {
            var confirmed = await App.ConfirmationDialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete this key".Translate(),
                Message = "{0} will stop working immediately. Anything using it will start failing.".TranslateFormat(row.DisplayName),
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            }) == ConfirmationResult.Primary;
            if (!confirmed) return;
        }

        try
        {
            var client = new ArgoApiClient(App.SharedHttpClient);
            await client.RevokeKeyAsync(
                api.CompanyUid, row.Id,
                LicenseAuthHelper.GetLicenseKey() ?? string.Empty,
                LicenseAuthHelper.GetDeviceId() ?? string.Empty);

            // Gone rather than greyed out. A deleted key can never work again,
            // so leaving it on screen only invites the question of why it is
            // still there.
            ArgoApiKeys.Remove(row);
            HasNoArgoApiKeys = ArgoApiKeys.Count == 0;
        }
        catch (Exception ex)
        {
            ArgoApiError = ex.Message;
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Deleting an Argo Books API key failed");
        }
    }

    /// <summary>Put one row into its rename form, seeded with the current name.</summary>
    [RelayCommand]
    private void BeginRenameArgoApiKey(ArgoApiKeyRow? row)
    {
        if (row == null) return;

        // One at a time. Two open editors invite typing into the wrong one and
        // then wondering which key just got the name.
        foreach (var other in ArgoApiKeys.Where(k => k.IsEditing && k != row))
            other.IsEditing = false;

        row.EditLabel = row.Label;
        row.IsEditing = true;
    }

    [RelayCommand]
    private void CancelRenameArgoApiKey(ArgoApiKeyRow? row)
    {
        if (row == null) return;
        row.IsEditing = false;
        row.EditLabel = string.Empty;
    }

    [RelayCommand]
    private async Task SaveArgoApiKeyNameAsync(ArgoApiKeyRow? row)
    {
        var api = App.CompanyManager?.CompanyData?.Settings.Integrations.ArgoApi;
        if (row == null || api?.CompanyUid == null || App.SharedHttpClient == null) return;

        var label = row.EditLabel.Trim();

        // Nothing to send. Closing the editor rather than erroring matches what
        // pressing Enter on an unchanged field is asking for.
        if (label.Length == 0 || label == row.Label)
        {
            row.IsEditing = false;
            return;
        }

        if (ArgoApiKeyNameTaken(label, row.Id))
        {
            await App.ShowWarningMessageBoxAsync("Argo Books API".Translate(),
                "There is already a key called \"{0}\". Give this one a different name.".TranslateFormat(label));
            return; // editor stays open, with what they typed still in it
        }

        ArgoApiError = null;
        try
        {
            var client = new ArgoApiClient(App.SharedHttpClient);
            await client.RenameKeyAsync(
                api.CompanyUid, row.Id, label,
                LicenseAuthHelper.GetLicenseKey() ?? string.Empty,
                LicenseAuthHelper.GetDeviceId() ?? string.Empty);

            // Edit the row in place. Reloading the list would work, but it also
            // collapses any other open editor and jumps the scroll position.
            row.Label = label;
            row.IsEditing = false;
        }
        catch (Exception ex)
        {
            ArgoApiError = ex.Message;
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Renaming an Argo Books API key failed");
        }
    }

    /// <summary>
    /// Turns the rate fetch's 0-100 into the line shown beside the button. Only the
    /// fetch reports a percentage, so the wording names that phase rather than the
    /// import as a whole, which would sit at 100% while it writes the rows.
    /// </summary>
    private static IProgress<int> SyncProgress(Action<string> set)
        => new Progress<int>(pct => set("Fetching exchange rates... {0}%".TranslateFormat(pct)));

    [RelayCommand]
    private void OpenArgoApiDocs()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                // BaseUrl, not a hardcoded host: these pages only exist wherever
                // the API itself has shipped. A debug build points at dev, which
                // has them; a release build points at production, which will have
                // them by the time one is cut. Hardcoding production would 404
                // for as long as the API is only on dev.
                //
                // The path is the real served one. These pages link their assets
                // relatively, so a shallower alias URL loads them unstyled.
                FileName = $"{ApiConfig.BaseUrl}/documentation/pages/api/overview.php",
                UseShellExecute = true
            });
        }
        catch { /* best-effort: opening the browser is non-critical */ }
    }

    /// <summary>
    /// Pull what developers have pushed, show the merchant what it adds up to, and
    /// import on approval as one undoable action.
    ///
    /// The undo also tells the server to release the batch, otherwise the queue
    /// would keep reporting data as imported that is no longer in anyone's books.
    /// </summary>
    [RelayCommand]
    private async Task SyncArgoApiAsync()
    {
        if (IsSyncingArgoApi) return;
        var data = App.CompanyManager?.CompanyData;
        var api = data?.Settings.Integrations.ArgoApi;
        if (data == null || api == null || !api.Enabled || App.SharedHttpClient == null) return;

        IsSyncingArgoApi = true;
        ArgoApiError = null;
        try
        {
            var svc = new ArgoApiSyncService(new ArgoApiClient(App.SharedHttpClient));
            var preview = await svc.PreviewAsync(data);

            if (!preview.HasActivity)
            {
                ArgoApiPendingSummary = null;
                await App.ShowInfoMessageBoxAsync("Argo Books API".Translate(),
                    "Nothing is waiting to be imported.".Translate());
                return;
            }

            ArgoApiPendingSummary = "{0} items waiting".TranslateFormat(preview.TotalObjects);

            if (App.ConfirmationDialog == null) return; // never import without a review step

            // Three answers, not two. Cancel leaves everything queued for next time, which
            // is the right response to "not now" but a poor one to "never": without Discard
            // an unwanted object is re-offered on every sync forever, and the app that sent
            // it cannot tell refusal from inattention.
            var choice = await App.ConfirmationDialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Import from the Argo Books API".Translate(),
                Message = "Import {0} items sent by your connected apps: {1} in revenue and {2} in expenses?\n\nDiscard removes them for good and tells the apps that sent them. Cancel leaves them waiting."
                    .TranslateFormat(
                        preview.TotalObjects,
                        preview.TotalRevenue.ToString("C2"),
                        preview.TotalExpenses.ToString("C2")),
                PrimaryButtonText = "Import".Translate(),
                SecondaryButtonText = "Discard".Translate(),
                IsSecondaryDestructive = true,
                CancelButtonText = "Cancel".Translate()
            });

            if (choice == ConfirmationResult.Secondary)
            {
                ArgoApiSyncStatus = "Discarding...".Translate();
                var discarded = await svc.RejectPreviewAsync(data, preview);
                ArgoApiPendingSummary = null;
                await App.ShowInfoMessageBoxAsync(
                    "Argo Books API".Translate(),
                    "Discarded {0} items. They will not be offered again."
                        .TranslateFormat(discarded));
                return;
            }

            if (choice != ConfirmationResult.Primary) return;

            var creation = await svc.ImportPreviewAsync(data, preview, SyncProgress(v => ArgoApiSyncStatus = v));

            if (creation.AnyCreated)
            {
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    "Import from the Argo Books API".Translate(),
                    () =>
                    {
                        creation.Undo(data);
                        if (creation.BatchId != null)
                            _ = svc.TryReleaseBatchAsync(data, creation.BatchId);
                        App.CompanyManager?.MarkAsChanged();
                    },
                    () =>
                    {
                        creation.Redo(data);
                        App.CompanyManager?.MarkAsChanged();
                        _ = ReclaimAfterRedoAsync(svc, data, creation);
                    }));
            }

            App.CompanyManager?.MarkAsChanged();
            ArgoApiPendingSummary = null;
            RefreshArgoApiLastSynced(api);

            await App.ShowInfoMessageBoxAsync("Argo Books API".Translate(),
                "Imported {0} sales and {1} expense entries.".TranslateFormat(creation.RevenuesCreated, creation.ExpensesCreated));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Argo Books API sync failed");
            ArgoApiError = ex.Message;
            await App.ShowWarningMessageBoxAsync("Argo Books API".Translate(),
                "Import failed: {0}".TranslateFormat(ex.Message));
        }
        finally
        {
            IsSyncingArgoApi = false;
            ArgoApiSyncStatus = string.Empty;
        }
    }

    /// <summary>
    /// Claim a redone import's objects again. Undo handed them back to the queue, so without this
    /// the next sync imports every one of them a second time. Same as the Revenue page's redo.
    /// </summary>
    private static async Task ReclaimAfterRedoAsync(ArgoApiSyncService svc, CompanyData data, ArgoApiImportCreation creation)
    {
        if (await svc.TryReclaimBatchAsync(data, creation) || creation.BatchId == null)
            return;

        // Redo re-recorded the old batch id, which names a batch the server has reverted.
        data.Settings.Integrations.ArgoApi.ImportedBatches.Remove(creation.BatchId);
        creation.BatchId = null;
        App.CompanyManager?.MarkAsChanged();

        await App.ShowWarningMessageBoxAsync(
            "Argo Books API".Translate(),
            ("The restored items are back in your books, but the server could not be told they were taken. " +
             "They may still show as waiting on your next sync. Importing them again would create duplicates, " +
             "so check before you do.").Translate());
    }

    private void RefreshArgoApiLastSynced(ArgoApiIntegrationSettings? api)
        => ArgoApiLastSyncedDisplay = api?.LastSyncTime is { } t
            ? "Last imported {0}".TranslateFormat(t.ToString("MMM d, yyyy h:mm tt"))
            : null;

    #endregion

    #region Bank Import Rules

    /// <summary>
    /// Rows shown in the "Bank import rules" tab, each wrapping a <see cref="BankCategoryRule"/>.
    /// </summary>
    public ObservableCollection<BankCategoryRuleRow> BankCategoryRules { get; } = [];

    /// <summary>
    /// Categories available for the rule category picker.
    /// </summary>
    public ObservableCollection<Category> AvailableBankCategories { get; } = [];

    /// <summary>
    /// Loads bank category rules and available categories from company data.
    /// Called each time the settings modal opens.
    /// </summary>
    public void LoadBankRules()
    {
        BankCategoryRules.Clear();
        AvailableBankCategories.Clear();

        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        foreach (var cat in data.Categories.OrderBy(c => c.Name))
            AvailableBankCategories.Add(cat);

        // Edit detached copies so typing / add / delete only affect a draft. The live rules in
        // company settings are replaced on Save and left untouched on Cancel, so closing the
        // modal without saving leaves no changes (and no unsaved-changes asterisk).
        foreach (var r in data.BankCategoryRules)
            BankCategoryRules.Add(new BankCategoryRuleRow(CloneRule(r), AvailableBankCategories));

        // Baseline for the unsaved-changes check, so editing a rule triggers the discard prompt.
        _originalBankRulesSignature = ComputeBankRulesSignature();
    }

    /// <summary>
    /// Adds a new blank bank category rule to the draft list. Committed on Save.
    /// </summary>
    [RelayCommand]
    private void AddBankRule()
    {
        var rule = new BankCategoryRule
        {
            Id = Guid.NewGuid().ToString("N"),
            Source = RuleSource.Manual
        };
        BankCategoryRules.Add(new BankCategoryRuleRow(rule, AvailableBankCategories));
    }

    /// <summary>
    /// Removes the given bank category rule row from the draft list. Committed on Save.
    /// </summary>
    [RelayCommand]
    private void DeleteBankRule(BankCategoryRuleRow? row)
    {
        if (row == null) return;
        BankCategoryRules.Remove(row);
    }

    /// <summary>
    /// Creates a detached copy of a rule so the settings modal edits a draft without mutating the
    /// live rule in company settings until the user saves.
    /// </summary>
    private static BankCategoryRule CloneRule(BankCategoryRule r) => new()
    {
        Id = r.Id,
        Pattern = r.Pattern,
        MatchType = r.MatchType,
        CategoryId = r.CategoryId,
        ProductId = r.ProductId,
        TransactionType = r.TransactionType,
        CounterpartyId = r.CounterpartyId,
        Source = r.Source,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    /// <summary>
    /// Tracks the outstanding CategorySaved subscription so a cancelled Create Category modal
    /// (which never fires CategorySaved) can't leak a handler onto the shared category modal VM.
    /// </summary>
    private EventHandler? _categorySavedHandler;

    /// <summary>
    /// Opens the standard Create Category modal. On save, reloads the bank categories list.
    /// </summary>
    [RelayCommand]
    private void OpenCreateCategory()
    {
        var categoryModals = App.CategoryModalsViewModel;
        if (categoryModals == null) return;

        // Detach any handler left over from a previous open that was cancelled without saving.
        if (_categorySavedHandler != null)
            categoryModals.CategorySaved -= _categorySavedHandler;

        void OnSaved(object? s, EventArgs e)
        {
            categoryModals.CategorySaved -= OnSaved;
            _categorySavedHandler = null;
            AvailableBankCategories.Clear();
            var companyData = App.CompanyManager?.CompanyData;
            if (companyData != null)
            {
                foreach (var cat in companyData.Categories.OrderBy(c => c.Name))
                    AvailableBankCategories.Add(cat);
            }
        }
        _categorySavedHandler = OnSaved;
        categoryModals.CategorySaved += OnSaved;
        categoryModals.OpenAddModal(isExpensesTab: true);
    }

    #endregion

    #region Mobile Sync Settings

    /// <summary>
    /// QR code image encoding the current pairing payload. Null until "Connect a phone" is used.
    /// </summary>
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _qrImage;

    /// <summary>
    /// Short, human-typeable form of the pairing code ("XXXX-XXXX"), shown behind an "Enter a
    /// code instead" reveal as a fallback to scanning the QR code.
    /// </summary>
    [ObservableProperty]
    private string _shortCodeDisplay = string.Empty;

    /// <summary>
    /// Whether the "Enter a code instead" reveal is expanded, showing <see cref="ShortCodeDisplay"/>.
    /// </summary>
    [ObservableProperty]
    private bool _isShortCodeRevealed;

    /// <summary>
    /// True once the phone has claimed the pairing token and been handed the encrypted sync key.
    /// Drives the "Phone connected" confirmation state, replacing the QR/code pairing UI.
    /// </summary>
    [ObservableProperty]
    private bool _isPhoneJustPaired;

    /// <summary>
    /// Phones currently paired with this company for mobile sync.
    /// </summary>
    public ObservableCollection<Core.Models.Tracking.PairedDevice> PairedDevices { get; } = [];

    /// <summary>
    /// True while a pairing token is being requested from the sync server.
    /// </summary>
    [ObservableProperty]
    private bool _isConnecting;

    /// <summary>
    /// True while the paired device list is being refreshed from the sync server.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshingDevices;

    /// <summary>
    /// The tab index of the "Mobile app" tab, used to cancel the pairing poll loop as soon as the
    /// user navigates away from it. Derived from <see cref="SettingsTab"/> so the position lives in
    /// one place (that enum must mirror the TabItem order in SettingsModal.axaml).
    /// </summary>
    internal const int MobileAppTabIndex = (int)SettingsTab.MobileApp;

    /// <summary>
    /// Whether the "Mobile app" tab is shown at all. The mobile app is not released yet, and the
    /// tab offers a QR code to pair a phone with, so in a shipped build it invites customers to
    /// open an app they cannot install. Debug builds keep it so it can still be worked on.
    ///
    /// Hiding rather than removing the tab keeps every later tab at the index
    /// <see cref="SettingsTab"/> gives it, so <see cref="MobileAppTabIndex"/> stays correct.
    ///
    /// Delete this and its binding in SettingsModal.axaml when the mobile app ships.
    /// </summary>
    public bool IsMobileAppTabVisible =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Cancellation source for the background loop polling the sync server for the phone to
    /// claim the pairing token. Cancelled as soon as the pairing screen stops being visible (tab
    /// change, modal close, or a fresh "Connect a phone" click), so the encrypted sync key is
    /// only ever delivered while the user can see the pairing UI.
    /// </summary>
    private CancellationTokenSource? _pairingCts;

    /// <summary>
    /// Cancels and clears any in-flight pairing poll loop.
    /// </summary>
    private void CancelPairingPoll()
    {
        _pairingCts?.Cancel();
        _pairingCts?.Dispose();
        _pairingCts = null;
    }

    /// <summary>
    /// Pure guard deciding whether the pairing screen is still eligible to have a QR/short code
    /// shown or a sync key delivered to it: the in-flight request/poll wasn't cancelled AND the
    /// pairing screen (settings modal open, Mobile app tab selected) is still visible. Extracted
    /// as a pure function so the "never deliver the key once the screen is gone" security
    /// property is directly unit-testable without mocking the sync service or timing a poll loop.
    /// </summary>
    internal static bool ShouldContinuePairing(bool isCancellationRequested, bool isModalOpen, int selectedTabIndex) =>
        !isCancellationRequested && isModalOpen && selectedTabIndex == MobileAppTabIndex;

    /// <summary>
    /// Cancels the pairing poll loop when the user navigates away from the Mobile app tab.
    /// </summary>
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value != MobileAppTabIndex)
            CancelPairingPoll();
    }

    /// <summary>
    /// Cancels the pairing poll loop when the settings modal closes.
    /// </summary>
    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
            CancelPairingPoll();
    }

    /// <summary>
    /// Loads mobile sync state (paired devices) from company data. Called each time the
    /// settings modal opens. Does not fetch a fresh QR code; that only happens when the
    /// user explicitly clicks "Connect a phone".
    /// </summary>
    public void LoadMobileSync()
    {
        CancelPairingPoll();

        QrImage = null;
        ShortCodeDisplay = string.Empty;
        IsShortCodeRevealed = false;
        IsPhoneJustPaired = false;
        PairedDevices.Clear();

        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        foreach (var device in data.PairedDevices)
            PairedDevices.Add(device);
    }

    /// <summary>
    /// Toggles the "Enter a code instead" reveal showing <see cref="ShortCodeDisplay"/>.
    /// </summary>
    [RelayCommand]
    private void ToggleShortCodeReveal()
    {
        IsShortCodeRevealed = !IsShortCodeRevealed;
    }

    /// <summary>
    /// Ensures this company has a mobile-sync identity (companyUid/syncKey), requests a
    /// short-lived pairing token from the server, and renders it as a QR code the phone can scan.
    /// </summary>
    [RelayCommand]
    private async Task ConnectPhoneAsync()
    {
        var companyManager = App.CompanyManager;
        var companyData = companyManager?.CompanyData;
        var syncService = App.SyncService;
        if (companyManager == null || companyData == null || syncService == null)
        {
            App.ErrorLogger?.LogError(
                new InvalidOperationException("Mobile sync is not initialized (CompanyManager, CompanyData, or SyncService is null)."),
                ErrorCategory.Api,
                "Sync.ConnectPhone.NotReady");
            await ShowErrorDialogAsync(
                "Couldn't Connect a Phone".Translate(),
                "Mobile sync isn't ready yet. Please reopen the app and try again.".Translate());
            return;
        }

        // A fresh click supersedes any pairing already in flight.
        CancelPairingPoll();
        IsPhoneJustPaired = false;

        // Create the CTS BEFORE the CreatePairingAsync network round-trip, and pass its token
        // in, so that a close/tab-change while that request is in flight actually cancels it
        // (OnIsOpenChanged/OnSelectedTabIndexChanged both call CancelPairingPoll, which cancels
        // and disposes whatever CTS is currently assigned to _pairingCts). Without this, the
        // token wouldn't exist yet for those handlers to cancel, and the key could be delivered
        // after the pairing screen was already closed.
        var pairingCts = new CancellationTokenSource();
        _pairingCts = pairingCts;

        IsConnecting = true;
        try
        {
            var mobileSync = companyData.Settings.MobileSync;
            mobileSync.CompanyUid ??= SyncCrypto.GenerateCompanyUid();
            mobileSync.SyncKeyBase64 ??= SyncCrypto.GenerateSyncKey();
            mobileSync.Enabled = true;
            await companyManager.SaveSettingsOnlyAsync();

            var companyLabel = string.IsNullOrWhiteSpace(companyData.Settings.Company.Name)
                ? "My Company"
                : companyData.Settings.Company.Name;

            var pairing = await syncService.CreatePairingAsync(mobileSync.CompanyUid, companyLabel, pairingCts.Token);
            if (pairing == null || string.IsNullOrEmpty(pairing.Token))
            {
                // Server responded but without a token (unexpected) - log it so the failure isn't invisible.
                App.ErrorLogger?.LogError(
                    new InvalidOperationException("Sync server returned no pairing_token."),
                    ErrorCategory.Api,
                    "Sync.ConnectPhone.NoToken");
                QrImage = null;
                ShortCodeDisplay = string.Empty;
                await ShowErrorDialogAsync(
                    "Couldn't Connect a Phone".Translate(),
                    "The sync server didn't return a pairing code. Please try again.".Translate());
                return;
            }

            // The pairing screen may have been closed, navigated away from, or superseded by a
            // fresh "Connect a phone" click while the request above was in flight. Re-check
            // before showing the QR/short code or starting the poll: if the screen is no longer
            // visible, the key must not be shown or delivered.
            if (!ShouldContinuePairing(pairingCts.Token.IsCancellationRequested, IsOpen, SelectedTabIndex))
            {
                // Only clean up if nothing else (a fresh click, CancelPairingPoll) already did.
                if (ReferenceEquals(_pairingCts, pairingCts))
                    CancelPairingPoll();
                return;
            }

            var token = pairing.Token;
            var payload = SyncCrypto.BuildQrPayload(token, mobileSync.CompanyUid, companyLabel, mobileSync.SyncKeyBase64);
            QrImage = new QrImageService().RenderBitmap(payload);
            ShortCodeDisplay = PairingCode.Format(pairing.ShortCode);
            IsShortCodeRevealed = false;

            // Start polling for the phone to claim this token. Cancelled on tab change, modal
            // close, or the next "Connect a phone" click (see CancelPairingPoll).
            _ = PollPairingAsync(token, pairingCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected: the pairing screen was closed/navigated away from (or superseded by a
            // fresh "Connect a phone" click) while CreatePairingAsync was in flight. The CTS was
            // already cancelled and disposed by whichever handler triggered this; nothing to do.
        }
        catch (Exception ex)
        {
            // Log to telemetry and tell the user, instead of failing silently.
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Sync.ConnectPhone");
            QrImage = null;
            ShortCodeDisplay = string.Empty;

            var message = ex is HttpRequestException
                ? "We couldn't reach the sync server. Check your internet connection and try again.".Translate()
                : "Something went wrong while connecting a phone. Please try again.".Translate();
            await ShowErrorDialogAsync("Couldn't Connect a Phone".Translate(), message);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>
    /// Polls the sync server every ~2s for the phone to claim <paramref name="pairingToken"/>.
    /// Once the phone's public key arrives, encrypts the company sync key to it and delivers it,
    /// then shows the "Phone connected" state and refreshes the paired device list. Runs until
    /// that happens or <paramref name="ct"/> is cancelled (tab change, modal close, or a fresh
    /// "Connect a phone" click - see <see cref="CancelPairingPoll"/>), and re-checks
    /// <see cref="ShouldContinuePairing"/> immediately before delivering, so the encrypted sync
    /// key is only ever handed over while the pairing screen is visible.
    /// </summary>
    private async Task PollPairingAsync(string pairingToken, CancellationToken ct)
    {
        var syncService = App.SyncService;
        if (syncService == null) return;

        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

                PairingStatusResult? status;
                try
                {
                    status = await syncService.GetPairingStatusAsync(pairingToken, ct);
                }
                catch (HttpRequestException) when (!ct.IsCancellationRequested)
                {
                    // Transient network hiccup while polling; try again on the next tick.
                    continue;
                }

                if (status?.PhonePublicKey is not { Length: > 0 } phonePublicKey) continue;

                // Defense in depth: re-check the same guard used in ConnectPhoneAsync right
                // before encrypting/delivering the key. ct is normally already cancelled by the
                // time the screen closes (CancelPairingPoll), which would have thrown out of the
                // Delay/GetPairingStatusAsync calls above, but this closes the gap if that ever
                // isn't true so the key is never delivered to a screen the user can't see.
                if (!ShouldContinuePairing(ct.IsCancellationRequested, IsOpen, SelectedTabIndex)) return;

                var mobileSync = App.CompanyManager?.CompanyData?.Settings.MobileSync;
                if (string.IsNullOrEmpty(mobileSync?.SyncKeyBase64)) return;

                var keyBytes = Convert.FromBase64String(mobileSync.SyncKeyBase64);
                var ciphertext = PairingKeyExchange.EncryptSyncKey(phonePublicKey, keyBytes);
                await syncService.DeliverKeyAsync(pairingToken, ciphertext, ct);

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    // The pairing token has now been consumed, so drop the QR/short code. If it were
                    // kept, it would reappear the moment IsPhoneJustPaired is cleared later (e.g. when
                    // the paired device is revoked), instead of falling back to the "Connect a phone"
                    // default.
                    QrImage = null;
                    ShortCodeDisplay = string.Empty;
                    IsShortCodeRevealed = false;
                    IsPhoneJustPaired = true;
                    await RefreshDevicesAsync();
                });
                _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.PhonePaired);

                // Push the first snapshot immediately. The phone polls /snapshot and shows
                // "Waiting for your desktop to sync" until one exists, and the only other uploader
                // (App.AutoMobileSyncAsync) is triggered by desktop navigation to the Receipts
                // page. Without this, a successful pairing leaves the phone stuck on that
                // placeholder until the user happens to open that page.
                await App.AutoMobileSyncAsync();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the pairing screen was closed (tab change/modal close) or superseded by
            // a fresh "Connect a phone" click before the phone finished pairing. Not an error.
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Sync.PollPairing");
        }
    }

    /// <summary>
    /// Refreshes the paired device list from the sync server.
    /// </summary>
    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        var companyUid = App.CompanyManager?.CompanyData?.Settings.MobileSync.CompanyUid;
        var syncService = App.SyncService;
        if (string.IsNullOrEmpty(companyUid) || syncService == null) return;

        IsRefreshingDevices = true;
        try
        {
            var list = await syncService.ListDevicesAsync(companyUid, CancellationToken.None);
            PairedDevices.Clear();
            foreach (var d in list)
            {
                PairedDevices.Add(new Core.Models.Tracking.PairedDevice
                {
                    Id = $"PDV-{d.Id}",
                    ServerDeviceId = d.Id,
                    Label = d.DeviceLabel,
                    LastSeenAt = d.LastSeenAt
                });
            }

            // Persist the refreshed list so it survives without another server round-trip.
            var data = App.CompanyManager?.CompanyData;
            if (data != null)
            {
                data.PairedDevices.Clear();
                data.PairedDevices.AddRange(PairedDevices);
            }
        }
        catch
        {
            // Silently fail; cached list stays as-is.
        }
        finally
        {
            IsRefreshingDevices = false;
        }
    }

    /// <summary>
    /// Revokes a paired phone's access, then refreshes the device list.
    /// </summary>
    [RelayCommand]
    private async Task RevokeDeviceAsync(Core.Models.Tracking.PairedDevice? device)
    {
        if (device == null) return;

        // Revoking is destructive (the phone must be paired again to sync), so confirm first.
        var dialog = App.ConfirmationDialog;
        if (dialog != null)
        {
            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Revoke Device".Translate(),
                Message = "Are you sure you want to revoke this phone? It will no longer be able to sync with this company until you pair it again.".Translate(),
                PrimaryButtonText = "Revoke".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;
        }

        var companyUid = App.CompanyManager?.CompanyData?.Settings.MobileSync.CompanyUid;
        var syncService = App.SyncService;
        if (string.IsNullOrEmpty(companyUid) || syncService == null) return;

        try
        {
            await syncService.RevokeDeviceAsync(companyUid, device.ServerDeviceId, CancellationToken.None);
        }
        catch
        {
            // Fall through to refresh regardless; if the revoke silently failed the device
            // will simply still be listed.
        }

        // The "Phone connected" confirmation is a just-paired state; once a device is revoked
        // it no longer applies, so clear it before refreshing the list.
        IsPhoneJustPaired = false;

        await RefreshDevicesAsync();
    }

    #endregion

    /// <summary>
    /// Whether there are unsaved changes in the settings.
    /// </summary>
    public bool HasUnsavedChanges =>
        SelectedTheme != _originalTheme ||
        SelectedAccentColor != _originalAccentColor ||
        SelectedLanguage != _originalLanguage ||
        SelectedDateFormat != _originalDateFormat ||
        SelectedTimeZone.Id != _originalTimeZone.Id ||
        SelectedTimeFormat != _originalTimeFormat ||
        MaxPieSlices != _originalMaxPieSlices ||
        LowStockAlert != _originalLowStockAlert ||
        OutOfStockAlert != _originalOutOfStockAlert ||
        InvoiceOverdue != _originalInvoiceOverdue ||
        RentalOverdue != _originalRentalOverdue ||
        UnsavedChangesReminder != _originalUnsavedChangesReminder ||
        UnsavedChangesReminderMinutes != _originalUnsavedChangesReminderMinutes ||
        PortalSendPaymentReminders != _originalPortalSendPaymentReminders ||
        PortalEmailOwnerOnPayment != _originalPortalEmailOwnerOnPayment ||
        ComputeBankRulesSignature() != _originalBankRulesSignature;

    // Baselines for the two server-side email preferences. They live among the
    // Notifications toggles, so they take part in the same unsaved-changes
    // prompt rather than applying the instant they are flipped.
    private bool _originalPortalSendPaymentReminders;
    private bool _originalPortalEmailOwnerOnPayment = true;

    // Snapshot of the bank import rules taken when the modal opens, so editing a rule's pattern or
    // category (or adding/removing a row) counts as an unsaved change and triggers the discard prompt.
    private string _originalBankRulesSignature = string.Empty;

    private string ComputeBankRulesSignature() =>
        string.Join("", BankCategoryRules.Select(r =>
            $"{r.Rule.Pattern}{r.Rule.CategoryId}{r.CategorySearchText}"));

    /// <summary>
    /// Default constructor.
    /// </summary>
    public SettingsModalViewModel()
    {
        // Sync with current ThemeService values
        _selectedTheme = ThemeService.Instance.CurrentThemeName;
        _selectedAccentColor = ThemeService.Instance.CurrentAccentColor;
        _originalTheme = _selectedTheme;
        _originalAccentColor = _selectedAccentColor;
    }

    #region Commands

    // The company the fields were loaded from. Save must not write them into any other.
    private CompanyData? _loadedCompanyData;

    /// <summary>
    /// Closes the modal without saving when its company closes or another one opens. It stays
    /// open through auto-lock and through a file opened from Finder, still showing the fields
    /// of the company it was opened on.
    /// </summary>
    public void CloseForCompanyChange()
    {
        var pendingNameUpdate = _portalCompanyNameCts;
        if (pendingNameUpdate != null)
        {
            pendingNameUpdate.Cancel();
            pendingNameUpdate.Dispose();
            _portalCompanyNameCts = null;
        }

        if (!IsOpen) return;

        ClosePasswordModalInternal();
        RevertChanges();
        IsOpen = false;
    }

    /// <summary>
    /// Opens the settings modal.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        OpenWithTab(SettingsTab.General);
    }

    /// <summary>Opens the settings modal with a specific tab selected (by name).</summary>
    internal void OpenWithTab(SettingsTab tab) => OpenWithTab((int)tab);

    /// <summary>
    /// Opens the settings modal with a specific tab selected.
    /// </summary>
    /// <param name="tabIndex">The tab index to select; prefer the <see cref="OpenWithTab(SettingsTab)"/> overload.</param>
    public void OpenWithTab(int tabIndex)
    {
        // Reset portal authentication, require re-auth each time settings opens
        _isPortalAuthenticated = false;
        _loadedCompanyData = App.CompanyManager?.CompanyData;

        // Sync with current ThemeService values
        SelectedTheme = ThemeService.Instance.CurrentThemeName;
        SelectedAccentColor = ThemeService.Instance.CurrentAccentColor;

        // Load language, date format and currency from company settings
        var settings = App.CompanyManager?.CompanyData?.Settings;
        if (settings != null)
        {
            // Load language without triggering change event
            SetLanguageWithoutNotify(settings.Localization.Language);
            SelectedDateFormat = settings.Localization.DateFormat;

            // Load notification settings
            LowStockAlert = settings.Notifications.LowStockAlert;
            OutOfStockAlert = settings.Notifications.OutOfStockAlert;
            InvoiceOverdue = settings.Notifications.InvoiceOverdueAlert;
            RentalOverdue = settings.Notifications.RentalOverdueAlert;
            UnsavedChangesReminder = settings.Notifications.UnsavedChangesReminder;
            UnsavedChangesReminderMinutes = settings.Notifications.UnsavedChangesReminderMinutes;
        }
        else
        {
            // Load from global settings when no company is open
            var globalSettings = App.SettingsService?.GlobalSettings;
            SetLanguageWithoutNotify(globalSettings != null
                ? globalSettings.Ui.Language
                : LanguageService.Instance.CurrentLanguage);
        }

        // Load max pie slices, timezone and time format from global settings
        {
            var globalSettings = App.SettingsService?.GlobalSettings;
            if (globalSettings != null)
            {
                MaxPieSlices = globalSettings.Ui.Chart.MaxPieSlices;
                SelectedTimeZone = TimeZones.FindById(globalSettings.Ui.TimeZone);
                SelectedTimeFormat = globalSettings.Ui.TimeFormat;
            }
        }

        LoadPortalSettings();

        // Load bank import rules
        LoadBankRules();

        // Load mobile sync state (paired devices)
        LoadMobileSync();

        _ = RefreshTelemetryStatsAsync();

        // Store original values for potential revert
        _originalTheme = SelectedTheme;
        _originalAccentColor = SelectedAccentColor;
        _originalLanguage = SelectedLanguage;
        _originalDateFormat = SelectedDateFormat;
        _originalTimeZone = SelectedTimeZone;
        _originalTimeFormat = SelectedTimeFormat;
        _originalMaxPieSlices = MaxPieSlices;
        _originalLowStockAlert = LowStockAlert;
        _originalOutOfStockAlert = OutOfStockAlert;
        _originalInvoiceOverdue = InvoiceOverdue;
        _originalRentalOverdue = RentalOverdue;
        _originalUnsavedChangesReminder = UnsavedChangesReminder;
        _originalUnsavedChangesReminderMinutes = UnsavedChangesReminderMinutes;
        _originalPortalSendPaymentReminders = PortalSendPaymentReminders;
        _originalPortalEmailOwnerOnPayment = PortalEmailOwnerOnPayment;
        SelectedTabIndex = tabIndex;
        IsOpen = true;
    }

    /// <summary>
    /// Closes the settings modal, prompting to save if there are unsaved changes.
    /// </summary>
    [RelayCommand]
    private async Task CloseAsync()
    {
        if (HasUnsavedChanges)
        {
            var dialog = App.ConfirmationDialog;
            if (dialog != null)
            {
                var result = await dialog.ShowAsync(new ConfirmationDialogOptions()
                {
                    Title = "Unsaved Changes".Translate(),
                    Message = "You have unsaved changes to your settings. Do you want to save them before closing?".Translate(),
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
                        // Don't save, revert and close
                        RevertChanges();
                        IsOpen = false;
                        return;
                    case ConfirmationResult.Cancel:
                    case ConfirmationResult.None:
                        // Stay open
                        return;
                }
            }
        }

        // No unsaved changes or dialog not available
        IsOpen = false;
    }

    /// <summary>
    /// Reverts changes to original values.
    /// </summary>
    private void RevertChanges()
    {
        if (SelectedTheme != _originalTheme)
        {
            SelectedTheme = _originalTheme;
            ApplyTheme(_originalTheme);
        }
        if (SelectedAccentColor != _originalAccentColor)
        {
            SelectedAccentColor = _originalAccentColor;
            ApplyAccentColor(_originalAccentColor);
        }
        if (SelectedLanguage != _originalLanguage)
        {
            SetLanguageWithoutNotify(_originalLanguage);
        }
        if (SelectedDateFormat != _originalDateFormat)
        {
            SelectedDateFormat = _originalDateFormat;
        }
        if (SelectedTimeZone.Id != _originalTimeZone.Id)
        {
            SelectedTimeZone = _originalTimeZone;
        }
        if (SelectedTimeFormat != _originalTimeFormat)
        {
            SelectedTimeFormat = _originalTimeFormat;
        }
        if (MaxPieSlices != _originalMaxPieSlices)
        {
            MaxPieSlices = _originalMaxPieSlices;
        }
        LowStockAlert = _originalLowStockAlert;
        OutOfStockAlert = _originalOutOfStockAlert;
        InvoiceOverdue = _originalInvoiceOverdue;
        RentalOverdue = _originalRentalOverdue;
        UnsavedChangesReminder = _originalUnsavedChangesReminder;
        UnsavedChangesReminderMinutes = _originalUnsavedChangesReminderMinutes;
    }

    /// <summary>
    /// Validates the bank-rule drafts. A fully blank row is ignored (it's dropped on save); a row
    /// with a pattern needs a category, and a row with a category needs a pattern. Sets per-row
    /// error state and returns false if any row is invalid.
    /// </summary>
    private bool ValidateBankRules()
    {
        var allValid = true;
        foreach (var row in BankCategoryRules)
        {
            row.HasPatternError = false;
            row.PatternError = null;
            row.HasCategoryError = false;
            row.CategoryError = null;

            var hasPattern = !string.IsNullOrWhiteSpace(row.Rule.Pattern);
            var hasCategory = row.SelectedCategory != null;
            // Typed-but-not-selected category text still counts as "touched", so a row with only
            // category text (no real selection, no pattern) reports errors instead of being skipped.
            var categoryTyped = !string.IsNullOrWhiteSpace(row.CategorySearchText);

            if (!hasPattern && !hasCategory && !categoryTyped) continue; // blank row, discarded on save

            if (!hasPattern)
            {
                row.HasPatternError = true;
                row.PatternError = "Enter a pattern.".Translate();
                allValid = false;
            }
            if (!hasCategory)
            {
                row.HasCategoryError = true;
                row.CategoryError = (categoryTyped ? "Pick a category from the list." : "Select a category.").Translate();
                allValid = false;
            }
        }
        return allValid;
    }

    /// <summary>
    /// Saves the settings and closes the modal.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!ReferenceEquals(App.CompanyManager?.CompanyData, _loadedCompanyData))
        {
            CloseForCompanyChange();
            return;
        }

        // Block the save and surface the errors if any bank import rule is incomplete.
        if (!ValidateBankRules())
        {
            SelectedTabIndex = (int)SettingsTab.BankImportRules;
            return;
        }

        // Check what changed before updating original values
        var languageChanged = SelectedLanguage != _originalLanguage;
        var themeChanged = SelectedTheme != _originalTheme;
        if (themeChanged)
            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ThemeChanged);
        var dateFormatChanged = SelectedDateFormat != _originalDateFormat;
        var timeSettingsChanged = SelectedTimeZone.Id != _originalTimeZone.Id ||
                                   SelectedTimeFormat != _originalTimeFormat;
        var maxPieSlicesChanged = MaxPieSlices != _originalMaxPieSlices;

        // Save the previous values in case download/fetch fails
        var previousLanguage = _originalLanguage;

        // Update original values to current (so close doesn't revert)
        _originalTheme = SelectedTheme;
        _originalAccentColor = SelectedAccentColor;
        _originalLanguage = SelectedLanguage;
        _originalDateFormat = SelectedDateFormat;
        _originalTimeZone = SelectedTimeZone;
        _originalTimeFormat = SelectedTimeFormat;
        _originalMaxPieSlices = MaxPieSlices;
        _originalLowStockAlert = LowStockAlert;
        _originalOutOfStockAlert = OutOfStockAlert;
        _originalInvoiceOverdue = InvoiceOverdue;
        _originalRentalOverdue = RentalOverdue;
        _originalUnsavedChangesReminder = UnsavedChangesReminder;
        _originalUnsavedChangesReminderMinutes = UnsavedChangesReminderMinutes;
        _originalPortalSendPaymentReminders = PortalSendPaymentReminders;
        _originalPortalEmailOwnerOnPayment = PortalEmailOwnerOnPayment;

        // The server owns these two, so Save is what actually applies them.
        // Fire-and-forget: the reconcile on next open corrects a dropped push,
        // and blocking Save on a network round trip would be worse.
        _ = PushPortalPreferencesAsync();

        // Save language, date format and currency to company settings
        var settings = App.CompanyManager?.CompanyData?.Settings;
        if (settings != null)
        {
            settings.Localization.Language = SelectedLanguage;
            settings.Localization.DateFormat = SelectedDateFormat;

            // Save notification settings
            settings.Notifications.LowStockAlert = LowStockAlert;
            settings.Notifications.OutOfStockAlert = OutOfStockAlert;
            settings.Notifications.InvoiceOverdueAlert = InvoiceOverdue;
            settings.Notifications.RentalOverdueAlert = RentalOverdue;
            settings.Notifications.UnsavedChangesReminder = UnsavedChangesReminder;
            settings.Notifications.UnsavedChangesReminderMinutes = UnsavedChangesReminderMinutes;

            // Save payment portal settings
            SavePortalSettings();

            // Commit the bank-rule drafts into company settings. Blank rows (no pattern) are
            // dropped so an unfilled "Add rule" is discarded. Replacing the list is how the draft
            // (with adds, edits, and deletes) becomes the live set, only on Save.
            settings.BankCategoryRules.Clear();
            foreach (var row in BankCategoryRules)
            {
                if (string.IsNullOrWhiteSpace(row.Rule.Pattern)) continue;
                settings.BankCategoryRules.Add(row.Rule);
            }

            // Restart the timers with new settings
            App.HeaderViewModel?.RestartUnsavedChangesReminderTimer();
            App.ApplyPortalSyncInterval();

            // Persist ONLY the settings file (appSettings.json) to the .argo.
            // SaveSettingsOnlyAsync writes just the settings, leaving the other
            // domain files and any outstanding ChangesMade flag untouched. Using
            // the full SaveCompanyAsync here would flush every in-memory edit to
            // disk and clear the unsaved-changes banner, so a theme-only change
            // would "save the entire app" instead of just the setting.
            if (App.CompanyManager?.IsCompanyOpen == true && !App.CompanyManager.IsSampleCompany)
            {
                await App.CompanyManager.SaveSettingsOnlyAsync();
            }
        }

        // Save max pie slices, language, timezone and time format to global settings
        var globalSettings = App.SettingsService?.GlobalSettings;
        if (globalSettings != null)
        {
            globalSettings.Ui.Chart.MaxPieSlices = MaxPieSlices;
            globalSettings.Ui.Language = SelectedLanguage;
            globalSettings.Ui.TimeZone = SelectedTimeZone.Id;
            globalSettings.Ui.TimeFormat = SelectedTimeFormat;
            await App.SettingsService!.SaveGlobalSettingsAsync();
        }

        // Notify that date format changed so views can refresh
        if (dateFormatChanged)
        {
            DateFormatService.NotifyDateFormatChanged();
        }

        // Notify that timezone or time format changed so views can refresh
        if (timeSettingsChanged)
        {
            TimeZoneService.NotifyTimeSettingsChanged();
        }

        // Notify that chart settings changed so charts can reload
        if (maxPieSlicesChanged)
        {
            ChartSettingsService.NotifyMaxPieSlicesChanged();
        }

        // Apply language change via LanguageService
        if (languageChanged)
        {
            IsDownloadingLanguage = true;
            try
            {
                var success = await LanguageService.Instance.SetLanguageAsync(SelectedLanguage);
                if (success)
                {
                    _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.LanguageChanged, SelectedLanguage);
                    // Notify that language was saved successfully
                    LanguageSettingsChanged?.Invoke(this, new LanguageSettingsChangedEventArgs(SelectedLanguage, true));
                }
                else
                {
                    var requestedLanguage = SelectedLanguage;

                    // Download failed - revert to previous language
                    SetLanguageWithoutNotify(previousLanguage);
                    _originalLanguage = previousLanguage;

                    // Revert in company settings
                    if (settings != null)
                    {
                        settings.Localization.Language = previousLanguage;
                    }

                    // Revert in global settings and save
                    if (globalSettings != null)
                    {
                        globalSettings.Ui.Language = previousLanguage;
                        await App.SettingsService!.SaveGlobalSettingsAsync();
                    }

                    // Show error message. A debug build talks to the dev server, which usually has no
                    // translation files; say so there, since "check your connection" sends a developer
                    // looking in the wrong place. Never shown in a release build, so not translated.
                    var missingOnDev = ApiConfig.IsSandbox && LanguageService.Instance.LastDownloadNotPublished;
                    var dialog = App.ConfirmationDialog;
                    if (dialog != null)
                    {
                        await dialog.ShowAsync(new ConfirmationDialogOptions
                        {
                            Title = missingOnDev
                                ? "No Translation Files on the Dev Server"
                                : "Language Download Failed".Translate(),
                            Message = missingOnDev
                                ? $"The dev server has no {requestedLanguage} translation file for version {AppInfo.VersionNumber}. Use a production build, or upload the language files to the dev server."
                                : "Could not download the language file from the server. Please check your internet connection and try again.".Translate(),
                            PrimaryButtonText = "OK".Translate(),
                            SecondaryButtonText = null,
                            CancelButtonText = null
                        });
                    }

                    // Everything else is saved; stay open so the language can be retried or changed.
                    // Bank rules are the one baseline Save doesn't refresh, since it normally closes.
                    _originalBankRulesSignature = ComputeBankRulesSignature();
                    return;
                }
            }
            finally
            {
                IsDownloadingLanguage = false;
            }
        }

        IsOpen = false;
    }

    /// <summary>
    /// Opens the add password modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddPassword()
    {
        ClearPasswordFields();
        IsAddPasswordModalOpen = true;
    }

    /// <summary>
    /// Opens the change password modal.
    /// </summary>
    [RelayCommand]
    private void OpenChangePassword()
    {
        ClearPasswordFields();
        IsChangePasswordModalOpen = true;
    }

    /// <summary>
    /// Opens the remove password modal.
    /// </summary>
    [RelayCommand]
    private void OpenRemovePassword()
    {
        ClearPasswordFields();
        IsRemovePasswordModalOpen = true;
    }

    /// <summary>
    /// Returns whether any password fields have been filled in.
    /// </summary>
    private bool HasPasswordModalInput =>
        !string.IsNullOrEmpty(CurrentPassword) ||
        !string.IsNullOrEmpty(NewPassword) ||
        !string.IsNullOrEmpty(ConfirmPassword);

    /// <summary>
    /// Closes all password modals, prompting to confirm if there is input.
    /// </summary>
    [RelayCommand]
    private async Task ClosePasswordModalAsync()
    {
        if ((IsAddPasswordModalOpen || IsChangePasswordModalOpen || IsRemovePasswordModalOpen) && HasPasswordModalInput)
        {
            if (!await ConfirmDiscardNewAsync()) return;
        }

        ClosePasswordModalInternal();
    }

    private void ClosePasswordModalInternal()
    {
        IsAddPasswordModalOpen = false;
        IsChangePasswordModalOpen = false;
        IsRemovePasswordModalOpen = false;
        ClearPasswordFields();
    }

    /// <summary>
    /// Confirms adding a new password.
    /// </summary>
    [RelayCommand]
    private void ConfirmAddPassword()
    {
        var validationError = Core.Security.PasswordValidator.GetValidationError(NewPassword);
        if (validationError != null)
        {
            PasswordError = validationError;
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            PasswordError = "Passwords do not match".Translate();
            return;
        }

        // Raise event to add password
        AddPasswordRequested?.Invoke(this, new PasswordChangeEventArgs(NewPassword));
        HasPassword = true;
        ClosePasswordModalInternal();
    }

    /// <summary>
    /// Confirms changing the password.
    /// </summary>
    [RelayCommand]
    private void ConfirmChangePassword()
    {
        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            PasswordError = "Current password is required".Translate();
            return;
        }
        var newPasswordError = Core.Security.PasswordValidator.GetValidationError(NewPassword);
        if (newPasswordError != null)
        {
            PasswordError = newPasswordError;
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            PasswordError = "Passwords do not match".Translate();
            return;
        }

        // Raise event to change password - handler will verify and call back
        ChangePasswordRequested?.Invoke(this, new PasswordChangeEventArgs(NewPassword, CurrentPassword));
        // Note: Don't close immediately - handler will call OnPasswordChanged or OnPasswordVerificationFailed
    }

    /// <summary>
    /// Called when password change succeeds.
    /// </summary>
    public void OnPasswordChanged()
    {
        ClosePasswordModalInternal();
    }

    /// <summary>
    /// Confirms removing the password.
    /// </summary>
    [RelayCommand]
    private void ConfirmRemovePassword()
    {
        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            PasswordError = "Current password is required".Translate();
            return;
        }

        // Raise event to remove password - handler will verify and call back
        RemovePasswordRequested?.Invoke(this, new PasswordChangeEventArgs(null, CurrentPassword));
        // Note: Don't close immediately - handler will call OnPasswordRemoved or OnPasswordError
    }

    /// <summary>
    /// Called when password removal succeeds.
    /// </summary>
    public void OnPasswordRemoved()
    {
        HasPassword = false;
        ClosePasswordModalInternal();
    }

    /// <summary>
    /// Called when password verification fails during removal.
    /// </summary>
    public void OnPasswordVerificationFailed()
    {
        PasswordError = "Incorrect password".Translate();
        CurrentPassword = string.Empty;

        // Request focus on the current password textbox
        FocusPasswordRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ClearPasswordFields()
    {
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        CurrentPassword = string.Empty;
        PasswordError = null;
        IsNewPasswordVisible = false;
        IsConfirmPasswordVisible = false;
        IsCurrentPasswordVisible = false;
    }

    /// <summary>
    /// Toggles new password visibility.
    /// </summary>
    [RelayCommand]
    private void ToggleNewPasswordVisibility()
    {
        IsNewPasswordVisible = !IsNewPasswordVisible;
    }

    /// <summary>
    /// Toggles confirm password visibility.
    /// </summary>
    [RelayCommand]
    private void ToggleConfirmPasswordVisibility()
    {
        IsConfirmPasswordVisible = !IsConfirmPasswordVisible;
    }

    /// <summary>
    /// Toggles current password visibility.
    /// </summary>
    [RelayCommand]
    private void ToggleCurrentPasswordVisibility()
    {
        IsCurrentPasswordVisible = !IsCurrentPasswordVisible;
    }

    /// <summary>
    /// Opens the telemetry data folder in the system file explorer.
    /// </summary>
    [RelayCommand]
    private async Task OpenTelemetryFolderAsync()
    {
        try
        {
            var telemetryPath = Path.Combine(
                PlatformServiceFactory.GetPlatformService().GetAppDataPath(),
                "telemetry");

            if (!Directory.Exists(telemetryPath))
            {
                Directory.CreateDirectory(telemetryPath);
            }

            // Open folder using platform-specific method
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", telemetryPath);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", telemetryPath);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", telemetryPath);
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to open telemetry folder");
            await ShowErrorDialogAsync("Error".Translate(), "Failed to open folder: {0}".TranslateFormat(ex.Message));
        }
    }

    /// <summary>
    /// Deletes all collected telemetry data.
    /// </summary>
    [RelayCommand]
    private async Task DeleteTelemetryDataAsync()
    {
        if (App.TelemetryManager == null) return;

        // Confirm deletion
        var dialog = App.ConfirmationDialog;
        if (dialog != null)
        {
            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Telemetry Data".Translate(),
                Message = "Are you sure you want to delete all collected telemetry data? This action cannot be undone.".Translate(),
                PrimaryButtonText = "Delete".Translate(),
                SecondaryButtonText = null,
                CancelButtonText = "Cancel".Translate()
            });

            if (result != ConfirmationResult.Primary)
                return;
        }

        IsDeletingTelemetry = true;
        try
        {
            await App.TelemetryManager.ClearAllDataAsync();
            TelemetryEventCount = 0;
            TelemetryPendingCount = 0;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to delete telemetry data");
            await ShowErrorDialogAsync("Error".Translate(), "Failed to delete telemetry data: {0}".TranslateFormat(ex.Message));
        }
        finally
        {
            IsDeletingTelemetry = false;
        }
    }

    /// <summary>
    /// Refreshes the telemetry statistics.
    /// </summary>
    private async Task RefreshTelemetryStatsAsync()
    {
        if (App.TelemetryManager == null) return;

        try
        {
            var stats = await App.TelemetryManager.GetStatisticsAsync();
            TelemetryEventCount = stats.TotalEvents;
            TelemetryPendingCount = stats.PendingEvents;
        }
        catch
        {
            // Ignore errors loading stats
        }
    }

    /// <summary>
    /// Closes the settings modal and opens the upgrade modal.
    /// </summary>
    [RelayCommand]
    private void UpgradeNow()
    {
        // Settings stays open behind it. Closing it meant coming back from the
        // upgrade prompt dropped you at the dashboard, having lost the tab you were
        // on and any unsaved change you had made there.
        UpgradeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects a theme.
    /// </summary>
    [RelayCommand]
    private void SelectTheme(string? theme)
    {
        if (!string.IsNullOrEmpty(theme))
        {
            SelectedTheme = theme;
            ApplyTheme(theme);
        }
    }

    /// <summary>
    /// Selects an accent color.
    /// </summary>
    [RelayCommand]
    private void SelectAccentColor(string? colorName)
    {
        if (!string.IsNullOrEmpty(colorName))
        {
            // Apply the accent color FIRST so PrimaryBrush is updated
            // before the binding triggers the MultiValueConverter
            ApplyAccentColor(colorName);
            SelectedAccentColor = colorName;
        }
    }

    private void ApplyTheme(string theme)
    {
        // Theme application will be handled by the ThemeService
        ThemeService.Instance.SetTheme(theme);
    }

    private void ApplyAccentColor(string colorName)
    {
        // Apply accent color via ThemeService
        ThemeService.Instance.SetAccentColor(colorName);
    }

    #endregion

    #region Refund-feature email verification + change

    /// <summary>
    /// Opens the registration email-verification modal. Called automatically
    /// after a successful portal registration when the server says verification
    /// is required.
    /// </summary>
    private void ShowEmailVerificationModal(string? maskedEmail)
    {
        App.RefundModalsViewModel?.OpenVerifyEmailModal(maskedEmail);
    }

    /// <summary>
    /// Working text for the "set initial owner email" inline input shown when
    /// the company has no owner email yet. The Set button is enabled once
    /// this contains a syntactically valid address.
    /// NotifyCanExecuteChangedFor wires the property change to the command's
    /// CanExecute reevaluation so the button enables as the user types.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetInitialOwnerEmailCommand))]
    private string _pendingOwnerEmail = string.Empty;

    /// <summary>
    /// True while the SetInitialOwnerEmail request is in flight. Disables
    /// the command's CanExecute so a rapid second click can't send a
    /// concurrent request (which would race the verify-modal open and
    /// produce confusing 409 recovery flows).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetInitialOwnerEmailCommand))]
    private bool _isSettingOwnerEmail;

    public bool CanSetInitialOwnerEmail
    {
        get
        {
            return !IsSettingOwnerEmail && DataValidator.IsValidEmail(PendingOwnerEmail);
        }
    }

    /// <summary>
    /// First-time setup of the portal owner email. The server holds the
    /// address as pending and emails a verification code to it; owner_email
    /// is only written once the code is confirmed. We immediately open the
    /// VerifyEmailModal so the user finishes the loop. Closing the modal
    /// without verifying sets nothing, locally or server-side.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSetInitialOwnerEmail))]
    private async Task SetInitialOwnerEmailAsync()
    {
        var refundService = App.RefundService;
        var companyData = App.CompanyManager?.CompanyData;
        if (refundService == null || companyData == null) return;

        var email = PendingOwnerEmail.Trim();

        IsSettingOwnerEmail = true;
        try
        {
            // Setting the owner email hits an authenticated portal endpoint, so the company must
            // already be registered (i.e. have an API key). Registration used to be triggered lazily
            // by the first Connect, but the owner email is now required *before* connecting, so
            // auto-register here first (same pattern as the logo upload). Without this the very first
            // action on a fresh company fails with "Invalid or missing API key".
            if (!PortalSettings.IsConfigured)
            {
                var portalService = App.PaymentPortalService;
                if (portalService == null) return;
                var registered = await TryRegisterPortalAsync(portalService);
                if (!registered) return;
            }

            await SetInitialOwnerEmailCoreAsync(refundService, companyData, email);
        }
        finally
        {
            IsSettingOwnerEmail = false;
        }
    }

    private async Task SetInitialOwnerEmailCoreAsync(
        RefundService refundService, CompanyData companyData, string email)
    {
        var result = await refundService.SetInitialOwnerEmailAsync(email);

        // Recovery path: server says email is already set on the company,
        // but the local .argo doesn't have it.
        // The 409 response includes the existing email, if it matches
        // what the user just typed, silently reconcile local state. If it
        // differs, surface the existing email so the user can act.
        if (!result.Ok && result.ErrorCode == "OWNER_EMAIL_ALREADY_SET")
        {
            var existing = result.OwnerEmail?.Trim();
            if (!string.IsNullOrEmpty(existing) &&
                string.Equals(existing, email, StringComparison.OrdinalIgnoreCase))
            {
                // Reconcile silently: mirror server's value locally and persist.
                await ReconcileOwnerEmailAsync(companyData, existing);
                return;
            }

            await ShowErrorDialogAsync(
                "Owner email already set".Translate(),
                string.IsNullOrEmpty(existing)
                    ? "An owner email is already on file. Use the Change flow to update it.".Translate()
                    : $"This portal account already has the owner email {existing}. Use the Change flow to update it.".Translate());

            // Even on the "different email" branch, the local .argo may still
            // be out of sync, pull the server's value down so the UI reflects
            // reality and the refund pre-flight stops false-blocking.
            if (!string.IsNullOrEmpty(existing))
            {
                await ReconcileOwnerEmailAsync(companyData, existing);
            }
            return;
        }

        if (!result.Ok)
        {
            var detail = result.Message
                ?? result.ErrorCode
                ?? $"The server rejected the request (HTTP {result.HttpStatus}).";
            await ShowErrorDialogAsync("Could not set owner email".Translate(), detail.Translate());
            return;
        }

        // Server sent a code to the new email; pop the verify modal so the
        // user can confirm it. The masked email comes back in the response
        // so we display the same masking server-side.
        //
        // Nothing is mirrored or persisted locally yet: the server holds the
        // address as pending and only writes owner_email once the code is
        // confirmed. If the user closes the modal without verifying, no email
        // is set anywhere and they can simply retry (PendingOwnerEmail keeps
        // their typed value). Mirror-and-persist happens in the OnVerified
        // callback via ReconcileOwnerEmailAsync.
        App.RefundModalsViewModel?.OpenVerifyEmailModal(
            result.MaskedEmail,
            onVerified: () => _ = ReconcileOwnerEmailAsync(companyData, email));
    }

    /// <summary>
    /// Mirror the server-known owner email into local state and persist via
    /// the scoped settings-only save. Used after successful verification of
    /// a newly set email, and by the OWNER_EMAIL_ALREADY_SET recovery path
    /// so a restart-with-broken-local-state can self-heal.
    /// </summary>
    private async Task ReconcileOwnerEmailAsync(CompanyData companyData, string serverEmail)
    {
        // The save below writes whichever company is open, so skip it once that is another one.
        if (!ReferenceEquals(App.CompanyManager?.CompanyData, companyData)) return;

        companyData.Settings.Company.Email = serverEmail;
        CompanyEmail = serverEmail;
        PendingOwnerEmail = string.Empty;
        companyData.ChangesMade = true;
        if (App.CompanyManager != null)
        {
            try { await App.CompanyManager.SaveSettingsOnlyAsync(); }
            catch (Exception ex) { App.ErrorLogger?.LogWarning($"Failed to persist reconciled owner email: {ex.Message}", "OwnerEmail"); }
        }
    }

    /// <summary>
    /// Pulls the authoritative owner email out of a portal status response and
    /// mirrors it into local state when it is safe to do so (see
    /// <see cref="ShouldReconcilePortalEmail"/>). This is how a server-side
    /// change, such as the email-change revert link, reaches this device.
    /// </summary>
    private async Task ReconcilePortalEmailFromStatusAsync(PortalStatusResponse status)
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var serverEmail = status.Company?.OwnerEmail;
        var localEmail = companyData.Settings.Company.Email;
        // The Company-details editor is the only place the email is hand-edited
        // outside the verified change flow; don't clobber an in-flight edit.
        var isEmailBeingEdited = App.EditCompanyModalViewModel?.IsOpen == true;

        if (!ShouldReconcilePortalEmail(serverEmail, localEmail, isEmailBeingEdited)) return;

        await ReconcileOwnerEmailAsync(companyData, serverEmail!.Trim());
    }

    /// <summary>
    /// "Server wins, but only when clean." Returns true only when the server
    /// owner email is present, actually differs from the local value, and the
    /// email is not being hand-edited. Pure so it can be unit-tested in
    /// isolation.
    /// </summary>
    internal static bool ShouldReconcilePortalEmail(string? serverEmail, string? localEmail, bool isEmailBeingEdited)
    {
        // Protect an in-flight local edit.
        if (isEmailBeingEdited) return false;
        // Never wipe local with a blank (covers the pre-set / pending-change windows).
        if (string.IsNullOrWhiteSpace(serverEmail)) return false;
        // Only write when the value actually changed, so we don't dirty the file needlessly.
        return !string.Equals(serverEmail.Trim(), (localEmail ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opens the 4-step "Change owner email" modal. Bound to a button in
    /// PortalSettings.
    /// </summary>
    [RelayCommand]
    private async Task OpenEmailChangeModalAsync()
    {
        var companyData = App.CompanyManager?.CompanyData;
        var companyManager = App.CompanyManager;
        if (companyData == null || companyManager == null) return;

        var portalService = App.PaymentPortalService;

        // Same reason as SetInitialOwnerEmailAsync: every step of the change flow is an
        // authenticated portal call. A company can reach Change rather than Set purely
        // because its own settings carry an email, which says nothing about whether a
        // portal account exists. The sample company is exactly that case, and without
        // this the flow opens and then fails with "Invalid or missing API key".
        if (!PortalSettings.IsConfigured)
        {
            if (portalService == null) return;
            if (!await TryRegisterPortalAsync(portalService)) return;
        }

        // Pre-flight sync: if the settings modal was already open when the owner
        // email changed on the server (e.g. a revert link was used), starting the
        // change flow from the stale local value would confuse the user. Pull the
        // authoritative email first. Best-effort: CheckStatusAsync swallows its own
        // network errors and returns Success=false, in which case we keep local.
        if (portalService != null && PortalSettings.IsConfigured)
        {
            // Cap the pre-flight so a bad network can't make the button feel
            // like it hung (the client's own timeout is 30s). On timeout the
            // call returns Success=false and we simply keep the local value.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var status = await portalService.CheckStatusAsync(cts.Token);
            if (status.Success)
                await ReconcilePortalEmailFromStatusAsync(status);
        }

        var currentEmail = companyData.Settings.Company.Email ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentEmail))
        {
            await ShowErrorDialogAsync("No email on file".Translate(),
                "This company has no owner email yet. Set one in the company details first.".Translate());
            return;
        }

        // Hand off to the AppShell-level RefundModals coordinator. On success
        // it invokes the callback below, which mirrors the new email into the
        // local Company settings and marks the file dirty.
        App.RefundModalsViewModel?.OpenEmailChangeModal(
            currentEmail,
            companyManager.IsEncrypted,
            password => companyManager.VerifyCurrentPassword(password),
            onCompleted: newEmail =>
            {
                companyData.Settings.Company.Email = newEmail;
                CompanyEmail = newEmail;
                companyData.ChangesMade = true;
            },
            currentEmailVerified: PortalOwnerEmailVerified);
    }

    #endregion
}

/// <summary>
/// Represents an accent color option.
/// </summary>
public class AccentColorItem(string name, string colorHex)
{
    public string Name { get; } = name;
    public string ColorHex { get; } = colorHex;
}

/// <summary>
/// Event args for password change operations.
/// </summary>
public class PasswordChangeEventArgs(string? newPassword, string? currentPassword = null) : EventArgs
{
    /// <summary>
    /// The new password (null to remove password).
    /// </summary>
    public string? NewPassword { get; } = newPassword;

    /// <summary>
    /// The current password (for verification when changing/removing).
    /// </summary>
    public string? CurrentPassword { get; } = currentPassword;
}

/// <summary>
/// Event args for auto-lock settings change.
/// </summary>
public class AutoLockSettingsEventArgs(string timeoutString) : EventArgs
{

    /// <summary>
    /// The timeout in minutes (0 for "Never").
    /// </summary>
    public int TimeoutMinutes { get; } = ParseTimeoutMinutes(timeoutString);

    private static int ParseTimeoutMinutes(string? timeoutString)
    {
        if (string.IsNullOrEmpty(timeoutString) || timeoutString == "Never")
            return 0;

        if (timeoutString.Contains("hour"))
            return 60;

        var parts = timeoutString.Split(' ');
        if (parts.Length >= 1 && int.TryParse(parts[0], out var minutes))
            return minutes;

        return 0;
    }

}

/// <summary>
/// Event args for biometric login setting changes.
/// </summary>
public class BiometricLoginEventArgs(bool enabled) : EventArgs
{
    /// <summary>
    /// Whether biometric login is enabled.
    /// </summary>
    public bool Enabled { get; } = enabled;
}

/// <summary>
/// Event args for language settings changes.
/// </summary>
public class LanguageSettingsChangedEventArgs(string language, bool applied) : EventArgs
{
    /// <summary>
    /// The selected language name (e.g., "French", "German").
    /// </summary>
    public string Language { get; } = language;

    /// <summary>
    /// Whether the language change has been applied (translations downloaded and active).
    /// </summary>
    public bool Applied { get; } = applied;
}

/// <summary>
/// Wraps a <see cref="BankCategoryRule"/> for display in the "Bank import rules" settings tab.
/// Exposes <see cref="SelectedCategory"/> as a <see cref="Category"/> object so the category
/// picker can bind to it, while keeping <see cref="BankCategoryRule.CategoryId"/> in sync.
/// </summary>
public class BankCategoryRuleRow : ObservableObject
{
    public BankCategoryRuleRow(BankCategoryRule rule, ObservableCollection<Category> allCategories)
    {
        Rule = rule;
        _selectedCategory = allCategories.FirstOrDefault(c => c.Id == rule.CategoryId);
        // The category picker is a SearchableDropdown, which shows its SearchText. Seed it with the
        // selected category's name; without this a loaded rule renders an empty picker even though a
        // category IS selected - which made imported rules look like they had no category.
        _categorySearchText = _selectedCategory?.Name;
    }

    /// <summary>The underlying rule stored in company data.</summary>
    public BankCategoryRule Rule { get; }

    /// <summary>
    /// The text pattern to match against bank statement descriptions.
    /// Bound TwoWay in the settings UI; writes through to <see cref="BankCategoryRule.Pattern"/>.
    /// </summary>
    public string Pattern
    {
        get => Rule.Pattern;
        set
        {
            if (Rule.Pattern == value) return;
            Rule.Pattern = value;
            Rule.UpdatedAt = DateTime.UtcNow;
            OnPropertyChanged();
            HasPatternError = false;
        }
    }

    private Category? _selectedCategory;

    /// <summary>
    /// The category object selected in the picker.
    /// Setting this updates <see cref="BankCategoryRule.CategoryId"/> and marks the file dirty.
    /// </summary>
    public Category? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value) return;
            _selectedCategory = value;
            Rule.CategoryId = value?.Id ?? string.Empty;
            Rule.UpdatedAt = DateTime.UtcNow;
            OnPropertyChanged();
            HasCategoryError = false;
        }
    }

    private string? _categorySearchText;
    /// <summary>
    /// Text typed into the category dropdown. Tracked so validation can flag a row where the user
    /// typed a category name but never picked a real one (SelectedCategory stays null), which would
    /// otherwise look like an untouched blank row and be silently skipped.
    /// </summary>
    public string? CategorySearchText
    {
        get => _categorySearchText;
        set => SetProperty(ref _categorySearchText, value);
    }

    private bool _hasPatternError;
    /// <summary>True when the row has a category but no pattern; shows an inline error.</summary>
    public bool HasPatternError { get => _hasPatternError; set => SetProperty(ref _hasPatternError, value); }

    private string? _patternError;
    public string? PatternError { get => _patternError; set => SetProperty(ref _patternError, value); }

    private bool _hasCategoryError;
    /// <summary>True when the row has a pattern but no valid category; shows an inline error.</summary>
    public bool HasCategoryError { get => _hasCategoryError; set => SetProperty(ref _hasCategoryError, value); }

    private string? _categoryError;
    public string? CategoryError { get => _categoryError; set => SetProperty(ref _categoryError, value); }
}
