using ArgoBooks.Core.Models;
using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.Services;

/// <summary>
/// Service for managing the first-time user tutorial system.
/// </summary>
public class TutorialService
{
    private IGlobalSettingsService? _globalSettingsService;
    private string? _currentCompanyPath;
    private bool _hintsDisabledThisSession;

    /// <summary>
    /// Gets the singleton instance of the TutorialService.
    /// </summary>
    public static TutorialService Instance => field ??= new TutorialService();

    /// <summary>
    /// Setup checklist item identifiers.
    /// </summary>
    public static class ChecklistItems
    {
        public const string ScanReceipt = "scan_receipt";
        public const string RecordExpense = "record_expense";
        public const string RecordRevenue = "record_revenue";
        public const string ExploreDashboard = "explore_dashboard";
        public const string VisitAnalytics = "visit_analytics";
    }

    /// <summary>
    /// Page identifiers for first-visit hints.
    /// </summary>
    public static class Pages
    {
        public const string Dashboard = "Dashboard";
        public const string Analytics = "Analytics";
        public const string Insights = "Insights";
        public const string Reports = "Reports";
        public const string Expenses = "Expenses";
        public const string Revenue = "Revenue";
        public const string Invoices = "Invoices";
        public const string Products = "Products";
        public const string Customers = "Customers";
        public const string Suppliers = "Suppliers";
        public const string Categories = "Categories";
        public const string StockLevels = "StockLevels";
        public const string Receipts = "Receipts";
        public const string RentalInventory = "RentalInventory";
        public const string RentalRecords = "RentalRecords";
    }

    /// <summary>
    /// Event raised when a checklist item is completed.
    /// </summary>
    public event EventHandler<string>? ChecklistItemCompleted;

    /// <summary>
    /// Event raised when all checklist items are completed.
    /// </summary>
    public event EventHandler? AllChecklistItemsCompleted;

    /// <summary>
    /// Event raised when a page is visited for the first time.
    /// </summary>
    public event EventHandler<string>? PageFirstVisited;

    /// <summary>
    /// Event raised when tutorial state changes.
    /// </summary>
    public event EventHandler? TutorialStateChanged;

    /// <summary>
    /// Event raised when completion guidance should be shown/hidden.
    /// </summary>
    public event EventHandler<bool>? CompletionGuidanceChanged;

    /// <summary>
    /// Event raised when the post-onboarding source survey should be shown/hidden.
    /// </summary>
    public event EventHandler<bool>? SourceSurveyVisibilityChanged;

