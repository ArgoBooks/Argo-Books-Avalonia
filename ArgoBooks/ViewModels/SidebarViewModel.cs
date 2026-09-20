using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using ArgoBooks.Controls;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the sidebar navigation component.
/// </summary>
public partial class SidebarViewModel : ViewModelBase
{
    private readonly INavigationService? _navigationService;

    #region Sidebar State

    [ObservableProperty]
    private bool _isCollapsed;

    [ObservableProperty]
    private string _collapseTooltip = Loc.Tr("Collapse sidebar");

    [ObservableProperty]
    private double _width = 250;

    // Dimensions from mockup: 250px expanded, 70px collapsed
    private const double ExpandedWidth = 250;
    private const double CollapsedWidth = 70;

    #endregion

    #region Company Info

    [ObservableProperty]
    private string? _companyName = "Argo Books";

    [ObservableProperty]
    private string _companyInitial = "AB";

    [ObservableProperty]
    private Bitmap? _companyLogo;

    [ObservableProperty]
    private bool _hasCompanyLogo;

    [ObservableProperty]
    private string? _userRole;

    #endregion

    #region Feature Visibility

    [ObservableProperty]
    private bool _showTransactions = true;

    [ObservableProperty]
    private bool _showInventory = true;

    [ObservableProperty]
    private bool _showRentals = true;

    [ObservableProperty]
    private bool _showPayroll = true;

    [ObservableProperty]
    private bool _hasPremium; // Premium plan

    #endregion

    #region Premium Feature Items

    private SidebarItemModel? _invoicesItem;

    #endregion

    #region Navigation Items

    [ObservableProperty]
    private string _currentPage = "Dashboard";

    public ObservableCollection<SidebarItemModel> MainItems { get; } = [];
    public ObservableCollection<SidebarItemModel> ExpenseItems { get; } = [];
    public ObservableCollection<SidebarItemModel> RevenueItems { get; } = [];
    public ObservableCollection<SidebarItemModel> ImportItems { get; } = [];
    public ObservableCollection<SidebarItemModel> RentalItems { get; } = [];
    public ObservableCollection<SidebarItemModel> InventoryItems { get; } = [];
    public ObservableCollection<SidebarItemModel> PayrollItems { get; } = [];
    public ObservableCollection<SidebarItemModel> TrackingItems { get; } = [];

    #endregion

    #region Events

    /// <summary>
    /// Event raised when the company header is clicked to open the company switcher.
    /// </summary>
    public event EventHandler? OpenCompanySwitcherRequested;

    /// <summary>
    /// Event raised when navigating to a page (so panels can be closed).
    /// </summary>
    public event EventHandler? NavigationRequested;

    #endregion

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public SidebarViewModel() : this(null)
    {
    }

    /// <summary>
    /// Constructor with dependency injection.
    /// </summary>
    public SidebarViewModel(INavigationService? navigationService)
    {
        _navigationService = navigationService;

        InitializeNavigationItems();

        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        CollapseTooltip = IsCollapsed ? Loc.Tr("Expand sidebar") : Loc.Tr("Collapse sidebar");
    }

    /// <summary>
    /// Initializes all navigation items.
    /// </summary>
    private void InitializeNavigationItems()
    {
        // Main Section (mockup: Dashboard, Analytics, Insights, Reports)
        MainItems.Add(CreateItem("Dashboard", "Dashboard", Icons.Dashboard));
        MainItems.Add(CreateItem("Analytics", "Analytics", Icons.Analytics));
        MainItems.Add(CreateItem("Insights", "Insights", Icons.Insights));
        MainItems.Add(CreateItem("Reports", "Reports", Icons.Reports));

        // Expenses Section. The category and product pages are one page each, entered
        // on the matching tab, so these route to a page name that presets it.
        ExpenseItems.Add(CreateItem("Expenses", "Expenses", Icons.Expenses));
        ExpenseItems.Add(CreateItem("Expense categories", "ExpenseCategories", Icons.Categories));
        ExpenseItems.Add(CreateItem("Expense products", "ExpenseProducts", Icons.Products));
        ExpenseItems.Add(CreateItem("Suppliers", "Suppliers", Icons.Suppliers));

        // Revenue Section
        RevenueItems.Add(CreateItem("Revenue", "Revenue", Icons.Revenue));
        RevenueItems.Add(CreateItem("Quotes", "Quotes", Icons.Quotes));
        _invoicesItem = CreateItem("Invoices", "Invoices", Icons.Invoices);
        _invoicesItem.IsVisible = true; // Available on free tier (with send limits)
        RevenueItems.Add(_invoicesItem);
        RevenueItems.Add(CreateItem("Revenue categories", "RevenueCategories", Icons.Categories));
        RevenueItems.Add(CreateItem("Revenue products", "RevenueProducts", Icons.Products));
        RevenueItems.Add(CreateItem("Customers", "Customers", Icons.Customers));

        // Import Section. Both screens turn outside evidence into transactions, and
        // both can produce an expense or a revenue, so neither belongs to one side.
        ImportItems.Add(CreateItem("Bank Matching", "BankMatching", Icons.Bank));
        ImportItems.Add(CreateItem("Receipts", "Receipts", Icons.Receipts));

        // Rentals Section
        RentalItems.Add(CreateItem("Rental Inventory", "RentalInventory", Icons.RentalInventory));
        RentalItems.Add(CreateItem("Rental Records", "RentalRecords", Icons.RentalRecords));

        // Inventory Section
        InventoryItems.Add(CreateItem("Stock Levels", "StockLevels", Icons.StockLevels));
        InventoryItems.Add(CreateItem("Adjustments", "StockAdjustments", Icons.Adjustments));
        InventoryItems.Add(CreateItem("Locations", "Locations", Icons.Locations));
        InventoryItems.Add(CreateItem("Purchase Orders", "PurchaseOrders", Icons.PurchaseOrders));

        // Payroll Section
        PayrollItems.Add(CreateItem("Employees", "Employees", Icons.Customers));
        PayrollItems.Add(CreateItem("Pay Runs", "PayRuns", Icons.Payments));

        // Tracking Section
        TrackingItems.Add(CreateItem("Returns", "Returns", Icons.Returns));
        TrackingItems.Add(CreateItem("Lost / Damaged", "LostDamaged", Icons.LostDamaged));

        // Set Dashboard as active by default
        SetActivePage("Dashboard");
    }

