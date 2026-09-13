using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Rental Records page.
/// </summary>
public partial class RentalRecordsPageViewModel : SortablePageViewModelBase
{
    #region Responsive Header

    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Statistics

    [ObservableProperty]
    private int _totalRentals;

    [ObservableProperty]
    private int _activeRentals;

    [ObservableProperty]
    private int _overdueRentals;

    [ObservableProperty]
    private decimal _totalRevenue;

    public string TotalRevenueFormatted => CurrencyService.Format(TotalRevenue);

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterRecords();
        });

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterCustomer;

    [ObservableProperty]
    private string? _filterItem;

    [ObservableProperty]
    private DateTime? _filterStartDateFrom;

    [ObservableProperty]
    private DateTime? _filterStartDateTo;

    [ObservableProperty]
    private DateTime? _filterDueDateFrom;

    [ObservableProperty]
    private DateTime? _filterDueDateTo;

    #endregion

    #region Column Visibility and Widths

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public RentalRecordsTableColumnWidths ColumnWidths => App.RentalRecordsColumnWidths;

    [ObservableProperty]
    private bool _showIdColumn = ColumnVisibilityHelper.Load("RentalRecords", "Id", true);

    [ObservableProperty]
    private bool _showItemColumn = ColumnVisibilityHelper.Load("RentalRecords", "Item", true);

    [ObservableProperty]
    private bool _showCustomerColumn = ColumnVisibilityHelper.Load("RentalRecords", "Customer", true);

    [ObservableProperty]
    private bool _showQuantityColumn = ColumnVisibilityHelper.Load("RentalRecords", "Quantity", true);

    [ObservableProperty]
    private bool _showStartDateColumn = ColumnVisibilityHelper.Load("RentalRecords", "StartDate", true);

    [ObservableProperty]
    private bool _showDueDateColumn = ColumnVisibilityHelper.Load("RentalRecords", "DueDate", true);

    [ObservableProperty]
    private bool _showStatusColumn = ColumnVisibilityHelper.Load("RentalRecords", "Status", true);

    [ObservableProperty]
    private bool _showTotalColumn = ColumnVisibilityHelper.Load("RentalRecords", "Total", true);

    [ObservableProperty]
    private bool _showDepositColumn = ColumnVisibilityHelper.Load("RentalRecords", "Deposit", true);

    [ObservableProperty]
    private bool _showPaidColumn = ColumnVisibilityHelper.Load("RentalRecords", "Paid", true);

    [ObservableProperty]
    private bool _showInvoiceColumn = ColumnVisibilityHelper.Load("RentalRecords", "Invoice", true);

    partial void OnShowIdColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Id", value); ColumnVisibilityHelper.Save("RentalRecords", "Id", value); }
    partial void OnShowItemColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Item", value); ColumnVisibilityHelper.Save("RentalRecords", "Item", value); }
    partial void OnShowCustomerColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Customer", value); ColumnVisibilityHelper.Save("RentalRecords", "Customer", value); }
    partial void OnShowQuantityColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Quantity", value); ColumnVisibilityHelper.Save("RentalRecords", "Quantity", value); }
    partial void OnShowStartDateColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("StartDate", value); ColumnVisibilityHelper.Save("RentalRecords", "StartDate", value); }
    partial void OnShowDueDateColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("DueDate", value); ColumnVisibilityHelper.Save("RentalRecords", "DueDate", value); }
    partial void OnShowStatusColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Status", value); ColumnVisibilityHelper.Save("RentalRecords", "Status", value); }
    partial void OnShowTotalColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Total", value); ColumnVisibilityHelper.Save("RentalRecords", "Total", value); }
    partial void OnShowDepositColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Deposit", value); ColumnVisibilityHelper.Save("RentalRecords", "Deposit", value); }
    partial void OnShowPaidColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Paid", value); ColumnVisibilityHelper.Save("RentalRecords", "Paid", value); }
    partial void OnShowInvoiceColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Invoice", value); ColumnVisibilityHelper.Save("RentalRecords", "Invoice", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("RentalRecords");
        ShowIdColumn = true;
        ShowItemColumn = true;
        ShowCustomerColumn = true;
        ShowQuantityColumn = true;
        ShowStartDateColumn = true;
        ShowDueDateColumn = true;
        ShowStatusColumn = true;
        ShowTotalColumn = true;
        ShowDepositColumn = true;
        ShowPaidColumn = true;
        ShowInvoiceColumn = true;
    }

    #endregion

    #region Records Collection

    private readonly List<RentalRecord> _allRecords = [];

    public BatchObservableCollection<RentalRecordDisplayItem> Records { get; } = [];

    public ObservableCollection<string> StatusOptions { get; } = new(RentalStatusExtensions.GetFilterOptions());

    #endregion

    #region Pagination

    [ObservableProperty]
    private string _paginationText = "0 records";

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterRecords();

    #endregion

    #region Constructor

    public RentalRecordsPageViewModel()
    {
        // Set default sort values for rental records
        SortColumn = "StartDate";
        SortDirection = SortDirection.Descending;

        LoadRecords();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        if (App.RentalRecordsModalsViewModel != null)
        {
            App.RentalRecordsModalsViewModel.RecordSaved += OnRecordSaved;
            App.RentalRecordsModalsViewModel.RecordDeleted += OnRecordDeleted;
            App.RentalRecordsModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.RentalRecordsModalsViewModel.FiltersCleared += OnFiltersCleared;
            App.RentalRecordsModalsViewModel.RecordReturned += OnRecordReturned;
        }

        if (App.RentalInventoryModalsViewModel != null)
        {
            App.RentalInventoryModalsViewModel.RentalCreated += OnRentalCreated;
        }

        if (App.InvoiceModalsViewModel != null)
        {
            App.InvoiceModalsViewModel.InvoiceSaved += OnInvoiceSaved;
        }
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
        if (App.RentalRecordsModalsViewModel != null)
        {
            App.RentalRecordsModalsViewModel.RecordSaved -= OnRecordSaved;
            App.RentalRecordsModalsViewModel.RecordDeleted -= OnRecordDeleted;
            App.RentalRecordsModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.RentalRecordsModalsViewModel.FiltersCleared -= OnFiltersCleared;
            App.RentalRecordsModalsViewModel.RecordReturned -= OnRecordReturned;
        }
        if (App.RentalInventoryModalsViewModel != null)
            App.RentalInventoryModalsViewModel.RentalCreated -= OnRentalCreated;
        if (App.InvoiceModalsViewModel != null)
            App.InvoiceModalsViewModel.InvoiceSaved -= OnInvoiceSaved;
    }

    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.RentalRecords)
        {
            _needsRefresh = true;
            return;
        }
        LoadRecords();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.RentalRecords && _needsRefresh)
        {
            _needsRefresh = false;
            LoadRecords();
        }
    }

    private void OnRecordSaved(object? sender, EventArgs e)
    {
        LoadRecords();
    }

    private void OnRecordDeleted(object? sender, EventArgs e)
    {
        LoadRecords();
    }

    private void OnRecordReturned(object? sender, EventArgs e)
    {
        LoadRecords();
    }

    private void OnRentalCreated(object? sender, EventArgs e)
    {
        LoadRecords();
    }

    private void OnInvoiceSaved(object? sender, EventArgs e)
    {
        LoadRecords();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        var modals = App.RentalRecordsModalsViewModel;
        if (modals != null)
        {
            FilterStatus = modals.FilterStatus;
            FilterCustomer = modals.FilterCustomer;
            FilterItem = modals.FilterItem;
            FilterStartDateFrom = modals.FilterStartDateFrom?.DateTime;
            FilterStartDateTo = modals.FilterStartDateTo?.DateTime;
            FilterDueDateFrom = modals.FilterDueDateFrom?.DateTime;
            FilterDueDateTo = modals.FilterDueDateTo?.DateTime;
        }
        CurrentPage = 1;
        FilterRecords();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterStatus = "All";
        FilterCustomer = null;
        FilterItem = null;
        FilterStartDateFrom = null;
        FilterStartDateTo = null;
        FilterDueDateFrom = null;
        FilterDueDateTo = null;
        SearchQuery = null;
        CurrentPage = 1;
        FilterRecords();
    }

    #endregion

    #region Data Loading

    private void LoadRecords()
    {
        _allRecords.Clear();
        Records.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Rentals == null)
            return;

        // Update overdue status for active rentals
        foreach (var rental in companyData.Rentals.Where(r => r.Status == RentalStatus.Active))
        {
            if (rental.IsOverdue)
            {
                rental.Status = RentalStatus.Overdue;
            }
        }

        // Reset incorrectly marked overdue rentals back to active if due date is in the future
        foreach (var rental in companyData.Rentals.Where(r => r.Status == RentalStatus.Overdue))
        {
            if (DateTime.Today <= rental.DueDate.Date)
            {
                rental.Status = RentalStatus.Active;
            }
        }

        _allRecords.AddRange(companyData.Rentals);
        UpdateStatistics();
        FilterRecords();
    }

    private void UpdateStatistics()
    {
        TotalRentals = _allRecords.Count;
        ActiveRentals = _allRecords.Count(r => r.Status == RentalStatus.Active);
        OverdueRentals = _allRecords.Count(r => r.Status == RentalStatus.Overdue);
        TotalRevenue = _allRecords.Where(r => r.Status == RentalStatus.Returned).Sum(r => r.TotalCost ?? 0);
        OnPropertyChanged(nameof(TotalRevenueFormatted));
    }

    [RelayCommand]
    private void RefreshRecords()
    {
        LoadRecords();
    }

    private void FilterRecords()
    {
        IEnumerable<RentalRecord> filtered = _allRecords;
        var companyData = App.CompanyManager?.CompanyData;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, r =>
                {
                    var itemName = RentalRecordsModalsViewModel.GetItemDisplayName(r, companyData);
                    var customer = companyData?.Customers.FirstOrDefault(c => c.Id == r.CustomerId);
                    return [r.Id, itemName, customer?.Name];
                })
                .ToList();
        }

        if (FilterStatus != "All")
        {
            var statusEnum = FilterStatus switch
            {
                "Active" => RentalStatus.Active,
                "Returned" => RentalStatus.Returned,
                "Overdue" => RentalStatus.Overdue,
                "Cancelled" => RentalStatus.Cancelled,
                _ => (RentalStatus?)null
            };
            if (statusEnum.HasValue)
            {
                filtered = filtered.Where(r => r.Status == statusEnum.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(FilterCustomer) && FilterCustomer != "All Customers")
        {
            var customer = companyData?.Customers.FirstOrDefault(c => c.Name == FilterCustomer);
            if (customer != null)
            {
                filtered = filtered.Where(r => r.CustomerId == customer.Id);
            }
        }

        if (!string.IsNullOrWhiteSpace(FilterItem) && FilterItem != "All Items")
        {
            // Resolve filter item name through chain: find all RentalItems whose resolved name matches
            var matchingRentalItemIds = companyData?.RentalInventory
                .Where(ri =>
                {
                    var invItem = companyData.Inventory.FirstOrDefault(inv => inv.Id == ri.InventoryItemId);
                    var product = invItem != null ? companyData.Products.FirstOrDefault(p => p.Id == invItem.ProductId) : null;
                    return product?.Name == FilterItem;
                })
                .Select(ri => ri.Id)
                .ToHashSet() ?? [];
            filtered = filtered.Where(r => matchingRentalItemIds.Contains(r.RentalItemId));
        }

        // Apply date filters
        if (FilterStartDateFrom.HasValue)
        {
            filtered = filtered.Where(r => r.StartDate >= FilterStartDateFrom.Value);
        }
        if (FilterStartDateTo.HasValue)
        {
            filtered = filtered.Where(r => r.StartDate <= FilterStartDateTo.Value);
        }
        if (FilterDueDateFrom.HasValue)
        {
            filtered = filtered.Where(r => r.DueDate >= FilterDueDateFrom.Value);
        }
        if (FilterDueDateTo.HasValue)
        {
            filtered = filtered.Where(r => r.DueDate <= FilterDueDateTo.Value);
        }

        var displayItems = filtered.Select(record =>
        {
            var customer = companyData?.Customers.FirstOrDefault(c => c.Id == record.CustomerId);
            var accountant = !string.IsNullOrEmpty(record.AccountantId)
                ? companyData?.Accountants.FirstOrDefault(a => a.Id == record.AccountantId)
                : null;

            return new RentalRecordDisplayItem
            {
                Id = record.Id,
                AccountantName = accountant?.Name ?? "System",
                ItemName = RentalRecordsModalsViewModel.GetItemDisplayName(record, companyData),
                ItemId = record.RentalItemId,
                CustomerName = customer?.Name ?? "Unknown Customer",
                CustomerId = record.CustomerId,
                Quantity = record.Quantity,
                RateType = record.RateType.ToString(),
                RateAmount = record.RateAmount,
                SecurityDeposit = record.SecurityDeposit,
                StartDate = record.StartDate,
                DueDate = record.DueDate,
                ReturnDate = record.ReturnDate,
                Status = record.Status.ToString(),
                TotalCost = record.TotalCost ?? 0,
                DaysOverdue = record.EffectiveDaysOverdue,
                IsActive = record.Status == RentalStatus.Active || record.Status == RentalStatus.Overdue,
                Paid = record.Paid,
                HasInvoices = record.HasInvoices,
                InvoiceId = record.InvoiceIds.FirstOrDefault() ?? string.Empty,
                IsHighlighted = record.Id == HighlightTransactionId
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<RentalRecordDisplayItem, object?>>
                {
                    ["Id"] = r => r.Id,
                    ["Item"] = r => r.ItemName,
                    ["Customer"] = r => r.CustomerName,
                    ["Quantity"] = r => r.Quantity,
                    ["Rate"] = r => r.RateAmount,
                    ["StartDate"] = r => r.StartDate,
                    ["DueDate"] = r => r.DueDate,
                    ["Status"] = r => r.Status,
                    ["Total"] = r => r.TotalCost,
                    ["Deposit"] = r => r.SecurityDeposit,
                    ["Paid"] = r => r.Paid,
                    ["Invoice"] = r => r.InvoiceId
                },
                r => r.StartDate);
        }

        // Navigate to highlighted item if set (from dashboard click)
        NavigateToHighlightedItem(displayItems, x => x.Id);

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination
        var pagedRecords = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Records.ReplaceAll(pagedRecords);
    }

    protected override void UpdatePageNumbers()
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
            totalCount, CurrentPage, PageSize, TotalPages, "record");
    }

    #endregion

    #region Modal Commands

    [RelayCommand]
    private void OpenAddModal()
    {
        App.RentalRecordsModalsViewModel?.OpenAddModal();
    }

    [RelayCommand]
    private void OpenEditModal(RentalRecordDisplayItem? record)
    {
        App.RentalRecordsModalsViewModel?.OpenEditModal(record);
    }

    [RelayCommand]
    private void OpenDeleteConfirm(RentalRecordDisplayItem? record)
    {
        App.RentalRecordsModalsViewModel?.OpenDeleteConfirm(record);
    }

    [RelayCommand]
    private void OpenFilterModal()
    {
        App.RentalRecordsModalsViewModel?.OpenFilterModal();
    }

    [RelayCommand]
    private void OpenReturnModal(RentalRecordDisplayItem? record)
    {
        App.RentalRecordsModalsViewModel?.OpenReturnModal(record);
    }

    [RelayCommand]
    private void OpenViewModal(RentalRecordDisplayItem? record)
    {
        App.RentalRecordsModalsViewModel?.OpenViewModal(record);
    }

    [RelayCommand]
    private void ViewInvoice(string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId)) return;
        App.InvoiceModalsViewModel?.OpenViewInvoice(invoiceId);
    }

    [RelayCommand]
    private void GenerateInvoice(RentalRecordDisplayItem? record)
    {
        if (record == null) return;
        App.InvoiceModalsViewModel?.OpenCreateFromRental(record.Id);
    }

    [RelayCommand]
    private void MarkAsPaid(RentalRecordDisplayItem? record)
    {
        if (record == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        var rental = companyData?.Rentals.FirstOrDefault(r => r.Id == record.Id);
        if (rental == null) return;

        rental.Paid = true;
        rental.UpdatedAt = DateTime.UtcNow;
        companyData!.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Mark rental '{rental.Id}' as paid",
            () =>
            {
                rental.Paid = false;
                companyData.MarkAsModified();
                LoadRecords();
            },
            () =>
            {
                rental.Paid = true;
                companyData.MarkAsModified();
                LoadRecords();
            }));

        LoadRecords();
    }

    #endregion
}