    /// <summary>
    /// Gets or sets whether the completion guidance overlay should be shown.
    /// </summary>
    public bool ShowCompletionGuidance
    {
        get => field;
        set
        {
            if (field != value)
            {
                field = value;
                CompletionGuidanceChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the type of completion guidance to show.
    /// </summary>
    public CompletionGuidanceType CurrentGuidanceType { get; set; } = CompletionGuidanceType.Standard;

    // Flag to skip the next dismiss call (used when guidance is shown during navigation)
    private bool _skipNextDismiss;

    /// <summary>
    /// Dismisses the completion guidance overlay.
    /// </summary>
    public void DismissCompletionGuidance()
    {
        if (_skipNextDismiss)
        {
            // Skip this dismiss call (happens during same navigation event as ShowGuidance)
            return;
        }
        ShowCompletionGuidance = false;
    }

    /// <summary>
    /// Shows completion guidance with the specified type.
    /// </summary>
    public void ShowGuidance(CompletionGuidanceType type)
    {
        CurrentGuidanceType = type;
        ShowCompletionGuidance = true;
        // Skip the next dismiss call to prevent navigation from immediately hiding the guidance
        _skipNextDismiss = true;

        // Clear the skip flag after the current event cycle so it only prevents
        // immediate dismissal (same navigation event), not user-initiated navigations
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _skipNextDismiss = false;
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private TutorialSettings Settings =>
        _globalSettingsService?.GetSettings()?.Tutorial ?? new TutorialSettings();

    /// <summary>
    /// Gets whether a tutorial is currently in progress on a specific company.
    /// </summary>
    public bool IsTutorialInProgressOnCompany =>
        !string.IsNullOrEmpty(Settings.TutorialStartedOnCompanyPath);

    /// <summary>
    /// Gets whether the welcome tutorial has been completed.
    /// </summary>
    public bool HasCompletedWelcomeTutorial => Settings.HasCompletedWelcomeTutorial;

    /// <summary>
    /// Gets whether the user skipped the tutorial in an older build, where skipping also
    /// switched off the setup checklist and first-visit hints. Nothing sets it any more.
    /// </summary>
    public bool HasSkippedTutorial => Settings.HasSkippedTutorial;

    /// <summary>
    /// Gets whether the app tour has been completed.
    /// </summary>
    public bool HasCompletedAppTour => Settings.HasCompletedAppTour;

    /// <summary>
    /// Gets whether the user has manually dismissed the setup checklist.
    /// </summary>
    public bool IsSetupChecklistDismissed => !Settings.ShowSetupChecklist;

    /// <summary>
    /// Gets whether first-visit hints should be shown.
    /// </summary>
    public bool ShowFirstVisitHints => !_hintsDisabledThisSession && Settings.ShowFirstVisitHints && !IsSampleCompanyOpen;

    // Tutorial progress is stored per install, not per company, so anything spent in the
    // sample (a checklist tick, a page hint) is already used up when the user opens their own.
    private static bool IsSampleCompanyOpen => App.CompanyManager?.IsSampleCompany == true;

    /// <summary>
    /// Sets the global settings service for tutorial persistence.
    /// </summary>
    public void SetGlobalSettingsService(IGlobalSettingsService? settingsService)
    {
        _globalSettingsService = settingsService;
    }

    /// <summary>
    /// Sets the current company path for tutorial tracking.
    /// </summary>
    public void SetCurrentCompanyPath(string? companyPath)
    {
        _currentCompanyPath = companyPath;
    }

    /// <summary>
    /// Checks if the tutorial should be shown on the current company.
    /// Returns false if the tutorial was skipped, if no company is set, if the sample
    /// company is open, or if we're on a different company than where the tutorial was started.
    /// </summary>
    public bool ShouldShowTutorialOnCurrentCompany()
    {
        if (string.IsNullOrEmpty(_currentCompanyPath) || IsSampleCompanyOpen)
            return false;

        if (Settings.HasSkippedTutorial)
            return false;

        var tutorialCompanyPath = Settings.TutorialStartedOnCompanyPath;
        if (string.IsNullOrEmpty(tutorialCompanyPath))
            return true;

        return string.Equals(_currentCompanyPath, tutorialCompanyPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records the current company as the company where the tutorial was started.
    /// Only records if the company has been saved (has a path).
    /// </summary>
    public void SetTutorialStartedOnCurrentCompany()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null && !string.IsNullOrEmpty(_currentCompanyPath))
        {
            settings.Tutorial.TutorialStartedOnCompanyPath = _currentCompanyPath;
            SaveSettings();
        }
    }

    /// <summary>
    /// Initializes the tutorial service for a new user if needed.
    /// Also records the current company as the tutorial company.
    /// </summary>
    public void InitializeForNewUser()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null && settings.Tutorial.FirstLaunchDate == null)
        {
            settings.Tutorial.FirstLaunchDate = DateTime.UtcNow;
            SaveSettings();
        }

        SetTutorialStartedOnCurrentCompany();
    }

    /// <summary>
    /// Marks the welcome tutorial as completed.
    /// </summary>
    public void CompleteWelcomeTutorial()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null)
        {
            settings.Tutorial.HasCompletedWelcomeTutorial = true;
            SaveSettings();
            TutorialStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Marks the app tour as completed.
    /// Note: We don't clear TutorialStartedOnCompanyPath here because the checklist
    /// should still show on this company until the user dismisses it.
    /// </summary>
    public void CompleteAppTour()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null)
        {
            settings.Tutorial.HasCompletedAppTour = true;
            SaveSettings();
            TutorialStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Skips the welcome overlay and the app tour. The setup checklist and first-visit hints
    /// stay on: the checklist is what leads a new user to their first transaction, and
    /// declining a tour is not a request to stop being shown where things are.
    /// </summary>
    public void SkipTour()
    {
        CompleteWelcomeTutorial();
        CompleteAppTour();
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.OnboardingSkipped);
    }

    /// <summary>
    /// Checks if a checklist item is completed.
    /// </summary>
    public bool IsChecklistItemCompleted(string itemId)
    {
        return Settings.CompletedChecklistItems.Contains(itemId);
    }

    /// <summary>
    /// Marks a checklist item as completed. Out-of-order calls are silently ignored.
    /// <para>
    /// The gated chain is RecordExpense -> VisitAnalytics.
    /// ScanReceipt is shown first but sits outside that chain: it has no prerequisites and
    /// is not a prerequisite for anything, so it can be completed at any point, including
    /// last. Items that aren't on the setup checklist are never gated.
    /// </para>
    /// </summary>
    public void CompleteChecklistItem(string itemId)
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial == null || settings.Tutorial.CompletedChecklistItems.Contains(itemId))
            return;

        // Don't process checklist items if the tutorial was skipped
        if (settings.Tutorial.HasSkippedTutorial || IsSampleCompanyOpen)
            return;

        // Check if previous items in sequence are completed
        if (!CanCompleteChecklistItem(itemId, settings.Tutorial.CompletedChecklistItems))
            return;

