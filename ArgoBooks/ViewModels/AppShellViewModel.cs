using System.Reflection;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels.Dashboard;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the application shell, managing sidebar and header.
/// </summary>
public partial class AppShellViewModel : ViewModelBase
{
    private readonly INavigationService? _navigationService;

    // Lazy modal VM backing fields
    private CustomerModalsViewModel? _customerModalsViewModel;
    private ProductModalsViewModel? _productModalsViewModel;
    private CategoryModalsViewModel? _categoryModalsViewModel;
    private SupplierModalsViewModel? _supplierModalsViewModel;
    private RentalInventoryModalsViewModel? _rentalInventoryModalsViewModel;
    private RentalAvailabilityModalViewModel? _rentalAvailabilityModalViewModel;
    private RentalRecordsModalsViewModel? _rentalRecordsModalsViewModel;
    private PaymentModalsViewModel? _paymentModalsViewModel;
    private InvoiceModalsViewModel? _invoiceModalsViewModel;
    private RefundModalsViewModel? _refundModalsViewModel;
    private InvoiceTemplateDesignerViewModel? _invoiceTemplateDesignerViewModel;
    private ExpenseModalsViewModel? _expenseModalsViewModel;
    private RevenueModalsViewModel? _revenueModalsViewModel;
    private StockLevelsModalsViewModel? _stockLevelsModalsViewModel;
    private LocationsModalsViewModel? _locationsModalsViewModel;
    private PayrollModalsViewModel? _payrollModalsViewModel;
    private PayRunModalsViewModel? _payRunModalsViewModel;
    private YearEndModalViewModel? _yearEndModalViewModel;
    private StockAdjustmentsModalsViewModel? _stockAdjustmentsModalsViewModel;
    private BankMatchingModalsViewModel? _bankMatchingModalsViewModel;
    private BankStatementImportModalViewModel? _bankStatementImportModalViewModel;
    private PurchaseOrdersModalsViewModel? _purchaseOrdersModalsViewModel;
    private ReceiptsModalsViewModel? _receiptsModalsViewModel;
    private LostDamagedModalsViewModel? _lostDamagedModalsViewModel;
    private ReturnsModalsViewModel? _returnsModalsViewModel;
    private ReportModalsViewModel? _reportModalsViewModel;

    /// <summary>
    /// Raised when any modal ViewModel saves or deletes data (for unsaved-changes tracking).
    /// </summary>
    internal event EventHandler? UnsavedChangesMade;

    private void RaiseUnsavedChanges(object? sender, EventArgs e) =>
        UnsavedChangesMade?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raised when the user requests to open a file for scanning via quick action.
    /// The view should handle this by opening a file picker.
    /// </summary>
    public event EventHandler? OpenFileScanRequested;

    /// <summary>
    /// Raised when the user requests to edit the company via quick action.
    /// </summary>
    public event EventHandler? EditCompanyRequested;

    /// <summary>
    /// Raised when the user requests to restart the tutorial from the Help panel.
    /// </summary>
    public event EventHandler? RestartTutorialRequested;

    #region ViewModels

    public SidebarViewModel SidebarViewModel { get; }

    public HeaderViewModel HeaderViewModel { get; }

    public QuickActionsViewModel QuickActionsViewModel { get; }

    public NotificationPanelViewModel NotificationPanelViewModel { get; }

    public FileMenuPanelViewModel FileMenuPanelViewModel { get; }

    public HelpPanelViewModel HelpPanelViewModel { get; }

    public UpgradeModalViewModel UpgradeModalViewModel { get; }

    /// <summary>
    /// Gets the create company wizard view model.
    /// </summary>
    public CreateCompanyViewModel CreateCompanyViewModel { get; }

    public CompanySwitcherPanelViewModel CompanySwitcherPanelViewModel { get; }

    public SettingsModalViewModel SettingsModalViewModel { get; }

    public CheckForUpdateModalViewModel CheckForUpdateModalViewModel { get; }

    public PredictionInfoModalViewModel PredictionInfoModalViewModel { get; }

    public PastPredictionsModalViewModel PastPredictionsModalViewModel { get; }

    public ImportModalViewModel ImportModalViewModel { get; }

    public ImportValidationDialogViewModel ImportValidationDialogViewModel { get; }

    public ImportResultDialogViewModel ImportResultDialogViewModel { get; }

    public ImportMappingDialogViewModel ImportMappingDialogViewModel { get; }

    /// <summary>
    /// Gets the currency-ambiguity resolution dialog view model (shown during import when an
    /// ambiguous currency symbol like "$" is found in the amount cells).
    /// </summary>
    public CurrencyAmbiguityDialogViewModel CurrencyAmbiguityDialogViewModel { get; }