/// <summary>
/// Display model for rental records in the UI.
/// </summary>
public partial class RentalRecordDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _accountantName = string.Empty;

    [ObservableProperty]
    private string _itemName = string.Empty;

    [ObservableProperty]
    private string _itemId = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _customerId = string.Empty;

    [ObservableProperty]
    private int _quantity;

    [ObservableProperty]
    private string _rateType = "Daily";

    [ObservableProperty]
    private decimal _rateAmount;

    [ObservableProperty]
    private decimal _securityDeposit;

    [ObservableProperty]
    private DateTime _startDate;

    [ObservableProperty]
    private DateTime _dueDate;

    [ObservableProperty]
    private DateTime? _returnDate;

    [ObservableProperty]
    private string _status = "Active";

    [ObservableProperty]
    private decimal _totalCost;

    [ObservableProperty]
    private int _daysOverdue;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _paid;

    [ObservableProperty]
    private bool _hasInvoices;

    [ObservableProperty]
    private string _invoiceId = string.Empty;

    [ObservableProperty]
    private bool _isHighlighted;

    public bool HasInvoiceId => !string.IsNullOrEmpty(InvoiceId);

    public string StartDateFormatted => StartDate.ToString("MMM d, yyyy");
    public string DueDateFormatted => DueDate.ToString("MMM d, yyyy");
    public string RateFormatted => $"{CurrencyService.Format(RateAmount)}/{RateType}";
    public string TotalCostFormatted => CurrencyService.Format(TotalCost);
    public string DepositFormatted => CurrencyService.Format(SecurityDeposit);
    public string DaysOverdueText => DaysOverdue > 0 ? $"{DaysOverdue} days" : "-";
    public bool CanGenerateInvoice => !Paid && !HasInvoices;
    public bool CanMarkAsPaid => !Paid && !IsActive;
}

/// <summary>
/// Navigation parameter for navigating to the Invoices page to create an invoice from a rental.
/// </summary>
public class RentalInvoiceNavigationParameter(string rentalRecordId)
{
    public string RentalRecordId { get; } = rentalRecordId;
}
