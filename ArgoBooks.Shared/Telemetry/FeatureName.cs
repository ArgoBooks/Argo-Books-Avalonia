namespace ArgoBooks.Shared.Telemetry;

/// <summary>
/// Features that can be tracked.
/// </summary>
public enum FeatureName
{
    // Reports
    ReportGenerated,

    // Receipts
    ReceiptScanned,

    // Data Management
    DataImported,
    BackupCreated,
    BackupRestored,

    // Transactions
    InvoiceCreated,
    ExpenseCreated,
    RevenueCreated,
    PaymentRecorded,
    BankMatchConfirmed,

    // Inventory
    ProductCreated,
    CategoryCreated,
    LocationCreated,
    StockAdjusted,
    PurchaseOrderCreated,
    ReturnRecorded,
    LostDamagedRecorded,

    // Contacts
    CustomerCreated,
    SupplierCreated,

    // Rentals
    RentalItemCreated,
    RentalRecordCreated,

    // Charts
    ChartExportedToGoogleSheets,
    ChartExportedToExcel,

    // Settings
    ThemeChanged,
    LanguageChanged,

    // Onboarding
    CompanyCreated,
    ChecklistStepCompleted,
    OnboardingCompleted,
    OnboardingSkipped,
    SampleCompanyOpened,

    // Attempts, so a completion can be divided by one. Without the denominator, abandonment is invisible.
    CompanyCreateOpened,
    ReceiptScanOpened,
    InvoiceCreateOpened,
    ExpenseCreateOpened,
    RevenueCreateOpened,
    ReportOpened,

    // The import funnel. The stage or reason travels in the context string, so a new failure
    // mode needs neither a new enum value nor a server change.
    ImportOpened,
    ImportPreviewShown,
    ImportAbandoned,
    ImportFailed,
    ReceiptScanFailed,

    // The paywall. Shown is the wall someone hit, opened is them acting on it; the limit or
    // entry point is in the context on both, so the pair divides into a conversion rate.
    UpgradePromptShown,
    UpgradeModalOpened,

    // A page rendered with nothing in it, with the page in the context.
    EmptyStateShown,

    WelcomeShown,

    // Payroll
    PayRunDrafted,
    PayRunApproved,
    PayStubsExported,
    T4SlipsGenerated,
    T4XmlGenerated,
    RoeWorksheetGenerated,
    RoeXmlGenerated,

    // The dashboard editor
    DashboardCustomized,
    DashboardReset
}
