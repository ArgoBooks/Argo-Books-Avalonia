using ArgoBooks.Core.Models.Integrations;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Models.Portal;

namespace ArgoBooks.Core.Models;

/// <summary>
/// Company-specific settings stored inside the .argo file.
/// </summary>
public class CompanySettings
{
    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.0";

    private bool _changesMade;

    /// <summary>
    /// Runtime-only flag to track unsaved changes. Not persisted to JSON.
    /// </summary>
    [JsonIgnore]
    public bool ChangesMade
    {
        get => _changesMade;
        set
        {
            _changesMade = value;
            if (value)
                ChangeCount++;
        }
    }

    /// <summary>
    /// Counts the times <see cref="ChangesMade"/> was set, so a save can tell whether an edit
    /// arrived while it was writing. Runtime only.
    /// </summary>
    [JsonIgnore]
    public long ChangeCount { get; private set; }

    /// <summary>
    /// Tracks the newest month of data when backtesting was last run.
    /// Format: "2025-12" (year-month). Used to avoid redundant backtests.
    /// </summary>
    [JsonPropertyName("lastBacktestedMonth")]
    public string? LastBacktestedMonth { get; set; }

    /// <summary>
    /// Tracks the backtest algorithm version. When the algorithm changes,
    /// this triggers a full re-backtest with the updated logic.
    /// </summary>
    [JsonPropertyName("backtestVersion")]
    public string? BacktestVersion { get; set; }

    /// <summary>
    /// Version marker for the one-time invoice-totals healing pass that runs on
    /// open. When it matches the app's current heal version, the pass is skipped.
    /// </summary>
    [JsonPropertyName("invoiceTotalsHealedVersion")]
    public string? InvoiceTotalsHealedVersion { get; set; }

    /// <summary>
    /// Marks that revenue-linked payments have been folded into their Revenue rows.
    /// See <c>CompanyManager.MigrateRevenueLinkedPayments</c>.
    /// </summary>
    [JsonPropertyName("revenuePaymentsMigratedVersion")]
    public string? RevenuePaymentsMigratedVersion { get; set; }

    [JsonPropertyName("company")]
    public CompanyInfo Company { get; set; } = new();
    [JsonPropertyName("localization")]
    public LocalizationSettings Localization { get; set; } = new();
    [JsonPropertyName("notifications")]
    public NotificationSettings Notifications { get; set; } = new();
    [JsonPropertyName("security")]
    public SecuritySettings Security { get; set; } = new();
    [JsonPropertyName("invoiceEmail")]
    public InvoiceEmailSettings InvoiceEmail { get; set; } = new();
    [JsonPropertyName("purchaseOrderEmail")]
    public PurchaseOrderEmailSettings PurchaseOrderEmail { get; set; } = new();
    [JsonPropertyName("paymentPortal")]
    public PortalSettings PaymentPortal { get; set; } = new();
    [JsonPropertyName("integrations")]
    public IntegrationsSettings Integrations { get; set; } = new();
    [JsonPropertyName("mobileSync")]
    public MobileSyncSettings MobileSync { get; set; } = new();

    /// <summary>
    /// Bank categorization rules for automated bank statement matching. Per company; edited in
    /// the Settings modal and learned automatically when categorizing rows during bank import.
    /// Stored here (a company setting) so edits save/cancel with the rest of the settings.
    /// </summary>
    [JsonPropertyName("bankCategoryRules")]
    public List<BankMatching.BankCategoryRule> BankCategoryRules { get; set; } = [];
}

