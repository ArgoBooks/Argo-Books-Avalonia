using ArgoBooks.Controls;
using ArgoBooks.Services;
using ArgoBooks.Localization;
using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for customer modals, shared between CustomersPage and AppShell.
/// </summary>
public partial class CustomerModalsViewModel : ViewModelBase
{
    #region Modal State

    [ObservableProperty]
    private bool _isAddModalOpen;

    [ObservableProperty]
    private bool _isEditModalOpen;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private bool _isHistoryModalOpen;

    [ObservableProperty]
    private bool _isHistoryFilterModalOpen;

    /// <summary>
    /// Whether the footer's "enter all required fields" line shows. Derived rather than
    /// stored: every check in ValidateModal sets one of the field errors, so a stored copy
    /// only added a second thing to remember to clear, and clearing a field error left the
    /// footer behind until the next Save.
    /// </summary>
    public bool HasValidationMessage =>
        ModalIdError != null || ModalFirstNameError != null
        || ModalEmailError != null || ModalPhoneError != null;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private string _modalId = string.Empty;

    [ObservableProperty]
    private string _modalFirstName = string.Empty;

    [ObservableProperty]
    private string _modalLastName = string.Empty;

    [ObservableProperty]
    private string _modalCompanyName = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalIdError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalFirstNameError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalEmailError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalPhoneError;

    partial void OnModalIdChanged(string value)
    {
        ModalIdError = null;
    }

    [ObservableProperty]
    private Bitmap? _modalAvatarSource;

    [ObservableProperty]
    private bool _hasModalAvatar;

    /// <summary>
    /// Path of an avatar image picked in the modal that should be applied on save.
    /// Null if the user did not pick a new image during this session.
    /// </summary>
    private string? _pendingAvatarSourcePath;

    /// <summary>
    /// True when the user clicked Remove on an existing avatar; on save the customer's
    /// avatar file should be deleted.
    /// </summary>
    private bool _shouldRemoveAvatarOnSave;

    /// <summary>
    /// Snapshot of whether the customer had an avatar when the edit modal was opened,
    /// used for change detection.
    /// </summary>
    private bool _originalHasAvatar;

    /// <summary>
    /// Live preview of the initials that would be shown if no avatar is set,
    /// driven from ModalFirstName + ModalLastName so the avatar circle in the
    /// modal updates as the user types.
    /// </summary>
    public string ModalInitialsPreview
    {
        get
        {
            var first = ModalFirstName?.Trim() ?? string.Empty;
            var last = ModalLastName?.Trim() ?? string.Empty;
            if (first.Length > 0 && last.Length > 0)
                return $"{char.ToUpperInvariant(first[0])}{char.ToUpperInvariant(last[0])}";
            if (first.Length >= 2)
                return first[..2].ToUpperInvariant();
            if (first.Length == 1)
                return first.ToUpperInvariant();
            if (last.Length >= 2)
                return last[..2].ToUpperInvariant();
            if (last.Length == 1)
                return last.ToUpperInvariant();
            return "?";
        }
    }

