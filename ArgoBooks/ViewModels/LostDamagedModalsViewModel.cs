using System.Collections.ObjectModel;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for Lost/Damaged modals (Filter, View Details, Undo).
/// </summary>
public partial class LostDamagedModalsViewModel : ViewModelBase
{
    #region Events

    /// <summary>
    /// Raised when filters are applied.
    /// </summary>
    public event EventHandler? FiltersApplied;

    /// <summary>
    /// Raised when filters are cleared.
    /// </summary>
    public event EventHandler? FiltersCleared;

    /// <summary>
    /// Raised when an item is undone.
    /// </summary>
    public event EventHandler? ItemUndone;

    #endregion

    #region Filter Modal State

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private string _filterType = "All";

    [ObservableProperty]
    private DateTimeOffset? _filterDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterDateTo;

    [ObservableProperty]
    private string _filterReason = "All";

    /// <summary>
    /// Type filter options.
    /// </summary>
    public ObservableCollection<string> TypeOptions { get; } = ["All", "Lost", "Damaged"];

    /// <summary>
    /// Reason filter options.
    /// </summary>
    public ObservableCollection<string> ReasonOptions { get; } = ["All", "Theft", "Breakage", "Spoilage", "Missing", "Other"];

    // Original filter values for change detection
    private string _originalFilterType = "All";
    private DateTimeOffset? _originalFilterDateFrom;
    private DateTimeOffset? _originalFilterDateTo;
    private string _originalFilterReason = "All";

    /// <summary>
    /// Returns true if any filter has been changed from its original value when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterType != _originalFilterType ||
        FilterDateFrom != _originalFilterDateFrom ||
        FilterDateTo != _originalFilterDateTo ||
        FilterReason != _originalFilterReason;

    /// <summary>
    /// Captures the current filter values as the original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterType = FilterType;
        _originalFilterDateFrom = FilterDateFrom;
        _originalFilterDateTo = FilterDateTo;
        _originalFilterReason = FilterReason;
    }

    /// <summary>
    /// Restores filter values to their original values when the modal was opened.
    /// </summary>
    private void RestoreOriginalFilterValues()
    {
        FilterType = _originalFilterType;
        FilterDateFrom = _originalFilterDateFrom;
        FilterDateTo = _originalFilterDateTo;
        FilterReason = _originalFilterReason;
    }

    #endregion

    #region Filter Modal Commands

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
    /// Resets all filter values to their defaults.
    /// </summary>
    private void ResetFilterDefaults()
    {
        FilterType = "All";
        FilterDateFrom = null;
        FilterDateTo = null;
        FilterReason = "All";
    }

    /// <summary>
    /// Requests to close the filter modal, showing confirmation if there are unapplied changes.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (HasFilterModalChanges)
        {
            if (!await ConfirmDiscardFiltersAsync()) return;

            RestoreOriginalFilterValues();
        }

        CloseFilterModal();
    }

    /// <summary>
    /// Applies the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        ResetFilterDefaults();
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    #endregion

    #region View Details Modal State

    [ObservableProperty]
    private bool _isViewDetailsModalOpen;

    [ObservableProperty]
    private string _viewDetailsId = string.Empty;

    [ObservableProperty]
    private string _viewDetailsProduct = string.Empty;

    [ObservableProperty]
    private string _viewDetailsType = string.Empty;

    [ObservableProperty]
    private string _viewDetailsReason = string.Empty;

    [ObservableProperty]
    private string _viewDetailsNotes = string.Empty;

    [ObservableProperty]
    private string _viewDetailsDate = string.Empty;

    [ObservableProperty]
    private string _viewDetailsValue = string.Empty;

    [ObservableProperty]
    private string _viewDetailsQuantity = string.Empty;

    #endregion

    #region View Details Modal Commands

    /// <summary>
    /// Opens the view details modal with the specified item data.
    /// </summary>
    public void OpenViewDetailsModal(string id, string product, string type, string reason, string notes, string date, string value, string quantity)
    {
        ViewDetailsId = id;
        ViewDetailsProduct = product;
        ViewDetailsType = type;
        ViewDetailsReason = reason;
        ViewDetailsNotes = string.IsNullOrWhiteSpace(notes) ? "No notes provided" : notes;
        ViewDetailsDate = date;
        ViewDetailsValue = value;
        ViewDetailsQuantity = quantity;
        IsViewDetailsModalOpen = true;
    }

    /// <summary>
    /// Closes the view details modal.
    /// </summary>
    [RelayCommand]
    private void CloseViewDetailsModal()
    {
        IsViewDetailsModalOpen = false;
    }

    #endregion

    #region Undo Item Modal State

    private LostDamaged? _undoItem;

    [ObservableProperty]
    private bool _isUndoItemModalOpen;

    [ObservableProperty]
    private string _undoItemDescription = string.Empty;

    [ObservableProperty]
    private string _undoItemReason = string.Empty;

    #endregion

    #region Undo Item Modal Commands

    /// <summary>
    /// Opens the undo item modal with the specified item.
    /// </summary>
    public void OpenUndoItemModal(LostDamaged item, string description)
    {
        _undoItem = item;
        UndoItemDescription = description;
        UndoItemReason = string.Empty;
        IsUndoItemModalOpen = true;
    }

    /// <summary>
    /// Closes the undo item modal.
    /// </summary>
    [RelayCommand]
    private void CloseUndoItemModal()
    {
        IsUndoItemModalOpen = false;
        _undoItem = null;
        UndoItemReason = string.Empty;
    }

    /// <summary>
    /// Confirms the undo operation and removes the item.
    /// </summary>
    [RelayCommand]
    private void ConfirmUndoItem()
    {
        if (_undoItem == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            CloseUndoItemModal();
            return;
        }

        var lostDamagedRecord = companyData.LostDamaged.FirstOrDefault(ld => ld.Id == _undoItem.Id);
        if (lostDamagedRecord != null)
        {
            companyData.LostDamaged.Remove(lostDamagedRecord);
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Undo lost/damaged '{lostDamagedRecord.Id}'",
                () =>
                {
                    companyData.LostDamaged.Add(lostDamagedRecord);
                    companyData.MarkAsModified();
                    ItemUndone?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    companyData.LostDamaged.Remove(lostDamagedRecord);
                    companyData.MarkAsModified();
                    ItemUndone?.Invoke(this, EventArgs.Empty);
                }));
            App.CompanyManager?.MarkAsChanged();
        }

        CloseUndoItemModal();
        ItemUndone?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
