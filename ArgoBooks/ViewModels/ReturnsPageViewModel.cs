using System.Collections.ObjectModel;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Returns page displaying expense and customer returns.
/// </summary>
public partial class ReturnsPageViewModel : ViewModelBase, ICleanupViewModel
{
    #region Responsive Header

    /// <summary>
    /// Helper for responsive header layout calculations.
    /// </summary>
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public ReturnsTableColumnWidths ColumnWidths => App.ReturnsColumnWidths;

    [ObservableProperty]
    private bool _isColumnMenuOpen;

    [ObservableProperty]
    private bool _showIdColumn = ColumnVisibilityHelper.Load("Returns", "Id", true);

    [ObservableProperty]
    private bool _showProductColumn = ColumnVisibilityHelper.Load("Returns", "Product", true);

    [ObservableProperty]
    private bool _showSupplierCustomerColumn = ColumnVisibilityHelper.Load("Returns", "SupplierCustomer", true);

    [ObservableProperty]
    private bool _showDateColumn = ColumnVisibilityHelper.Load("Returns", "Date", true);

    [ObservableProperty]
    private bool _showReasonColumn = ColumnVisibilityHelper.Load("Returns", "Reason", true);

    [ObservableProperty]
    private bool _showRefundColumn = ColumnVisibilityHelper.Load("Returns", "Refund", true);

