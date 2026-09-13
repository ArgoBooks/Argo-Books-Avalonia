using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for Locations modals (Add, Edit, Delete, Filter).
/// </summary>
public partial class LocationsModalsViewModel : ViewModelBase
{
    #region Events

    /// <summary>
    /// Raised when a location is saved (added or edited).
    /// </summary>
    public event EventHandler? LocationSaved;

    /// <summary>
    /// The Id of the location most recently created via the Add modal. Lets a caller that
    /// opened "create location" from another modal auto-select the new location after save.
    /// </summary>
    public string? LastSavedLocationId { get; private set; }

    /// <summary>
    /// Raised when a location is deleted.
    /// </summary>
    public event EventHandler? LocationDeleted;

    /// <summary>
    /// Raised when filters are applied.
    /// </summary>
    public event EventHandler<LocationsFilterAppliedEventArgs>? FiltersApplied;

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
    private string _modalName = string.Empty;

    [ObservableProperty]
    private string _modalCode = string.Empty;

    [ObservableProperty]
    private string _modalType = "Warehouse";

    [ObservableProperty]
    private string _modalStreetAddress = string.Empty;

    [ObservableProperty]
    private string _modalCity = string.Empty;

    [ObservableProperty]
    private string _modalStateProvince = string.Empty;

    [ObservableProperty]
    private string _modalPostalCode = string.Empty;

    [ObservableProperty]
    private string _modalCountry = string.Empty;

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    [ObservableProperty]
    private string? _modalNameError;

    [ObservableProperty]
    private string? _modalError;

    /// <summary>
    /// The location being edited (null for add).
    /// </summary>
    private Location? _editingLocation;

    // Original values for change detection in edit mode
    private string _originalName = string.Empty;
    private string _originalCode = string.Empty;
    private string _originalType = "Warehouse";
    private string _originalStreetAddress = string.Empty;
    private string _originalCity = string.Empty;
    private string _originalStateProvince = string.Empty;
    private string _originalPostalCode = string.Empty;
    private string _originalCountry = string.Empty;
    private string _originalNotes = string.Empty;

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasAddModalEnteredData =>
        !string.IsNullOrWhiteSpace(ModalName) ||
        !string.IsNullOrWhiteSpace(ModalCode) ||
        ModalType != "Warehouse" ||
        !string.IsNullOrWhiteSpace(ModalStreetAddress) ||
        !string.IsNullOrWhiteSpace(ModalCity) ||
        !string.IsNullOrWhiteSpace(ModalStateProvince) ||
        !string.IsNullOrWhiteSpace(ModalPostalCode) ||
        !string.IsNullOrWhiteSpace(ModalCountry) ||
        !string.IsNullOrWhiteSpace(ModalNotes);

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges =>
        ModalName != _originalName ||
        ModalCode != _originalCode ||
        ModalType != _originalType ||
        ModalStreetAddress != _originalStreetAddress ||
        ModalCity != _originalCity ||
        ModalStateProvince != _originalStateProvince ||
        ModalPostalCode != _originalPostalCode ||
        ModalCountry != _originalCountry ||
        ModalNotes != _originalNotes;

    #endregion

    #region Dropdown Options

    /// <summary>
    /// Location type options.
    /// </summary>
    public ObservableCollection<string> TypeOptions { get; } = ["Warehouse", "Storage Facility", "Factory", "Retail Store", "Distribution Center"];

    #endregion

    #region Filter State

    [ObservableProperty]
    private string _filterType = "All";

    [ObservableProperty]
    private string _filterStatus = "All";

    /// <summary>
    /// Filter type options.
    /// </summary>
    public ObservableCollection<string> FilterTypeOptions { get; } = ["All", "Warehouse", "Storage Facility", "Factory", "Retail Store", "Distribution Center"];

    /// <summary>
    /// Filter status options.
    /// </summary>
    public ObservableCollection<string> FilterStatusOptions { get; } = ["All", "Active", "Inactive"];

    // Original filter values for change detection (captured when modal opens)
    private string _originalFilterType = "All";
    private string _originalFilterStatus = "All";

    /// <summary>
    /// Returns true if any filter has been changed from the state when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterType != _originalFilterType ||
        FilterStatus != _originalFilterStatus;

