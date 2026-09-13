using System.Collections.ObjectModel;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for Returns modals (Filter, View Details, Undo).
/// </summary>
public partial class ReturnsModalsViewModel : ViewModelBase
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
    /// Raised when a return is undone.
    /// </summary>
    public event EventHandler? ReturnUndone;

    #endregion

    #region Filter Modal State

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private DateTimeOffset? _filterDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterDateTo;

    [ObservableProperty]
    private string _filterReason = "All";

    /// <summary>
    /// Reason filter options.
    /// </summary>
    public ObservableCollection<string> ReasonOptions { get; } = ["All", "Defective", "Wrong Item", "Not as Described", "Changed Mind", "Other"];

    #endregion

    #region Filter Modal Commands

    // Original filter values for change detection
    private DateTimeOffset? _originalFilterDateFrom;
    private DateTimeOffset? _originalFilterDateTo;
    private string _originalFilterReason = "All";

    /// <summary>
    /// Returns true if any filter has been changed from its original value when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterDateFrom != _originalFilterDateFrom ||
        FilterDateTo != _originalFilterDateTo ||
        FilterReason != _originalFilterReason;

    /// <summary>
    /// Captures the current filter values as the original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterDateFrom = FilterDateFrom;
        _originalFilterDateTo = FilterDateTo;
        _originalFilterReason = FilterReason;
    }

    /// <summary>
    /// Restores filter values to their original values when the modal was opened.
    /// </summary>
    private void RestoreOriginalFilterValues()
    {
        FilterDateFrom = _originalFilterDateFrom;
        FilterDateTo = _originalFilterDateTo;
        FilterReason = _originalFilterReason;
    }

    /// <summary>
    /// Resets all filter values to their defaults.
    /// </summary>
    private void ResetFilterDefaults()
    {
        FilterDateFrom = null;
        FilterDateTo = null;
        FilterReason = "All";
    }

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
    /// Requests to close the filter modal, showing confirmation if there are unapplied changes.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseFilterModalAsync()
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
    private string _viewDetailsDate = string.Empty;

    [ObservableProperty]
    private string _viewDetailsRefund = string.Empty;

    [ObservableProperty]
    private string _viewDetailsReason = string.Empty;

    [ObservableProperty]
    private string _viewDetailsNotes = string.Empty;

    /// <summary>
    /// Opens the view details modal with the specified return details.
    /// </summary>
    public void OpenViewDetailsModal(string id, string product, string date, string refund, string reason, string notes)
    {
        ViewDetailsId = id;
        ViewDetailsProduct = product;
        ViewDetailsDate = date;
        ViewDetailsRefund = refund;
        ViewDetailsReason = reason;
        ViewDetailsNotes = string.IsNullOrWhiteSpace(notes) ? "No notes provided" : notes;
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

    #region Undo Return Modal State

    private Return? _undoReturn;

    [ObservableProperty]
    private bool _isUndoReturnModalOpen;

    [ObservableProperty]
    private string _undoReturnItemDescription = string.Empty;

    [ObservableProperty]
    private string _undoReturnReason = string.Empty;

    /// <summary>
    /// Opens the undo return modal for the specified return.
    /// </summary>
    public void OpenUndoReturnModal(Return returnItem, string description)
    {
        _undoReturn = returnItem;
        UndoReturnItemDescription = description;
        UndoReturnReason = string.Empty;
        IsUndoReturnModalOpen = true;
    }

    /// <summary>
    /// Closes the undo return modal.
    /// </summary>
    [RelayCommand]
    private void CloseUndoReturnModal()
    {
        IsUndoReturnModalOpen = false;
        _undoReturn = null;
    }

    /// <summary>
    /// Confirms the undo return action.
    /// </summary>
    [RelayCommand]
    private void ConfirmUndoReturn()
    {
        if (_undoReturn == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        // Recording a return only adds this record (line quantities, stock and the transaction are
        // left alone), so undoing it only removes the record.
        var returnRecord = _undoReturn;
        companyData.Returns.Remove(returnRecord);
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Undo return '{returnRecord.Id}'",
            () =>
            {
                companyData.Returns.Add(returnRecord);
                companyData.MarkAsModified();
                ReturnUndone?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Returns.Remove(returnRecord);
                companyData.MarkAsModified();
                ReturnUndone?.Invoke(this, EventArgs.Empty);
            }));

        App.CompanyManager?.MarkAsChanged();
        CloseUndoReturnModal();
        ReturnUndone?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