    partial void OnShowIdColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Id", value); ColumnVisibilityHelper.Save("Returns", "Id", value); }
    partial void OnShowProductColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Product", value); ColumnVisibilityHelper.Save("Returns", "Product", value); }
    partial void OnShowSupplierCustomerColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("SupplierCustomer", value); ColumnVisibilityHelper.Save("Returns", "SupplierCustomer", value); }
    partial void OnShowDateColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Date", value); ColumnVisibilityHelper.Save("Returns", "Date", value); }
    partial void OnShowReasonColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Reason", value); ColumnVisibilityHelper.Save("Returns", "Reason", value); }
    partial void OnShowRefundColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Refund", value); ColumnVisibilityHelper.Save("Returns", "Refund", value); }

    [RelayCommand]
    private void ToggleColumnMenu()
    {
        IsColumnMenuOpen = !IsColumnMenuOpen;
    }

    [RelayCommand]
    private void CloseColumnMenu()
    {
        IsColumnMenuOpen = false;
    }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Returns");
        ShowIdColumn = true;
        ShowProductColumn = true;
        ShowSupplierCustomerColumn = true;
        ShowDateColumn = true;
        ShowReasonColumn = true;
        ShowRefundColumn = true;
    }

    #endregion

    #region Statistics

    [ObservableProperty]
    private int _totalReturns;

    [ObservableProperty]
    private int _expenseReturns;

    [ObservableProperty]
    private int _customerReturns;

    [ObservableProperty]
    private string _totalRefunded = "$0.00";

    #endregion

    #region Tabs

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        CurrentPage = 1;
        FilterReturns();
    }

    public bool IsExpenseTabActive => SelectedTabIndex == 0;

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterReturns();
        });

    #endregion

    #region Returns Collection

    private readonly List<Return> _allReturns = [];

    public BatchObservableCollection<ReturnDisplayItem> Returns { get; } = [];

    #endregion

    #region Pagination

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _pageSize = 10;

    public ObservableCollection<int> PageSizeOptions { get; } = [5, 10, 15, 25, 50];

    partial void OnPageSizeChanged(int value)
    {
        CurrentPage = 1;
        FilterReturns();
    }

    [ObservableProperty]
    private string _paginationText = "0 returns";

    public ObservableCollection<int> PageNumbers { get; } = [];

    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        FilterReturns();
    }

    [RelayCommand]
    private void GoToPreviousPage()
    {
        if (CanGoToPreviousPage)
            CurrentPage--;
    }

    [RelayCommand]
    private void GoToNextPage()
    {
        if (CanGoToNextPage)
            CurrentPage++;
    }

    [RelayCommand]
    private void GoToPage(int page)
    {
        if (page >= 1 && page <= TotalPages)
            CurrentPage = page;
    }

    #endregion

    #region Constructor

    public ReturnsPageViewModel()
    {
        ColumnVisibilityHelper.SyncToManager(this);
        LoadReturns();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        // Subscribe to modal events
        if (App.ReturnsModalsViewModel != null)
        {
            App.ReturnsModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.ReturnsModalsViewModel.FiltersCleared += OnFiltersCleared;
            App.ReturnsModalsViewModel.ReturnUndone += OnReturnUndone;
        }

        // Subscribe to language changes to refresh translated content
        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Unsubscribes from app-level and singleton events so this page VM can be garbage collected when
    /// the company is switched. Called by ClearPageCaches via <see cref="ICleanupViewModel"/>.
    /// </summary>
    public void Cleanup()
    {
        CancelPendingSearch();
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
        if (App.ReturnsModalsViewModel != null)
        {
            App.ReturnsModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.ReturnsModalsViewModel.FiltersCleared -= OnFiltersCleared;
            App.ReturnsModalsViewModel.ReturnUndone -= OnReturnUndone;
        }
        LanguageService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // Trigger property change notification to refresh translated titles via converters
        OnPropertyChanged(nameof(IsExpenseTabActive));
        FilterReturns();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        CurrentPage = 1;
        FilterReturns();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        SearchQuery = null;
        CurrentPage = 1;
        FilterReturns();
    }

    private void OnReturnUndone(object? sender, EventArgs e)
    {
        LoadReturns();
    }

    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.Returns)
        {
            _needsRefresh = true;
            return;
        }
        LoadReturns();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.Returns && _needsRefresh)
        {
            _needsRefresh = false;
            LoadReturns();
        }
    }

    #endregion

    #region Data Loading

    private void LoadReturns()
    {
        _allReturns.Clear();
        Returns.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Returns == null)
            return;

        _allReturns.AddRange(companyData.Returns);
        UpdateStatistics();
        FilterReturns();
    }

    private void UpdateStatistics()
    {
        TotalReturns = _allReturns.Count;
        ExpenseReturns = _allReturns.Count(r => r.ReturnType == "Expense");
        CustomerReturns = _allReturns.Count(r => r.ReturnType == "Customer");
        var totalRefundedValue = _allReturns.Sum(r => r.NetRefund);
        TotalRefunded = CurrencyService.Format(totalRefundedValue);
    }

    [RelayCommand]
    private void RefreshReturns()
    {
        LoadReturns();
    }

    private void FilterReturns()
    {
        IEnumerable<Return> filtered = _allReturns;

        // Get filter values from modals view model
        var modals = App.ReturnsModalsViewModel;
        var filterReason = modals?.FilterReason ?? "All";
        var filterDateFrom = modals?.FilterDateFrom;
        var filterDateTo = modals?.FilterDateTo;

        // Filter by tab (expense vs customer)
        var returnType = IsExpenseTabActive ? "Expense" : "Customer";
        filtered = filtered.Where(r => r.ReturnType == returnType);

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered.RankBySearch(SearchQuery, r => [r.Id, r.OriginalTransactionId, GetProductNames(r), GetSupplierOrCustomerName(r)]);
        }

        if (filterReason != "All")
        {
            filtered = filtered.Where(r =>
                r.Items.Any(item => item.Reason.Equals(filterReason, StringComparison.OrdinalIgnoreCase))
            );
        }

        if (filterDateFrom.HasValue)
        {
            filtered = filtered.Where(r => r.ReturnDate >= filterDateFrom.Value.DateTime);
        }
        if (filterDateTo.HasValue)
        {
            filtered = filtered.Where(r => r.ReturnDate <= filterDateTo.Value.DateTime);
        }

        // Sort by date descending (newest first), materialize here for display + pagination
        var displayItems = filtered.OrderByDescending(r => r.ReturnDate)
            .Select(CreateDisplayItem).ToList();

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination and add to collection
        var pagedReturns = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Returns.ReplaceAll(pagedReturns);
    }

    private ReturnDisplayItem CreateDisplayItem(Return returnRecord)
    {
        var productNames = GetProductNames(returnRecord);
        var supplierOrCustomerName = GetSupplierOrCustomerName(returnRecord);
        var processedByName = GetProcessedByName(returnRecord);
        var reason = returnRecord.Items.FirstOrDefault()?.Reason ?? "Not specified";

        return new ReturnDisplayItem
        {
            Id = returnRecord.Id,
            OriginalTransactionId = returnRecord.OriginalTransactionId,
            ReturnType = returnRecord.ReturnType,
            ProductNames = productNames,
            SupplierOrCustomerName = supplierOrCustomerName,
            ReturnDate = returnRecord.ReturnDate,
            Reason = reason,
            ProcessedBy = processedByName,
            RefundAmount = returnRecord.NetRefund,
            Notes = returnRecord.Notes,
            ItemCount = returnRecord.Items.Sum(i => i.Quantity)
        };
    }

    private string GetProductNames(Return returnRecord)
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return "Unknown";

        var productNames = returnRecord.Items
            .Select(item => companyData.GetProduct(item.ProductId)?.Name ?? "Unknown Product")
            .Distinct()
            .ToList();

        return productNames.Count > 2
            ? $"{productNames[0]}, {productNames[1]} +{productNames.Count - 2} more"
            : string.Join(", ", productNames);
    }

    private string GetSupplierOrCustomerName(Return returnRecord)
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return "Unknown";

        if (returnRecord.ReturnType == "Expense")
        {
            // For expense returns, look up the supplier from the original purchase
            var purchase = companyData.Expenses.FirstOrDefault(p => p.Id == returnRecord.OriginalTransactionId);
            if (purchase != null)
            {
                var supplier = companyData.GetSupplier(purchase.SupplierId ?? "");
                // Fall back to expense description if supplier not found
                return supplier?.Name ?? (string.IsNullOrEmpty(purchase.Description) ? "-" : purchase.Description);
            }
            return "-";
        }
        else
        {
            // For customer returns, look up the customer
            var customer = companyData.GetCustomer(returnRecord.CustomerId);
            return customer?.Name ?? "Unknown Customer";
        }
    }

    private string GetProcessedByName(Return returnRecord)
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return "Unknown";

        var accountant = companyData.GetAccountant(returnRecord.ProcessedBy ?? "");
        return accountant?.Name ?? returnRecord.ProcessedBy ?? "Unknown";
    }

    private void UpdatePageNumbers()
    {
        PageNumbers.Clear();
        var startPage = Math.Max(1, CurrentPage - 2);
        var endPage = Math.Min(TotalPages, startPage + 4);
        startPage = Math.Max(1, endPage - 4);

        for (var i = startPage; i <= endPage; i++)
        {
            PageNumbers.Add(i);
        }
    }

    private void UpdatePaginationText(int totalCount)
    {
        PaginationText = PaginationTextHelper.FormatPaginationText(
            totalCount, CurrentPage, PageSize, TotalPages, "return");
    }

    #endregion

    #region Filter Modal Commands

    [RelayCommand]
    private void OpenFilterModal()
    {
        App.ReturnsModalsViewModel?.OpenFilterModal();
    }

    #endregion

    #region Action Commands

    [RelayCommand]
    private void ViewReturnDetails(ReturnDisplayItem? item)
    {
        if (item == null) return;

        App.ReturnsModalsViewModel?.OpenViewDetailsModal(
            item.Id,
            item.ProductNames,
            item.DateFormatted,
            item.RefundAmountFormatted,
            item.Reason,
            item.Notes);
    }

    [RelayCommand]
    private void UndoReturn(ReturnDisplayItem? item)
    {
        if (item == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        var returnRecord = companyData?.Returns.FirstOrDefault(r => r.Id == item.Id);
        if (returnRecord != null)
        {
            App.ReturnsModalsViewModel?.OpenUndoReturnModal(returnRecord, $"{item.Id} - {item.ProductNames}");
        }
    }

    #endregion
}

/// <summary>
/// Display model for returns in the UI.
/// </summary>
public partial class ReturnDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _originalTransactionId = string.Empty;

    [ObservableProperty]
    private string _returnType = string.Empty;

    [ObservableProperty]
    private string _productNames = string.Empty;

    [ObservableProperty]
    private string _supplierOrCustomerName = string.Empty;

    [ObservableProperty]
    private DateTime _returnDate;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string _processedBy = string.Empty;

    [ObservableProperty]
    private decimal _refundAmount;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private int _itemCount;

    // Computed properties for display
    public string DateFormatted => ReturnDate.ToString("MMM d, yyyy");
    public string RefundAmountFormatted => CurrencyService.Format(RefundAmount);
}