        settings.Tutorial.CompletedChecklistItems.Add(itemId);
        SaveSettings();
        ChecklistItemCompleted?.Invoke(this, itemId);

        // Anonymous onboarding telemetry: report which step was finished, so the
        // setup funnel shows where users stop rather than only who reached the
        // end. The Contains guard above means each step reports at most once.
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ChecklistStepCompleted, itemId);

        // Anonymous onboarding telemetry: VisitAnalytics closes the gated chain, and the
        // Contains guard above means it is added once, so this fires at most once.
        //
        // This is NOT the same as the checklist being finished.
        // AreAllChecklistItemsCompleted() also requires ScanReceipt, which is deliberately
        // not a prerequisite for VisitAnalytics, so someone who never scans fires this
        // while the checklist is still on screen. Read the metric as "worked through the
        // manual chain", not "finished setup": it runs ahead of the latter.
        if (itemId == ChecklistItems.VisitAnalytics)
        {
            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.OnboardingCompleted);
        }

        // Only while the checklist is on screen: the guidance points the user back to its next step,
        // and closing the checklist is how someone says they are done being guided.
        if (ShouldShowTutorialOnCurrentCompany() && !IsSetupChecklistDismissed)
        {
            // Show completion guidance for main tutorial tasks
            if (itemId == ChecklistItems.ScanReceipt ||
                itemId == ChecklistItems.RecordExpense)
            {
                ShowGuidance(CompletionGuidanceType.Standard);
            }
            else if (itemId == ChecklistItems.VisitAnalytics)
            {
                ShowGuidance(CompletionGuidanceType.Analytics);
            }
        }

        if (AreAllChecklistItemsCompleted())
        {
            AllChecklistItemsCompleted?.Invoke(this, EventArgs.Empty);
        }

        TutorialStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Checks if a checklist item can be completed based on the required order.
    /// </summary>
    private static bool CanCompleteChecklistItem(string itemId, List<string> completedItems)
    {
        // Define the required order: each item requires all previous items to be completed
        return itemId switch
        {
            // Scanning a receipt is the first step and the fastest path to a visible
            // result. It has no prerequisites, and it is deliberately NOT a
            // prerequisite for anything else: someone who came for invoicing must
            // still be able to work the manual path without scanning anything.
            ChecklistItems.ScanReceipt => true,
            ChecklistItems.RecordExpense => true,
            ChecklistItems.VisitAnalytics => completedItems.Contains(ChecklistItems.RecordExpense),
            _ => true // Other items (not in main checklist) can be completed anytime
        };
    }

    public IReadOnlyList<string> GetCompletedChecklistItems()
    {
        return Settings.CompletedChecklistItems.AsReadOnly();
    }

    /// <summary>
    /// Checks if all checklist items are completed.
    /// </summary>
    public bool AreAllChecklistItemsCompleted()
    {
        var completed = Settings.CompletedChecklistItems;
        return completed.Contains(ChecklistItems.ScanReceipt) &&
               completed.Contains(ChecklistItems.RecordExpense) &&
               completed.Contains(ChecklistItems.VisitAnalytics);
    }

    /// <summary>
    /// Gets the total count of checklist items.
    /// </summary>
    public int GetTotalChecklistCount()
    {
        // Core items that all users should complete
        return 3;
    }

    /// <summary>
    /// Checks if a page has been visited before.
    /// </summary>
    public bool HasVisitedPage(string pageId)
    {
        return Settings.VisitedPages.Contains(pageId);
    }

    /// <summary>
    /// Marks a page as visited.
    /// </summary>
    public void MarkPageVisited(string pageId)
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null && !settings.Tutorial.VisitedPages.Contains(pageId))
        {
            settings.Tutorial.VisitedPages.Add(pageId);
            SaveSettings();
            PageFirstVisited?.Invoke(this, pageId);
        }
    }

    /// <summary>
    /// Hides the setup checklist.
    /// </summary>
    public void HideSetupChecklist()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null)
        {
            settings.Tutorial.ShowSetupChecklist = false;
            SaveSettings();
            TutorialStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Shows the setup checklist.
    /// </summary>
    public void ShowSetupChecklist()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null)
        {
            settings.Tutorial.ShowSetupChecklist = true;
            SaveSettings();
            TutorialStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Disables first-visit hints.
    /// </summary>
    public void DisableFirstVisitHints()
    {
        _hintsDisabledThisSession = true;
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null)
        {
            settings.Tutorial.ShowFirstVisitHints = false;
            SaveSettings();
        }
        TutorialStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets all tutorial progress (for restart functionality).
    /// Clears the tutorial company so it will show on the next company opened.
    /// </summary>
    public void ResetAllTutorials()
    {
        _hintsDisabledThisSession = false;
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null)
        {
            settings.Tutorial.HasCompletedWelcomeTutorial = false;
            settings.Tutorial.HasCompletedAppTour = false;
            settings.Tutorial.HasSkippedTutorial = false;
            settings.Tutorial.ShowSetupChecklist = true;
            settings.Tutorial.CompletedChecklistItems.Clear();
            settings.Tutorial.VisitedPages.Clear();
            settings.Tutorial.ShowFirstVisitHints = true;
            settings.Tutorial.TutorialStartedOnCompanyPath = null;
            SaveSettings();
            TutorialStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the first-visit hint text for a page.
    /// </summary>
    public static string? GetFirstVisitHint(string pageId)
    {
        return pageId switch
        {
            Pages.Dashboard => "This is your business overview. Key metrics and recent activity appear here.",
            Pages.Analytics => "Explore your business data with interactive charts. Try the different tabs to see various insights.",
            Pages.Insights => "AI-powered recommendations to help grow your business appear here.",
            Pages.Reports => "Create and customize financial reports. Use templates or build your own.",
            Pages.Expenses => "Record business expenses here. Use categories to organize them for tax time.",
            Pages.Revenue => "Track your income and sales. Each entry can have line items for detailed records.",
            Pages.Invoices => "Create professional invoices for your customers. Track payments and send reminders.",
            Pages.Products => "Manage your product catalog with pricing, categories, and inventory tracking.",
            Pages.Customers => "Keep track of customer information, purchase history, and contact details.",
            Pages.Suppliers => "Manage your vendors and suppliers for easy reordering.",
            Pages.Categories => "Organize your transactions and products with custom categories.",
            Pages.StockLevels => "Monitor inventory levels and get alerts when stock runs low.",
            Pages.Receipts => "Upload receipt images and our AI will extract the details automatically.",
            Pages.RentalInventory => "Track equipment available for rental with availability status.",
            Pages.RentalRecords => "Record rental transactions and manage rental periods.",
            _ => null
        };
    }

    /// <summary>
    /// Whether the source survey should be presented to this user. True only when
    /// FirstRunReporter has run and recorded a no-token install, and the user
    /// hasn't already answered or dismissed.
    /// </summary>
    public bool ShouldShowSourceSurvey()
    {
        if (Settings.SourceSurveyAnswer != null) return false;
        if (Settings.IsSourceSurveyDismissed) return false;

        var reason = InstallAttributionReason.ReadFirstRunMarkerReason();
        return reason == "no_token";
    }

    /// <summary>
    /// Requests the survey overlay to open if <see cref="ShouldShowSourceSurvey"/> is true.
    /// Safe to call from any path that wants to surface the survey (checklist-completion
    /// handler, dashboard banner button, etc).
    /// </summary>
    public void RequestShowSourceSurvey()
    {
        if (ShouldShowSourceSurvey())
        {
            SourceSurveyVisibilityChanged?.Invoke(this, true);
        }
    }

    /// <summary>
    /// Persists the user's survey answer and closes the overlay. Caller is responsible
    /// for POSTing the answer to the server before invoking this.
    /// </summary>
    public void MarkSourceSurveyAnswered(string answer)
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null)
        {
            settings.Tutorial.SourceSurveyAnswer = answer;
            settings.Tutorial.HasShownSourceSurvey = true;
            SaveSettings();
        }
        SourceSurveyVisibilityChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Closes the survey overlay without recording an answer or a dismissal. Used when a
    /// submit failure leaves the user stuck: they can cancel out of the overlay, but the
    /// dashboard banner keeps prompting (ShouldShowSourceSurvey stays true) until they
    /// successfully answer or explicitly dismiss it.
    /// </summary>
    public void CloseSourceSurveyOverlay()
    {
        SourceSurveyVisibilityChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Persists that the user dismissed the survey without answering. Closes the overlay
    /// and prevents the dashboard banner from showing on future loads.
    /// </summary>
    public void MarkSourceSurveyDismissed()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Tutorial != null)
        {
            settings.Tutorial.IsSourceSurveyDismissed = true;
            settings.Tutorial.HasShownSourceSurvey = true;
            SaveSettings();
        }
        SourceSurveyVisibilityChanged?.Invoke(this, false);
    }

    private void SaveSettings()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings != null)
        {
            _globalSettingsService?.SaveSettings(settings);
        }
    }
}

/// <summary>
/// Types of completion guidance messages.
/// </summary>
public enum CompletionGuidanceType
{
    /// <summary>
    /// Standard message to go back to dashboard for next step.
    /// </summary>
    Standard,

    /// <summary>
    /// Analytics-specific message about charts updating with data.
    /// </summary>
    Analytics
}
