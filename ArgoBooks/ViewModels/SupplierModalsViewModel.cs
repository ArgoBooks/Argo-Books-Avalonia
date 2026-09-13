using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for supplier modals, shared between SuppliersPage and AppShell.
/// </summary>
public partial class SupplierModalsViewModel : ViewModelBase
{
    #region Modal State

    [ObservableProperty]
    private bool _isAddModalOpen;

    [ObservableProperty]
    private bool _isEditModalOpen;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    /// <summary>
    /// Whether the footer's "enter all required fields" line shows. Derived rather than
    /// stored, for the same reason as the customer modal: every check in ValidateModal sets
    /// one of the field errors, so a stored copy only added a second thing to remember to
    /// clear, and the per-field handlers cleared the error and left the footer behind.
    /// </summary>
    public bool HasValidationMessage =>
        ModalError != null || ModalIdError != null || ModalSupplierNameError != null
        || ModalEmailError != null || ModalPhoneError != null;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private string _modalId = string.Empty;

    [ObservableProperty]
    private string _modalSupplierName = string.Empty;

    [ObservableProperty]
    private string _modalEmail = string.Empty;

    [ObservableProperty]
    private string _modalPhone = string.Empty;

    [ObservableProperty]
    private string _modalWebsite = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalIdError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalSupplierNameError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalEmailError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _modalPhoneError;

    [ObservableProperty]
    private Bitmap? _modalAvatarSource;

    [ObservableProperty]
    private bool _hasModalAvatar;

    /// <summary>
    /// Local file path picked via the file picker. Applied on save.
    /// </summary>
    private string? _pendingAvatarSourcePath;

    /// <summary>
    /// Bytes downloaded from the supplier's website /favicon.ico. Applied on save
    /// only when the user has not picked their own image.
    /// </summary>
    private byte[]? _pendingFaviconBytes;

    /// <summary>
    /// True when the user clicked Remove on an existing avatar; on save the supplier's
    /// avatar file should be deleted.
    /// </summary>
    private bool _shouldRemoveAvatarOnSave;

    /// <summary>
    /// Snapshot of whether the supplier had an avatar when the edit modal was opened,
    /// used for change detection.
    /// </summary>
    private bool _originalHasAvatar;

    /// <summary>
    /// Cancels an in-flight favicon download when the user keeps typing or closes the modal.
    /// </summary>
    private CancellationTokenSource? _faviconCts;

    /// <summary>
    /// Live preview of the initials shown when no avatar is set.
    /// Driven from ModalSupplierName so the avatar circle updates as the user types.
    /// </summary>
    public string ModalInitialsPreview
    {
        get
        {
            var name = ModalSupplierName.Trim();
            if (name.Length == 0) return "?";

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            if (parts[0].Length >= 2)
                return parts[0][..2].ToUpperInvariant();
            return parts[0].ToUpperInvariant();
        }
    }

    partial void OnModalIdChanged(string value)
    {
        ModalIdError = null;
    }

    partial void OnModalWebsiteChanged(string value)
    {
        // Auto-fetch the supplier's /favicon.ico, but only when the user has not
        // already picked or kept their own image. The check on HasModalAvatar handles
        // every "image already there" case (existing avatar in edit mode, prior favicon
        // fetch in this session, manual file pick).
        if (HasModalAvatar || _pendingAvatarSourcePath != null)
            return;

        TriggerFaviconFetch(value);
    }

    private Supplier? _editingSupplier;

