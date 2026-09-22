using System.Collections.ObjectModel;
using ArgoBooks.Controls;
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
public partial class ReturnsPageViewModel : SortablePageViewModelBase
{
    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public ReturnsTableColumnWidths ColumnWidths => App.ReturnsColumnWidths;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Returns", new Dictionary<string, bool>
    {
        ["Id"] = true,
        ["Product"] = true,
        ["SupplierCustomer"] = true,
        ["Date"] = true,
        ["Reason"] = true,
        ["Refund"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showIdColumn = ColumnDefaults.Load("Id");

    [ObservableProperty]
    private bool _showProductColumn = ColumnDefaults.Load("Product");

    [ObservableProperty]
    private bool _showSupplierCustomerColumn = ColumnDefaults.Load("SupplierCustomer");

    [ObservableProperty]
    private bool _showDateColumn = ColumnDefaults.Load("Date");

    [ObservableProperty]
    private bool _showReasonColumn = ColumnDefaults.Load("Reason");

    [ObservableProperty]
    private bool _showRefundColumn = ColumnDefaults.Load("Refund");

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

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterReturns();

    #endregion

    #region Constructor

    public ReturnsPageViewModel()
    {
        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        LoadReturns();

        EnableDeferredUndoRefresh(p => p == PageNames.Returns, LoadReturns);

        // Subscribe to modal events
        if (App.ReturnsModalsViewModel != null)
        {
            App.ReturnsModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.ReturnsModalsViewModel.FiltersCleared += OnFiltersCleared;
            App.ReturnsModalsViewModel.ReturnUndone += OnReturnUndone;
        }
    }

    /// <summary>
    /// Unsubscribes from app-level and singleton events so this page VM can be garbage collected when
    /// the company is switched. Called by ClearPageCaches via <see cref="ICleanupViewModel"/>.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.ReturnsModalsViewModel != null)
        {
            App.ReturnsModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.ReturnsModalsViewModel.FiltersCleared -= OnFiltersCleared;
            App.ReturnsModalsViewModel.ReturnUndone -= OnReturnUndone;
        }
    }

    protected override void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // Trigger property change notification to refresh translated titles via converters
        OnPropertyChanged(nameof(IsExpenseTabActive));
        base.OnLanguageChanged(sender, e);
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

        var displayItems = filtered.Select(CreateDisplayItem).ToList();

        displayItems = displayItems.ApplySort(
            SortColumn,
            SortDirection,
            new Dictionary<string, Func<ReturnDisplayItem, object?>>
            {
                ["Id"] = r => r.Id,
                ["Product"] = r => r.ProductNames,
                ["SupplierCustomer"] = r => r.SupplierOrCustomerName,
                ["Date"] = r => r.ReturnDate,
                ["Reason"] = r => r.Reason,
                ["Refund"] = r => r.RefundAmount
            },
            r => r.ReturnDate);

        var pagedReturns = Paginate(displayItems, "return");

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
