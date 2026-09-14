using System.Globalization;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Customers page.
/// </summary>
public partial class CustomersPageViewModel : SortablePageViewModelBase
{
    #region Statistics

    [ObservableProperty]
    private int _totalCustomers;

    [ObservableProperty]
    private int _activeCustomers;

    [ObservableProperty]
    private int _bannedCustomers;

    [ObservableProperty]
    private int _newThisMonth;

    #endregion

    #region Responsive Header

    /// <summary>
    /// Helper for responsive header layout.
    /// </summary>
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public CustomersTableColumnWidths ColumnWidths => App.CustomersColumnWidths;

    #endregion

    #region Column Visibility

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Customers", new Dictionary<string, bool>
    {
        ["Customer"] = true,
        ["Email"] = true,
        ["Phone"] = true,
        ["Address"] = true,
        ["Country"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showCustomerColumn = ColumnDefaults.Load("Customer");

    [ObservableProperty]
    private bool _showEmailColumn = ColumnDefaults.Load("Email");

    [ObservableProperty]
    private bool _showPhoneColumn = ColumnDefaults.Load("Phone");

    [ObservableProperty]
    private bool _showAddressColumn = ColumnDefaults.Load("Address");

    [ObservableProperty]
    private bool _showCountryColumn = ColumnDefaults.Load("Country");

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterCustomers();
        });

    [ObservableProperty]
    private string _filterPaymentStatus = "All";

    [ObservableProperty]
    private string _filterCustomerStatus = "All";

    [ObservableProperty]
    private string _filterCountry = "All";

    /// <summary>Outstanding balance bounds, compared in USD.</summary>
    [ObservableProperty]
    private string? _filterOutstandingMin;

    [ObservableProperty]
    private string? _filterOutstandingMax;

    [ObservableProperty]
    private DateTime? _filterLastRentalFrom;

    [ObservableProperty]
    private DateTime? _filterLastRentalTo;

    #endregion

    #region Customers Collection

    /// <summary>
    /// All customers (unfiltered).
    /// </summary>
    private readonly List<Customer> _allCustomers = [];

    /// <summary>
    /// Customers for display in the table.
    /// </summary>
    public BatchObservableCollection<CustomerDisplayItem> Customers { get; } = [];

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterCustomers();

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public CustomersPageViewModel()
    {
        LoadCustomers();

        EnableDeferredUndoRefresh(p => p == PageNames.Customers, LoadCustomers);

        // Subscribe to customer modal events to refresh data
        if (App.CustomerModalsViewModel != null)
        {
            App.CustomerModalsViewModel.CustomerSaved += OnCustomerSaved;
            App.CustomerModalsViewModel.CustomerDeleted += OnCustomerDeleted;
            App.CustomerModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.CustomerModalsViewModel.FiltersCleared += OnFiltersCleared;
        }
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.CustomerModalsViewModel != null)
        {
            App.CustomerModalsViewModel.CustomerSaved -= OnCustomerSaved;
            App.CustomerModalsViewModel.CustomerDeleted -= OnCustomerDeleted;
            App.CustomerModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.CustomerModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    /// <summary>
    /// Handles customer saved event from modals.
    /// </summary>
    private void OnCustomerSaved(object? sender, EventArgs e)
    {
        LoadCustomers();
    }

    /// <summary>
    /// Handles customer deleted event from modals.
    /// </summary>
    private void OnCustomerDeleted(object? sender, EventArgs e)
    {
        LoadCustomers();
    }

    /// <summary>
    /// Handles filters applied event from modals.
    /// </summary>
    internal void OnFiltersApplied(object? sender, EventArgs e)
    {
        if (sender is CustomerModalsViewModel modals)
        {
            FilterPaymentStatus = modals.FilterPaymentStatus;
            FilterCustomerStatus = modals.FilterCustomerStatus;
            FilterCountry = modals.FilterCountry;
            FilterOutstandingMin = modals.FilterOutstandingMin;
            FilterOutstandingMax = modals.FilterOutstandingMax;
            FilterLastRentalFrom = modals.FilterLastRentalFrom;
            FilterLastRentalTo = modals.FilterLastRentalTo;
        }
        CurrentPage = 1;
        FilterCustomers();
    }

    /// <summary>
    /// Handles filters cleared event from modals.
    /// </summary>
    internal void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterPaymentStatus = "All";
        FilterCustomerStatus = "All";
        FilterCountry = "All";
        FilterOutstandingMin = null;
        FilterOutstandingMax = null;
        FilterLastRentalFrom = null;
        FilterLastRentalTo = null;
        SearchQuery = null;
        CurrentPage = 1;
        FilterCustomers();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads customers from the company data.
    /// </summary>
    private void LoadCustomers()
    {
        _allCustomers.Clear();
        Customers.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Customers == null)
            return;

        _allCustomers.AddRange(companyData.Customers);
        UpdateStatistics();
        FilterCustomers();
    }