    // Original values for change detection in edit mode
    private string _originalId = string.Empty;
    private string _originalSupplierName = string.Empty;
    private string _originalEmail = string.Empty;
    private string _originalPhone = string.Empty;
    private string _originalWebsite = string.Empty;
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
        !string.IsNullOrWhiteSpace(ModalSupplierName) ||
        !string.IsNullOrWhiteSpace(ModalEmail) ||
        !string.IsNullOrWhiteSpace(ModalPhone) ||
        !string.IsNullOrWhiteSpace(ModalWebsite) ||
        !string.IsNullOrWhiteSpace(ModalStreetAddress) ||
        !string.IsNullOrWhiteSpace(ModalCity) ||
        !string.IsNullOrWhiteSpace(ModalStateProvince) ||
        !string.IsNullOrWhiteSpace(ModalZipCode) ||
        !string.IsNullOrWhiteSpace(ModalCountry) ||
        !string.IsNullOrWhiteSpace(ModalNotes);

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges =>
        ModalId.Trim() != _originalId ||
        ModalSupplierName != _originalSupplierName ||
        ModalEmail != _originalEmail ||
        ModalPhone != _originalPhone ||
        ModalWebsite != _originalWebsite ||
        ModalStreetAddress != _originalStreetAddress ||
        ModalCity != _originalCity ||
        ModalStateProvince != _originalStateProvince ||
        ModalZipCode != _originalZipCode ||
        ModalCountry != _originalCountry ||
        ModalNotes != _originalNotes ||
        _pendingAvatarSourcePath != null ||
        _pendingFaviconBytes != null ||
        _shouldRemoveAvatarOnSave;

    #endregion

    #region Filter Fields

    [ObservableProperty]
    private string _filterCountry = "All";

    [ObservableProperty]
    private string _filterStatus = "All";

    public ObservableCollection<string> CountryOptions { get; } = ["All"];
    public ObservableCollection<string> StatusOptions { get; } = ["All", "Active", "Inactive"];

    // Original filter values for change detection (captured when modal opens)
    private string _originalFilterCountry = "All";
    private string _originalFilterStatus = "All";

    /// <summary>
    /// Returns true if any filter has been changed from the state when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterCountry != _originalFilterCountry ||
        FilterStatus != _originalFilterStatus;