    /// <summary>
    /// Captures the current filter state as original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterType = FilterType;
        _originalFilterStatus = FilterStatus;
    }

    #endregion

    #region Add Location

    /// <summary>
    /// Opens the Add Location modal.
    /// </summary>
    public void OpenAddModal()
    {
        ClearModalFields();
        IsAddModalOpen = true;
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
    /// Requests to close the Add modal, showing confirmation if data was entered.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseAddModalAsync()
    {
        if (HasAddModalEnteredData)
        {
            if (!await ConfirmDiscardNewAsync())
                return;
        }

        CloseAddModal();
    }

    /// <summary>
    /// Saves a new location.
    /// </summary>
    [RelayCommand]
    private void SaveNewLocation()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var newId = string.IsNullOrWhiteSpace(ModalCode)
            ? new Core.Data.IdGenerator(companyData).NextLocationId()
            : ModalCode.Trim().ToUpperInvariant();

        // Check for duplicate ID
        if (companyData.Locations.Any(l => l.Id == newId))
        {
            ModalError = "A location with this code already exists.".Translate();
            return;
        }

        var newLocation = new Location
        {
            Id = newId,
            Name = ModalName.Trim(),
            Address = new Address
            {
                Street = ModalStreetAddress.Trim(),
                City = ModalCity.Trim(),
                State = ModalStateProvince.Trim(),
                ZipCode = ModalPostalCode.Trim(),
                Country = ModalCountry.Trim()
            },
            ContactPerson = string.Empty,
            Phone = string.Empty,
            Capacity = 0,
            CurrentUtilization = 0,
            CreatedAt = DateTime.UtcNow
        };

        companyData.Locations.Add(newLocation);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.LocationCreated);
        companyData.MarkAsModified();

        // Record undo action
        var locationToUndo = newLocation;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add location '{newLocation.Name}'",
            () =>
            {
                companyData.Locations.Remove(locationToUndo);
                companyData.MarkAsModified();
                LocationSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Locations.Add(locationToUndo);
                companyData.MarkAsModified();
                LocationSaved?.Invoke(this, EventArgs.Empty);
            }));

        LastSavedLocationId = newLocation.Id;
        LocationSaved?.Invoke(this, EventArgs.Empty);
        CloseAddModal();
    }

    #endregion

    #region Edit Location

    /// <summary>
    /// Opens the Edit Location modal.
    /// </summary>
    public void OpenEditModal(LocationDisplayItem? item)
    {
        if (item == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        var location = companyData?.Locations.FirstOrDefault(l => l.Id == item.Id);
        if (location == null) return;

        _editingLocation = location;
        ModalName = location.Name;
        ModalCode = location.Id;
        ModalType = item.Type;
        ModalStreetAddress = location.Address.Street;
        ModalCity = location.Address.City;
        ModalStateProvince = location.Address.State;
        ModalPostalCode = location.Address.ZipCode;
        ModalCountry = location.Address.Country;
        ModalNotes = string.Empty;
        ModalNameError = null;
        ModalError = null;

        // Store original values for change detection
        _originalName = ModalName;
        _originalCode = ModalCode;
        _originalType = ModalType;
        _originalStreetAddress = ModalStreetAddress;
        _originalCity = ModalCity;
        _originalStateProvince = ModalStateProvince;
        _originalPostalCode = ModalPostalCode;
        _originalCountry = ModalCountry;
        _originalNotes = ModalNotes;

        IsEditModalOpen = true;
    }

    /// <summary>
    /// Closes the Edit modal.
    /// </summary>
    [RelayCommand]
    private void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingLocation = null;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Edit modal, showing confirmation if changes were made.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseEditModalAsync()
    {
        if (HasEditModalChanges)
        {
            if (!await ConfirmDiscardEditsAsync())
                return;
        }

        CloseEditModal();
    }

    /// <summary>
    /// Saves changes to an existing location.
    /// </summary>
    [RelayCommand]
    private void SaveEditedLocation()
    {
        if (!ValidateModal() || _editingLocation == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Store old values for undo
        var oldName = _editingLocation.Name;
        var oldAddress = new Address
        {
            Street = _editingLocation.Address.Street,
            City = _editingLocation.Address.City,
            State = _editingLocation.Address.State,
            ZipCode = _editingLocation.Address.ZipCode,
            Country = _editingLocation.Address.Country
        };

        // Store new values
        var newName = ModalName.Trim();
        var newAddress = new Address
        {
            Street = ModalStreetAddress.Trim(),
            City = ModalCity.Trim(),
            State = ModalStateProvince.Trim(),
            ZipCode = ModalPostalCode.Trim(),
            Country = ModalCountry.Trim()
        };

        // Update the location
        var locationToEdit = _editingLocation;
        var changes = new Dictionary<string, FieldChange>();
        if (oldName != newName) changes["Name"] = new FieldChange { OldValue = oldName, NewValue = newName };
        var oldAddr = $"{oldAddress.Street}, {oldAddress.City}, {oldAddress.State} {oldAddress.ZipCode}".Trim(' ', ',');
        var newAddr = $"{newAddress.Street}, {newAddress.City}, {newAddress.State} {newAddress.ZipCode}".Trim(' ', ',');
        if (oldAddr != newAddr) changes["Address"] = new FieldChange { OldValue = oldAddr, NewValue = newAddr };
        if (changes.Count > 0) App.EventLogService?.SetPendingChanges(changes);
        locationToEdit.Name = newName;
        locationToEdit.Address = newAddress;

        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit location '{newName}'",
            () =>
            {
                locationToEdit.Name = oldName;
                locationToEdit.Address = oldAddress;
                companyData.MarkAsModified();
                LocationSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                locationToEdit.Name = newName;
                locationToEdit.Address = newAddress;
                companyData.MarkAsModified();
                LocationSaved?.Invoke(this, EventArgs.Empty);
            }));

        LocationSaved?.Invoke(this, EventArgs.Empty);
        CloseEditModal();
    }

    #endregion

    #region Delete Location

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    public async void OpenDeleteConfirm(LocationDisplayItem? item)
    {
        try
        {
            if (item == null) return;

            // Check if location is in use
            var cd = App.CompanyManager?.CompanyData;
            if (cd != null)
            {
                var usages = new List<string>();
                if (cd.Inventory.Any(i => i.LocationId == item.Id))
                    usages.Add("Inventory".Translate());
                if (cd.StockTransfers.Any(t => t.SourceLocationId == item.Id || t.DestinationLocationId == item.Id))
                    usages.Add("Stock Transfer".Translate());
                if (usages.Count > 0)
                {
                    await App.ShowWarningMessageBoxAsync(
                        "Cannot Delete".Translate(),
                        "This location cannot be deleted because it is referenced by one or more: {0}.".TranslateFormat(string.Join(", ", usages)));
                    return;
                }
            }

            var dialog = App.ConfirmationDialog;
            if (dialog == null) return;

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Location".Translate(),
                Message = "Are you sure you want to delete this location?\n\n{0}".TranslateFormat(item.Name),
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;

            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null)
                return;

            var location = companyData.Locations.FirstOrDefault(l => l.Id == item.Id);
            if (location != null)
            {
                var deletedLocation = location;
                companyData.Locations.Remove(location);
                companyData.MarkAsModified();

                App.UndoRedoManager.RecordAction(new DelegateAction(
                    $"Delete location '{deletedLocation.Name}'",
                    () =>
                    {
                        companyData.Locations.Add(deletedLocation);
                        companyData.MarkAsModified();
                        LocationDeleted?.Invoke(this, EventArgs.Empty);
                    },
                    () =>
                    {
                        companyData.Locations.Remove(deletedLocation);
                        companyData.MarkAsModified();
                        LocationDeleted?.Invoke(this, EventArgs.Empty);
                    }));
            }

            LocationDeleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Location.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    public void OpenFilterModal()
    {
        CaptureOriginalFilterValues();
        IsFilterModalOpen = true;
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
    /// Requests to close the filter modal, showing confirmation if filters have been changed.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (HasFilterModalChanges)
        {
            if (!await ConfirmDiscardFiltersAsync())
                return;

            // Restore filter values to the state when modal was opened
            FilterType = _originalFilterType;
            FilterStatus = _originalFilterStatus;
        }

        CloseFilterModal();
    }

    /// <summary>
    /// Applies the current filters and closes the modal.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        FiltersApplied?.Invoke(this, new LocationsFilterAppliedEventArgs(FilterType, FilterStatus));
        CloseFilterModal();
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        ResetFilterDefaults();
        CloseFilterModal();
    }

    /// <summary>
    /// Resets filter values to their defaults.
    /// </summary>
    private void ResetFilterDefaults()
    {
        FilterType = "All";
        FilterStatus = "All";
    }

    #endregion

    #region Property Changed Handlers

    partial void OnModalNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ModalNameError = null;
        }
    }

    #endregion

    #region Modal Helpers

    private void ClearModalFields()
    {
        ModalName = string.Empty;
        ModalCode = string.Empty;
        ModalType = "Warehouse";
        ModalStreetAddress = string.Empty;
        ModalCity = string.Empty;
        ModalStateProvince = string.Empty;
        ModalPostalCode = string.Empty;
        ModalCountry = string.Empty;
        ModalNotes = string.Empty;
        ModalNameError = null;
        ModalError = null;
    }

    private bool ValidateModal()
    {
        ModalNameError = null;
        ModalError = null;
        var isValid = true;

        // Validate name (required)
        if (string.IsNullOrWhiteSpace(ModalName))
        {
            ModalNameError = "Location name is required.".Translate();
            isValid = false;
        }

        return isValid;
    }

    #endregion
}

/// <summary>
/// Event args for filter applied events.
/// </summary>
public class LocationsFilterAppliedEventArgs(string type, string status) : EventArgs
{
    public string Type { get; } = type;
    public string Status { get; } = status;
}