    partial void OnModalFirstNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ModalFirstNameError = null;
        }
        OnPropertyChanged(nameof(ModalInitialsPreview));
    }

    partial void OnModalLastNameChanged(string value)
    {
        OnPropertyChanged(nameof(ModalInitialsPreview));
    }

    partial void OnModalPhoneChanged(string value)
    {
        ModalPhoneError = null;
    }

    partial void OnModalEmailChanged(string value)
    {
        // Clear error when email is modified (validation will happen on save)
        ModalEmailError = null;
    }

    /// <summary>
    /// The customer being edited (null for add).
    /// </summary>
    private Customer? _editingCustomer;

    /// <summary>
    /// The customer whose history is being viewed.
    /// </summary>
    private CustomerDisplayItem? _historyCustomer;

    // Original values for change detection in edit mode
    private string _originalId = string.Empty;
    private string _originalFirstName = string.Empty;
    private string _originalLastName = string.Empty;
    private string _originalCompanyName = string.Empty;
    private string _originalEmail = string.Empty;
    private string _originalPhone = string.Empty;
    private string _originalStreetAddress = string.Empty;
    private string _originalCity = string.Empty;
    private string _originalStateProvince = string.Empty;
    private string _originalZipCode = string.Empty;
    private string _originalCountry = string.Empty;
    private string _originalNotes = string.Empty;

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasAddModalEnteredData =>
        !string.IsNullOrWhiteSpace(ModalFirstName) ||
        !string.IsNullOrWhiteSpace(ModalLastName) ||
        !string.IsNullOrWhiteSpace(ModalCompanyName) ||
        !string.IsNullOrWhiteSpace(ModalEmail) ||
        !string.IsNullOrWhiteSpace(ModalPhone) ||
        !string.IsNullOrWhiteSpace(ModalStreetAddress) ||
        !string.IsNullOrWhiteSpace(ModalCity) ||
        !string.IsNullOrWhiteSpace(ModalStateProvince) ||
        !string.IsNullOrWhiteSpace(ModalZipCode) ||
        !string.IsNullOrWhiteSpace(ModalCountry) ||
        !string.IsNullOrWhiteSpace(ModalNotes) ||
        HasModalAvatar;

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges =>
        ModalId.Trim() != _originalId ||
        ModalFirstName != _originalFirstName ||
        ModalLastName != _originalLastName ||
        ModalCompanyName != _originalCompanyName ||
        ModalEmail != _originalEmail ||
        ModalPhone != _originalPhone ||
        ModalStreetAddress != _originalStreetAddress ||
        ModalCity != _originalCity ||
        ModalStateProvince != _originalStateProvince ||
        ModalZipCode != _originalZipCode ||
        ModalCountry != _originalCountry ||
        ModalNotes != _originalNotes ||
        _pendingAvatarSourcePath != null ||
        _shouldRemoveAvatarOnSave;

    #endregion

    #region Filter Fields

    [ObservableProperty]
    private string _filterPaymentStatus = "All";

    [ObservableProperty]
    private string _filterCustomerStatus = "All";

    [ObservableProperty]
    private string _filterCountry = "All";

    [ObservableProperty]
    private string? _filterOutstandingMin;

    [ObservableProperty]
    private string? _filterOutstandingMax;

    [ObservableProperty]
    private DateTime? _filterLastRentalFrom;

    [ObservableProperty]
    private DateTime? _filterLastRentalTo;

    // Original filter values for change detection (captured when modal opens)
    private string _originalFilterPaymentStatus = "All";
    private string _originalFilterCustomerStatus = "All";
    private string _originalFilterCountry = "All";
    private string? _originalFilterOutstandingMin;
    private string? _originalFilterOutstandingMax;
    private DateTime? _originalFilterLastRentalFrom;
    private DateTime? _originalFilterLastRentalTo;

    #endregion

    #region Customer History

    [ObservableProperty]
    private string _historyCustomerName = string.Empty;

    public ObservableCollection<CustomerHistoryItem> CustomerHistory { get; } = [];

    [ObservableProperty]
    private string _historyFilterType = "All";

    [ObservableProperty]
    private string _historyFilterStatus = "All";

    [ObservableProperty]
    private DateTime? _historyFilterDateFrom;

    [ObservableProperty]
    private DateTime? _historyFilterDateTo;

    [ObservableProperty]
    private string? _historyFilterAmountMin;

    [ObservableProperty]
    private string? _historyFilterAmountMax;

    public ObservableCollection<string> HistoryTypeOptions { get; } = ["All", "Rental", "Purchase", "Return", "Payment"];
    public ObservableCollection<string> HistoryStatusOptions { get; } = ["All", "Completed", "Pending", "Overdue", "Refunded"];

    #endregion

    #region Dropdown Options

    public ObservableCollection<string> StatusOptions { get; } = ["Active", "Inactive", "Banned"];
    public ObservableCollection<string> PaymentStatusOptions { get; } = ["All", "Current", "Overdue", "Delinquent"];
    public ObservableCollection<string> CustomerStatusOptions { get; } = ["All", "Active", "Inactive", "Banned"];
    public ObservableCollection<string> CountryOptions { get; } = ["All"];

    #endregion

    #region Events

    /// <summary>
    /// Fired when a customer is saved (added or edited).
    /// </summary>
    public event EventHandler? CustomerSaved;

    /// <summary>
    /// The Id of the customer most recently created via the Add modal. Lets a caller that
    /// opened "create customer" from another modal auto-select the new customer after save.
    /// </summary>
    public string? LastSavedCustomerId { get; private set; }

    /// <summary>
    /// Fired when a customer is deleted.
    /// </summary>
    public event EventHandler? CustomerDeleted;

    /// <summary>
    /// Fired when filters are applied.
    /// </summary>
    public event EventHandler? FiltersApplied;

    /// <summary>
    /// Fired when filters are cleared.
    /// </summary>
    public event EventHandler? FiltersCleared;

    /// <summary>
    /// Fired when the user clicks the avatar in the modal to pick a new image.
    /// App.axaml.cs subscribes and shows the OS file picker.
    /// </summary>
    public event EventHandler? BrowseAvatarRequested;

    #endregion

    #region Avatar Commands

    [RelayCommand]
    private void BrowseAvatar()
    {
        BrowseAvatarRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveAvatar()
    {
        ModalAvatarSource = null;
        HasModalAvatar = false;
        _pendingAvatarSourcePath = null;
        _shouldRemoveAvatarOnSave = _originalHasAvatar;
        OnPropertyChanged(nameof(ModalInitialsPreview));
    }

    /// <summary>
    /// Called by App.axaml.cs after the user picks an avatar image. Updates the
    /// preview bitmap and stages the source path to be applied on save.
    /// </summary>
    public void SetPendingAvatar(string path, Bitmap bitmap)
    {
        _pendingAvatarSourcePath = path;
        _shouldRemoveAvatarOnSave = false;
        ModalAvatarSource = bitmap;
        HasModalAvatar = true;
    }

    #endregion

    #region Add Customer

    [RelayCommand]
    public void OpenAddModal()
    {
        _editingCustomer = null;
        ClearModalFields();
        IsAddModalOpen = true;
    }

    [RelayCommand]
    public void CloseAddModal()
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

    [RelayCommand]
    public async Task SaveNewCustomerAsync()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        string newId;
        if (!string.IsNullOrWhiteSpace(ModalId))
        {
            newId = ModalId.Trim();
        }
        else
        {
            newId = new Core.Data.IdGenerator(companyData).NextCustomerId();
        }

        var newCustomer = new Customer
        {
            Id = newId,
            Name = $"{ModalFirstName.Trim()} {ModalLastName.Trim()}".Trim(),
            CompanyName = string.IsNullOrWhiteSpace(ModalCompanyName) ? null : ModalCompanyName.Trim(),
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

        // Persist the avatar image (if one was picked) into the company temp directory
        // *after* the customer is in the collection so its Id is stable.
        await ApplyPendingAvatarChangeAsync(newCustomer);

        // Snapshot the saved avatar bytes so undo can delete the file and redo can
        // recreate it. Tiny PNG so the closure capture is cheap.
        var newAvatarBytes = App.CompanyManager?.ReadCustomerAvatarBytes(newCustomer);
        var customerToUndo = newCustomer;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add customer '{newCustomer.Name}'",
            () =>
            {
                if (newAvatarBytes != null)
                    App.CompanyManager?.RestoreCustomerAvatar(customerToUndo, null);
                companyData.Customers.Remove(customerToUndo);
                companyData.MarkAsModified();
                CustomerSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Customers.Add(customerToUndo);
                if (newAvatarBytes != null)
                    App.CompanyManager?.RestoreCustomerAvatar(customerToUndo, newAvatarBytes);
                companyData.MarkAsModified();
                CustomerSaved?.Invoke(this, EventArgs.Empty);
            }));

        LastSavedCustomerId = newCustomer.Id;
        CustomerSaved?.Invoke(this, EventArgs.Empty);
        CloseAddModal();
    }

    /// <summary>
    /// Applies any pending avatar add/remove from the modal to the customer record.
    /// Callers capture the resulting bytes via ReadCustomerAvatarBytes if they need
    /// to include the change in an undo action.
    /// </summary>
    private async Task ApplyPendingAvatarChangeAsync(Customer customer)
    {
        var manager = App.CompanyManager;
        if (manager == null)
            return;

        try
        {
            if (_pendingAvatarSourcePath != null)
            {
                await manager.SetCustomerAvatarAsync(customer, _pendingAvatarSourcePath);
            }
            else if (_shouldRemoveAvatarOnSave)
            {
                await manager.RemoveCustomerAvatarAsync(customer);
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Customer.ApplyAvatarChange");
        }
    }

    #endregion

    #region Edit Customer

    public void OpenEditModal(CustomerDisplayItem? item)
    {
        if (item == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var customer = companyData?.Customers.FirstOrDefault(c => c.Id == item.Id);
        if (customer == null)
            return;

        _editingCustomer = customer;

        ModalId = customer.Id;
        var nameParts = customer.Name.Split(' ', 2);
        ModalFirstName = nameParts.Length > 0 ? nameParts[0] : string.Empty;
        ModalLastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;
        ModalCompanyName = customer.CompanyName ?? string.Empty;
        ModalEmail = customer.Email;
        ModalPhone = customer.Phone;
        ModalStreetAddress = customer.Address.Street;
        ModalCity = customer.Address.City;
        ModalStateProvince = customer.Address.State;
        ModalZipCode = customer.Address.ZipCode;
        ModalCountry = customer.Address.Country;
        ModalNotes = customer.Notes;
        ModalStatus = customer.Status switch
        {
            EntityStatus.Active => "Active",
            EntityStatus.Inactive => "Inactive",
            EntityStatus.Archived => "Banned",
            _ => "Active"
        };

        // Store original values for change detection
        _originalId = ModalId;
        _originalFirstName = ModalFirstName;
        _originalLastName = ModalLastName;
        _originalCompanyName = ModalCompanyName;
        _originalEmail = ModalEmail;
        _originalPhone = ModalPhone;
        _originalStreetAddress = ModalStreetAddress;
        _originalCity = ModalCity;
        _originalStateProvince = ModalStateProvince;
        _originalZipCode = ModalZipCode;
        _originalCountry = ModalCountry;
        _originalNotes = ModalNotes;

        // Load existing avatar (if any) into the modal preview.
        // _originalHasAvatar tracks the persisted state (used for change detection so
        // a missing/corrupt file can still be cleared on save). HasModalAvatar drives
        // the *visual*, only set it when the bitmap actually decoded, otherwise the
        // UI would show a blank Image control instead of falling back to initials.
        _pendingAvatarSourcePath = null;
        _shouldRemoveAvatarOnSave = false;
        _originalHasAvatar = !string.IsNullOrEmpty(customer.AvatarFileName);
        HasModalAvatar = false;
        ModalAvatarSource = null;

        var avatarPath = App.CompanyManager?.GetCustomerAvatarPath(customer);
        if (avatarPath != null)
        {
            try
            {
                ModalAvatarSource = new Bitmap(avatarPath);
                HasModalAvatar = true;
            }
            catch
            {
                ModalAvatarSource = null;
            }
        }
        OnPropertyChanged(nameof(ModalInitialsPreview));

        ClearModalErrors();
        IsEditModalOpen = true;
    }

    [RelayCommand]
    public void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingCustomer = null;
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

    [RelayCommand]
    public async Task SaveEditedCustomerAsync()
    {
        if (!ValidateModal() || _editingCustomer == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var oldId = _editingCustomer.Id;
        var oldName = _editingCustomer.Name;
        var oldCompanyName = _editingCustomer.CompanyName;
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

        var newId = ModalId.Trim();
        var newName = $"{ModalFirstName.Trim()} {ModalLastName.Trim()}".Trim();
        var newCompanyName = string.IsNullOrWhiteSpace(ModalCompanyName) ? null : ModalCompanyName.Trim();
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

        // Check if anything actually changed
        var hasIdChange = oldId != newId;
        var hasFieldChanges = hasIdChange ||
                         oldName != newName ||
                         oldCompanyName != newCompanyName ||
                         oldEmail != newEmail ||
                         oldPhone != newPhone ||
                         oldAddress.Street != newAddress.Street ||
                         oldAddress.City != newAddress.City ||
                         oldAddress.State != newAddress.State ||
                         oldAddress.ZipCode != newAddress.ZipCode ||
                         oldAddress.Country != newAddress.Country ||
                         oldNotes != newNotes ||
                         oldStatus != newStatus;

        var hasAvatarChanges = _pendingAvatarSourcePath != null || _shouldRemoveAvatarOnSave;

        // If nothing changed, just close the modal without recording an action
        if (!hasFieldChanges && !hasAvatarChanges)
        {
            CloseEditModal();
            return;
        }

        // Snapshot the avatar bytes BEFORE applying the change so the undo callback
        // can write them back; capture again AFTER so redo restores the new state.
        // Tiny resized PNG, so closure capture is cheap.
        byte[]? oldAvatarBytes = null;
        byte[]? newAvatarBytes = null;
        if (hasAvatarChanges)
        {
            oldAvatarBytes = App.CompanyManager?.ReadCustomerAvatarBytes(_editingCustomer);
            await ApplyPendingAvatarChangeAsync(_editingCustomer);
            newAvatarBytes = App.CompanyManager?.ReadCustomerAvatarBytes(_editingCustomer);
        }

        if (!hasFieldChanges)
        {
            // Only the avatar changed, record a dedicated undo entry so the user
            // can revert just the image change.
            var customerForAvatarUndo = _editingCustomer;
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Change customer '{customerForAvatarUndo.Name}' photo",
                () =>
                {
                    App.CompanyManager?.RestoreCustomerAvatar(customerForAvatarUndo, oldAvatarBytes);
                    companyData.MarkAsModified();
                    CustomerSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    App.CompanyManager?.RestoreCustomerAvatar(customerForAvatarUndo, newAvatarBytes);
                    companyData.MarkAsModified();
                    CustomerSaved?.Invoke(this, EventArgs.Empty);
                }));

            companyData.MarkAsModified();
            CustomerSaved?.Invoke(this, EventArgs.Empty);
            CloseEditModal();
            return;
        }

        var customerToEdit = _editingCustomer;
        var changes = new Dictionary<string, FieldChange>();
        if (hasIdChange) changes["ID"] = new FieldChange { OldValue = oldId, NewValue = newId };
        if (oldName != newName) changes["Name"] = new FieldChange { OldValue = oldName, NewValue = newName };
        if (oldCompanyName != newCompanyName) changes["Company"] = new FieldChange { OldValue = oldCompanyName ?? "", NewValue = newCompanyName ?? "" };
        if (oldEmail != newEmail) changes["Email"] = new FieldChange { OldValue = oldEmail, NewValue = newEmail };
        if (oldPhone != newPhone) changes["Phone"] = new FieldChange { OldValue = oldPhone, NewValue = newPhone };
        var oldAddr = $"{oldAddress.Street}, {oldAddress.City}, {oldAddress.State} {oldAddress.ZipCode}".Trim(' ', ',');
        var newAddr = $"{newAddress.Street}, {newAddress.City}, {newAddress.State} {newAddress.ZipCode}".Trim(' ', ',');
        if (oldAddr != newAddr) changes["Address"] = new FieldChange { OldValue = oldAddr, NewValue = newAddr };
        if (oldNotes != newNotes) changes["Notes"] = new FieldChange { OldValue = oldNotes, NewValue = newNotes };
        if (oldStatus != newStatus) changes["Status"] = new FieldChange { OldValue = oldStatus.ToString(), NewValue = newStatus.ToString() };
        if (changes.Count > 0) App.EventLogService?.SetPendingChanges(changes);

        // Apply the Id rename FIRST so a failure (e.g. unique-constraint race) doesn't
        // leave the entity with new field values but the old Id. Validation already
        // prevents conflicts; this ordering is defense-in-depth.
        if (hasIdChange)
        {
            try
            {
                App.CompanyManager?.ChangeCustomerId(customerToEdit, newId);
            }
            catch (Exception ex)
            {
                App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Customer.ChangeId");
                ModalIdError = ex.Message;
                return;
            }
        }

        customerToEdit.Name = newName;
        customerToEdit.CompanyName = newCompanyName;
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
                if (hasIdChange)
                    App.CompanyManager?.ChangeCustomerId(customerToEdit, oldId);
                customerToEdit.Name = oldName;
                customerToEdit.CompanyName = oldCompanyName;
                customerToEdit.Email = oldEmail;
                customerToEdit.Phone = oldPhone;
                customerToEdit.Address = oldAddress;
                customerToEdit.Notes = oldNotes;
                customerToEdit.Status = oldStatus;
                if (hasAvatarChanges)
                    App.CompanyManager?.RestoreCustomerAvatar(customerToEdit, oldAvatarBytes);
                companyData.MarkAsModified();
                CustomerSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                if (hasIdChange)
                    App.CompanyManager?.ChangeCustomerId(customerToEdit, newId);
                customerToEdit.Name = newName;
                customerToEdit.CompanyName = newCompanyName;
                customerToEdit.Email = newEmail;
                customerToEdit.Phone = newPhone;
                customerToEdit.Address = newAddress;
                customerToEdit.Notes = newNotes;
                customerToEdit.Status = newStatus;
                if (hasAvatarChanges)
                    App.CompanyManager?.RestoreCustomerAvatar(customerToEdit, newAvatarBytes);
                companyData.MarkAsModified();
                CustomerSaved?.Invoke(this, EventArgs.Empty);
            }));

        CustomerSaved?.Invoke(this, EventArgs.Empty);
        CloseEditModal();
    }

    #endregion

    #region Delete Customer

    public async void OpenDeleteConfirm(CustomerDisplayItem? item)
    {
        try
        {
            if (item == null)
                return;

            // Check if customer is in use
            var cd = App.CompanyManager?.CompanyData;
            if (cd != null)
            {
                var usages = new List<string>();
                if (cd.Invoices.Any(i => i.CustomerId == item.Id))
                    usages.Add("Invoice".Translate());
                if (cd.Revenues.Any(r => r.CustomerId == item.Id))
                    usages.Add("Revenue".Translate());
                if (cd.Rentals.Any(r => r.CustomerId == item.Id))
                    usages.Add("Rental".Translate());
                if (cd.RecurringInvoices.Any(ri => ri.CustomerId == item.Id))
                    usages.Add("Recurring Invoice".Translate());
                if (RecurringTransactionService.IsCustomerInUse(cd, item.Id))
                    usages.Add("Recurring Revenue".Translate());
                if (cd.Payments.Any(p => p.CustomerId == item.Id))
                    usages.Add("Payment".Translate());
                if (cd.Returns.Any(r => r.CustomerId == item.Id))
                    usages.Add("Return".Translate());
                if (usages.Count > 0)
                {
                    await App.ShowWarningMessageBoxAsync(
                        "Cannot Delete".Translate(),
                        "This customer cannot be deleted because it is referenced by one or more: {0}.".TranslateFormat(string.Join(", ", usages)));
                    return;
                }
            }

            var dialog = App.ConfirmationDialog;
            if (dialog == null)
                return;

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Customer".Translate(),
                Message = "Are you sure you want to delete this customer?\n\n{0}".TranslateFormat(item.Name),
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary)
                return;

            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null)
                return;

            var customer = companyData.Customers.FirstOrDefault(c => c.Id == item.Id);
            if (customer != null)
            {
                var deletedCustomer = customer;

                // Snapshot the avatar bytes BEFORE deleting so undo can restore the
                // file alongside the customer record. The customer's AvatarFileName is
                // also captured implicitly, the customer object stays alive in the
                // closure and is mutated in place by RestoreCustomerAvatar.
                var deletedAvatarBytes = App.CompanyManager?.ReadCustomerAvatarBytes(deletedCustomer);
                var savedAvatarFileName = deletedCustomer.AvatarFileName;

                // Clean up the avatar file before removing the customer. This avoids
                // bloat (and retention of deleted-customer images) inside the .argo
                // archive on next save.
                if (App.CompanyManager != null && !string.IsNullOrEmpty(savedAvatarFileName))
                {
                    try { await App.CompanyManager.RemoveCustomerAvatarAsync(deletedCustomer); }
                    catch (Exception ex) { App.ErrorLogger?.LogWarning($"Failed to remove customer avatar on delete: {ex.Message}", "Customer.Delete"); }
                }

                companyData.Customers.Remove(customer);
                companyData.MarkAsModified();

                App.UndoRedoManager.RecordAction(new DelegateAction(
                    $"Delete customer '{deletedCustomer.Name}'",
                    () =>
                    {
                        companyData.Customers.Add(deletedCustomer);
                        if (deletedAvatarBytes != null)
                            App.CompanyManager?.RestoreCustomerAvatar(deletedCustomer, deletedAvatarBytes);
                        companyData.MarkAsModified();
                        CustomerDeleted?.Invoke(this, EventArgs.Empty);
                    },
                    () =>
                    {
                        if (deletedAvatarBytes != null)
                            App.CompanyManager?.RestoreCustomerAvatar(deletedCustomer, null);
                        companyData.Customers.Remove(deletedCustomer);
                        companyData.MarkAsModified();
                        CustomerDeleted?.Invoke(this, EventArgs.Empty);
                    }));
            }

            CustomerDeleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Customer.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Returns true if any filter has been changed from the state when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterPaymentStatus != _originalFilterPaymentStatus ||
        FilterCustomerStatus != _originalFilterCustomerStatus ||
        FilterCountry != _originalFilterCountry ||
        FilterOutstandingMin != _originalFilterOutstandingMin ||
        FilterOutstandingMax != _originalFilterOutstandingMax ||
        FilterLastRentalFrom != _originalFilterLastRentalFrom ||
        FilterLastRentalTo != _originalFilterLastRentalTo;

    /// <summary>
    /// Captures the current filter state as original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterPaymentStatus = FilterPaymentStatus;
        _originalFilterCustomerStatus = FilterCustomerStatus;
        _originalFilterCountry = FilterCountry;
        _originalFilterOutstandingMin = FilterOutstandingMin;
        _originalFilterOutstandingMax = FilterOutstandingMax;
        _originalFilterLastRentalFrom = FilterLastRentalFrom;
        _originalFilterLastRentalTo = FilterLastRentalTo;
    }

    [RelayCommand]
    public void OpenFilterModal()
    {
        UpdateCountryOptions();
        CaptureOriginalFilterValues();
        IsFilterModalOpen = true;
    }

    private void UpdateCountryOptions()
    {
        CountryOptions.Clear();
        CountryOptions.Add("All");

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var countries = companyData.Customers
            .Select(c => c.Address.Country)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c);

        foreach (var country in countries)
        {
            CountryOptions.Add(country);
        }
    }

    [RelayCommand]
    public void CloseFilterModal()
    {
        IsFilterModalOpen = false;
    }

    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (HasFilterModalChanges)
        {
            if (!await ConfirmDiscardFiltersAsync())
                return;

            // Restore filter values to the state when modal was opened
            FilterPaymentStatus = _originalFilterPaymentStatus;
            FilterCustomerStatus = _originalFilterCustomerStatus;
            FilterCountry = _originalFilterCountry;
            FilterOutstandingMin = _originalFilterOutstandingMin;
            FilterOutstandingMax = _originalFilterOutstandingMax;
            FilterLastRentalFrom = _originalFilterLastRentalFrom;
            FilterLastRentalTo = _originalFilterLastRentalTo;
        }

        CloseFilterModal();
    }

    private void ResetFilterDefaults()
    {
        FilterPaymentStatus = "All";
        FilterCustomerStatus = "All";
        FilterCountry = "All";
        FilterOutstandingMin = null;
        FilterOutstandingMax = null;
        FilterLastRentalFrom = null;
        FilterLastRentalTo = null;
    }

    [RelayCommand]
    public void ApplyFilters()
    {
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        ResetFilterDefaults();
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    #endregion

    #region Customer History Modal

    public void OpenHistoryModal(CustomerDisplayItem? item)
    {
        if (item == null)
            return;

        _historyCustomer = item;
        HistoryCustomerName = item.Name;
        LoadCustomerHistory(item.Id);
        IsHistoryModalOpen = true;
    }

    /// <summary>
    /// Loads the transaction history for a customer.
    /// </summary>
    private void LoadCustomerHistory(string customerId)
    {
        CustomerHistory.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var historyItems = new List<CustomerHistoryItem>();

        // Add invoices
        var invoices = companyData.Invoices.Where(i => i.CustomerId == customerId);
        foreach (var invoice in invoices)
        {
            historyItems.Add(new CustomerHistoryItem
            {
                Date = invoice.IssueDate,
                Type = "Invoice",
                Description = $"Invoice #{invoice.InvoiceNumber}",
                Amount = invoice.Total,
                Status = invoice.Status.ToString()
            });
        }

        // Add payments
        var payments = companyData.Payments.Where(p => p.CustomerId == customerId);
        foreach (var payment in payments)
        {
            historyItems.Add(new CustomerHistoryItem
            {
                Date = payment.Date,
                Type = "Payment",
                Description = $"Payment - {payment.PaymentMethod}",
                Amount = -payment.Amount, // Negative because it reduces balance
                Status = "Completed"
            });
        }

        // Add rentals
        var rentals = companyData.Rentals.Where(r => r.CustomerId == customerId);
        foreach (var rental in rentals)
        {
            var rentalItem = companyData.RentalInventory?.FirstOrDefault(p => p.Id == rental.RentalItemId);
            var invItem = rentalItem != null ? companyData.Inventory.FirstOrDefault(i => i.Id == rentalItem.InventoryItemId) : null;
            var rentalProductName = invItem != null ? companyData.GetProduct(invItem.ProductId)?.Name : null;
            historyItems.Add(new CustomerHistoryItem
            {
                Date = rental.StartDate,
                Type = "Rental",
                Description = $"Rental - {rentalProductName ?? "Unknown Item"}",
                Amount = rental.TotalCost ?? 0,
                Status = rental.Status.ToString()
            });
        }

        // Add returns
        var returns = companyData.Returns.Where(r => r.CustomerId == customerId);
        foreach (var returnItem in returns)
        {
            var firstItem = returnItem.Items.FirstOrDefault();
            var product = firstItem != null ? companyData.Products?.FirstOrDefault(p => p.Id == firstItem.ProductId) : null;
            historyItems.Add(new CustomerHistoryItem
            {
                Date = returnItem.ReturnDate,
                Type = "Return",
                Description = $"Return - {product?.Name ?? "Unknown Product"}",
                Amount = -returnItem.RefundAmount,
                Status = returnItem.Status.ToString()
            });
        }

        var filtered = ApplyHistoryFiltersInternal(historyItems);

        // Sort by date descending and add to collection
        foreach (var historyItem in filtered.OrderByDescending(h => h.Date))
        {
            CustomerHistory.Add(historyItem);
        }
    }

    /// <summary>
    /// Applies the history filters to the given items.
    /// </summary>
    private List<CustomerHistoryItem> ApplyHistoryFiltersInternal(List<CustomerHistoryItem> items)
    {
        var filtered = items.AsEnumerable();

        if (HistoryFilterType != "All")
        {
            filtered = filtered.Where(h => h.Type == HistoryFilterType);
        }

        if (HistoryFilterStatus != "All")
        {
            filtered = filtered.Where(h => h.Status == HistoryFilterStatus);
        }

        // Filter by date range
        if (HistoryFilterDateFrom.HasValue)
        {
            filtered = filtered.Where(h => h.Date.Date >= HistoryFilterDateFrom.Value.Date);
        }
        if (HistoryFilterDateTo.HasValue)
        {
            filtered = filtered.Where(h => h.Date.Date <= HistoryFilterDateTo.Value.Date);
        }

        // Filter by amount range
        if (decimal.TryParse(HistoryFilterAmountMin, out var minAmount))
        {
            filtered = filtered.Where(h => Math.Abs(h.Amount) >= minAmount);
        }
        if (decimal.TryParse(HistoryFilterAmountMax, out var maxAmount))
        {
            filtered = filtered.Where(h => Math.Abs(h.Amount) <= maxAmount);
        }

        return filtered.ToList();
    }

    [RelayCommand]
    public void CloseHistoryModal()
    {
        IsHistoryModalOpen = false;
        _historyCustomer = null;
        CustomerHistory.Clear();
    }

    /// <summary>
    /// Returns true if any history filter has been changed from its default value.
    /// </summary>
    public bool HasHistoryFilterChanges =>
        HistoryFilterType != "All" ||
        HistoryFilterStatus != "All" ||
        HistoryFilterDateFrom != null ||
        HistoryFilterDateTo != null ||
        !string.IsNullOrWhiteSpace(HistoryFilterAmountMin) ||
        !string.IsNullOrWhiteSpace(HistoryFilterAmountMax);

    [RelayCommand]
    public void OpenHistoryFilterModal()
    {
        IsHistoryFilterModalOpen = true;
    }

    [RelayCommand]
    public void CloseHistoryFilterModal()
    {
        IsHistoryFilterModalOpen = false;
    }

    [RelayCommand]
    public async Task RequestCloseHistoryFilterModalAsync()
    {
        if (HasHistoryFilterChanges)
        {
            if (!await ConfirmDiscardFiltersAsync())
                return;

            ResetHistoryFilterDefaults();
        }

        CloseHistoryFilterModal();
    }

    private void ResetHistoryFilterDefaults()
    {
        HistoryFilterType = "All";
        HistoryFilterStatus = "All";
        HistoryFilterDateFrom = null;
        HistoryFilterDateTo = null;
        HistoryFilterAmountMin = null;
        HistoryFilterAmountMax = null;
    }

    [RelayCommand]
    public void ApplyHistoryFilters()
    {
        if (_historyCustomer != null)
        {
            LoadCustomerHistory(_historyCustomer.Id);
        }
        CloseHistoryFilterModal();
    }

    [RelayCommand]
    public void ClearHistoryFilters()
    {
        ResetHistoryFilterDefaults();
        if (_historyCustomer != null)
        {
            LoadCustomerHistory(_historyCustomer.Id);
        }
        CloseHistoryFilterModal();
    }

    #endregion

    #region Modal Helpers

    private void ClearModalFields()
    {
        ModalId = string.Empty;
        _originalId = string.Empty;
        ModalFirstName = string.Empty;
        ModalLastName = string.Empty;
        ModalCompanyName = string.Empty;
        ModalEmail = string.Empty;
        ModalPhone = string.Empty;
        ModalStreetAddress = string.Empty;
        ModalCity = string.Empty;
        ModalStateProvince = string.Empty;
        ModalZipCode = string.Empty;
        ModalCountry = string.Empty;
        ModalNotes = string.Empty;
        ModalStatus = "Active";
        ModalAvatarSource = null;
        HasModalAvatar = false;
        _pendingAvatarSourcePath = null;
        _shouldRemoveAvatarOnSave = false;
        _originalHasAvatar = false;
        OnPropertyChanged(nameof(ModalInitialsPreview));
        ClearModalErrors();
    }

    private void ClearModalErrors()
    {
        ModalIdError = null;
        ModalFirstNameError = null;
        ModalEmailError = null;
        ModalPhoneError = null;
    }

    private bool ValidateModal()
    {
        ClearModalErrors();
        var isValid = true;

        if (string.IsNullOrWhiteSpace(ModalFirstName))
        {
            ModalFirstNameError = "First name is required.".Translate();
            isValid = false;
        }

        if (!string.IsNullOrWhiteSpace(ModalEmail))
        {
            if (!ModalEmail.Contains('@') || !ModalEmail.Contains('.'))
            {
                ModalEmailError = "Please enter a valid email address.".Translate();
                isValid = false;
            }
        }

        // Duplicate detection
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData != null)
        {
            var trimmedId = ModalId.Trim();
            if (_editingCustomer != null && string.IsNullOrEmpty(trimmedId))
            {
                ModalIdError = "ID cannot be empty.".Translate();
                isValid = false;
            }
            else if (!string.IsNullOrEmpty(trimmedId))
            {
                var existingWithSameId = companyData.Customers.Any(c =>
                    c.Id == trimmedId &&
                    (_editingCustomer == null || !ReferenceEquals(c, _editingCustomer)));
                if (existingWithSameId)
                {
                    ModalIdError = "A customer with this ID already exists.".Translate();
                    isValid = false;
                }
            }

            var fullName = $"{ModalFirstName.Trim()} {ModalLastName.Trim()}".Trim();
            var existingWithSameName = companyData.Customers.Any(c =>
                c.Name.Equals(fullName, StringComparison.OrdinalIgnoreCase) &&
                (_editingCustomer == null || c.Id != _editingCustomer.Id));

            if (existingWithSameName)
            {
                ModalFirstNameError = "A customer with this name already exists.".Translate();
                isValid = false;
            }

            if (!string.IsNullOrWhiteSpace(ModalEmail))
            {
                var existingWithSameEmail = companyData.Customers.Any(c =>
                    !string.IsNullOrWhiteSpace(c.Email) &&
                    c.Email.Equals(ModalEmail.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    (_editingCustomer == null || c.Id != _editingCustomer.Id));

                if (existingWithSameEmail)
                {
                    ModalEmailError = "A customer with this email already exists.".Translate();
                    isValid = false;
                }
            }

            if (!PhoneInput.IsFullPhoneComplete(ModalPhone))
            {
                ModalPhoneError = "Please enter a complete phone number.".Translate();
                isValid = false;
            }

            if (!string.IsNullOrWhiteSpace(ModalPhone))
            {
                var existingWithSamePhone = companyData.Customers.Any(c =>
                    !string.IsNullOrWhiteSpace(c.Phone) &&
                    c.Phone.Equals(ModalPhone.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    (_editingCustomer == null || c.Id != _editingCustomer.Id));

                if (existingWithSamePhone)
                {
                    ModalPhoneError = "A customer with this phone number already exists.".Translate();
                    isValid = false;
                }
            }
        }

        return isValid;
    }

    #endregion
}