    /// <summary>
    /// Captures the current filter state as original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterCountry = FilterCountry;
        _originalFilterStatus = FilterStatus;
    }

    #endregion

    #region Events

    public event EventHandler? SupplierSaved;
    public event EventHandler? SupplierDeleted;

    /// <summary>
    /// The Id of the supplier most recently created via the Add modal. Lets a caller that
    /// opened "create supplier" from another modal auto-select the new supplier after save.
    /// </summary>
    public string? LastSavedSupplierId { get; private set; }
    public event EventHandler? FiltersApplied;
    public event EventHandler? FiltersCleared;

    /// <summary>
    /// Fired when the user clicks the avatar in the modal to pick a new image.
    /// App.axaml.cs subscribes and shows the OS file picker.
    /// </summary>
    public event EventHandler? BrowseAvatarRequested;

    #endregion

    #region Avatar Commands

    [RelayCommand]
    private void BrowseAvatar() => BrowseAvatarRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RemoveAvatar()
    {
        ModalAvatarSource = null;
        HasModalAvatar = false;
        _pendingAvatarSourcePath = null;
        _pendingFaviconBytes = null;
        _shouldRemoveAvatarOnSave = _originalHasAvatar;

        // Re-evaluate the website: with no avatar showing, the favicon fetch becomes
        // applicable again. Mirrors the rule "if the user did not select an image,
        // pull the website's favicon".
        TriggerFaviconFetch(ModalWebsite);
    }

    /// <summary>
    /// Called by App.axaml.cs after the user picks an avatar image. Updates the preview
    /// bitmap and stages the source path to be applied on save. Manual pick wins over
    /// any pending favicon.
    /// </summary>
    public void SetPendingAvatar(string path, Bitmap bitmap)
    {
        _pendingAvatarSourcePath = path;
        _pendingFaviconBytes = null;
        _shouldRemoveAvatarOnSave = false;
        ModalAvatarSource = bitmap;
        HasModalAvatar = true;
    }

    /// <summary>
    /// Cancels any in-flight favicon download and kicks off a new one (debounced)
    /// for the given URL. Silently does nothing on bad URLs / network failures.
    /// </summary>
    private void TriggerFaviconFetch(string? websiteUrl)
    {
        // Cancel and dispose the previous CTS, keystroke-driven calls would otherwise
        // leak a CancellationTokenSource (and its underlying timer) per keystroke.
        var previous = _faviconCts;
        if (previous != null)
        {
            previous.Cancel();
            previous.Dispose();
        }
        _faviconCts = null;

        if (string.IsNullOrWhiteSpace(websiteUrl))
            return;

        var cts = new CancellationTokenSource();
        _faviconCts = cts;
        var urlSnapshot = websiteUrl;

        _ = Task.Run(async () =>
        {
            try
            {
                // Debounce: give the user time to keep typing before we hit the network.
                await Task.Delay(500, cts.Token).ConfigureAwait(false);
                if (cts.IsCancellationRequested) return;

                var bytes = await FaviconService.TryFetchFaviconAsync(urlSnapshot, cts.Token).ConfigureAwait(false);
                if (bytes == null || cts.IsCancellationRequested) return;

                Bitmap? bitmap;
                try
                {
                    using var ms = new MemoryStream(bytes);
                    bitmap = new Bitmap(ms);
                }
                catch
                {
                    return; // Format Avalonia can't decode; ignore.
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    // Re-check the gating conditions on the UI thread, the user may
                    // have picked a file or removed-with-empty-website while we were
                    // waiting on the network.
                    if (HasModalAvatar || _pendingAvatarSourcePath != null)
                    {
                        bitmap.Dispose();
                        return;
                    }
                    ModalAvatarSource = bitmap;
                    HasModalAvatar = true;
                    _pendingFaviconBytes = bytes;
                });
            }
            catch (TaskCanceledException) { /* user kept typing */ }
            catch (Exception ex)
            {
                App.ErrorLogger?.LogWarning($"Favicon fetch failed: {ex.Message}", "Supplier.Favicon");
            }
            finally
            {
                // Drop our reference if the field still points at this CTS (i.e. no newer
                // keystroke has replaced it). The CTS is disposable; release it.
                if (ReferenceEquals(_faviconCts, cts))
                    _faviconCts = null;
                cts.Dispose();
            }
        });
    }

    #endregion

    #region Add Supplier

    [RelayCommand]
    public void OpenAddModal()
    {
        _editingSupplier = null;
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
    public async Task SaveNewSupplierAsync()
    {
        if (!ValidateModal()) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        string newId;
        if (!string.IsNullOrWhiteSpace(ModalId))
        {
            newId = ModalId.Trim();
        }
        else
        {
            newId = new Core.Data.IdGenerator(companyData).NextSupplierId();
        }

        var newSupplier = new Supplier
        {
            Id = newId,
            Name = ModalSupplierName.Trim(),
            Email = string.IsNullOrWhiteSpace(ModalEmail) ? string.Empty : ModalEmail.Trim(),
            Phone = string.IsNullOrWhiteSpace(ModalPhone) ? string.Empty : ModalPhone.Trim(),
            Website = string.IsNullOrWhiteSpace(ModalWebsite) ? string.Empty : ModalWebsite.Trim(),
            Address = new Address
            {
                Street = string.IsNullOrWhiteSpace(ModalStreetAddress) ? string.Empty : ModalStreetAddress.Trim(),
                City = string.IsNullOrWhiteSpace(ModalCity) ? string.Empty : ModalCity.Trim(),
                State = string.IsNullOrWhiteSpace(ModalStateProvince) ? string.Empty : ModalStateProvince.Trim(),
                ZipCode = string.IsNullOrWhiteSpace(ModalZipCode) ? string.Empty : ModalZipCode.Trim(),
                Country = string.IsNullOrWhiteSpace(ModalCountry) ? string.Empty : ModalCountry.Trim()
            },
            Notes = string.IsNullOrWhiteSpace(ModalNotes) ? string.Empty : ModalNotes.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        companyData.Suppliers.Add(newSupplier);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.SupplierCreated);
        companyData.MarkAsModified();

        // Persist the avatar (manual pick or auto-fetched favicon) into the company
        // temp directory after the supplier is in the collection so its Id is stable.
        await ApplyPendingAvatarChangeAsync(newSupplier);

        // Snapshot the saved avatar bytes so undo can delete the file and redo can
        // recreate it.
        var newAvatarBytes = App.CompanyManager?.ReadSupplierAvatarBytes(newSupplier);
        var supplierToUndo = newSupplier;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add supplier '{newSupplier.Name}'",
            () =>
            {
                if (newAvatarBytes != null)
                    App.CompanyManager?.RestoreSupplierAvatar(supplierToUndo, null);
                companyData.Suppliers.Remove(supplierToUndo);
                companyData.MarkAsModified();
                SupplierSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Suppliers.Add(supplierToUndo);
                if (newAvatarBytes != null)
                    App.CompanyManager?.RestoreSupplierAvatar(supplierToUndo, newAvatarBytes);
                companyData.MarkAsModified();
                SupplierSaved?.Invoke(this, EventArgs.Empty);
            }));

        LastSavedSupplierId = newSupplier.Id;
        SupplierSaved?.Invoke(this, EventArgs.Empty);
        CloseAddModal();
    }

    /// <summary>
    /// Applies any pending avatar change (manual upload, fetched favicon, or removal)
    /// to the supplier record. Callers capture the resulting bytes via
    /// ReadSupplierAvatarBytes if they need to include the change in an undo action.
    /// Manual upload wins over a pending favicon if both somehow exist.
    /// </summary>
    private async Task ApplyPendingAvatarChangeAsync(Supplier supplier)
    {
        var manager = App.CompanyManager;
        if (manager == null) return;

        try
        {
            if (_pendingAvatarSourcePath != null)
                await manager.SetSupplierAvatarAsync(supplier, _pendingAvatarSourcePath);
            else if (_pendingFaviconBytes != null)
                await manager.SetSupplierAvatarFromBytesAsync(supplier, _pendingFaviconBytes);
            else if (_shouldRemoveAvatarOnSave)
                await manager.RemoveSupplierAvatarAsync(supplier);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Supplier.ApplyAvatarChange");
        }
    }

    #endregion

    #region Edit Supplier

    public void OpenEditModal(SupplierDisplayItem? item)
    {
        if (item == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        var supplier = companyData?.Suppliers.FirstOrDefault(s => s.Id == item.Id);
        if (supplier == null) return;

        _editingSupplier = supplier;
        ModalId = supplier.Id;
        _originalId = supplier.Id;
        ModalSupplierName = supplier.Name;
        ModalEmail = supplier.Email;
        ModalPhone = supplier.Phone;

        // Load the existing avatar BEFORE setting ModalWebsite, that way the
        // OnModalWebsiteChanged hook sees HasModalAvatar=true and skips the favicon
        // fetch instead of racing to overwrite the existing avatar.
        // _originalHasAvatar tracks the persisted state for change detection;
        // HasModalAvatar drives the *visual* and is only set when the bitmap actually
        // decoded, otherwise the UI would show a blank Image instead of falling back
        // to initials. (If decode fails and the supplier has a website, the favicon
        // hook will then auto-fetch a replacement, which is graceful recovery.)
        _pendingAvatarSourcePath = null;
        _pendingFaviconBytes = null;
        _shouldRemoveAvatarOnSave = false;
        _originalHasAvatar = !string.IsNullOrEmpty(supplier.AvatarFileName);
        HasModalAvatar = false;
        ModalAvatarSource = null;
        var existingAvatarPath = App.CompanyManager?.GetSupplierAvatarPath(supplier);
        if (existingAvatarPath != null)
        {
            try
            {
                ModalAvatarSource = new Bitmap(existingAvatarPath);
                HasModalAvatar = true;
            }
            catch { ModalAvatarSource = null; }
        }
        OnPropertyChanged(nameof(ModalInitialsPreview));

        ModalWebsite = supplier.Website;
        ModalStreetAddress = supplier.Address.Street;
        ModalCity = supplier.Address.City;
        ModalStateProvince = supplier.Address.State;
        ModalZipCode = supplier.Address.ZipCode;
        ModalCountry = supplier.Address.Country;
        ModalNotes = supplier.Notes;

        // Store original values for change detection
        _originalSupplierName = ModalSupplierName;
        _originalEmail = ModalEmail;
        _originalPhone = ModalPhone;
        _originalWebsite = ModalWebsite;
        _originalStreetAddress = ModalStreetAddress;
        _originalCity = ModalCity;
        _originalStateProvince = ModalStateProvince;
        _originalZipCode = ModalZipCode;
        _originalCountry = ModalCountry;
        _originalNotes = ModalNotes;

        ModalError = null;
        IsEditModalOpen = true;
    }

    [RelayCommand]
    public void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingSupplier = null;
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
    public async Task SaveEditedSupplierAsync()
    {
        if (!ValidateModal() || _editingSupplier == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var oldId = _editingSupplier.Id;
        var oldName = _editingSupplier.Name;
        var oldEmail = _editingSupplier.Email;
        var oldPhone = _editingSupplier.Phone;
        var oldWebsite = _editingSupplier.Website;
        var oldAddress = new Address
        {
            Street = _editingSupplier.Address.Street,
            City = _editingSupplier.Address.City,
            State = _editingSupplier.Address.State,
            ZipCode = _editingSupplier.Address.ZipCode,
            Country = _editingSupplier.Address.Country
        };
        var oldNotes = _editingSupplier.Notes;

        var newId = ModalId.Trim();
        var newName = ModalSupplierName.Trim();
        var newEmail = string.IsNullOrWhiteSpace(ModalEmail) ? string.Empty : ModalEmail.Trim();
        var newPhone = string.IsNullOrWhiteSpace(ModalPhone) ? string.Empty : ModalPhone.Trim();
        var newWebsite = string.IsNullOrWhiteSpace(ModalWebsite) ? string.Empty : ModalWebsite.Trim();
        var newAddress = new Address
        {
            Street = string.IsNullOrWhiteSpace(ModalStreetAddress) ? string.Empty : ModalStreetAddress.Trim(),
            City = string.IsNullOrWhiteSpace(ModalCity) ? string.Empty : ModalCity.Trim(),
            State = string.IsNullOrWhiteSpace(ModalStateProvince) ? string.Empty : ModalStateProvince.Trim(),
            ZipCode = string.IsNullOrWhiteSpace(ModalZipCode) ? string.Empty : ModalZipCode.Trim(),
            Country = string.IsNullOrWhiteSpace(ModalCountry) ? string.Empty : ModalCountry.Trim()
        };
        var newNotes = string.IsNullOrWhiteSpace(ModalNotes) ? string.Empty : ModalNotes.Trim();

        // Check if anything actually changed
        var hasIdChange = oldId != newId;
        var hasFieldChanges = hasIdChange ||
                         oldName != newName ||
                         oldEmail != newEmail ||
                         oldPhone != newPhone ||
                         oldWebsite != newWebsite ||
                         oldAddress.Street != newAddress.Street ||
                         oldAddress.City != newAddress.City ||
                         oldAddress.State != newAddress.State ||
                         oldAddress.ZipCode != newAddress.ZipCode ||
                         oldAddress.Country != newAddress.Country ||
                         oldNotes != newNotes;

        var hasAvatarChanges = _pendingAvatarSourcePath != null
                            || _pendingFaviconBytes != null
                            || _shouldRemoveAvatarOnSave;

        // If nothing changed, just close the modal without recording an action
        if (!hasFieldChanges && !hasAvatarChanges)
        {
            CloseEditModal();
            return;
        }

        // Snapshot the avatar bytes BEFORE applying the change so undo can write them
        // back; capture again AFTER so redo restores the new state.
        byte[]? oldAvatarBytes = null;
        byte[]? newAvatarBytes = null;
        if (hasAvatarChanges)
        {
            oldAvatarBytes = App.CompanyManager?.ReadSupplierAvatarBytes(_editingSupplier);
            await ApplyPendingAvatarChangeAsync(_editingSupplier);
            newAvatarBytes = App.CompanyManager?.ReadSupplierAvatarBytes(_editingSupplier);
        }

        if (!hasFieldChanges)
        {
            // Only the avatar changed, record a dedicated undo entry so the user
            // can revert just the image change.
            var supplierForAvatarUndo = _editingSupplier;
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Change supplier '{supplierForAvatarUndo.Name}' photo",
                () =>
                {
                    App.CompanyManager?.RestoreSupplierAvatar(supplierForAvatarUndo, oldAvatarBytes);
                    companyData.MarkAsModified();
                    SupplierSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    App.CompanyManager?.RestoreSupplierAvatar(supplierForAvatarUndo, newAvatarBytes);
                    companyData.MarkAsModified();
                    SupplierSaved?.Invoke(this, EventArgs.Empty);
                }));

            companyData.MarkAsModified();
            SupplierSaved?.Invoke(this, EventArgs.Empty);
            CloseEditModal();
            return;
        }

        var supplierToEdit = _editingSupplier;
        var changes = new Dictionary<string, FieldChange>();
        if (hasIdChange) changes["ID"] = new FieldChange { OldValue = oldId, NewValue = newId };
        if (oldName != newName) changes["Name"] = new FieldChange { OldValue = oldName, NewValue = newName };
        if (oldEmail != newEmail) changes["Email"] = new FieldChange { OldValue = oldEmail, NewValue = newEmail };
        if (oldPhone != newPhone) changes["Phone"] = new FieldChange { OldValue = oldPhone, NewValue = newPhone };
        if (oldWebsite != newWebsite) changes["Website"] = new FieldChange { OldValue = oldWebsite, NewValue = newWebsite };
        var oldAddr = $"{oldAddress.Street}, {oldAddress.City}, {oldAddress.State} {oldAddress.ZipCode}".Trim(' ', ',');
        var newAddr = $"{newAddress.Street}, {newAddress.City}, {newAddress.State} {newAddress.ZipCode}".Trim(' ', ',');
        if (oldAddr != newAddr) changes["Address"] = new FieldChange { OldValue = oldAddr, NewValue = newAddr };
        if (oldNotes != newNotes) changes["Notes"] = new FieldChange { OldValue = oldNotes, NewValue = newNotes };
        if (changes.Count > 0) App.EventLogService?.SetPendingChanges(changes);

        // Apply the Id rename FIRST so a failure doesn't leave the entity with new
        // field values but the old Id. Validation already prevents conflicts; this
        // ordering is defense-in-depth.
        if (hasIdChange)
        {
            try
            {
                App.CompanyManager?.ChangeSupplierId(supplierToEdit, newId);
            }
            catch (Exception ex)
            {
                App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Supplier.ChangeId");
                ModalIdError = ex.Message;
                return;
            }
        }

        supplierToEdit.Name = newName;
        supplierToEdit.Email = newEmail;
        supplierToEdit.Phone = newPhone;
        supplierToEdit.Website = newWebsite;
        supplierToEdit.Address = newAddress;
        supplierToEdit.Notes = newNotes;
        supplierToEdit.UpdatedAt = DateTime.UtcNow;

        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit supplier '{newName}'",
            () => {
                if (hasIdChange) App.CompanyManager?.ChangeSupplierId(supplierToEdit, oldId);
                supplierToEdit.Name = oldName; supplierToEdit.Email = oldEmail; supplierToEdit.Phone = oldPhone; supplierToEdit.Website = oldWebsite; supplierToEdit.Address = oldAddress; supplierToEdit.Notes = oldNotes;
                if (hasAvatarChanges) App.CompanyManager?.RestoreSupplierAvatar(supplierToEdit, oldAvatarBytes);
                companyData.MarkAsModified(); SupplierSaved?.Invoke(this, EventArgs.Empty);
            },
            () => {
                if (hasIdChange) App.CompanyManager?.ChangeSupplierId(supplierToEdit, newId);
                supplierToEdit.Name = newName; supplierToEdit.Email = newEmail; supplierToEdit.Phone = newPhone; supplierToEdit.Website = newWebsite; supplierToEdit.Address = newAddress; supplierToEdit.Notes = newNotes;
                if (hasAvatarChanges) App.CompanyManager?.RestoreSupplierAvatar(supplierToEdit, newAvatarBytes);
                companyData.MarkAsModified(); SupplierSaved?.Invoke(this, EventArgs.Empty);
            }));

        SupplierSaved?.Invoke(this, EventArgs.Empty);
        CloseEditModal();
    }

    #endregion

    #region Delete Supplier

    public async void OpenDeleteConfirm(SupplierDisplayItem? item)
    {
        try
        {
            if (item == null) return;

            // Check if supplier is in use
            var cd = App.CompanyManager?.CompanyData;
            if (cd != null)
            {
                var usages = new List<string>();
                if (cd.Products.Any(p => p.SupplierId == item.Id))
                    usages.Add("Product".Translate());
                if (cd.Expenses.Any(e => e.SupplierId == item.Id))
                    usages.Add("Expense".Translate());
                if (cd.PurchaseOrders.Any(po => po.SupplierId == item.Id))
                    usages.Add("Purchase Order".Translate());
                if (cd.Returns.Any(r => r.SupplierId == item.Id))
                    usages.Add("Return".Translate());
                if (RecurringTransactionService.IsSupplierInUse(cd, item.Id))
                    usages.Add("Recurring Expense".Translate());
                if (usages.Count > 0)
                {
                    await App.ShowWarningMessageBoxAsync(
                        "Cannot Delete".Translate(),
                        "This supplier cannot be deleted because it is referenced by one or more: {0}.".TranslateFormat(string.Join(", ", usages)));
                    return;
                }
            }

            var dialog = App.ConfirmationDialog;
            if (dialog == null) return;

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Supplier".Translate(),
                Message = "Are you sure you want to delete this supplier?\n\n{0}".TranslateFormat(item.Name),
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;

            var companyData = App.CompanyManager?.CompanyData;

            var supplier = companyData?.Suppliers.FirstOrDefault(s => s.Id == item.Id);
            if (supplier == null) return;

            var deletedSupplier = supplier;

            // Snapshot the avatar bytes BEFORE deleting so undo can restore the
            // file alongside the supplier record.
            var deletedAvatarBytes = App.CompanyManager?.ReadSupplierAvatarBytes(deletedSupplier);

            // Clean up the avatar file before removing the supplier, avoids bloat and
            // retention of deleted-supplier images in the .argo archive on next save.
            if (App.CompanyManager != null && !string.IsNullOrEmpty(deletedSupplier.AvatarFileName))
            {
                try { await App.CompanyManager.RemoveSupplierAvatarAsync(deletedSupplier); }
                catch (Exception ex) { App.ErrorLogger?.LogWarning($"Failed to remove supplier avatar on delete: {ex.Message}", "Supplier.Delete"); }
            }

            companyData?.Suppliers.Remove(supplier);
            companyData?.MarkAsModified();

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Delete supplier '{supplier.Name}'",
                () => {
                    companyData?.Suppliers.Add(deletedSupplier);
                    if (deletedAvatarBytes != null)
                        App.CompanyManager?.RestoreSupplierAvatar(deletedSupplier, deletedAvatarBytes);
                    companyData?.MarkAsModified();
                    SupplierDeleted?.Invoke(this, EventArgs.Empty);
                },
                () => {
                    if (deletedAvatarBytes != null)
                        App.CompanyManager?.RestoreSupplierAvatar(deletedSupplier, null);
                    companyData?.Suppliers.Remove(deletedSupplier);
                    companyData?.MarkAsModified();
                    SupplierDeleted?.Invoke(this, EventArgs.Empty);
                }));

            SupplierDeleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Supplier.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Filter Modal

    [RelayCommand]
    public void OpenFilterModal()
    {
        UpdateCountryOptions();
        CaptureOriginalFilterValues();
        IsFilterModalOpen = true;
    }

    [RelayCommand]
    public void CloseFilterModal()
    {
        IsFilterModalOpen = false;
    }

    /// <summary>
    /// Requests to close the Filter modal, showing confirmation if filters have been changed.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (HasFilterModalChanges)
        {
            if (!await ConfirmDiscardFiltersAsync())
                return;

            // Restore filter values to the state when modal was opened
            FilterCountry = _originalFilterCountry;
            FilterStatus = _originalFilterStatus;
        }

        CloseFilterModal();
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

    private void UpdateCountryOptions()
    {
        CountryOptions.Clear();
        CountryOptions.Add("All");

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var countries = companyData.Suppliers
            .Select(s => s.Address.Country)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c);

        foreach (var country in countries)
            CountryOptions.Add(country);
    }

    #endregion

    #region Property Changed Handlers

    partial void OnModalSupplierNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ModalSupplierNameError = null;
        }
        OnPropertyChanged(nameof(ModalInitialsPreview));
    }

    partial void OnModalEmailChanged(string value)
    {
        // Clear error when user modifies the field
        ModalEmailError = null;
    }

    partial void OnModalPhoneChanged(string value)
    {
        // Clear error when user modifies the field
        ModalPhoneError = null;
    }

    #endregion

    #region Helpers

    private void ResetFilterDefaults()
    {
        FilterCountry = "All";
        FilterStatus = "All";
    }

    private void ClearModalFields()
    {
        // Cancel and dispose any in-flight favicon fetch from a previous open of the modal.
        var fetchInFlight = _faviconCts;
        if (fetchInFlight != null)
        {
            fetchInFlight.Cancel();
            fetchInFlight.Dispose();
        }
        _faviconCts = null;

        ModalId = string.Empty;
        _originalId = string.Empty;
        ModalIdError = null;
        ModalSupplierName = string.Empty;
        ModalEmail = string.Empty;
        ModalPhone = string.Empty;
        ModalWebsite = string.Empty;
        ModalStreetAddress = string.Empty;
        ModalCity = string.Empty;
        ModalStateProvince = string.Empty;
        ModalZipCode = string.Empty;
        ModalCountry = string.Empty;
        ModalNotes = string.Empty;
        ModalError = null;
        ModalSupplierNameError = null;
        ModalEmailError = null;
        ModalPhoneError = null;

        ModalAvatarSource = null;
        HasModalAvatar = false;
        _pendingAvatarSourcePath = null;
        _pendingFaviconBytes = null;
        _shouldRemoveAvatarOnSave = false;
        _originalHasAvatar = false;
        OnPropertyChanged(nameof(ModalInitialsPreview));
    }

    private bool ValidateModal()
    {
        ModalError = null;
        ModalIdError = null;
        ModalSupplierNameError = null;
        ModalEmailError = null;
        ModalPhoneError = null;
        var isValid = true;

        var companyDataForId = App.CompanyManager?.CompanyData;
        if (companyDataForId != null)
        {
            var trimmedId = ModalId.Trim();
            if (_editingSupplier != null && string.IsNullOrEmpty(trimmedId))
            {
                ModalIdError = "ID cannot be empty.".Translate();
                isValid = false;
            }
            else if (!string.IsNullOrEmpty(trimmedId))
            {
                var existingWithSameId = companyDataForId.Suppliers.Any(s =>
                    s.Id == trimmedId &&
                    (_editingSupplier == null || !ReferenceEquals(s, _editingSupplier)));
                if (existingWithSameId)
                {
                    ModalIdError = "A supplier with this ID already exists.".Translate();
                    isValid = false;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(ModalSupplierName))
        {
            ModalSupplierNameError = "Supplier name is required.".Translate();
            isValid = false;
        }
        else
        {
            var companyData = App.CompanyManager?.CompanyData;
            var existingWithSameName = companyData?.Suppliers.Any(s =>
                s.Name.Equals(ModalSupplierName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (_editingSupplier == null || s.Id != _editingSupplier.Id)) ?? false;

            if (existingWithSameName)
            {
                ModalSupplierNameError = "A supplier with this name already exists.".Translate();
                isValid = false;
            }
        }

        if (!string.IsNullOrWhiteSpace(ModalEmail) && !ModalEmail.Contains('@'))
        {
            ModalEmailError = "Please enter a valid email address.".Translate();
            isValid = false;
        }

        if (!PhoneInput.IsFullPhoneComplete(ModalPhone))
        {
            ModalPhoneError = "Please enter a complete phone number.".Translate();
            isValid = false;
        }

        return isValid;
    }

    #endregion
}
