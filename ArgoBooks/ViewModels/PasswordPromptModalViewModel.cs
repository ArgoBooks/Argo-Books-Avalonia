using ArgoBooks.Localization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the password prompt modal.
/// Used when opening encrypted company files.
/// </summary>
public partial class PasswordPromptModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _biometricLoginAvailable;

    [ObservableProperty]
    private bool _isBiometricAuthenticating;

    [ObservableProperty]
    private bool _showBiometricSuccess;

    private TaskCompletionSource<string?>? _completionSource;
    private TaskCompletionSource<bool>? _biometricSuccessCompletionSource;

    /// <summary>
    /// Event raised when the password textbox should be focused.
    /// </summary>
    public event EventHandler? FocusPasswordRequested;

    /// <summary>
    /// Event raised when biometric authentication is requested.
    /// </summary>
    public event EventHandler? BiometricAuthRequested;

    /// <summary>
    /// Gets the task that completes when the user submits a password.
    /// Use this to wait for password retries after calling ShowError.
    /// </summary>
    public Task<string?> WaitForPasswordAsync()
    {
        return _completionSource?.Task ?? Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Icon for password visibility toggle.
    /// </summary>
    public string PasswordVisibilityIcon => IsPasswordVisible ? Icons.EyeOff : Icons.Eye;

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

    /// <summary>
    /// Default constructor.
    /// </summary>
    public PasswordPromptModalViewModel()
    {
    }

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordVisibilityIcon));
        OnPropertyChanged(nameof(PasswordMaskChar));
    }

    /// <summary>
    /// Shows the password prompt and waits for user input.
    /// </summary>
    /// <param name="companyName">Name of the company file.</param>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="biometricLoginAvailable">Whether biometric is available and enabled for this file.</param>
    /// <param name="message">Optional custom message. If null, defaults to "Enter password for {companyName}".</param>
    /// <returns>The entered password, or null if cancelled.</returns>
    public Task<string?> ShowAsync(string companyName, string filePath, bool biometricLoginAvailable = false, string? message = null)
    {
        CompanyName = companyName;
        FilePath = filePath;
        Message = message ?? string.Format("Enter password for {0}".Translate(), companyName);
        Password = string.Empty;
        ErrorMessage = string.Empty;
        HasError = false;
        IsLoading = false;
        IsPasswordVisible = false;
        BiometricLoginAvailable = biometricLoginAvailable;
        IsBiometricAuthenticating = false;
        ShowBiometricSuccess = false;
        IsOpen = true;

        // Use RunContinuationsAsynchronously to prevent TrySetResult from running
        // the awaiting continuation inline, which can block UI updates
        _completionSource = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _completionSource.Task;
    }

    /// <summary>
    /// Shows an error message (e.g., wrong password) and keeps modal open for retry.
    /// </summary>
    public void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
        IsLoading = false;
        IsBiometricAuthenticating = false;
        IsOpen = true; // Ensure modal stays/reopens

        // Create a new completion source for the retry
        // Use RunContinuationsAsynchronously to prevent inline continuations
        _completionSource = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Request focus on the password textbox
        FocusPasswordRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called when biometric authentication succeeds.
    /// Shows success animation and then completes with a special marker.
    /// </summary>
    public void OnBiometricSuccess()
    {
        IsBiometricAuthenticating = false;
        ShowBiometricSuccess = true;
        _biometricSuccessCompletionSource = new TaskCompletionSource<bool>();
    }

    /// <summary>
    /// Called when the success animation continue button is clicked.
    /// </summary>
    [RelayCommand]
    private void ContinueAfterSuccess()
    {
        ShowBiometricSuccess = false;
        IsOpen = false;
        _biometricSuccessCompletionSource?.TrySetResult(true);
        // Complete with special marker to indicate biometric was used
        CompletedWithBiometric = true;
        _completionSource?.TrySetResult(BiometricMarker);
    }

    /// <summary>What the prompt answers with when the fingerprint or face check passed.</summary>
    public const string BiometricMarker = "__BIOMETRIC__";

    /// <summary>
    /// Whether the last answer came from a passed biometric check. The marker travels the same way
    /// a typed password does, so on its own it proves nothing: typing it into the box used to
    /// unlock the company with the stored password and no check at all.
    /// </summary>
    public bool CompletedWithBiometric { get; private set; }

    /// <summary>True only for the marker produced by a passed biometric check, never a typed one.</summary>
    public bool IsBiometricAnswer(string? answer) => CompletedWithBiometric && answer == BiometricMarker;

    /// <summary>
    /// Called when biometric authentication fails.
    /// </summary>
    public void OnBiometricFailed()
    {
        IsBiometricAuthenticating = false;
        // Don't show error - user may have cancelled, just let them try password
    }

    /// <summary>
    /// Closes the modal after successful password entry.
    /// Call this when the password was accepted.
    /// </summary>
    public void Close()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CloseCore();
        }
        else
        {
            Dispatcher.UIThread.Post(CloseCore);
        }
    }

    private void CloseCore()
    {
        IsOpen = false;
        IsLoading = false;
        IsBiometricAuthenticating = false;
        ShowBiometricSuccess = false;
        Password = string.Empty;
        ErrorMessage = string.Empty;
        HasError = false;
    }

    #region Commands

    /// <summary>
    /// Toggles password visibility.
    /// </summary>
    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    /// <summary>
    /// Submits the password.
    /// </summary>
    [RelayCommand]
    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter the password";
            HasError = true;
            return;
        }

        HasError = false;
        IsLoading = true;

        // Complete with the password but keep modal open
        // The caller will close it on success or call ShowError on failure
        CompletedWithBiometric = false;
        _completionSource?.TrySetResult(Password);
    }

    /// <summary>
    /// Authenticates using biometric.
    /// </summary>
    [RelayCommand]
    private void UseBiometricLogin()
    {
        if (!BiometricLoginAvailable) return;

        HasError = false;
        IsBiometricAuthenticating = true;

        // Request biometric authentication from the app
        BiometricAuthRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Cancels the password prompt, showing confirmation if a password was entered.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (!string.IsNullOrEmpty(Password))
        {
            if (!await ConfirmDiscardNewAsync()) return;
        }

        IsOpen = false;
        Password = string.Empty;
        IsBiometricAuthenticating = false;
        ShowBiometricSuccess = false;
        _completionSource?.TrySetResult(null);
    }

    #endregion
}