    /// <summary>
    /// Gets the rate-unavailable dialog view model (shown during import when the exact-date
    /// exchange rates cannot be fetched, offering connect-and-retry).
    /// </summary>
    public RateUnavailableDialogViewModel RateUnavailableDialogViewModel { get; }

    public ExportAsModalViewModel ExportAsModalViewModel { get; }

    public SwitchAccountModalViewModel SwitchAccountModalViewModel { get; }

    public PasswordPromptModalViewModel PasswordPromptModalViewModel { get; }

    public EditCompanyModalViewModel EditCompanyModalViewModel { get; }

    public CustomerModalsViewModel CustomerModalsViewModel
    {
        get
        {
            if (_customerModalsViewModel == null)
            {
                _customerModalsViewModel = new CustomerModalsViewModel();
                _customerModalsViewModel.CustomerSaved += RaiseUnsavedChanges;
                _customerModalsViewModel.CustomerDeleted += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _customerModalsViewModel;
        }
    }

    public ProductModalsViewModel ProductModalsViewModel
    {
        get
        {
            if (_productModalsViewModel == null)
            {
                _productModalsViewModel = new ProductModalsViewModel();
                _productModalsViewModel.ProductSaved += RaiseUnsavedChanges;
                _productModalsViewModel.ProductDeleted += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _productModalsViewModel;
        }
    }

    private RecurringScheduleEditorViewModel? _recurringScheduleEditorViewModel;

    public RecurringScheduleEditorViewModel RecurringScheduleEditorViewModel
    {
        get
        {
            if (_recurringScheduleEditorViewModel == null)
            {
                _recurringScheduleEditorViewModel = new RecurringScheduleEditorViewModel();
                _recurringScheduleEditorViewModel.Saved += () => RaiseUnsavedChanges(this, EventArgs.Empty);
                OnPropertyChanged();
            }

            return _recurringScheduleEditorViewModel;
        }
    }

    public CategoryModalsViewModel CategoryModalsViewModel
    {
        get
        {
            if (_categoryModalsViewModel == null)
            {
                _categoryModalsViewModel = new CategoryModalsViewModel();
                _categoryModalsViewModel.CategorySaved += RaiseUnsavedChanges;
                _categoryModalsViewModel.CategoryDeleted += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _categoryModalsViewModel;
        }
    }

    public SupplierModalsViewModel SupplierModalsViewModel
    {
        get
        {
            if (_supplierModalsViewModel == null)
            {
                _supplierModalsViewModel = new SupplierModalsViewModel();
                _supplierModalsViewModel.SupplierSaved += RaiseUnsavedChanges;
                _supplierModalsViewModel.SupplierDeleted += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _supplierModalsViewModel;
        }
    }

    public RentalInventoryModalsViewModel RentalInventoryModalsViewModel
    {
        get
        {
            if (_rentalInventoryModalsViewModel == null)
            {
                _rentalInventoryModalsViewModel = new RentalInventoryModalsViewModel();
                _rentalInventoryModalsViewModel.ItemSaved += RaiseUnsavedChanges;
                _rentalInventoryModalsViewModel.ItemDeleted += RaiseUnsavedChanges;
                _rentalInventoryModalsViewModel.RentalCreated += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _rentalInventoryModalsViewModel;
        }
    }

    /// <summary>
    /// Gets the rental availability modal view model (per-item calendar).
    /// </summary>
    public RentalAvailabilityModalViewModel RentalAvailabilityModalViewModel
    {
        get
        {
            if (_rentalAvailabilityModalViewModel == null)
            {
                _rentalAvailabilityModalViewModel = new RentalAvailabilityModalViewModel();
                OnPropertyChanged();
            }
            return _rentalAvailabilityModalViewModel;
        }
    }

    public RentalRecordsModalsViewModel RentalRecordsModalsViewModel
    {
        get
        {
            if (_rentalRecordsModalsViewModel == null)
            {
                _rentalRecordsModalsViewModel = new RentalRecordsModalsViewModel();
                _rentalRecordsModalsViewModel.RecordSaved += RaiseUnsavedChanges;
                _rentalRecordsModalsViewModel.RecordDeleted += RaiseUnsavedChanges;
                _rentalRecordsModalsViewModel.RecordReturned += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _rentalRecordsModalsViewModel;
        }
    }

    public PaymentModalsViewModel PaymentModalsViewModel
    {
        get
        {
            if (_paymentModalsViewModel == null)
            {
                _paymentModalsViewModel = new PaymentModalsViewModel();
                _paymentModalsViewModel.PaymentSaved += RaiseUnsavedChanges;
                _paymentModalsViewModel.PaymentDeleted += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _paymentModalsViewModel;
        }
    }

    /// <summary>
    /// Coordinates the three refund-feature modals (refund, email-verify, email-change).
    /// Bound to the &lt;modals:RefundModals&gt; UserControl in AppShell.axaml.
    /// </summary>
    public RefundModalsViewModel RefundModalsViewModel
    {
        get
        {
            if (_refundModalsViewModel == null)
            {
                _refundModalsViewModel = new RefundModalsViewModel();
                OnPropertyChanged();
            }
            return _refundModalsViewModel;
        }
    }

    public InvoiceModalsViewModel InvoiceModalsViewModel
    {
        get
        {
            if (_invoiceModalsViewModel == null)
            {
                _invoiceModalsViewModel = new InvoiceModalsViewModel();
                _invoiceModalsViewModel.InvoiceDeleted += RaiseUnsavedChanges;
                _invoiceModalsViewModel.HasPremium = SidebarViewModel.HasPremium;
                OnPropertyChanged();
            }
            return _invoiceModalsViewModel;
        }
    }

    /// <summary>
    /// Gets the invoice template designer modal view model.
    /// </summary>
    public InvoiceTemplateDesignerViewModel InvoiceTemplateDesignerViewModel
    {
        get
        {
            if (_invoiceTemplateDesignerViewModel == null)
            {
                _invoiceTemplateDesignerViewModel = new InvoiceTemplateDesignerViewModel();
                _invoiceTemplateDesignerViewModel.TemplateSaved += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _invoiceTemplateDesignerViewModel;
        }
    }

    public ExpenseModalsViewModel ExpenseModalsViewModel
    {
        get
        {
            if (_expenseModalsViewModel == null)
            {
                _expenseModalsViewModel = new ExpenseModalsViewModel();
                _expenseModalsViewModel.ExpenseSaved += RaiseUnsavedChanges;
                _expenseModalsViewModel.ExpenseDeleted += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _expenseModalsViewModel;
        }
    }

    public RevenueModalsViewModel RevenueModalsViewModel
    {
        get
        {
            if (_revenueModalsViewModel == null)
            {
                _revenueModalsViewModel = new RevenueModalsViewModel();
                _revenueModalsViewModel.RevenueSaved += RaiseUnsavedChanges;
                _revenueModalsViewModel.RevenueDeleted += RaiseUnsavedChanges;
                OnPropertyChanged();
            }
            return _revenueModalsViewModel;
        }
    }

    public StockLevelsModalsViewModel StockLevelsModalsViewModel
    {
        get
        {
            if (_stockLevelsModalsViewModel == null)
            {
                _stockLevelsModalsViewModel = new StockLevelsModalsViewModel();
                OnPropertyChanged();
            }
            return _stockLevelsModalsViewModel;
        }
    }

    public LocationsModalsViewModel LocationsModalsViewModel
    {
        get
        {
            if (_locationsModalsViewModel == null)
            {
                _locationsModalsViewModel = new LocationsModalsViewModel();
                OnPropertyChanged();
            }
            return _locationsModalsViewModel;
        }
    }

    public PayrollModalsViewModel PayrollModalsViewModel
    {
        get
        {
            if (_payrollModalsViewModel == null)
            {
                _payrollModalsViewModel = new PayrollModalsViewModel();
                OnPropertyChanged();
            }
            return _payrollModalsViewModel;
        }
    }

    /// <summary>
    /// Gets the pay run modal view model.
    /// </summary>
    public PayRunModalsViewModel PayRunModalsViewModel
    {
        get
        {
            if (_payRunModalsViewModel == null)
            {
                _payRunModalsViewModel = new PayRunModalsViewModel();
                OnPropertyChanged();
            }
            return _payRunModalsViewModel;
        }
    }

    public YearEndModalViewModel YearEndModalViewModel
    {
        get
        {
            if (_yearEndModalViewModel == null)
            {
                _yearEndModalViewModel = new YearEndModalViewModel();
                OnPropertyChanged();
            }
            return _yearEndModalViewModel;
        }
    }

    public StockAdjustmentsModalsViewModel StockAdjustmentsModalsViewModel
    {
        get
        {
            if (_stockAdjustmentsModalsViewModel == null)
            {
                _stockAdjustmentsModalsViewModel = new StockAdjustmentsModalsViewModel();
                OnPropertyChanged();
            }
            return _stockAdjustmentsModalsViewModel;
        }
    }

    public BankMatchingModalsViewModel BankMatchingModalsViewModel
    {
        get
        {
            if (_bankMatchingModalsViewModel == null)
            {
                _bankMatchingModalsViewModel = new BankMatchingModalsViewModel();
                OnPropertyChanged();
            }
            return _bankMatchingModalsViewModel;
        }
    }

    public BankStatementImportModalViewModel BankStatementImportModalViewModel
    {
        get
        {
            if (_bankStatementImportModalViewModel == null)
            {
                _bankStatementImportModalViewModel = new BankStatementImportModalViewModel();
                OnPropertyChanged();
            }
            return _bankStatementImportModalViewModel;
        }
    }

    public PurchaseOrdersModalsViewModel PurchaseOrdersModalsViewModel
    {
        get
        {
            if (_purchaseOrdersModalsViewModel == null)
            {
                _purchaseOrdersModalsViewModel = new PurchaseOrdersModalsViewModel();
                OnPropertyChanged();
            }
            return _purchaseOrdersModalsViewModel;
        }
    }

    public ReceiptsModalsViewModel ReceiptsModalsViewModel
    {
        get
        {
            if (_receiptsModalsViewModel == null)
            {
                // No InvalidateScanServices here: a fresh VM has nothing cached, and it builds its
                // scan services from the live licence on first use.
                _receiptsModalsViewModel = new ReceiptsModalsViewModel();
                OnPropertyChanged();
            }
            return _receiptsModalsViewModel;
        }
    }

    public LostDamagedModalsViewModel LostDamagedModalsViewModel
    {
        get
        {
            if (_lostDamagedModalsViewModel == null)
            {
                _lostDamagedModalsViewModel = new LostDamagedModalsViewModel();
                OnPropertyChanged();
            }
            return _lostDamagedModalsViewModel;
        }
    }

    public ReturnsModalsViewModel ReturnsModalsViewModel
    {
        get
        {
            if (_returnsModalsViewModel == null)
            {
                _returnsModalsViewModel = new ReturnsModalsViewModel();
                OnPropertyChanged();
            }
            return _returnsModalsViewModel;
        }
    }

    public ReportModalsViewModel ReportModalsViewModel
    {
        get
        {
            if (_reportModalsViewModel == null)
            {
                _reportModalsViewModel = new ReportModalsViewModel();
                OnPropertyChanged();
            }
            return _reportModalsViewModel;
        }
    }

    public VersionHistoryModalViewModel VersionHistoryModalViewModel { get; }

    public UnsavedChangesDialogViewModel UnsavedChangesDialogViewModel { get; }

    public CustomDateRangeModalViewModel CustomDateRangeModalViewModel { get; }

    #endregion

    #region Navigation Properties

    /// <summary>
    /// Reference to the current ReportsPageViewModel when on Reports page.
    /// </summary>
    private ReportsPageViewModel? _reportsPageViewModel;

    /// <summary>
    /// Widget catalog VM exposed so AppShell can show the catalog modal above the entire window.
    /// </summary>
    [ObservableProperty]
    private WidgetCatalogViewModel? _widgetCatalog;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _currentPageName = "Dashboard";

    #endregion

    #region Update Available Banner

    [ObservableProperty]
    private bool _showUpdateAvailableBanner;

    [ObservableProperty]
    private string _updateBannerMessage = "";

    /// <summary>
    /// Shows the update available banner with the given version.
    /// </summary>
    public void ShowUpdateBanner(string version)
    {
        UpdateBannerMessage = $"{"A new version".Translate()} {version} {"is available.".Translate()}";
        ShowUpdateAvailableBanner = true;
    }

    /// <summary>
    /// Dismisses the update available banner.
    /// </summary>
    [RelayCommand]
    private void DismissUpdateBanner()
    {
        ShowUpdateAvailableBanner = false;
    }

    /// <summary>
    /// Opens the Check for Update modal from the banner and starts downloading.
    /// </summary>
    [RelayCommand]
    private void OpenUpdateModalFromBanner()
    {
        ShowUpdateAvailableBanner = false;
        CheckForUpdateModalViewModel.OpenAndDownloadCommand.Execute(null);
    }

    #endregion

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public AppShellViewModel() : this(null)
    {
    }

    /// <summary>
    /// Constructor with dependency injection.
    /// </summary>
    /// <param name="navigationService">Navigation service.</param>
    /// <param name="updateService">Optional update service (desktop only).</param>
    public AppShellViewModel(INavigationService? navigationService, IUpdateService? updateService = null)
    {
        _navigationService = navigationService;

        SidebarViewModel = new SidebarViewModel(navigationService);

        HeaderViewModel = new HeaderViewModel(navigationService);

        // Create quick actions panel with navigation service and link to sidebar
        QuickActionsViewModel = new QuickActionsViewModel(navigationService);
        QuickActionsViewModel.SetSidebarViewModel(SidebarViewModel);

        // Create notification panel with header view model (shares notifications)
        NotificationPanelViewModel = new NotificationPanelViewModel(HeaderViewModel);

        // Create file menu panel with navigation service and link to sidebar for dynamic positioning
        FileMenuPanelViewModel = new FileMenuPanelViewModel();
        FileMenuPanelViewModel.SetSidebarViewModel(SidebarViewModel);

        // Create help panel with navigation service
        HelpPanelViewModel = new HelpPanelViewModel();

        UpgradeModalViewModel = new UpgradeModalViewModel();

        // Wire up license verification to enable premium features
        UpgradeModalViewModel.KeyVerified += (_, _) =>
        {
            SetPlanStatus(true);
        };

        // Create company creation wizard
        CreateCompanyViewModel = new CreateCompanyViewModel();

        CompanySwitcherPanelViewModel = new CompanySwitcherPanelViewModel();

        SettingsModalViewModel = new SettingsModalViewModel();

        // Create check for update modal (with real update service on desktop, stub otherwise)
        CheckForUpdateModalViewModel = updateService != null
            ? new CheckForUpdateModalViewModel(updateService)
            : new CheckForUpdateModalViewModel();

        PredictionInfoModalViewModel = new PredictionInfoModalViewModel();

        PastPredictionsModalViewModel = new PastPredictionsModalViewModel();

        ImportModalViewModel = new ImportModalViewModel();

        ImportValidationDialogViewModel = new ImportValidationDialogViewModel();
        ImportResultDialogViewModel = new ImportResultDialogViewModel();

        ImportMappingDialogViewModel = new ImportMappingDialogViewModel();
        CurrencyAmbiguityDialogViewModel = new CurrencyAmbiguityDialogViewModel();
        RateUnavailableDialogViewModel = new RateUnavailableDialogViewModel();

        ExportAsModalViewModel = new ExportAsModalViewModel();

        SwitchAccountModalViewModel = new SwitchAccountModalViewModel();

        PasswordPromptModalViewModel = new PasswordPromptModalViewModel();

        EditCompanyModalViewModel = new EditCompanyModalViewModel();

        VersionHistoryModalViewModel = new VersionHistoryModalViewModel();

        UnsavedChangesDialogViewModel = new UnsavedChangesDialogViewModel();

        CustomDateRangeModalViewModel = new CustomDateRangeModalViewModel();

        // Register navigation guard for unsaved changes check
        if (_navigationService is NavigationService navService)
        {
            navService.RegisterNavigationGuard(CheckUnsavedChangesBeforeNavigation);
        }

        // Wire up switch account modal's create account to open company wizard
        SwitchAccountModalViewModel.CreateAccountRequested += (_, _) => CreateCompanyViewModel.OpenCommand.Execute(null);

        // Wire up hamburger menu to toggle sidebar
        HeaderViewModel.ToggleSidebarRequested += (_, _) => SidebarViewModel.IsCollapsed = !SidebarViewModel.IsCollapsed;

        // Wire up header's quick actions button to open the panel in dropdown mode. Re-sync the
        // query from the header searchbox first: closing the panel clears the panel's own
        // SearchQuery, but the textbox keeps its text, so without this the reopened panel would
        // show unfiltered results while the textbox still displays the search text.
        HeaderViewModel.OpenQuickActionsRequested += (_, _) =>
        {
            QuickActionsViewModel.SearchQuery = HeaderViewModel.SearchQuery;
            QuickActionsViewModel.OpenDropdownCommand.Execute(null);
        };

        // Wire up header's notification button to toggle the notification panel
        HeaderViewModel.OpenNotificationsRequested += (_, _) => NotificationPanelViewModel.ToggleCommand.Execute(null);

        // Wire up header's file menu button to toggle the file menu panel
        HeaderViewModel.OpenFileMenuRequested += (_, _) => FileMenuPanelViewModel.ToggleCommand.Execute(null);

        // Wire up notification panel's settings to open settings modal at notifications tab
        NotificationPanelViewModel.OpenNotificationSettingsRequested += (_, _) => SettingsModalViewModel.OpenWithTab(SettingsTab.Notifications);

        // Wire up settings modal's upgrade request to open upgrade modal
        SettingsModalViewModel.UpgradeRequested += (_, _) => UpgradeModalViewModel.OpenCommand.Execute(null);

        // Wire up header's help button to toggle help panel
        HeaderViewModel.OpenHelpRequested += (_, _) => HelpPanelViewModel.ToggleCommand.Execute(null);

        // Wire up header's upgrade button to open upgrade modal
        HeaderViewModel.OpenUpgradeRequested += (_, _) => UpgradeModalViewModel.OpenCommand.Execute(null);

        // Wire up header's settings button to open settings modal
        HeaderViewModel.OpenSettingsRequested += (_, _) => SettingsModalViewModel.OpenCommand.Execute(null);

        // Wire up header's history button to open version history modal
        HeaderViewModel.OpenHistoryRequested += (_, _) => VersionHistoryModalViewModel.OpenCommand.Execute(null);

        // Wire up file menu's create new company to open the wizard (prompts to save first if
        // the current company has unsaved changes).
        FileMenuPanelViewModel.CreateNewCompanyRequested += (_, _) => _ = App.RequestCreateNewCompanyAsync();

        // Wire up sidebar's company header click to open the company switcher
        SidebarViewModel.OpenCompanySwitcherRequested += (_, _) => CompanySwitcherPanelViewModel.ToggleCommand.Execute(null);

        // Wire up sidebar navigation to close all panels
        SidebarViewModel.NavigationRequested += (_, _) => CloseAllPanels();

        // Wire up company switcher's create new company to open the wizard (prompts to save first
        // if the current company has unsaved changes).
        CompanySwitcherPanelViewModel.CreateNewCompanyRequested += (_, _) => _ = App.RequestCreateNewCompanyAsync();

        // Wire up help panel's check for updates to open the check for update modal
        HelpPanelViewModel.CheckForUpdatesRequested += (_, _) => CheckForUpdateModalViewModel.OpenCommand.Execute(null);

        // Wire up help panel's restart tutorial to show the tutorial welcome
        HelpPanelViewModel.RestartTutorialRequested += OnRestartTutorialRequested;

        // Wire up file menu's import to open the import modal
        FileMenuPanelViewModel.ImportRequested += (_, _) => ImportModalViewModel.OpenCommand.Execute(null);

        // Wire up file menu's export as to open the export as modal
        FileMenuPanelViewModel.ExportAsRequested += (_, _) => ExportAsModalViewModel.OpenCommand.Execute(null);

        // Sync search query between header and quick actions
        HeaderViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HeaderViewModel.SearchQuery))
            {
                QuickActionsViewModel.SearchQuery = HeaderViewModel.SearchQuery;
            }
        };

        // Wire up header search key events to quick actions panel
        HeaderViewModel.SearchKeyPressed += (_, action) =>
        {
            switch (action)
            {
                case SearchKeyAction.Escape:
                    QuickActionsViewModel.CloseCommand.Execute(null);
                    HeaderViewModel.SearchQuery = null;
                    break;
                case SearchKeyAction.Up:
                    QuickActionsViewModel.MoveUpCommand.Execute(null);
                    break;
                case SearchKeyAction.Down:
                    QuickActionsViewModel.MoveDownCommand.Execute(null);
                    break;
                case SearchKeyAction.Enter:
                    QuickActionsViewModel.ExecuteSelectedCommand.Execute(null);
                    break;
            }
        };

        // Wire up quick action execution for modals
        QuickActionsViewModel.ActionRequested += (_, e) =>
        {
            // Handle actions that open modals after navigation
            switch (QuickActionNameExtensions.ParseQuickActionName(e.ActionName))
            {
                case QuickActionName.OpenAddModal:
                    switch (NavigationTargetExtensions.ParseNavigationTarget(e.NavigationTarget))
                    {
                        case NavigationTarget.RentalInventory:
                            RentalInventoryModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.RentalRecords:
                            RentalRecordsModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.Customers:
                            CustomerModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.Products:
                            ProductModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.Suppliers:
                            SupplierModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.Invoices:
                            InvoiceModalsViewModel.OpenCreateModal();
                            break;
                        case NavigationTarget.Expenses:
                            ExpenseModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.Revenue:
                            RevenueModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.Payments:
                            PaymentModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.Categories:
                            CategoryModalsViewModel.OpenAddModalCommand.Execute(null);
                            break;
                        case NavigationTarget.Locations:
                            LocationsModalsViewModel.OpenAddModal();
                            break;
                        case NavigationTarget.PurchaseOrders:
                            PurchaseOrdersModalsViewModel.OpenAddModal();
                            break;
                        case NavigationTarget.Adjustments:
                            StockAdjustmentsModalsViewModel.OpenAddModal();
                            break;
                    }
                    break;
                case QuickActionName.OpenSettings:
                    SettingsModalViewModel.OpenCommand.Execute(null);
                    break;
                case QuickActionName.OpenHelp:
                    HelpPanelViewModel.ToggleCommand.Execute(null);
                    break;
                case QuickActionName.OpenExport:
                    ExportAsModalViewModel.OpenCommand.Execute(null);
                    break;
                case QuickActionName.OpenImport:
                    ImportModalViewModel.OpenCommand.Execute(null);
                    break;
                case QuickActionName.OpenScanModal:
                    OpenFileScanRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case QuickActionName.OpenBankImport:
                    _ = App.OpenBankStatementImportAsync();
                    break;
                case QuickActionName.OpenEditCompany:
                    EditCompanyRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case QuickActionName.OpenCheckForUpdates:
                    CheckForUpdateModalViewModel.OpenCommand.Execute(null);
                    break;
            }
        };

        // Subscribe to navigation events to update UI state
        if (_navigationService != null)
        {
            _navigationService.Navigated += OnNavigated;
        }
    }

    /// <summary>
    /// Opens the quick actions panel in modal mode (Ctrl+K).
    /// </summary>
    [RelayCommand]
    private void OpenQuickActions()
    {
        QuickActionsViewModel.OpenModalCommand.Execute(null);
    }

    /// <summary>
    /// Handles navigation events to update UI state.
    /// </summary>
    private void OnNavigated(object? sender, Core.Services.NavigationEventArgs e)
    {
        CurrentPageName = e.PageName;
        SidebarViewModel.SetActivePage(e.PageName);
        HeaderViewModel.SetPageTitle(e.PageName);

        if (e.PageName == "Dashboard" && CurrentPage is Control { DataContext: DashboardPageViewModel dashVm })
        {
            WidgetCatalog = dashVm.LayoutViewModel.Catalog;
        }
        else
        {
            // Close the catalog before clearing the reference, otherwise the
            // panel's IsVisible binding breaks (null DataContext → default true)
            if (WidgetCatalog != null)
                WidgetCatalog.IsOpen = false;
            WidgetCatalog = null;
        }

        if (e.PageName == "Reports" && CurrentPage is Control { DataContext: ReportsPageViewModel reportsVm })
        {
            _reportsPageViewModel = reportsVm;
            // Wire up the unsaved changes confirmation for Previous button
            _reportsPageViewModel.ConfirmDiscardChangesAsync = ConfirmDiscardReportChangesAsync;
            // Set the ReportsPageViewModel on the ReportModalsViewModel for modal access
            ReportModalsViewModel.ReportsPageViewModel = reportsVm;
        }
        else if (e.PageName != "Reports")
        {
            _reportsPageViewModel = null;
            ReportModalsViewModel.ReportsPageViewModel = null;
        }
    }

    /// <summary>
    /// Confirms discarding unsaved report changes when going back from Layout Designer.
    /// </summary>
    /// <returns>True if changes should be discarded, false to cancel.</returns>
    private async Task<bool> ConfirmDiscardReportChangesAsync()
    {
        var result = await UnsavedChangesDialogViewModel.ShowSimpleAsync(
            "Unsaved Report Changes".Translate(),
            "You have unsaved changes in the layout designer. Would you like to save them?".Translate());

        switch (result)
        {
            case UnsavedChangesResult.Save:
                // Open the save template modal and wait for it to complete
                if (_reportsPageViewModel != null)
                {
                    var saved = await _reportsPageViewModel.OpenSaveTemplateAndWaitAsync();
                    return saved;
                }
                return false;

            case UnsavedChangesResult.DontSave:
                return true; // Discard changes and go back

            case UnsavedChangesResult.Cancel:
            default:
                return false; // Cancel, stay on current step
        }
    }

    /// <summary>
    /// Navigation guard that checks for unsaved changes when leaving the Reports page.
    /// </summary>
    private async Task<bool> CheckUnsavedChangesBeforeNavigation(string fromPage, string toPage)
    {
        // Check when leaving the Dashboard page in edit mode
        if (fromPage == "Dashboard" && CurrentPage is Control { DataContext: DashboardPageViewModel dashVm }
            && dashVm.LayoutViewModel.IsEditMode)
        {
            var result = await UnsavedChangesDialogViewModel.ShowSimpleAsync(
                "Unsaved Dashboard Changes".Translate(),
                "You have unsaved changes to the dashboard layout. Would you like to save them before leaving?".Translate());

            switch (result)
            {
                case UnsavedChangesResult.Save:
                    await dashVm.LayoutViewModel.SaveEditCommand.ExecuteAsync(null);
                    return true;

                case UnsavedChangesResult.DontSave:
                    dashVm.LayoutViewModel.CancelEditCommand.Execute(null);
                    return true;

                case UnsavedChangesResult.Cancel:
                default:
                    return false;
            }
        }

        // Check when leaving the Reports page
        if (fromPage != "Reports" || _reportsPageViewModel == null)
        {
            return true; // Allow navigation
        }

        if (!_reportsPageViewModel.HasUnsavedChanges)
        {
            return true; // No changes, allow navigation
        }

        var result2 = await UnsavedChangesDialogViewModel.ShowSimpleAsync(
            "Unsaved Report Changes".Translate(),
            "You have unsaved changes in the report designer. Would you like to save them?".Translate());

        switch (result2)
        {
            case UnsavedChangesResult.Save:
                // Open the save template modal and wait for it to complete
                var saved = await _reportsPageViewModel.OpenSaveTemplateAndWaitAsync();
                // If user cancelled the save modal, don't proceed with navigation
                return saved;

            case UnsavedChangesResult.DontSave:
                return true; // Discard changes and navigate

            case UnsavedChangesResult.Cancel:
            default:
                return false; // Cancel navigation
        }
    }

    /// <summary>
    /// Sets the company information on the sidebar.
    /// </summary>
    public void SetCompanyInfo(string? companyName, Bitmap? logo = null, string? userRole = null)
    {
        SidebarViewModel.SetCompanyInfo(companyName, logo, userRole);
    }

    /// <summary>
    /// Applies the plan to every screen, showing or hiding premium features. The single entry point
    /// for a plan change, whether it comes from a licence loaded at startup, an online revalidation
    /// that failed, or a key entered in the upgrade modal.
    /// </summary>
    public void SetPlanStatus(bool hasPremium)
    {
        // SidebarViewModel.HasPremium is the current plan: screens built later read it back from here.
        SidebarViewModel.HasPremium = hasPremium;
        SettingsModalViewModel.HasPremium = hasPremium;
        UpgradeModalViewModel.HasPremium = hasPremium;
        HeaderViewModel.HasPremium = hasPremium;

        if (_invoiceModalsViewModel != null)
            _invoiceModalsViewModel.HasPremium = hasPremium;

        // Only invalidate if already created
        _receiptsModalsViewModel?.InvalidateScanServices();

        // Notify lazily-created page ViewModels (e.g., InsightsPageViewModel)
        App.RaisePlanStatusChanged(hasPremium);
    }

    /// <summary>
    /// Checks if the reports page has unsaved changes.
    /// </summary>
    public bool HasReportsPageUnsavedChanges => _reportsPageViewModel?.HasUnsavedChanges ?? false;

    /// <summary>
    /// Shows the unsaved changes dialog for reports and returns whether to proceed.
    /// </summary>
    /// <returns>True if should proceed (save or discard), false if cancelled.</returns>
    public async Task<bool> ConfirmReportsUnsavedChangesAsync()
    {
        if (_reportsPageViewModel == null || !_reportsPageViewModel.HasUnsavedChanges)
        {
            return true;
        }

        var result = await UnsavedChangesDialogViewModel.ShowSimpleAsync(
            "Unsaved Report Changes".Translate(),
            "You have unsaved changes in the report designer. Would you like to save them?".Translate());

        switch (result)
        {
            case UnsavedChangesResult.Save:
                // Open the save template modal and wait for it to complete
                var saved = await _reportsPageViewModel.OpenSaveTemplateAndWaitAsync();
                // If user cancelled the save modal, don't proceed with navigation
                return saved;

            case UnsavedChangesResult.DontSave:
                return true;

            case UnsavedChangesResult.Cancel:
            default:
                return false;
        }
    }

    /// <summary>
    /// Closes all open panels.
    /// </summary>
    private void CloseAllPanels()
    {
        NotificationPanelViewModel.CloseCommand.Execute(null);
        FileMenuPanelViewModel.CloseCommand.Execute(null);
        HelpPanelViewModel.CloseCommand.Execute(null);
        QuickActionsViewModel.CloseCommand.Execute(null);
        CompanySwitcherPanelViewModel.CloseCommand.Execute(null);
        ClosePageContextMenus();
    }

    /// <summary>
    /// Closes any open context menus on the current page (column visibility, chart context, reports context).
    /// </summary>
    public void ClosePageContextMenus()
    {
        if (CurrentPage is not Control { DataContext: { } vm })
            return;

        var type = vm.GetType();

        // Close column visibility menu
        type.GetProperty("IsColumnMenuOpen", BindingFlags.Public | BindingFlags.Instance)?
            .SetValue(vm, false);

        type.GetProperty("IsChartContextMenuOpen", BindingFlags.Public | BindingFlags.Instance)?
            .SetValue(vm, false);

        // Close reports context menu
        type.GetProperty("IsContextMenuOpen", BindingFlags.Public | BindingFlags.Instance)?
            .SetValue(vm, false);
    }

    /// <summary>
    /// Handles the restart tutorial request from the Help panel.
    /// </summary>
    private void OnRestartTutorialRequested(object? sender, EventArgs e)
    {
        RestartTutorialRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a notification.
    /// </summary>
    /// <param name="title">Notification title.</param>
    /// <param name="message">Notification message.</param>
    /// <param name="type">Notification type.</param>
    public void AddNotification(string title, string message, NotificationType type = NotificationType.Info,
        Action? onClick = null)
    {
        HeaderViewModel.AddNotification(new NotificationItem
        {
            Title = title,
            Message = message,
            Type = type,
            OnClick = onClick
        });
    }
}