public class CompanyInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("businessType")]
    public string? BusinessType { get; set; }
    [JsonPropertyName("industry")]
    public string? Industry { get; set; }
    [JsonPropertyName("email")]
    public string? Email { get; set; }
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
    [JsonPropertyName("address")]
    public string? Address { get; set; }
    [JsonPropertyName("city")]
    public string? City { get; set; }
    [JsonPropertyName("provinceState")]
    public string? ProvinceState { get; set; }
    [JsonPropertyName("country")]
    public string? Country { get; set; }
    [JsonPropertyName("logoFileName")]
    public string? LogoFileName { get; set; }

    /// <summary>
    /// Postal code. Only payroll needs it so far, because a T4 carries the employer's full
    /// address, but it is ordinary company information rather than a payroll field.
    /// </summary>
    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    /// <summary>
    /// CRA payroll program account, the fifteen character BN15 in the form 000000000RP0000.
    /// Required on both the T4 slip (box 54) and the summary, and CRA validates that the two
    /// match, so it is stored once here rather than per employee.
    /// </summary>
    [JsonPropertyName("payrollAccountNumber")]
    public string? PayrollAccountNumber { get; set; }

    /// <summary>
    /// The person CRA should call about a T4 filing, required on the summary. Kept separate
    /// from the company's own name and phone because it is a named individual, and because a
    /// bookkeeper filing on the owner's behalf puts themselves here.
    /// </summary>
    [JsonPropertyName("payrollContactName")]
    public string? PayrollContactName { get; set; }

    [JsonPropertyName("payrollContactPhone")]
    public string? PayrollContactPhone { get; set; }

    /// <summary>
    /// Where CRA writes back about an electronic filing. Required by the T619 transmittal record
    /// that wraps every submission, and the only field of it this app did not already hold.
    ///
    /// CRA states it is used "to facilitate communication regarding the processing of information
    /// returns received", so it is where a rejection or a query arrives. Getting it wrong is
    /// quiet: the submission goes through and the answer goes nowhere.
    /// </summary>
    [JsonPropertyName("payrollContactEmail")]
    public string? PayrollContactEmail { get; set; }

    /// <summary>
    /// The Revenu Quebec identification number, ten digits then a two letter file code then
    /// four digits, as in 1234567890RS0001. Needed only by an employer with Quebec staff, and
    /// it is NOT the CRA payroll account number: a Quebec employer holds both, files a T4 with
    /// one and an RL-1 with the other, and the two look nothing alike.
    /// </summary>
    [JsonPropertyName("quebecIdentificationNumber")]
    public string? QuebecIdentificationNumber { get; set; }

    /// <summary>
    /// How often CRA expects the deductions, which it assigns and tells the employer rather than
    /// anything derivable from the pay runs here.
    ///
    /// Defaults to regular. That is where most small employers sit, and it is also the value
    /// every existing company file will deserialize to, which matches what the app assumed
    /// before this setting existed.
    /// </summary>
    [JsonPropertyName("remitterType")]
    public Payroll.RemitterType RemitterType { get; set; } = Payroll.RemitterType.Regular;
}

public class LocalizationSettings
{
    /// <summary>
    /// The display language name (e.g., "English", "French", "German").
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "English";
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";
    [JsonPropertyName("dateFormat")]
    public string DateFormat { get; set; } = "MM/DD/YYYY";
}

public class NotificationSettings
{
    [JsonPropertyName("lowStockAlert")]
    public bool LowStockAlert { get; set; } = true;
    [JsonPropertyName("outOfStockAlert")]
    public bool OutOfStockAlert { get; set; } = true;
    [JsonPropertyName("invoiceOverdueAlert")]
    public bool InvoiceOverdueAlert { get; set; } = true;
    [JsonPropertyName("rentalOverdueAlert")]
    public bool RentalOverdueAlert { get; set; } = true;
    [JsonPropertyName("unsavedChangesReminder")]
    public bool UnsavedChangesReminder { get; set; } = true;
    [JsonPropertyName("unsavedChangesReminderMinutes")]
    public int UnsavedChangesReminderMinutes { get; set; } = 5;

    /// <summary>
    /// The date when startup notifications were last checked/sent.
    /// Used to avoid sending duplicate notifications on each app open.
    /// </summary>
    [JsonPropertyName("lastAlertCheckDate")]
    public DateTime? LastAlertCheckDate { get; set; }
}

public class SecuritySettings
{
    [JsonPropertyName("autoLockEnabled")]
    public bool AutoLockEnabled { get; set; } = false;
    [JsonPropertyName("autoLockMinutes")]
    public int AutoLockMinutes { get; set; } = 5;
    [JsonPropertyName("biometricEnabled")]
    public bool BiometricEnabled { get; set; } = false;
}
