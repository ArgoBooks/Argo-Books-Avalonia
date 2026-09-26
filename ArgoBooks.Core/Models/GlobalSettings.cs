using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Core.Services;

namespace ArgoBooks.Core.Models;

/// <summary>
/// Global application settings stored in the AppData directory.
/// </summary>
public class GlobalSettings
{
    public List<string> RecentCompanies { get; set; } = [];
    public UpdateSettings Updates { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public LicenseSettings License { get; set; } = new();
    public WindowStateSettings? WindowState { get; set; }
    public ReportExportSettings ReportExport { get; set; } = new();
    public TutorialSettings Tutorial { get; set; } = new();
    public UpdateEmailSettings UpdateEmail { get; set; } = new();

    /// <summary>
    /// Set once the per-user file type registrations written by 2.0.13 and earlier have been
    /// removed, so the one-time cleanup does not run again.
    /// </summary>
    public bool LegacyFileAssociationsCleared { get; set; }
}

public class UpdateSettings
{
    public bool AutoOpenRecentAfterUpdate { get; set; } = true;

    /// <summary>
    /// A company file that could not open because it was saved by a newer version, when the
    /// user chose to update from that prompt. Opened in place of the most recent company after
    /// the restart.
    /// </summary>
    public string? FileToOpenAfterUpdate { get; set; }
}

public class UiSettings
{
    public bool SidebarCollapsed { get; set; } = false;
    public bool ReportsElementPanelCollapsed { get; set; } = false;
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "Blue";
    public string Language { get; set; } = "English";

    /// <summary>
    /// App version that last refreshed cached translation files. When this differs from
    /// the running version, cached translations are re-downloaded so users on the new
    /// version get the latest translations.
    /// </summary>
    public string? LastLanguageVersion { get; set; }
    /// <summary>
    /// User's preferred timezone for displaying times. Defaults to UTC.
    /// Uses system timezone identifiers.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";
    /// <summary>
    /// User's preferred time format. "12h" for 12-hour (AM/PM), "24h" for 24-hour.
    /// </summary>
    public string TimeFormat { get; set; } = "12h";
    public ChartSettings Chart { get; set; } = new();

    /// <summary>
    /// Per-company chart settings, keyed by company file path.
    /// </summary>
    public Dictionary<string, CompanyChartPreferences> CompanyChartSettings { get; set; } = new();

    public QuickActionsSettings QuickActions { get; set; } = new();
    public EmojiPickerSettings EmojiPicker { get; set; } = new();

    /// <summary>
    /// Persisted sidebar section expanded states, keyed by section key (e.g., "Main", "Transactions").
    /// Value is true when expanded, false when collapsed.
    /// </summary>
    public Dictionary<string, bool> SidebarSectionExpanded { get; set; } = new();

    /// <summary>
    /// Persisted column visibility settings per page.
    /// Key is the page name (e.g., "Expenses"), value is a dictionary of column name to visibility.
    /// </summary>
    public Dictionary<string, Dictionary<string, bool>> ColumnVisibility { get; set; } = new();

    /// <summary>
    /// Whether the grid is shown on the report designer canvas.
    /// </summary>
    public bool ReportsShowGrid { get; set; } = true;

    /// <summary>
    /// Per-company dashboard layouts, keyed by company file path.
    /// </summary>
    public Dictionary<string, DashboardLayout> CompanyDashboardLayouts { get; set; } = new();
}

public class EmojiPickerSettings
{
    /// <summary>
    /// Recently used emojis (most recent first).
    /// </summary>
    public List<string> RecentEmojis { get; set; } = [];

    /// <summary>
    /// User's favorite emojis.
    /// </summary>
    public List<string> FavoriteEmojis { get; set; } = [];

    /// <summary>
    /// Maximum number of recent emojis to store.
    /// </summary>
    public int MaxRecentEmojis { get; set; } = 24;
}

public class QuickActionsSettings
{
    // Primary actions (shown by default)
    public bool ShowNewInvoice { get; set; } = true;
    public bool ShowNewExpense { get; set; } = true;
    public bool ShowNewRevenue { get; set; } = true;
    public bool ShowScanReceipt { get; set; } = true;
    public bool ShowImportBankStatement { get; set; } = false;

