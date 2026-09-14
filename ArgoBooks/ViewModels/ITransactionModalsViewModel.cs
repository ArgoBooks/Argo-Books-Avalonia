using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// What the shared expense and revenue modals bind to. The view models' base class is generic,
/// which compiled XAML bindings cannot name.
/// </summary>
public interface ITransactionModalsViewModel
{
    event EventHandler? ScrollToLineItemsRequested;

    bool IsRevenue { get; }

    bool IsAddEditModalOpen { get; set; }
    string ModalTitle { get; }
    string SaveButtonText { get; }
    IAsyncRelayCommand RequestCloseAddEditModalCommand { get; }
    IRelayCommand CloseAddEditModalCommand { get; }
    IAsyncRelayCommand SaveTransactionCommand { get; }
    bool IsSavingTransaction { get; }
    bool HasValidationMessage { get; }

    DateTimeOffset? ModalDate { get; set; }
    ObservableCollection<string> PaymentMethodOptions { get; }
    string SelectedPaymentMethod { get; set; }

    ObservableCollection<CounterpartyOption> CounterpartyOptions { get; }
    CounterpartyOption? SelectedCounterparty { get; set; }
    bool HasCounterpartyError { get; }
    IRelayCommand OpenCreateCounterpartyCommand { get; }

    ICollection LineItems { get; }
    ObservableCollection<ProductOption> ProductOptions { get; }
    ObservableCollection<CategoryOption> CategoryOptions { get; }
    IRelayCommand AddLineItemCommand { get; }
    ICommand RemoveLineItemCommand { get; }
    ICommand OpenCreateProductCommand { get; }
    ICommand OpenCreateCategoryCommand { get; }

    decimal ModalTaxAmount { get; set; }
    decimal ModalShipping { get; set; }
    decimal ModalDiscount { get; set; }
    decimal ModalFee { get; set; }
    string TaxCurrencyLabel { get; }

    string ReceiptFileName { get; }
    bool HasReceipt { get; }
    IAsyncRelayCommand AttachReceiptCommand { get; }

    bool ModalPaid { get; set; }
    string ModalNotes { get; set; }

    string SubtotalFormatted { get; }
    string TaxAmountFormatted { get; }
    string ShippingAmountFormatted { get; }
    string DiscountAmountFormatted { get; }
    bool HasDiscount { get; }
    string FeeAmountFormatted { get; }
    string TotalFormatted { get; }
    bool HasTotalMismatchWarning { get; }
    string TotalMismatchWarningMessage { get; }

    bool HasSaveError { get; set; }
    string SaveErrorMessage { get; }
    IRelayCommand DismissSaveErrorCommand { get; }
    IAsyncRelayCommand RetrySaveCommand { get; }

    bool IsFilterModalOpen { get; set; }
    IAsyncRelayCommand RequestCloseFilterModalCommand { get; }
    IRelayCommand ApplyFiltersCommand { get; }
    IRelayCommand ClearFiltersCommand { get; }
    ObservableCollection<string> StatusFilterOptions { get; }
    string FilterStatus { get; set; }
    CounterpartyOption? FilterSelectedCounterparty { get; set; }
    CategoryOption? FilterSelectedCategory { get; set; }
    DateTimeOffset? FilterDateFrom { get; set; }
    DateTimeOffset? FilterDateTo { get; set; }
    string? FilterAmountMin { get; set; }
    string? FilterAmountMax { get; set; }
    ObservableCollection<string> ReceiptFilterOptions { get; }
    string FilterReceiptStatus { get; set; }

    bool IsItemStatusModalOpen { get; set; }
    string ItemStatusModalTitle { get; }
    string ItemStatusItemDescription { get; }
    ObservableCollection<string> CurrentReasonOptions { get; }
    string? SelectedItemStatusReason { get; set; }
    bool HasItemStatusReasonError { get; }
    string ItemStatusReasonErrorMessage { get; }
    string ItemStatusNotes { get; set; }
    string ItemStatusSaveButtonText { get; }
    IRelayCommand CloseItemStatusModalCommand { get; }
    IRelayCommand ConfirmItemStatusCommand { get; }
}
