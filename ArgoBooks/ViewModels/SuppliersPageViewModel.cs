using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Data;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Suppliers page.
/// </summary>
public partial class SuppliersPageViewModel : SortablePageViewModelBase
{
    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public SuppliersTableColumnWidths ColumnWidths => App.SuppliersColumnWidths;

    #endregion

    #region Column Visibility

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnVisibilityHelper.Load("Suppliers", "Supplier", true);

    [ObservableProperty]
    private bool _showEmailColumn = ColumnVisibilityHelper.Load("Suppliers", "Email", true);

    [ObservableProperty]
    private bool _showPhoneColumn = ColumnVisibilityHelper.Load("Suppliers", "Phone", true);

    [ObservableProperty]
    private bool _showAddressColumn = ColumnVisibilityHelper.Load("Suppliers", "Address", true);

    [ObservableProperty]
    private bool _showCountryColumn = ColumnVisibilityHelper.Load("Suppliers", "Country", true);

    [ObservableProperty]
    private bool _showProductsColumn = ColumnVisibilityHelper.Load("Suppliers", "Products", true);

    partial void OnShowSupplierColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Supplier", value); ColumnVisibilityHelper.Save("Suppliers", "Supplier", value); }
    partial void OnShowEmailColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Email", value); ColumnVisibilityHelper.Save("Suppliers", "Email", value); }
    partial void OnShowPhoneColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Phone", value); ColumnVisibilityHelper.Save("Suppliers", "Phone", value); }
    partial void OnShowAddressColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Address", value); ColumnVisibilityHelper.Save("Suppliers", "Address", value); }
    partial void OnShowCountryColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Country", value); ColumnVisibilityHelper.Save("Suppliers", "Country", value); }
    partial void OnShowProductsColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Products", value); ColumnVisibilityHelper.Save("Suppliers", "Products", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Suppliers");
        ShowSupplierColumn = true;
        ShowEmailColumn = true;
        ShowPhoneColumn = true;
        ShowAddressColumn = true;
        ShowCountryColumn = true;
        ShowProductsColumn = true;
    }

    #endregion

    #region Responsive Header

    /// <summary>
    /// Helper for responsive header layout.
    /// </summary>
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterSuppliers();
        });

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterCountry;

    #endregion

    #region Pagination

    [ObservableProperty]
    private string _paginationText = "0 suppliers";

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterSuppliers();

    /// <summary>
    /// Updates the pagination text to display item count.
    /// </summary>
    private void UpdatePaginationText(int totalItems)
    {
        PaginationText = PaginationTextHelper.FormatSimpleCount(totalItems, "supplier");
    }

    #endregion

    #region Statistics

    [ObservableProperty]
    private int _totalSuppliers;

    [ObservableProperty]
    private int _activeSuppliers;

    [ObservableProperty]
    private int _totalCountries;

    [ObservableProperty]
    private int _totalProductsSupplied;

    #endregion

    #region Suppliers Collection

    /// <summary>
    /// All suppliers (unfiltered).
    /// </summary>
    private readonly List<Supplier> _allSuppliers = [];

    /// <summary>
    /// Filtered suppliers for display.
    /// </summary>
    public BatchObservableCollection<SupplierDisplayItem> Suppliers { get; } = [];

    /// <summary>
    /// Available countries for filter dropdown.
    /// </summary>
    public ObservableCollection<string> AvailableCountries { get; } = [];

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
    private string _modalSupplierName = string.Empty;

    [ObservableProperty]
    private string _modalContactPerson = string.Empty;

    [ObservableProperty]
    private string _modalEmail = string.Empty;

    [ObservableProperty]
    private string _modalFullPhone = string.Empty;

    [ObservableProperty]
    private string _modalStreet = string.Empty;

    [ObservableProperty]
    private string _modalCity = string.Empty;

    [ObservableProperty]
    private string _modalState = string.Empty;

    [ObservableProperty]
    private string _modalZipCode = string.Empty;

    [ObservableProperty]
    private string _modalCountry = string.Empty;

    [ObservableProperty]
    private string _modalWebsite = string.Empty;

    [ObservableProperty]
    private string _modalPaymentTerms = string.Empty;

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    [ObservableProperty]
    private bool _modalIsActive = true;

    [ObservableProperty]
    private string? _modalError;

    [ObservableProperty]
    private string? _modalSupplierNameError;

    [ObservableProperty]
    private string? _modalEmailError;

    [ObservableProperty]
    private string? _modalPhoneError;

    /// <summary>
    /// The supplier being edited (null for add).
    /// </summary>
    private Supplier? _editingSupplier;

    /// <summary>
    /// The supplier being deleted.
    /// </summary>
    private SupplierDisplayItem? _deletingSupplier;

    #endregion

    #region Dropdown Options

    /// <summary>
    /// All countries for dropdown (from shared Countries data).
    /// </summary>
    public IReadOnlyList<string> CountryOptions { get; } = Countries.Names;

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public SuppliersPageViewModel()
    {
        LoadSuppliers();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        // Subscribe to shared modal events to refresh data
        if (App.SupplierModalsViewModel != null)
        {
            App.SupplierModalsViewModel.SupplierSaved += OnSupplierModalClosed;
            App.SupplierModalsViewModel.SupplierDeleted += OnSupplierModalClosed;
            App.SupplierModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.SupplierModalsViewModel.FiltersCleared += OnFiltersCleared;
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
        if (App.SupplierModalsViewModel != null)
        {
            App.SupplierModalsViewModel.SupplierSaved -= OnSupplierModalClosed;
            App.SupplierModalsViewModel.SupplierDeleted -= OnSupplierModalClosed;
            App.SupplierModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.SupplierModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    /// <summary>
    /// Handles supplier modal closed events by refreshing the suppliers.
    /// </summary>
    private void OnSupplierModalClosed(object? sender, EventArgs e)
    {
        LoadSuppliers();
    }

    /// <summary>
    /// Handles filters applied event from shared modal.
    /// </summary>
    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        if (App.SupplierModalsViewModel != null)
        {
            FilterCountry = App.SupplierModalsViewModel.FilterCountry == "All" ? null : App.SupplierModalsViewModel.FilterCountry;
            FilterStatus = App.SupplierModalsViewModel.FilterStatus;
        }
        FilterSuppliers();
    }

    /// <summary>
    /// Handles filters cleared event from shared modal.
    /// </summary>
    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterCountry = null;
        FilterStatus = "All";
        SearchQuery = null;
        FilterSuppliers();
    }

    /// <summary>
    /// Handles undo/redo state changes by refreshing the suppliers.
    /// </summary>
    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.Suppliers)
        {
            _needsRefresh = true;
            return;
        }
        LoadSuppliers();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.Suppliers && _needsRefresh)
        {
            _needsRefresh = false;
            LoadSuppliers();
        }
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads suppliers from the company data.
    /// </summary>
    private void LoadSuppliers()
    {
        _allSuppliers.Clear();
        Suppliers.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Suppliers == null)
            return;

        _allSuppliers.AddRange(companyData.Suppliers);
        UpdateStatistics();
        UpdateAvailableCountries();
        FilterSuppliers();
    }

    /// <summary>
    /// Updates the statistics based on current data.
    /// </summary>
    private void UpdateStatistics()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        TotalSuppliers = _allSuppliers.Count;

        // Count active suppliers (those with at least one product or used in purchases)
        var suppliersWithProducts = companyData.Products
            .Where(p => !string.IsNullOrEmpty(p.SupplierId))
            .Select(p => p.SupplierId)
            .Distinct()
            .ToHashSet();

        ActiveSuppliers = _allSuppliers.Count(s => suppliersWithProducts.Contains(s.Id));

        // Count unique countries
        TotalCountries = _allSuppliers
            .Select(s => s.Address.Country)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        // Count products supplied
        TotalProductsSupplied = companyData.Products
            .Count(p => !string.IsNullOrEmpty(p.SupplierId));
    }

    /// <summary>
    /// Updates the list of available countries for filtering.
    /// </summary>
    private void UpdateAvailableCountries()
    {
        AvailableCountries.Clear();
        AvailableCountries.Add("All Countries");

        var countries = _allSuppliers
            .Select(s => s.Address.Country)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c);

        foreach (var country in countries)
        {
            AvailableCountries.Add(country);
        }
    }

    /// <summary>
    /// Refreshes the suppliers from the data source.
    /// </summary>
    [RelayCommand]
    private void RefreshSuppliers()
    {
        LoadSuppliers();
    }

    /// <summary>
    /// Filters suppliers based on search query and filters.
    /// </summary>
    private void FilterSuppliers()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var filtered = _allSuppliers.AsEnumerable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .Select(s => new
                {
                    Supplier = s,
                    NameScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, s.Name),
                    EmailScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, s.Email),
                    ContactScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, s.ContactPerson)
                })
                .Where(x => x.NameScore >= 0 || x.EmailScore >= 0 || x.ContactScore >= 0)
                .OrderByDescending(x => Math.Max(Math.Max(x.NameScore, x.EmailScore), x.ContactScore))
                .Select(x => x.Supplier);
        }

        if (!string.IsNullOrWhiteSpace(FilterCountry) && FilterCountry != "All Countries")
        {
            filtered = filtered.Where(s =>
                s.Address.Country.Equals(FilterCountry, StringComparison.OrdinalIgnoreCase));
        }

        if (FilterStatus != "All")
        {
            var suppliersWithProducts = companyData.Products
                .Where(p => !string.IsNullOrEmpty(p.SupplierId))
                .Select(p => p.SupplierId)
                .Distinct()
                .ToHashSet();

            filtered = FilterStatus == "Active"
                ? filtered.Where(s => suppliersWithProducts.Contains(s.Id))
                : filtered.Where(s => !suppliersWithProducts.Contains(s.Id));
        }

        // Pre-build product count lookup for O(1) access per supplier
        var productCountBySupplier = companyData.Products
            .Where(p => !string.IsNullOrEmpty(p.SupplierId))
            .GroupBy(p => p.SupplierId!)
            .ToDictionary(g => g.Key, g => g.Count());

        // Convert to a list and create display items with additional computed properties
        var displayItems = filtered.Select(supplier =>
        {
            var productCount = productCountBySupplier.GetValueOrDefault(supplier.Id);

            // Format address as comma-separated parts
            var addressParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(supplier.Address.Street))
                addressParts.Add(supplier.Address.Street);
            if (!string.IsNullOrWhiteSpace(supplier.Address.City))
                addressParts.Add(supplier.Address.City);
            if (!string.IsNullOrWhiteSpace(supplier.Address.State))
                addressParts.Add(supplier.Address.State);
            var addressString = addressParts.Count > 0 ? string.Join(", ", addressParts) : "-";

            var avatarBitmap = AvatarBitmapLoader.LoadSupplier(supplier);

            return new SupplierDisplayItem
            {
                Id = supplier.Id,
                Name = supplier.Name,
                ContactPerson = supplier.ContactPerson,
                Email = string.IsNullOrWhiteSpace(supplier.Email) ? "-" : supplier.Email,
                Phone = string.IsNullOrWhiteSpace(supplier.Phone) ? "-" : supplier.Phone,
                Address = addressString,
                Country = string.IsNullOrWhiteSpace(supplier.Address.Country) ? "-" : supplier.Address.Country,
                ProductCount = productCount,
                Initials = GetInitials(supplier.Name),
                AvatarBitmap = avatarBitmap,
                HasAvatar = avatarBitmap != null,
                IsHighlighted = supplier.Id == HighlightTransactionId
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<SupplierDisplayItem, object?>>
                {
                    ["Name"] = s => s.Name,
                    ["Email"] = s => s.Email,
                    ["Phone"] = s => s.Phone,
                    ["Address"] = s => s.Address,
                    ["Country"] = s => s.Country,
                    ["Products"] = s => s.ProductCount
                },
                s => s.Name);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        // Calculate pagination
        var totalItems = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)PageSize));

        // Ensure current page is valid
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;
        if (CurrentPage < 1)
            CurrentPage = 1;

        UpdatePageNumbers();
        UpdatePaginationText(totalItems);
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));

        // Apply pagination
        var pagedItems = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        // Replace all items in collection
        Suppliers.ReplaceAll(pagedItems);
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
            return $"{words[0][0]}{words[1][0]}".ToUpperInvariant();

        return name.Length >= 2
            ? name[..2].ToUpperInvariant()
            : name.ToUpperInvariant();
    }

    #endregion

    #region Add Supplier

    /// <summary>
    /// Opens the Add Supplier modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.SupplierModalsViewModel?.OpenAddModal();
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
    /// Saves a new supplier.
    /// </summary>
    [RelayCommand]
    private void SaveNewSupplier()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Generate new ID
        companyData.IdCounters.Supplier++;
        var newId = $"SUP-{companyData.IdCounters.Supplier:D3}";

        var newSupplier = new Supplier
        {
            Id = newId,
            Name = ModalSupplierName.Trim(),
            ContactPerson = ModalContactPerson.Trim(),
            Email = ModalEmail.Trim(),
            Phone = ModalFullPhone.Trim(),
            Address = new Address
            {
                Street = ModalStreet.Trim(),
                City = ModalCity.Trim(),
                State = ModalState.Trim(),
                ZipCode = ModalZipCode.Trim(),
                Country = ModalCountry.Trim()
            },
            Website = (string.IsNullOrWhiteSpace(ModalWebsite) ? null : ModalWebsite.Trim()) ?? "",
            PaymentTerms = ModalPaymentTerms.Trim(),
            Notes = ModalNotes.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        companyData.Suppliers.Add(newSupplier);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.SupplierCreated);
        companyData.MarkAsModified();

        // Record undo action
        var supplierToUndo = newSupplier;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add supplier '{newSupplier.Name}'",
            () =>
            {
                companyData.Suppliers.Remove(supplierToUndo);
                companyData.MarkAsModified();
                LoadSuppliers();
            },
            () =>
            {
                companyData.Suppliers.Add(supplierToUndo);
                companyData.MarkAsModified();
                LoadSuppliers();
            }));

        // Reload and close
        LoadSuppliers();
        CloseAddModal();
    }

    #endregion

    #region Edit Supplier

    /// <summary>
    /// Opens the Edit Supplier modal.
    /// </summary>
    [RelayCommand]
    private void OpenEditModal(SupplierDisplayItem? item)
    {
        App.SupplierModalsViewModel?.OpenEditModal(item);
    }

    /// <summary>
    /// Closes the Edit modal.
    /// </summary>
    [RelayCommand]
    private void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingSupplier = null;
        ClearModalFields();
    }

    /// <summary>
    /// Saves changes to an existing supplier.
    /// </summary>
    [RelayCommand]
    private void SaveEditedSupplier()
    {
        if (!ValidateModal() || _editingSupplier == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Store old values for undo
        var oldName = _editingSupplier.Name;
        var oldContactPerson = _editingSupplier.ContactPerson;
        var oldEmail = _editingSupplier.Email;
        var oldPhone = _editingSupplier.Phone;
        var oldAddress = new Address
        {
            Street = _editingSupplier.Address.Street,
            City = _editingSupplier.Address.City,
            State = _editingSupplier.Address.State,
            ZipCode = _editingSupplier.Address.ZipCode,
            Country = _editingSupplier.Address.Country
        };
        var oldWebsite = _editingSupplier.Website;
        var oldPaymentTerms = _editingSupplier.PaymentTerms;
        var oldNotes = _editingSupplier.Notes;

        // Store new values
        var newName = ModalSupplierName.Trim();
        var newContactPerson = ModalContactPerson.Trim();
        var newEmail = ModalEmail.Trim();
        var newPhone = ModalFullPhone.Trim();
        var newAddress = new Address
        {
            Street = ModalStreet.Trim(),
            City = ModalCity.Trim(),
            State = ModalState.Trim(),
            ZipCode = ModalZipCode.Trim(),
            Country = ModalCountry.Trim()
        };
        var newWebsite = (string.IsNullOrWhiteSpace(ModalWebsite) ? null : ModalWebsite.Trim()) ?? "";
        var newPaymentTerms = ModalPaymentTerms.Trim();
        var newNotes = ModalNotes.Trim();

        // Update the supplier
        var supplierToEdit = _editingSupplier;
        supplierToEdit.Name = newName;
        supplierToEdit.ContactPerson = newContactPerson;
        supplierToEdit.Email = newEmail;
        supplierToEdit.Phone = newPhone;
        supplierToEdit.Address = newAddress;
        supplierToEdit.Website = newWebsite;
        supplierToEdit.PaymentTerms = newPaymentTerms;
        supplierToEdit.Notes = newNotes;
        supplierToEdit.UpdatedAt = DateTime.UtcNow;

        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit supplier '{newName}'",
            () =>
            {
                supplierToEdit.Name = oldName;
                supplierToEdit.ContactPerson = oldContactPerson;
                supplierToEdit.Email = oldEmail;
                supplierToEdit.Phone = oldPhone;
                supplierToEdit.Address = oldAddress;
                supplierToEdit.Website = oldWebsite;
                supplierToEdit.PaymentTerms = oldPaymentTerms;
                supplierToEdit.Notes = oldNotes;
                companyData.MarkAsModified();
                LoadSuppliers();
            },
            () =>
            {
                supplierToEdit.Name = newName;
                supplierToEdit.ContactPerson = newContactPerson;
                supplierToEdit.Email = newEmail;
                supplierToEdit.Phone = newPhone;
                supplierToEdit.Address = newAddress;
                supplierToEdit.Website = newWebsite;
                supplierToEdit.PaymentTerms = newPaymentTerms;
                supplierToEdit.Notes = newNotes;
                companyData.MarkAsModified();
                LoadSuppliers();
            }));

        // Reload and close
        LoadSuppliers();
        CloseEditModal();
    }

    #endregion

    #region Delete Supplier

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(SupplierDisplayItem? item)
    {
        App.SupplierModalsViewModel?.OpenDeleteConfirm(item);
    }

    /// <summary>
    /// Closes the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
        _deletingSupplier = null;
    }

    /// <summary>
    /// Confirms and deletes the supplier.
    /// </summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        if (_deletingSupplier == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var supplier = companyData.Suppliers.FirstOrDefault(s => s.Id == _deletingSupplier.Id);
        if (supplier != null)
        {
            // Clear supplier references from products
            var affectedProducts = companyData.Products
                .Where(p => p.SupplierId == supplier.Id)
                .ToList();

            foreach (var product in affectedProducts)
            {
                product.SupplierId = null;
            }

            var deletedSupplier = supplier;
            var productSupplierMappings = affectedProducts.ToDictionary(p => p.Id, _ => supplier.Id);

            companyData.Suppliers.Remove(supplier);
            companyData.MarkAsModified();

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Delete supplier '{deletedSupplier.Name}'",
                () =>
                {
                    // Undo: restore supplier and product references
                    companyData.Suppliers.Add(deletedSupplier);
                    foreach (var kvp in productSupplierMappings)
                    {
                        var product = companyData.Products.FirstOrDefault(p => p.Id == kvp.Key);
                        product?.SupplierId = kvp.Value;
                    }
                    companyData.MarkAsModified();
                    LoadSuppliers();
                },
                () =>
                {
                    // Redo: delete again
                    foreach (var kvp in productSupplierMappings)
                    {
                        var product = companyData.Products.FirstOrDefault(p => p.Id == kvp.Key);
                        product?.SupplierId = null;
                    }
                    companyData.Suppliers.Remove(deletedSupplier);
                    companyData.MarkAsModified();
                    LoadSuppliers();
                }));
        }

        LoadSuppliers();
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
        App.SupplierModalsViewModel?.OpenFilterModal();
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
        FilterSuppliers();
        CloseFilterModal();
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        FilterStatus = "All";
        FilterCountry = null;
        SearchQuery = null;
        FilterSuppliers();
        CloseFilterModal();
    }

    #endregion

    #region Modal Helpers

    private void ClearModalFields()
    {
        ModalSupplierName = string.Empty;
        ModalContactPerson = string.Empty;
        ModalEmail = string.Empty;
        ModalFullPhone = string.Empty;
        ModalStreet = string.Empty;
        ModalCity = string.Empty;
        ModalState = string.Empty;
        ModalZipCode = string.Empty;
        ModalCountry = string.Empty;
        ModalWebsite = string.Empty;
        ModalPaymentTerms = string.Empty;
        ModalNotes = string.Empty;
        ModalIsActive = true;
        ModalError = null;
        ModalSupplierNameError = null;
        ModalEmailError = null;
        ModalPhoneError = null;
    }

    private bool ValidateModal()
    {
        // Clear all errors first
        ModalError = null;
        ModalSupplierNameError = null;
        ModalEmailError = null;
        ModalPhoneError = null;

        var isValid = true;

        // Validate supplier name (required)
        if (string.IsNullOrWhiteSpace(ModalSupplierName))
        {
            ModalSupplierNameError = "Supplier name is required.".Translate();
            isValid = false;
        }
        else
        {
            // Check for duplicate names
            var existingWithSameName = _allSuppliers.Any(s =>
                s.Name.Equals(ModalSupplierName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (_editingSupplier == null || s.Id != _editingSupplier.Id));

            if (existingWithSameName)
            {
                ModalSupplierNameError = "A supplier with this name already exists.".Translate();
                isValid = false;
            }
        }

        // Validate email format if provided
        if (!string.IsNullOrWhiteSpace(ModalEmail) &&
            !System.Text.RegularExpressions.Regex.IsMatch(ModalEmail.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ModalEmailError = "Please enter a valid email address.".Translate();
            isValid = false;
        }

        // Validate phone number if provided
        if (!string.IsNullOrWhiteSpace(ModalFullPhone))
        {
            var digits = new string(ModalFullPhone.Where(char.IsDigit).ToArray());
            if (digits.Length < 7)
            {
                ModalPhoneError = "Please enter a valid phone number.".Translate();
                isValid = false;
            }
        }

        return isValid;
    }

    #endregion
}

/// <summary>
/// Display model for suppliers in the UI.
/// </summary>
public partial class SupplierDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _contactPerson = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _country = string.Empty;

    [ObservableProperty]
    private int _productCount;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private Bitmap? _avatarBitmap;

    [ObservableProperty]
    private bool _hasAvatar;

    [ObservableProperty]
    private bool _isHighlighted;
}