    // Contact actions
    public bool ShowNewCustomer { get; set; } = false;
    public bool ShowNewSupplier { get; set; } = false;

    // Product & Inventory actions
    public bool ShowNewProduct { get; set; } = false;

    // Rental actions
    public bool ShowNewRentalItem { get; set; } = false;
    public bool ShowNewRentalRecord { get; set; } = true;

    // Organization actions
    public bool ShowNewCategory { get; set; } = false;
    public bool ShowNewLocation { get; set; } = false;

    // Order & Stock actions
    public bool ShowNewPurchaseOrder { get; set; } = false;
    public bool ShowNewStockAdjustment { get; set; } = false;
}

public class ChartSettings
{
    /// <summary>
    /// Maximum number of slices to show in pie charts before grouping into "Other".
    /// </summary>
    public int MaxPieSlices { get; set; } = 6;
}

/// <summary>
/// The chart type and date range last chosen for one company.
/// </summary>
public class CompanyChartPreferences
{
    public string ChartType { get; set; } = "Line";
    public string DateRange { get; set; } = "This Month";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }
}

public class LicenseSettings
{
    /// <summary>
    /// Obfuscated license data (encrypted with machine-specific key).
    /// </summary>
    public string? LicenseData { get; set; }

    /// <summary>
    /// Salt used for obfuscation.
    /// </summary>
    public string? Salt { get; set; }

    /// <summary>
    /// IV used for obfuscation.
    /// </summary>
    public string? Iv { get; set; }
}

/// <summary>
/// Whether the optional "email me about updates" offer has been answered. The address itself is
/// deliberately not kept here: it lives only on the list the person can unsubscribe from, so
/// nothing in the company file or the settings ties a machine to an identity.
/// </summary>
public class UpdateEmailSettings
{
    /// <summary>Set when the offer was declined, so the dashboard stops asking.</summary>
    public bool Dismissed { get; set; }

    /// <summary>Set once a confirmation email has been requested, for the same reason.</summary>
    public bool Submitted { get; set; }
}

public class ReportExportSettings
{
    public string? LastExportDirectory { get; set; }
    public bool OpenAfterExport { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;
}

/// <summary>
/// Settings for the first-time user tutorial system.
/// </summary>
public class TutorialSettings
{
    /// <summary>
    /// Whether the user has completed or dismissed the initial welcome tutorial.
    /// </summary>
    public bool HasCompletedWelcomeTutorial { get; set; } = false;

    /// <summary>
    /// Whether the user has completed the interactive app tour.
    /// </summary>
    public bool HasCompletedAppTour { get; set; } = false;

    /// <summary>
    /// The file path of the company where the tutorial was started.
    /// Tutorial will only show on this company until completed or dismissed.
    /// </summary>
    public string? TutorialStartedOnCompanyPath { get; set; }

    /// <summary>
    /// Whether the user skipped the tutorial entirely.
    /// </summary>
    public bool HasSkippedTutorial { get; set; } = false;

    /// <summary>
    /// Whether to show the setup checklist on the dashboard.
    /// </summary>
    public bool ShowSetupChecklist { get; set; } = true;

    /// <summary>
    /// Completed setup checklist items by their identifier.
    /// </summary>
    public List<string> CompletedChecklistItems { get; set; } = [];

    /// <summary>
    /// Pages that have been visited (for first-visit hints).
    /// </summary>
    public List<string> VisitedPages { get; set; } = [];

    /// <summary>
    /// Whether to show first-visit hints on pages.
    /// </summary>
    public bool ShowFirstVisitHints { get; set; } = true;

    /// <summary>
    /// When the user first started using the app.
    /// </summary>
    public DateTime? FirstLaunchDate { get; set; }

    /// <summary>
    /// Whether the post-onboarding source survey has been shown to the user.
    /// </summary>
    public bool HasShownSourceSurvey { get; set; } = false;

    /// <summary>
    /// The user's answer to "Where did you hear about Argo Books?". One of:
    /// google, bing, youtube, reddit, friend, email, other. Null if not answered.
    /// </summary>
    public string? SourceSurveyAnswer { get; set; }

    /// <summary>
    /// Whether the user explicitly dismissed the source survey without answering.
    /// </summary>
    public bool IsSourceSurveyDismissed { get; set; } = false;
}
