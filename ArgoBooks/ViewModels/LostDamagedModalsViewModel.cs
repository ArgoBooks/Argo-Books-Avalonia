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

    private sealed record FilterValues(string Type, DateTimeOffset? DateFrom, DateTimeOffset? DateTo, string Reason)
    {
        public static readonly FilterValues Default = new("All", null, null, "All");
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterType, FilterDateFrom, FilterDateTo, FilterReason),
        v =>
        {
            FilterType = v.Type;
            FilterDateFrom = v.DateFrom;
            FilterDateTo = v.DateTo;
            FilterReason = v.Reason;
        });

    #endregion

    #region Filter Modal Commands

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
    public async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
            CloseFilterModal();
    }

    /// <summary>How many filters are applied, for the page's Filter button.</summary>
    public int ActiveFilterCount { get; private set; }

    /// <summary>
    /// Applies the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        ActiveFilterCount = Filters.ActiveCount;
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
        ActiveFilterCount = 0;
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
        if (lostDamagedRecord == null)
        {
            CloseUndoItemModal();
            ItemUndone?.Invoke(this, EventArgs.Empty);
            return;
        }

        RemoveWithUndo(companyData, companyData.LostDamaged, lostDamagedRecord, $"Undo lost/damaged '{lostDamagedRecord.Id}'",
            () => ItemUndone?.Invoke(this, EventArgs.Empty));
        App.CompanyManager?.MarkAsChanged();
        CloseUndoItemModal();
    }

    #endregion
}