    /// <summary>
    /// Creates a sidebar item model.
    /// </summary>
    private SidebarItemModel CreateItem(string text, string pageName, string iconData)
    {
        return new SidebarItemModel
        {
            Text = text,
            PageName = pageName,
            IconData = iconData,
            Command = NavigateCommand
        };
    }

    /// <summary>
    /// Updates width when collapsed state changes.
    /// </summary>
    partial void OnIsCollapsedChanged(bool value)
    {
        Width = value ? CollapsedWidth : ExpandedWidth;
        CollapseTooltip = value ? Loc.Tr("Expand sidebar") : Loc.Tr("Collapse sidebar");

        // Update all items with collapsed state
        UpdateItemsCollapsedState(value);

        // Persist collapsed state to settings
        var settings = App.SettingsService?.GlobalSettings;
        if (settings != null)
        {
            settings.Ui.SidebarCollapsed = value;
            _ = App.SettingsService?.SaveGlobalSettingsAsync();
        }
    }

    /// <summary>
    /// Updates the company initial when name changes.
    /// </summary>
    partial void OnCompanyNameChanged(string? value)
    {
        CompanyInitial = Helpers.InitialsHelper.From(value);
    }

    /// <summary>
    /// Updates collapsed state on all items.
    /// </summary>
    private void UpdateItemsCollapsedState(bool isCollapsed)
    {
        foreach (var item in MainItems) item.IsCollapsed = isCollapsed;
        foreach (var item in ExpenseItems) item.IsCollapsed = isCollapsed;
        foreach (var item in RevenueItems) item.IsCollapsed = isCollapsed;
        foreach (var item in ImportItems) item.IsCollapsed = isCollapsed;
        foreach (var item in RentalItems) item.IsCollapsed = isCollapsed;
        foreach (var item in InventoryItems) item.IsCollapsed = isCollapsed;
        foreach (var item in PayrollItems) item.IsCollapsed = isCollapsed;
        foreach (var item in TrackingItems) item.IsCollapsed = isCollapsed;
    }

    /// <summary>
    /// Toggles the sidebar collapsed state.
    /// </summary>
    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }

    /// <summary>
    /// Opens the company switcher panel.
    /// </summary>
    [RelayCommand]
    private void OpenCompanySwitcher()
    {
        OpenCompanySwitcherRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Navigates to the specified page.
    /// </summary>
    [RelayCommand]
    private async Task NavigateAsync(string? pageName)
    {
        if (string.IsNullOrEmpty(pageName))
            return;

        // Raise event so panels can be closed
        NavigationRequested?.Invoke(this, EventArgs.Empty);

        // Use async navigation to allow navigation guards to check for unsaved changes
        if (_navigationService != null)
        {
            var navigated = await _navigationService.NavigateToAsync(pageName);
            if (navigated)
            {
                SetActivePage(pageName);
            }
        }
    }

    /// <summary>
    /// Sets the active page and updates item states.
    /// </summary>
    public void SetActivePage(string pageName)
    {
        CurrentPage = pageName;

        // Update active state on all items
        foreach (var item in MainItems) item.IsActive = item.PageName == pageName;
        foreach (var item in ExpenseItems) item.IsActive = item.PageName == pageName;
        foreach (var item in RevenueItems) item.IsActive = item.PageName == pageName;
        foreach (var item in ImportItems) item.IsActive = item.PageName == pageName;
        foreach (var item in RentalItems) item.IsActive = item.PageName == pageName;
        foreach (var item in InventoryItems) item.IsActive = item.PageName == pageName;
        foreach (var item in PayrollItems) item.IsActive = item.PageName == pageName;
        foreach (var item in TrackingItems) item.IsActive = item.PageName == pageName;
    }

    /// <summary>
    /// Updates feature visibility based on settings.
    /// </summary>
    public void UpdateFeatureVisibility(bool showTransactions, bool showInventory, bool showRentals,
                                        bool showPayroll)
    {
        ShowTransactions = showTransactions;
        ShowInventory = showInventory;
        ShowRentals = showRentals;
        ShowPayroll = showPayroll;
    }

    /// <summary>
    /// Sets the company information.
    /// </summary>
    public void SetCompanyInfo(string? name, Bitmap? logo = null, string? userRole = null)
    {
        CompanyName = name ?? "Argo Books";
        CompanyLogo = logo;
        HasCompanyLogo = logo != null;
        UserRole = userRole;
    }
}
