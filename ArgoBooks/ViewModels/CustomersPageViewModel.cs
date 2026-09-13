using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Shared.Telemetry;

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

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    [ObservableProperty]
    private bool _showCustomerColumn = ColumnVisibilityHelper.Load("Customers", "Customer", true);

    [ObservableProperty]
    private bool _showEmailColumn = ColumnVisibilityHelper.Load("Customers", "Email", true);

    [ObservableProperty]
    private bool _showPhoneColumn = ColumnVisibilityHelper.Load("Customers", "Phone", true);

    [ObservableProperty]
    private bool _showAddressColumn = ColumnVisibilityHelper.Load("Customers", "Address", true);

    [ObservableProperty]
    private bool _showCountryColumn = ColumnVisibilityHelper.Load("Customers", "Country", true);

    partial void OnShowCustomerColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Customer", value); ColumnVisibilityHelper.Save("Customers", "Customer", value); }
    partial void OnShowEmailColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Email", value); ColumnVisibilityHelper.Save("Customers", "Email", value); }
    partial void OnShowPhoneColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Phone", value); ColumnVisibilityHelper.Save("Customers", "Phone", value); }
    partial void OnShowAddressColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Address", value); ColumnVisibilityHelper.Save("Customers", "Address", value); }
    partial void OnShowCountryColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Country", value); ColumnVisibilityHelper.Save("Customers", "Country", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Customers");
        ShowCustomerColumn = true;
        ShowEmailColumn = true;
        ShowPhoneColumn = true;
        ShowAddressColumn = true;
        ShowCountryColumn = true;
    }

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
    private string _filterCustomerStatus = "All";

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

    /// <summary>
    /// Customer status options for filter.
    /// </summary>
    public ObservableCollection<string> CustomerStatusOptions { get; } = ["All", "Active", "Inactive", "Banned"];

    #endregion

    #region Pagination

    [ObservableProperty]
    private string _paginationText = "0 customers";

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterCustomers();

    #endregion

    #region Modal State

    [ObservableProperty]
    private bool _isAddModalOpen;

    [ObservableProperty]
    private bool _isEditModalOpen;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private string _modalFirstName = string.Empty;

    [ObservableProperty]
    private string _modalLastName = string.Empty;

    [ObservableProperty]
    private string _modalEmail = string.Empty;

    [ObservableProperty]
    private string _modalPhone = string.Empty;

    [ObservableProperty]
    private string _modalStreetAddress = string.Empty;

    [ObservableProperty]
    private string _modalCity = string.Empty;

    [ObservableProperty]
    private string _modalStateProvince = string.Empty;

    [ObservableProperty]
    private string _modalZipCode = string.Empty;

    [ObservableProperty]
    private string _modalCountry = string.Empty;

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    [ObservableProperty]
    private string _modalStatus = "Active";

    [ObservableProperty]
    private string? _modalFirstNameError;

    [ObservableProperty]
    private string? _modalEmailError;

    /// <summary>
    /// The customer being edited (null for add).
    /// </summary>
    private Customer? _editingCustomer;

    /// <summary>
    /// The customer being deleted.
    /// </summary>
    private CustomerDisplayItem? _deletingCustomer;

    #endregion

    #region Dropdown Options

    /// <summary>
    /// Status options for edit modal.
    /// </summary>
    public ObservableCollection<string> StatusOptions { get; } = ["Active", "Inactive", "Banned"];

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public CustomersPageViewModel()
    {
        LoadCustomers();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

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
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
        if (App.CustomerModalsViewModel != null)
        {
            App.CustomerModalsViewModel.CustomerSaved -= OnCustomerSaved;
            App.CustomerModalsViewModel.CustomerDeleted -= OnCustomerDeleted;
            App.CustomerModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.CustomerModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    /// <summary>
    /// Handles undo/redo state changes by refreshing the customers.
    /// </summary>
    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.Customers)
        {
            _needsRefresh = true;
            return;
        }
        LoadCustomers();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.Customers && _needsRefresh)
        {
            _needsRefresh = false;
            LoadCustomers();
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
    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        // Copy filter values from shared ViewModel
        var modals = App.CustomerModalsViewModel;
        if (modals != null)
        {
            FilterCustomerStatus = modals.FilterCustomerStatus;
            FilterLastRentalFrom = modals.FilterLastRentalFrom;
            FilterLastRentalTo = modals.FilterLastRentalTo;
        }
        CurrentPage = 1;
        FilterCustomers();
    }

    /// <summary>
    /// Handles filters cleared event from modals.
    /// </summary>
    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterCustomerStatus = "All";
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
    /// Refreshes the customers from the data source.
    /// </summary>
    [RelayCommand]
    private void RefreshCustomers()
    {
        LoadCustomers();
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
                .Select(c => new
                {
                    Customer = c,
                    NameScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, c.Name),
                    EmailScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, c.Email),
                    PhoneScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, c.Phone),
                    IdScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, c.Id)
                })
                .Where(x => x.NameScore >= 0 || x.EmailScore >= 0 || x.PhoneScore >= 0 || x.IdScore >= 0)
                .OrderByDescending(x => Math.Max(Math.Max(x.NameScore, x.EmailScore), Math.Max(x.PhoneScore, x.IdScore)))
                .Select(x => x.Customer)
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

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination and add to collection
        var pagedCustomers = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Customers.ReplaceAll(pagedCustomers);
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
            totalCount, CurrentPage, PageSize, TotalPages, "customer");
    }

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

    /// <summary>
    /// Closes the Add modal.
    /// </summary>
    [RelayCommand]
    private void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearModalFields();
    }

    /// <summary>
    /// Saves a new customer.
    /// </summary>
    [RelayCommand]
    private void SaveNewCustomer()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Generate new ID
        companyData.IdCounters.Customer++;
        var newId = $"CUS-{companyData.IdCounters.Customer:D3}";

        var newCustomer = new Customer
        {
            Id = newId,
            Name = $"{ModalFirstName.Trim()} {ModalLastName.Trim()}".Trim(),
            Email = ModalEmail.Trim(),
            Phone = ModalPhone.Trim(),
            Address = new Address
            {
                Street = ModalStreetAddress.Trim(),
                City = ModalCity.Trim(),
                State = ModalStateProvince.Trim(),
                ZipCode = ModalZipCode.Trim(),
                Country = ModalCountry.Trim()
            },
            Notes = ModalNotes.Trim(),
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        companyData.Customers.Add(newCustomer);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.CustomerCreated);
        companyData.MarkAsModified();

        // Record undo action
        var customerToUndo = newCustomer;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add customer '{newCustomer.Name}'",
            () =>
            {
                companyData.Customers.Remove(customerToUndo);
                companyData.MarkAsModified();
                LoadCustomers();
            },
            () =>
            {
                companyData.Customers.Add(customerToUndo);
                companyData.MarkAsModified();
                LoadCustomers();
            }));

        // Reload and close
        LoadCustomers();
        CloseAddModal();
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

    /// <summary>
    /// Closes the Edit modal.
    /// </summary>
    [RelayCommand]
    private void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingCustomer = null;
        ClearModalFields();
    }

    /// <summary>
    /// Saves changes to an existing customer.
    /// </summary>
    [RelayCommand]
    private void SaveEditedCustomer()
    {
        if (!ValidateModal() || _editingCustomer == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Store old values for undo
        var oldName = _editingCustomer.Name;
        var oldEmail = _editingCustomer.Email;
        var oldPhone = _editingCustomer.Phone;
        var oldAddress = new Address
        {
            Street = _editingCustomer.Address.Street,
            City = _editingCustomer.Address.City,
            State = _editingCustomer.Address.State,
            ZipCode = _editingCustomer.Address.ZipCode,
            Country = _editingCustomer.Address.Country
        };
        var oldNotes = _editingCustomer.Notes;
        var oldStatus = _editingCustomer.Status;

        // Store new values
        var newName = $"{ModalFirstName.Trim()} {ModalLastName.Trim()}".Trim();
        var newEmail = ModalEmail.Trim();
        var newPhone = ModalPhone.Trim();
        var newAddress = new Address
        {
            Street = ModalStreetAddress.Trim(),
            City = ModalCity.Trim(),
            State = ModalStateProvince.Trim(),
            ZipCode = ModalZipCode.Trim(),
            Country = ModalCountry.Trim()
        };
        var newNotes = ModalNotes.Trim();
        var newStatus = ModalStatus switch
        {
            "Active" => EntityStatus.Active,
            "Inactive" => EntityStatus.Inactive,
            "Banned" => EntityStatus.Archived,
            _ => EntityStatus.Active
        };

        // Update the customer
        var customerToEdit = _editingCustomer;
        customerToEdit.Name = newName;
        customerToEdit.Email = newEmail;
        customerToEdit.Phone = newPhone;
        customerToEdit.Address = newAddress;
        customerToEdit.Notes = newNotes;
        customerToEdit.Status = newStatus;
        customerToEdit.UpdatedAt = DateTime.UtcNow;

        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit customer '{newName}'",
            () =>
            {
                customerToEdit.Name = oldName;
                customerToEdit.Email = oldEmail;
                customerToEdit.Phone = oldPhone;
                customerToEdit.Address = oldAddress;
                customerToEdit.Notes = oldNotes;
                customerToEdit.Status = oldStatus;
                companyData.MarkAsModified();
                LoadCustomers();
            },
            () =>
            {
                customerToEdit.Name = newName;
                customerToEdit.Email = newEmail;
                customerToEdit.Phone = newPhone;
                customerToEdit.Address = newAddress;
                customerToEdit.Notes = newNotes;
                customerToEdit.Status = newStatus;
                companyData.MarkAsModified();
                LoadCustomers();
            }));

        // Reload and close
        LoadCustomers();
        CloseEditModal();
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

    /// <summary>
    /// Closes the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
        _deletingCustomer = null;
    }

    /// <summary>
    /// Confirms and deletes the customer.
    /// </summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        if (_deletingCustomer == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var customer = companyData.Customers.FirstOrDefault(c => c.Id == _deletingCustomer.Id);
        if (customer != null)
        {
            var deletedCustomer = customer;
            companyData.Customers.Remove(customer);
            companyData.MarkAsModified();

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Delete customer '{deletedCustomer.Name}'",
                () =>
                {
                    companyData.Customers.Add(deletedCustomer);
                    companyData.MarkAsModified();
                    LoadCustomers();
                },
                () =>
                {
                    companyData.Customers.Remove(deletedCustomer);
                    companyData.MarkAsModified();
                    LoadCustomers();
                }));
        }

        LoadCustomers();
        CloseDeleteConfirm();
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

    /// <summary>
    /// Closes the filter modal.
    /// </summary>
    [RelayCommand]
    private void CloseFilterModal()
    {
        IsFilterModalOpen = false;
    }

    /// <summary>
    /// Applies the current filters and closes the modal.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        CurrentPage = 1;
        FilterCustomers();
        CloseFilterModal();
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        FilterCustomerStatus = "All";
        FilterLastRentalFrom = null;
        FilterLastRentalTo = null;
        SearchQuery = null;
        CurrentPage = 1;
        FilterCustomers();
        CloseFilterModal();
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

    #region Modal Helpers

    private void ClearModalFields()
    {
        ModalFirstName = string.Empty;
        ModalLastName = string.Empty;
        ModalEmail = string.Empty;
        ModalPhone = string.Empty;
        ModalStreetAddress = string.Empty;
        ModalCity = string.Empty;
        ModalStateProvince = string.Empty;
        ModalZipCode = string.Empty;
        ModalCountry = string.Empty;
        ModalNotes = string.Empty;
        ModalStatus = "Active";
        ClearModalErrors();
    }

    private void ClearModalErrors()
    {
        ModalFirstNameError = null;
        ModalEmailError = null;
    }

    private bool ValidateModal()
    {
        ClearModalErrors();
        var isValid = true;

        // Validate first name (required)
        if (string.IsNullOrWhiteSpace(ModalFirstName))
        {
            ModalFirstNameError = "First name is required.".Translate();
            isValid = false;
        }

        // Validate email format if provided
        if (!string.IsNullOrWhiteSpace(ModalEmail))
        {
            if (!ModalEmail.Contains('@') || !ModalEmail.Contains('.'))
            {
                ModalEmailError = "Please enter a valid email address.".Translate();
                isValid = false;
            }
        }

        return isValid;
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
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            if (parts is [{ Length: >= 2 }])
                return parts[0][..2].ToUpperInvariant();
            if (parts is [{ Length: 1 }])
                return parts[0].ToUpperInvariant();
            return "?";
        }
    }

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

