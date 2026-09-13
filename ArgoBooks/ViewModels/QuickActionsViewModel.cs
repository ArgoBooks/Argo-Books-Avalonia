using System.Collections.ObjectModel;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Quick Actions panel (command palette).
/// </summary>
public partial class QuickActionsViewModel : ViewModelBase
{
    private readonly INavigationService? _navigationService;
    private readonly List<QuickActionItem> _allActions = [];
    private SidebarViewModel? _sidebarViewModel;

    #region Properties

    [ObservableProperty]
    private string? _searchQuery;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isDropdownMode;

    [ObservableProperty]
    private int _selectedIndex;

    /// <summary>
    /// Gets the sidebar width for layout positioning.
    /// </summary>
    public double SidebarWidth => _sidebarViewModel?.Width ?? 250;

    /// <summary>
    /// Filtered quick actions based on search query.
    /// </summary>
    public ObservableCollection<QuickActionItem> QuickActions { get; } = [];

    /// <summary>
    /// Filtered navigation pages based on search query.
    /// </summary>
    public ObservableCollection<QuickActionItem> NavigationItems { get; } = [];

    /// <summary>
    /// Filtered tools and settings based on search query.
    /// </summary>
    public ObservableCollection<QuickActionItem> ToolsItems { get; } = [];

    /// <summary>
    /// Top results with strong matches (shown first when searching).
    /// </summary>
    public ObservableCollection<QuickActionItem> TopResults { get; } = [];

    /// <summary>
    /// Data search results from CompanyData (customers, products, invoices, etc.).
    /// </summary>
    public ObservableCollection<QuickActionItem> SearchResults { get; } = [];

    /// <summary>
    /// Gets whether there are any data search results visible.
    /// </summary>
    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>
    /// Gets whether there are any top results visible.
    /// </summary>
    public bool HasTopResults => TopResults.Count > 0;

    /// <summary>
    /// Gets whether there are any quick actions visible.
    /// </summary>
    public bool HasQuickActions => QuickActions.Count > 0;

    /// <summary>
    /// Gets whether there are any navigation items visible.
    /// </summary>
    public bool HasNavigationItems => NavigationItems.Count > 0;

    /// <summary>
    /// Gets whether there are any tools items visible.
    /// </summary>
    public bool HasToolsItems => ToolsItems.Count > 0;

    /// <summary>
    /// Gets whether there are any results at all.
    /// </summary>
    public bool HasResults => HasSearchResults || HasTopResults || HasQuickActions || HasNavigationItems || HasToolsItems;

    #endregion

    /// <summary>
    /// Creates a new QuickActionsViewModel.
    /// </summary>
    public QuickActionsViewModel() : this(null)
    {
    }

    /// <summary>
    /// Creates a new QuickActionsViewModel with navigation service.
    /// </summary>
    public QuickActionsViewModel(INavigationService? navigationService)
    {
        _navigationService = navigationService;
        InitializeActions();
        FilterActions(null);

        // Subscribe to language changes to refresh translated content
        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Called when the language changes. Refreshes the lists to update translations.
    /// </summary>
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // Re-filter to force ItemsControls to re-render with new translations
        FilterActions(SearchQuery);
    }