    /// <summary>
    /// Updates the statistics based on current data.
    /// </summary>
    private void UpdateStatistics()
    {
        TotalCustomers = _allCustomers.Count;
        ActiveCustomers = _allCustomers.Count(c => c.Status == EntityStatus.Active);
        BannedCustomers = _allCustomers.Count(c => c.Status == EntityStatus.Archived);
        var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        NewThisMonth = _allCustomers.Count(c => c.CreatedAt >= monthStart);
    }

    /// <summary>
    /// Filters customers based on search query and filters.
    /// </summary>
    private void FilterCustomers()
    {
        IEnumerable<Customer> filtered = _allCustomers;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, c => [c.Name, c.Email, c.Phone, c.Id])
                .ToList();
        }

        if (FilterCustomerStatus != "All")
        {
            var status = FilterCustomerStatus switch
            {
                "Active" => EntityStatus.Active,
                "Inactive" => EntityStatus.Inactive,
                "Banned" => EntityStatus.Archived,
                _ => EntityStatus.Active
            };
            filtered = filtered.Where(c => c.Status == status);
        }

        // Apply last rental date filter
        if (FilterLastRentalFrom.HasValue)
        {
            filtered = filtered.Where(c => c.LastTransactionDate >= FilterLastRentalFrom.Value);
        }
        if (FilterLastRentalTo.HasValue)
        {
            filtered = filtered.Where(c => c.LastTransactionDate <= FilterLastRentalTo.Value);
        }

        if (FilterCountry != "All")
        {
            var country = Countries.NormalizeCountryOrKeep(FilterCountry);
            filtered = filtered.Where(c => string.Equals(
                Countries.NormalizeCountryOrKeep(c.Address.Country), country, StringComparison.OrdinalIgnoreCase));
        }

        var outstandingMin = ParseAmount(FilterOutstandingMin);
        var outstandingMax = ParseAmount(FilterOutstandingMax);
        if (FilterPaymentStatus != "All" || outstandingMin.HasValue || outstandingMax.HasValue)
        {
            var standings = PaymentStandings(App.CompanyManager?.CompanyData?.Invoices ?? [], DateTime.Today);
            var paymentStatus = FilterPaymentStatus;
            filtered = filtered.Where(c =>
            {
                var standing = standings.GetValueOrDefault(c.Id);
                return (paymentStatus == "All" || PaymentStatusOf(standing) == paymentStatus)
                       && (!outstandingMin.HasValue || standing.OutstandingUSD >= outstandingMin.Value)
                       && (!outstandingMax.HasValue || standing.OutstandingUSD <= outstandingMax.Value);
            });
        }

        var displayItems = filtered.Select(customer =>
        {
            var addressParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(customer.Address.Street))
                addressParts.Add(customer.Address.Street);
            if (!string.IsNullOrWhiteSpace(customer.Address.City))
                addressParts.Add(customer.Address.City);
            if (!string.IsNullOrWhiteSpace(customer.Address.State))
                addressParts.Add(customer.Address.State);
            var addressString = addressParts.Count > 0 ? string.Join(", ", addressParts) : "-";

            var avatarBitmap = AvatarBitmapLoader.LoadCustomer(customer);

            return new CustomerDisplayItem
            {
                Id = customer.Id,
                Name = customer.Name,
                Email = string.IsNullOrWhiteSpace(customer.Email) ? "-" : customer.Email,
                Phone = string.IsNullOrWhiteSpace(customer.Phone) ? "-" : customer.Phone,
                Address = addressString,
                Country = string.IsNullOrWhiteSpace(customer.Address.Country) ? "-" : customer.Address.Country,
                Status = customer.Status,
                IsHighlighted = customer.Id == HighlightTransactionId,
                AvatarBitmap = avatarBitmap,
                HasAvatar = avatarBitmap != null
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<CustomerDisplayItem, object?>>
                {
                    ["Name"] = c => c.Name,
                    ["Email"] = c => c.Email,
                    ["Phone"] = c => c.Phone,
                    ["Address"] = c => c.Address
                },
                c => c.Name);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        var pagedCustomers = Paginate(displayItems, "customer");

        Customers.ReplaceAll(pagedCustomers);
    }

    /// <summary>What a customer owes on open invoices, in USD, and how many days late the oldest overdue one is.</summary>
    internal readonly record struct PaymentStanding(decimal OutstandingUSD, int DaysPastDue);

    /// <summary>Beyond the AR aging report's last bucket (90+ days).</summary>
    internal const int DelinquentAfterDays = 90;

    /// <summary>
    /// Standings by customer id, over the invoices the Invoices page counts as outstanding: drafts
    /// were never sent, and paid or cancelled invoices are settled.
    /// </summary>
    internal static Dictionary<string, PaymentStanding> PaymentStandings(IEnumerable<Invoice> invoices, DateTime today) =>
        invoices
            .Where(i => i.Status is not (InvoiceStatus.Draft or InvoiceStatus.Paid or InvoiceStatus.Cancelled))
            .GroupBy(i => i.CustomerId)
            .ToDictionary(g => g.Key, g => new PaymentStanding(
                g.Sum(i => i.EffectiveBalanceUSD),
                g.Where(i => i.IsOverdue || i.Status == InvoiceStatus.Overdue)
                    .Select(i => Math.Max(1, (today.Date - i.DueDate.Date).Days))
                    .DefaultIfEmpty(0)
                    .Max()));

    /// <summary>
    /// The payment status filter's tiers, which don't overlap: Current has nothing overdue, Overdue is
    /// up to 90 days late, Delinquent is later than that.
    /// </summary>
    internal static string PaymentStatusOf(PaymentStanding standing) => standing.DaysPastDue switch
    {
        > DelinquentAfterDays => "Delinquent",
        > 0 => "Overdue",
        _ => "Current"
    };

    private static decimal? ParseAmount(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    #endregion

    #region Add Customer

    /// <summary>
    /// Opens the Add Customer modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.CustomerModalsViewModel?.OpenAddModal();
    }

    #endregion

    #region Edit Customer

    /// <summary>
    /// Opens the Edit Customer modal.
    /// </summary>
    [RelayCommand]
    private void OpenEditModal(CustomerDisplayItem? item)
    {
        App.CustomerModalsViewModel?.OpenEditModal(item);
    }

    #endregion

    #region Delete Customer

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(CustomerDisplayItem? item)
    {
        App.CustomerModalsViewModel?.OpenDeleteConfirm(item);
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    [RelayCommand]
    private void OpenFilterModal()
    {
        App.CustomerModalsViewModel?.OpenFilterModal();
    }

    #endregion

    #region Customer History Modal

    /// <summary>
    /// Opens the customer history modal.
    /// </summary>
    [RelayCommand]
    private void OpenHistoryModal(CustomerDisplayItem? item)
    {
        App.CustomerModalsViewModel?.OpenHistoryModal(item);
    }

    #endregion
}

/// <summary>
/// Display model for customers in the UI.
/// </summary>
public partial class CustomerDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _country = string.Empty;

    [ObservableProperty]
    private EntityStatus _status = EntityStatus.Active;

    [ObservableProperty]
    private Bitmap? _avatarBitmap;

    [ObservableProperty]
    private bool _hasAvatar;

    /// <summary>
    /// Gets the initials from the customer name for avatar display.
    /// </summary>
    public string Initials => Helpers.InitialsHelper.From(Name);

    [ObservableProperty]
    private bool _isHighlighted;
}

/// <summary>
/// Display model for customer transaction history.
/// </summary>
public class CustomerHistoryItem
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;

    public string DateFormatted => Date.ToString("MMM d, yyyy");
    public string AmountFormatted => Amount < 0 ? $"-{CurrencyService.Format(Math.Abs(Amount))}" : CurrencyService.Format(Amount);
}

