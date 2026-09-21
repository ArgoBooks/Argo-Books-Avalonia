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

    private sealed record FilterValues(DateTimeOffset? DateFrom, DateTimeOffset? DateTo, string Reason)
    {
        public static readonly FilterValues Default = new(null, null, "All");
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterDateFrom, FilterDateTo, FilterReason),
        v =>
        {
            FilterDateFrom = v.DateFrom;
            FilterDateTo = v.DateTo;
            FilterReason = v.Reason;
        });

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    public void OpenFilterModal()
    {
        Filters.Capture();
        IsFilterModalOpen = true;
    }

    private void CloseFilterModal() => IsFilterModalOpen = false;

    /// <summary>
    /// Closes the filter modal, asking first and putting the filters back if they were changed.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
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
        Filters.Reset();
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
        if (companyData == null)
        {
            CloseUndoReturnModal();
            return;
        }

        // Recording a return only adds this record (line quantities, stock and the transaction are
        // left alone), so undoing it only removes the record.
        var returnRecord = _undoReturn;
        RemoveWithUndo(companyData, companyData.Returns, returnRecord, $"Undo return '{returnRecord.Id}'",
            () => ReturnUndone?.Invoke(this, EventArgs.Empty));

        App.CompanyManager?.MarkAsChanged();
        CloseUndoReturnModal();
    }

    #endregion
}