    /// <summary>
    /// Sets the sidebar view model to track sidebar width for centering.
    /// </summary>
    public void SetSidebarViewModel(SidebarViewModel sidebarViewModel)
    {
        _sidebarViewModel = sidebarViewModel;
        _sidebarViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.Width))
            {
                OnPropertyChanged(nameof(SidebarWidth));
            }
        };
    }

    /// <summary>
    /// Initializes all available actions.
    /// </summary>
    private void InitializeActions()
    {
        // Quick Actions - Creation tasks
        _allActions.AddRange([
            new QuickActionItem("New Invoice", "Create a new invoice", Icons.Invoices, QuickActionType.QuickAction, "Invoices", "OpenAddModal"),
            new QuickActionItem("New Expense", "Record a new expense", Icons.Expenses, QuickActionType.QuickAction, "Expenses", "OpenAddModal"),
            new QuickActionItem("New Revenue", "Record a new revenue entry", Icons.Revenue, QuickActionType.QuickAction, "Revenue", "OpenAddModal"),
            new QuickActionItem("Scan Receipt", "Scan and import a receipt using AI", Icons.ScanReceipt, QuickActionType.QuickAction, "Receipts", "OpenScanModal"),
            new QuickActionItem("Import Bank Statement", "Import transactions from a bank statement", Icons.Bank, QuickActionType.QuickAction, null, "OpenBankImport"),
            new QuickActionItem("New Customer", "Add a new customer", Icons.NewCustomer, QuickActionType.QuickAction, "Customers", "OpenAddModal"),
            new QuickActionItem("New Product", "Add a new product or service", Icons.NewProduct, QuickActionType.QuickAction, "Products", "OpenAddModal"),
            new QuickActionItem("New Supplier", "Add a new supplier", Icons.Suppliers, QuickActionType.QuickAction, "Suppliers", "OpenAddModal"),
            new QuickActionItem("New Rental Item", "Add a new rental item", Icons.NewRentalItem, QuickActionType.QuickAction, "RentalInventory", "OpenAddModal"),
            new QuickActionItem("New Rental Record", "Create a new rental transaction", Icons.RentalRecords, QuickActionType.QuickAction, "RentalRecords", "OpenAddModal"),
            new QuickActionItem("New Category", "Add a new category", Icons.Categories, QuickActionType.QuickAction, "Categories", "OpenAddModal"),
            new QuickActionItem("New Location", "Add a new location", Icons.Locations, QuickActionType.QuickAction, "Locations", "OpenAddModal"),
            new QuickActionItem("New Purchase Order", "Create a new purchase order", Icons.NewPurchaseOrder, QuickActionType.QuickAction, "PurchaseOrders", "OpenAddModal"),
            new QuickActionItem("New Stock Adjustment", "Record a stock adjustment", Icons.NewStockAdjustment, QuickActionType.QuickAction, "StockAdjustments", "OpenAddModal"),
        ]);

        // Navigation - Go to pages
        _allActions.AddRange([
            // Main
            new QuickActionItem("Dashboard", "Go to Dashboard", Icons.Dashboard, QuickActionType.Navigation, "Dashboard"),
            new QuickActionItem("Analytics", "View analytics and charts", Icons.Analytics, QuickActionType.Navigation, "Analytics"),
            new QuickActionItem("Reports", "Generate and view reports", Icons.Reports, QuickActionType.Navigation, "Reports"),
            new QuickActionItem("Insights", "View business insights (Premium)", Icons.Insights, QuickActionType.Navigation, "Insights"),

            // Transactions
            new QuickActionItem("Expenses", "View and manage expenses", Icons.Expenses, QuickActionType.Navigation, "Expenses"),
            new QuickActionItem("Revenue", "View and manage revenue", Icons.Revenue, QuickActionType.Navigation, "Revenue"),
            new QuickActionItem("Invoices", "Manage invoices", Icons.Invoices, QuickActionType.Navigation, "Invoices"),
            new QuickActionItem("Receipts", "View and manage receipts", Icons.Receipts, QuickActionType.Navigation, "Receipts"),
            new QuickActionItem("Purchase Orders", "Manage purchase orders", Icons.PurchaseOrders, QuickActionType.Navigation, "PurchaseOrders"),
            new QuickActionItem("Returns", "Manage returns", Icons.Returns, QuickActionType.Navigation, "Returns"),
            new QuickActionItem("Lost & Damaged", "Track lost and damaged items", Icons.LostDamaged, QuickActionType.Navigation, "LostDamaged"),

            // Contacts
            new QuickActionItem("Customers", "Manage customers", Icons.Customers, QuickActionType.Navigation, "Customers"),
            new QuickActionItem("Suppliers", "Manage suppliers", Icons.Suppliers, QuickActionType.Navigation, "Suppliers"),

            // Inventory
            new QuickActionItem("Products", "Manage products and services", Icons.Products, QuickActionType.Navigation, "Products"),
            new QuickActionItem("Stock Levels", "Monitor inventory levels", Icons.StockLevels, QuickActionType.Navigation, "StockLevels"),
            new QuickActionItem("Adjustments", "Manage stock adjustments", Icons.Adjustments, QuickActionType.Navigation, "StockAdjustments"),
            new QuickActionItem("Categories", "Manage categories", Icons.Categories, QuickActionType.Navigation, "Categories"),
            new QuickActionItem("Locations", "Manage locations (Enterprise)", Icons.Locations, QuickActionType.Navigation, "Locations"),

            // Rentals
            new QuickActionItem("Rental Inventory", "Manage rental items", Icons.RentalInventory, QuickActionType.Navigation, "RentalInventory"),
            new QuickActionItem("Rental Records", "View rental transactions", Icons.RentalRecords, QuickActionType.Navigation, "RentalRecords"),
        ]);

        // Tools & Settings
        _allActions.AddRange([
            new QuickActionItem("Settings", "Configure application settings", Icons.Settings, QuickActionType.Tools, null, "OpenSettings"),
            new QuickActionItem("Edit Company", "Edit company information", Icons.EditCompany, QuickActionType.Tools, null, "OpenEditCompany"),
            new QuickActionItem("Check for Updates", "Check for application updates", Icons.CheckForUpdates, QuickActionType.Tools, null, "OpenCheckForUpdates"),
            new QuickActionItem("Export Data", "Export your data", Icons.ExportData, QuickActionType.Tools, null, "OpenExport"),
            new QuickActionItem("Import Data", "Import data from file", Icons.ImportData, QuickActionType.Tools, null, "OpenImport"),
            new QuickActionItem("Help & Support", "Get help and documentation", Icons.Help, QuickActionType.Tools, null, "OpenHelp"),
            new QuickActionItem("Keyboard Shortcuts", "View keyboard shortcuts", Icons.KeyboardShortcuts, QuickActionType.Tools, null, "OpenKeyboardShortcuts"),
        ]);
    }

    /// <summary>
    /// Filters actions based on search query.
    /// </summary>
    partial void OnSearchQueryChanged(string? value)
    {
        FilterActions(value);
    }

    /// <summary>
    /// Minimum score threshold for a "strong" match (prefix, word-start, or substring).
    /// Items with title scores at or above this threshold appear in Top Results.
    /// </summary>
    private const double StrongMatchThreshold = 0.8;

    /// <summary>
    /// Filters all action lists based on query using fuzzy matching.
    /// </summary>
    private void FilterActions(string? query)
    {
        QuickActions.Clear();
        NavigationItems.Clear();
        ToolsItems.Clear();
        TopResults.Clear();
        SearchResults.Clear();

        IEnumerable<(QuickActionItem Item, double Score, double TitleScore)> scoredItems;

        if (string.IsNullOrWhiteSpace(query))
        {
            // No query - show all items with score 1, no title score tracking needed
            scoredItems = _allActions.Select(a => (a, 1.0, 0.0));
        }
        else
        {
            // Use Levenshtein-based fuzzy search with title prioritization
            scoredItems = _allActions
                .Select(a =>
                {
                    var titleScore = LevenshteinDistance.ComputeSearchScore(query, a.Title);
                    var descScore = LevenshteinDistance.ComputeSearchScore(query, a.Description);

                    // Title matches are prioritized:
                    // - Strong title match (>= 0.8): use title score with small desc boost
                    // - Weak title match with strong desc match: boost slightly but less than title match
                    // - Both weak: use max
                    double finalScore;
                    if (titleScore >= StrongMatchThreshold)
                    {
                        // Strong title match - prioritize heavily
                        finalScore = titleScore + (descScore > 0 ? descScore * 0.05 : 0);
                    }
                    else if (descScore >= StrongMatchThreshold && titleScore < StrongMatchThreshold)
                    {
                        // Strong description match but weak/no title match - reduce score
                        // This prevents "Scan Receipt" (desc contains "import") from ranking high for "export"
                        finalScore = descScore * 0.75;
                    }
                    else
                    {
                        // Both are fuzzy/weak matches - use max
                        finalScore = Math.Max(titleScore, descScore);
                    }

                    return (Item: a, Score: finalScore, TitleScore: titleScore);
                })
                .Where(x => x.Score > 0) // Only include matches
                .OrderByDescending(x => x.Score);
        }

        var filteredList = scoredItems.ToList();

        // When searching, put strong title matches in Top Results section
        if (!string.IsNullOrWhiteSpace(query))
        {
            var topResultItems = new HashSet<QuickActionItem>();
            foreach (var (item, _, _) in filteredList.Where(x => x.TitleScore >= StrongMatchThreshold).Take(4))
            {
                TopResults.Add(item);
                topResultItems.Add(item);
            }

            // Exclude top results from other categories to avoid duplication
            var remainingItems = filteredList
                .Where(x => !topResultItems.Contains(x.Item))
                .Select(x => x.Item)
                .ToList();

            foreach (var item in remainingItems.Where(a => a.Type == QuickActionType.QuickAction).Take(6))
                QuickActions.Add(item);

            foreach (var item in remainingItems.Where(a => a.Type == QuickActionType.Navigation).Take(8))
                NavigationItems.Add(item);

            foreach (var item in remainingItems.Where(a => a.Type == QuickActionType.Tools).Take(4))
                ToolsItems.Add(item);
        }
        else
        {
            // No query - show every item, grouped by category. The results list scrolls, so
            // there's no need to cap each section the way a search does; showing the full set
            // gives users a usable menu the moment the panel opens.
            var items = filteredList.Select(x => x.Item).ToList();

            foreach (var item in items.Where(a => a.Type == QuickActionType.QuickAction))
                QuickActions.Add(item);

            foreach (var item in items.Where(a => a.Type == QuickActionType.Navigation))
                NavigationItems.Add(item);

            foreach (var item in items.Where(a => a.Type == QuickActionType.Tools))
                ToolsItems.Add(item);
        }

        // Search CompanyData when there's a query
        if (!string.IsNullOrWhiteSpace(query))
        {
            SearchCompanyData(query);
        }

        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(HasTopResults));
        OnPropertyChanged(nameof(HasQuickActions));
        OnPropertyChanged(nameof(HasNavigationItems));
        OnPropertyChanged(nameof(HasToolsItems));
        OnPropertyChanged(nameof(HasResults));

        SelectedIndex = -1;
    }

    #region Commands

    /// <summary>
    /// Opens the Quick Actions panel in dropdown mode (below search box).
    /// Preserves any existing search query.
    /// </summary>
    [RelayCommand]
    private void OpenDropdown()
    {
        IsDropdownMode = true;
        IsOpen = true;
        // Don't clear SearchQuery - it may already be set from the header
        FilterActions(SearchQuery);
    }

    /// <summary>
    /// Opens the Quick Actions panel in modal mode (centered).
    /// </summary>
    [RelayCommand]
    private void OpenModal()
    {
        IsDropdownMode = false;
        IsOpen = true;
        SearchQuery = null;
        FilterActions(null);
    }

    /// <summary>
    /// Opens the Quick Actions panel (defaults to modal mode).
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        OpenModal();
    }

    /// <summary>
    /// Closes the Quick Actions panel.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        SearchQuery = null;
    }

    /// <summary>
    /// Executes the selected action.
    /// </summary>
    [RelayCommand]
    private void ExecuteAction(QuickActionItem? action)
    {
        if (action == null)
            return;

        Close();

        if (!string.IsNullOrEmpty(action.NavigationTarget))
        {
            // For search results, navigate with highlight parameter
            if (action.Type == QuickActionType.SearchResult && action.EntityId != null)
            {
                _navigationService?.NavigateTo(action.NavigationTarget, new TransactionNavigationParameter(action.EntityId));
            }
            else
            {
                _navigationService?.NavigateTo(action.NavigationTarget);
            }
        }

        // Execute action after navigation if specified (not for search results)
        if (action.Type != QuickActionType.SearchResult && !string.IsNullOrEmpty(action.ActionName))
        {
            ActionRequested?.Invoke(this, new QuickActionEventArgs(action.NavigationTarget, action.ActionName));
        }
    }

    /// <summary>
    /// Event raised when an action needs to be executed after navigation.
    /// </summary>
    public event EventHandler<QuickActionEventArgs>? ActionRequested;

    /// <summary>
    /// Moves selection up.
    /// </summary>
    [RelayCommand]
    private void MoveUp()
    {
        var totalCount = SearchResults.Count + TopResults.Count + QuickActions.Count + NavigationItems.Count + ToolsItems.Count;
        if (totalCount == 0) return;
        SelectedIndex = (SelectedIndex - 1 + totalCount) % totalCount;
    }

    /// <summary>
    /// Moves selection down.
    /// </summary>
    [RelayCommand]
    private void MoveDown()
    {
        var totalCount = SearchResults.Count + TopResults.Count + QuickActions.Count + NavigationItems.Count + ToolsItems.Count;
        if (totalCount == 0) return;
        SelectedIndex = (SelectedIndex + 1) % totalCount;
    }

    /// <summary>
    /// All result items in visual order (must match the section order in QuickActionsPanel.axaml
    /// so SelectedIndex lines up with the rendered rows; data Results render last).
    /// </summary>
    private IEnumerable<QuickActionItem> OrderedItems =>
        TopResults.Concat(QuickActions).Concat(NavigationItems).Concat(ToolsItems).Concat(SearchResults);

    /// <summary>
    /// Highlights the item at the new selected index (and clears the rest) so the searchbox can
    /// keep focus while the user navigates with the arrow keys.
    /// </summary>
    partial void OnSelectedIndexChanged(int value)
    {
        var index = 0;
        foreach (var item in OrderedItems)
        {
            item.IsSelected = index == value;
            index++;
        }
    }

    /// <summary>
    /// Executes the currently selected action.
    /// </summary>
    [RelayCommand]
    private void ExecuteSelected()
    {
        var allItems = OrderedItems.ToList();
        if (SelectedIndex >= 0 && SelectedIndex < allItems.Count)
        {
            ExecuteAction(allItems[SelectedIndex]);
        }
    }

    #endregion

    #region Data Search

    private const int MaxResultsPerType = 3;
    private const int MaxTotalResults = 15;

    /// <summary>
    /// Searches CompanyData collections and populates SearchResults.
    /// </summary>
    private void SearchCompanyData(string query)
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var results = new List<(QuickActionItem Item, double Score)>();

        // Customers
        foreach (var c in companyData.Customers)
        {
            var score = LevenshteinDistance.BestScore(query, c.Name, c.CompanyName ?? "", c.Email, c.Phone);
            if (score > 0)
                results.Add((new QuickActionItem(c.Name, string.IsNullOrWhiteSpace(c.CompanyName) ? c.Email : c.CompanyName, Icons.Customers, QuickActionType.SearchResult, "Customers", entityId: c.Id), score));
        }

        // Products
        foreach (var p in companyData.Products)
        {
            var score = LevenshteinDistance.BestScore(query, p.Name, p.Sku, p.Description);
            if (score > 0)
                results.Add((new QuickActionItem(p.Name, string.IsNullOrWhiteSpace(p.Sku) ? p.Description : $"SKU: {p.Sku}", Icons.Products, QuickActionType.SearchResult, "Products", entityId: p.Id), score));
        }

        // Categories
        foreach (var cat in companyData.Categories)
        {
            var score = LevenshteinDistance.BestScore(query, cat.Name, cat.Description ?? "");
            if (score > 0)
                results.Add((new QuickActionItem(cat.Name, $"{cat.Type} category", Icons.Categories, QuickActionType.SearchResult, "Categories", entityId: cat.Id), score));
        }

        // Invoices
        foreach (var inv in companyData.Invoices)
        {
            var customerName = companyData.Customers.FirstOrDefault(c => c.Id == inv.CustomerId)?.Name ?? "";
            var score = LevenshteinDistance.BestScore(query, inv.InvoiceNumber, customerName, inv.Status.ToString());
            if (score > 0)
                results.Add((new QuickActionItem(inv.InvoiceNumber, $"{customerName} · {inv.Status}", Icons.Invoices, QuickActionType.SearchResult, "Invoices", entityId: inv.Id), score));
        }

        // Expenses
        foreach (var e in companyData.Expenses)
        {
            var score = LevenshteinDistance.BestScore(query, e.Description, e.ReferenceNumber, e.Id);
            if (score > 0)
                results.Add((new QuickActionItem(e.Description, $"{CurrencyService.Format(e.Amount)} · {e.Date:MMM dd, yyyy}", Icons.Expenses, QuickActionType.SearchResult, "Expenses", entityId: e.Id), score));
        }

        // Revenues
        foreach (var r in companyData.Revenues)
        {
            var score = LevenshteinDistance.BestScore(query, r.Description, r.ReferenceNumber, r.Id);
            if (score > 0)
                results.Add((new QuickActionItem(r.Description, $"{CurrencyService.Format(r.Amount)} · {r.Date:MMM dd, yyyy}", Icons.Revenue, QuickActionType.SearchResult, "Revenue", entityId: r.Id), score));
        }

        // Suppliers
        foreach (var s in companyData.Suppliers)
        {
            var score = LevenshteinDistance.BestScore(query, s.Name, s.ContactPerson, s.Email);
            if (score > 0)
                results.Add((new QuickActionItem(s.Name, string.IsNullOrWhiteSpace(s.ContactPerson) ? s.Email : s.ContactPerson, Icons.Suppliers, QuickActionType.SearchResult, "Suppliers", entityId: s.Id), score));
        }

        // Payments. They have no page of their own, so a hit opens the invoice it belongs to.
        foreach (var p in companyData.Payments)
        {
            if (string.IsNullOrEmpty(p.InvoiceId))
                continue;
            var score = LevenshteinDistance.BestScore(query, p.Id, p.ReferenceNumber ?? "", p.InvoiceId);
            if (score > 0)
                results.Add((new QuickActionItem(p.Id, $"{CurrencyService.Format(p.Amount)} · {p.PaymentMethod}", Icons.Payments, QuickActionType.SearchResult, "Invoices", entityId: p.InvoiceId), score));
        }

        // Rental Records
        foreach (var r in companyData.Rentals)
        {
            var customerName = companyData.Customers.FirstOrDefault(c => c.Id == r.CustomerId)?.Name ?? "";
            var score = LevenshteinDistance.BestScore(query, r.Id, customerName, r.Status.ToString());
            if (score > 0)
                results.Add((new QuickActionItem(r.Id, $"{customerName} · {r.Status}", Icons.RentalRecords, QuickActionType.SearchResult, "RentalRecords", entityId: r.Id), score));
        }

        // Inventory Items
        foreach (var i in companyData.Inventory)
        {
            var product = companyData.Products.FirstOrDefault(p => p.Id == i.ProductId);
            var location = companyData.Locations.FirstOrDefault(l => l.Id == i.LocationId);
            var productName = product?.Name ?? "";
            var score = LevenshteinDistance.BestScore(query, productName, i.Sku, product?.Sku ?? "", location?.Name ?? "");
            if (score > 0)
                results.Add((new QuickActionItem($"{productName} @ {location?.Name ?? "Default"}", $"SKU: {i.Sku} · In Stock: {i.InStock}", Icons.StockLevels, QuickActionType.SearchResult, "StockLevels", entityId: i.Id), score));
        }

        foreach (var po in companyData.PurchaseOrders)
        {
            var supplierName = companyData.Suppliers.FirstOrDefault(s => s.Id == po.SupplierId)?.Name ?? "";
            var score = LevenshteinDistance.BestScore(query, po.PoNumber, supplierName, po.Status.ToString());
            if (score > 0)
                results.Add((new QuickActionItem(po.PoNumber, $"{supplierName} · {po.Status}", Icons.PurchaseOrders, QuickActionType.SearchResult, "PurchaseOrders", entityId: po.Id), score));
        }

        // Sort by score and take top results, max 3 per type
        var typeCounts = new Dictionary<string, int>();
        var added = 0;
        foreach (var (item, _) in results.OrderByDescending(r => r.Score))
        {
            if (added >= MaxTotalResults) break;
            var key = item.NavigationTarget ?? "";
            typeCounts.TryGetValue(key, out var count);
            if (count >= MaxResultsPerType) continue;
            typeCounts[key] = count + 1;
            SearchResults.Add(item);
            added++;
        }
    }

    #endregion
}

