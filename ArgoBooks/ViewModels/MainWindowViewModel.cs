using ArgoBooks.Core.Models;
using Avalonia.Controls;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the main application window. Manages window state,
/// current view, and company state.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGlobalSettingsService? _globalSettingsService;

    #region Window State Properties

    [ObservableProperty]
    private double _windowWidth = 1280;

    [ObservableProperty]
    private double _windowHeight = 800;

    [ObservableProperty]
    private double _windowLeft = -1;

    [ObservableProperty]
    private double _windowTop = -1;

    [ObservableProperty]
    private WindowState _windowState = WindowState.Normal;

    [ObservableProperty]
    private string _windowTitle = "Argo Books";

    #endregion

    #region Sample Company Banner

    /// <summary>
    /// The demo has always been reachable and always been leavable, through File. What it
    /// never had was anything that asked at the moment the answer might be yes, which is
    /// why the sample gets opened far more often than a real company gets created. This
    /// banner is that ask: always on screen while the demo is open, never a modal, and
    /// dismissible so it cannot become nagging.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSampleBanner))]
    [NotifyPropertyChangedFor(nameof(SampleBannerText))]
    private bool _isSampleCompany;

    /// <summary>
    /// Receipts scanned in this demo session. Someone who has scanned a few has just
    /// watched the thing work and is as convinced as they will ever be, so the message
    /// stops being a general notice and starts naming what they are about to throw away.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SampleBannerText))]
    private int _sampleReceiptScans;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSampleBanner))]
    private bool _sampleBannerDismissed;

    public bool ShowSampleBanner => IsSampleCompany && !SampleBannerDismissed;

    public string SampleBannerText => SampleReceiptScans >= 3
        ? string.Format(
            "You have scanned {0} receipts in the demo. None of them are being kept."
                .Translate(),
            SampleReceiptScans)
        : "You are in the demo company. Nothing you do here is saved to your own books."
            .Translate();

    /// <summary>Counts a scan and is a no-op outside the demo.</summary>
    public void NoteSampleReceiptScan()
    {
        if (IsSampleCompany)
        {
            SampleReceiptScans++;
        }
    }

    [RelayCommand]
    private void DismissSampleBanner() => SampleBannerDismissed = true;

    [RelayCommand]
    private static async Task CreateMyCompanyAsync() => await App.RequestCreateNewCompanyAsync();

    #endregion

    #region Application State Properties

    [ObservableProperty]
    private bool _hasCompanyOpen;

    [ObservableProperty]
    private string? _currentCompanyName;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadingMessage;

    [ObservableProperty]
    private string? _loadingDetail;

    [ObservableProperty]
    private double _loadingProgress = -1;

    [ObservableProperty]
    private bool _isLoadingCancellable;

    private CancellationTokenSource? _loadingCts;
    private Func<Task<bool>>? _cancelConfirmation;
    private CancellationTokenSource? _savedFeedbackCts;

    public bool IsLoadingIndeterminate => LoadingProgress < 0;

    public string? LoadingProgressText => LoadingProgress >= 0 ? $"{LoadingProgress:0}%" : null;

    partial void OnLoadingProgressChanged(double value)
    {
        OnPropertyChanged(nameof(IsLoadingIndeterminate));
        OnPropertyChanged(nameof(LoadingProgressText));
    }

    [RelayCommand]
    private async Task CancelLoadingAsync()
    {
        if (_cancelConfirmation != null)
        {
            var confirmed = await _cancelConfirmation();
            if (!confirmed) return;
        }
        _loadingCts?.Cancel();
    }

    /// <summary>
    /// Gets or sets the CreateCompanyViewModel for the full-screen wizard.
    /// </summary>
    [ObservableProperty]
    private CreateCompanyViewModel? _createCompanyViewModel;

    /// <summary>
    /// Gets or sets the WelcomeScreenViewModel for the full-screen welcome overlay.
    /// </summary>
    [ObservableProperty]
    private WelcomeScreenViewModel? _welcomeScreenViewModel;

    /// <summary>
    /// Gets or sets the PasswordPromptModalViewModel for the password prompt overlay.
    /// </summary>
    [ObservableProperty]
    private PasswordPromptModalViewModel? _passwordPromptModalViewModel;

    /// <summary>
    /// Gets or sets the ConfirmationDialogViewModel for showing confirmation dialogs.
    /// </summary>
    [ObservableProperty]
    private ConfirmationDialogViewModel? _confirmationDialogViewModel;

    /// <summary>
    /// Gets or sets the UnsavedChangesDialogViewModel for showing unsaved changes dialogs.
    /// </summary>
    [ObservableProperty]
    private UnsavedChangesDialogViewModel? _unsavedChangesDialogViewModel;

    /// <summary>
    /// Gets or sets the ReceiptViewerModalViewModel for viewing receipt images.
    /// </summary>
    [ObservableProperty]
    private ReceiptViewerModalViewModel? _receiptViewerModalViewModel;

    /// <summary>
    /// Gets or sets the TutorialWelcomeViewModel for the first-time user welcome overlay.
    /// </summary>
    [ObservableProperty]
    private TutorialWelcomeViewModel? _tutorialWelcomeViewModel;

    /// <summary>
    /// Gets or sets the AppTourViewModel for the interactive app tour.
    /// </summary>
    [ObservableProperty]
    private AppTourViewModel? _appTourViewModel;

    /// <summary>
    /// Gets or sets the CategoriesTutorialViewModel for the Categories page tutorial.
    /// </summary>
    [ObservableProperty]
    private CategoriesTutorialViewModel? _categoriesTutorialViewModel;

    /// <summary>
    /// Gets or sets the ProductsTutorialViewModel for the Products page tutorial.
    /// </summary>
    [ObservableProperty]
    private ProductsTutorialViewModel? _productsTutorialViewModel;

    /// <summary>
    /// Whether to show the welcome screen (when no company is open).
    /// </summary>
    [ObservableProperty]
    private bool _showWelcomeScreen = true;

    /// <summary>
    /// Whether the current company has unsaved changes.
    /// </summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// Whether to show the "Saved" indicator.
    /// </summary>
    [ObservableProperty]
    private bool _showSavedIndicator;

    /// <summary>
    /// Opacity for the saved indicator (for fade animation).
    /// </summary>
    [ObservableProperty]
    private double _savedIndicatorOpacity;

    #endregion

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public MainWindowViewModel() : this(null)
    {
    }

    /// <summary>
    /// Constructor with dependency injection.
    /// </summary>
    /// <param name="globalSettingsService">Service for persisting window state.</param>
    public MainWindowViewModel(IGlobalSettingsService? globalSettingsService)
    {
        _globalSettingsService = globalSettingsService;
    }

    /// <summary>
    /// Updates the window title when company name changes.
    /// </summary>
    partial void OnCurrentCompanyNameChanged(string? value)
    {
        HasCompanyOpen = !string.IsNullOrEmpty(value);
        UpdateWindowTitle();
    }

    /// <summary>
    /// Updates the window title when unsaved changes status changes.
    /// </summary>
    partial void OnHasUnsavedChangesChanged(bool value)
    {
        UpdateWindowTitle();
    }

    /// <summary>
    /// Updates the window title based on company name and unsaved changes.
    /// </summary>
    private void UpdateWindowTitle()
    {
        if (string.IsNullOrEmpty(CurrentCompanyName))
        {
            WindowTitle = "Argo Books";
        }
        else
        {
            var asterisk = HasUnsavedChanges ? "*" : "";
            WindowTitle = $"{asterisk}{CurrentCompanyName} - Argo Books";
        }
    }

    /// <summary>
    /// Marks that there are unsaved changes.
    /// </summary>
    public void MarkUnsavedChanges()
    {
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Shows the saved indicator and hides it after 3 seconds.
    /// </summary>
    public async void ShowSavedFeedback()
    {
        try
        {
            // Cancel any previous feedback animation
            _savedFeedbackCts?.Cancel();
            _savedFeedbackCts?.Dispose();
            var cts = new CancellationTokenSource();
            _savedFeedbackCts = cts;

            HasUnsavedChanges = false;
            ShowSavedIndicator = true;
            SavedIndicatorOpacity = 1.0;

            // Wait 3 seconds then fade out
            await Task.Delay(3000, cts.Token);

            // Fade out by setting opacity to 0 (animation handled in XAML)
            SavedIndicatorOpacity = 0;

            // Wait for fade animation
            await Task.Delay(300, cts.Token);

            ShowSavedIndicator = false;
        }
        catch (TaskCanceledException)
        {
            // Superseded by a newer call, do nothing
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "ShowSavedFeedback");
        }
    }

    /// <summary>
    /// Loads the saved window state from settings.
    /// </summary>
    public void LoadWindowState()
    {
        if (_globalSettingsService == null)
            return;

        try
        {
            var settings = _globalSettingsService.GetSettings();
            if (settings?.WindowState != null)
            {
                var ws = settings.WindowState;

                if (ws.Width > 0)
                    WindowWidth = ws.Width;
                if (ws.Height > 0)
                    WindowHeight = ws.Height;
                if (ws.Left >= 0)
                    WindowLeft = ws.Left;
                if (ws.Top >= 0)
                    WindowTop = ws.Top;

                WindowState = ws.IsMaximized ? WindowState.Maximized : WindowState.Normal;
            }
        }
        catch
        {
            // Ignore errors loading window state - use defaults
        }
    }

    /// <summary>
    /// Saves the current window state to settings.
    /// </summary>
    public void SaveWindowState()
    {
        if (_globalSettingsService == null)
            return;

        try
        {
            var settings = _globalSettingsService.GetSettings() ?? new GlobalSettings();
            settings.WindowState = new WindowStateSettings
            {
                Width = WindowWidth,
                Height = WindowHeight,
                Left = WindowLeft,
                Top = WindowTop,
                IsMaximized = WindowState == WindowState.Maximized
            };

            _globalSettingsService.SaveSettings(settings);
        }
        catch
        {
            // Ignore errors saving window state
        }
    }

    /// <summary>
    /// Navigates to a view by setting it as the current view.
    /// </summary>
    /// <param name="view">The view to display.</param>
    public void NavigateTo(object view)
    {
        CurrentView = view;
    }

    /// <summary>
    /// Shows a loading overlay with optional message.
    /// </summary>
    /// <param name="message">Loading message to display.</param>
    /// <param name="detail">Optional secondary detail text.</param>
    /// <param name="progress">Progress value 0-100, or -1 for indeterminate.</param>
    public void ShowLoading(string? message = null, string? detail = null, double progress = -1,
        CancellationTokenSource? cts = null, Func<Task<bool>>? cancelConfirmation = null)
    {
        LoadingMessage = message ?? "Loading...";
        LoadingDetail = detail;
        LoadingProgress = progress;
        _loadingCts = cts;
        _cancelConfirmation = cancelConfirmation;
        IsLoadingCancellable = cts != null;
        IsLoading = true;
    }

    /// <summary>
    /// Moves the progress bar without disturbing the message or the cancel wiring, so a progress
    /// callback does not have to hold the CancellationTokenSource just to report a percentage.
    /// </summary>
    public void UpdateLoadingProgress(double progress) => LoadingProgress = progress;

    /// <summary>
    /// Hides the loading overlay.
    /// </summary>
    public void HideLoading()
    {
        IsLoading = false;
        LoadingMessage = null;
        LoadingDetail = null;
        LoadingProgress = -1;
        _loadingCts = null;
        _cancelConfirmation = null;
        IsLoadingCancellable = false;
    }

    /// <summary>
    /// Opens a company with the given name.
    /// </summary>
    /// <param name="companyName">Name of the company to display.</param>
    public void OpenCompany(string companyName)
    {
        CurrentCompanyName = companyName;
        ShowWelcomeScreen = false;

        // Show tutorial welcome for first-time users
        TutorialWelcomeViewModel?.ShowIfNeeded();
    }

    /// <summary>
    /// Closes the current company.
    /// </summary>
    public void CloseCompany()
    {
        CurrentCompanyName = null;
        ShowWelcomeScreen = true;
        ReportWelcomeShown();
    }

    /// <summary>
    /// Called explicitly rather than from a property-changed hook: the property starts true, so
    /// launch raises no notification, and telemetry is not up yet when the window first draws.
    /// </summary>
    public static void ReportWelcomeShown()
    {
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.WelcomeShown);
    }
}