/// <summary>
/// Represents a quick action item.
/// </summary>
public class QuickActionItem : ObservableObject
{
    /// <summary>
    /// Whether this item is the currently highlighted result (keyboard navigation). Kept separate
    /// from focus so the searchbox can retain focus while the user arrows through results.
    /// </summary>
    public bool IsSelected
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Action title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Action description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Icon path data.
    /// </summary>
    public string IconData { get; }

    /// <summary>
    /// Type of action.
    /// </summary>
    public QuickActionType Type { get; }

    /// <summary>
    /// Navigation target page name (if applicable).
    /// </summary>
    public string? NavigationTarget { get; }

    /// <summary>
    /// Action name to execute after navigation (e.g., "OpenAddModal").
    /// </summary>
    public string? ActionName { get; }

    /// <summary>
    /// Entity ID for search results (used for row highlighting on navigation).
    /// </summary>
    public string? EntityId { get; }

    /// <summary>
    /// Creates a new QuickActionItem.
    /// </summary>
    public QuickActionItem(string title, string description, string iconData, QuickActionType type, string? navigationTarget = null, string? actionName = null, string? entityId = null)
    {
        Title = title;
        Description = description;
        IconData = iconData;
        Type = type;
        NavigationTarget = navigationTarget;
        ActionName = actionName;
        EntityId = entityId;
    }
}

/// <summary>
/// Types of quick actions.
/// </summary>
public enum QuickActionType
{
    /// <summary>
    /// Quick action (create, add, etc.).
    /// </summary>
    QuickAction,

    /// <summary>
    /// Navigation to a page.
    /// </summary>
    Navigation,

    /// <summary>
    /// Tools and settings.
    /// </summary>
    Tools,

    /// <summary>
    /// Data search result from CompanyData.
    /// </summary>
    SearchResult
}

/// <summary>
/// Event args for quick action execution.
/// </summary>
public class QuickActionEventArgs : EventArgs
{
    /// <summary>
    /// The navigation target page name.
    /// </summary>
    public string? NavigationTarget { get; }

    /// <summary>
    /// The action to execute after navigation.
    /// </summary>
    public string ActionName { get; }

    /// <summary>
    /// Creates a new QuickActionEventArgs.
    /// </summary>
    public QuickActionEventArgs(string? navigationTarget, string actionName)
    {
        NavigationTarget = navigationTarget;
        ActionName = actionName;
    }
}
