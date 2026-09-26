using ArgoBooks.Localization;
using ArgoBooks.Services;
using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for revenue modals (Add, Edit, Delete, Filter).
/// </summary>
public partial class RevenueModalsViewModel : TransactionModalsViewModelBase<RevenueDisplayItem, RevenueLineItem>
{
    public RevenueModalsViewModel()
    {
        // Reset ModalPaid to true when opening the add modal
        PropertyChanged += OnRevenuePropertyChanged;
    }

    private void OnRevenuePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsAddEditModalOpen) && IsAddEditModalOpen && !IsEditMode)
            ModalPaid = true;
    }

    #region Abstract Property Implementations

    protected override string TransactionTypeName => "Revenue";
    protected override string CounterpartyName => "Customer";
    protected override CategoryType CategoryTypeFilter => CategoryType.Revenue;
    protected override bool UseCostPrice => false;
    protected override bool AllowsTypedItems => true;

    #endregion

    #region Events (Revenue-specific aliases)

    public event EventHandler? RevenueSaved
    {
        add => TransactionSaved += value;
        remove => TransactionSaved -= value;
    }

    public event EventHandler? RevenueDeleted
    {
        add => TransactionDeleted += value;
        remove => TransactionDeleted -= value;
    }

    #endregion

    #region Revenue-Specific Properties

    // Customer alias for counterparty
    public CounterpartyOption? SelectedCustomer
    {
        get => SelectedCounterparty;
        set => SelectedCounterparty = value;
    }

    // Notify SelectedCustomer when SelectedCounterparty changes so UI bindings update
    protected override void OnCounterpartyChanged(CounterpartyOption? value)
    {
        OnPropertyChanged(nameof(SelectedCustomer));
    }

    public ObservableCollection<CounterpartyOption> CustomerOptions => CounterpartyOptions;

    public string? FilterCustomerId
    {
        get => FilterCounterpartyId;
        set => FilterCounterpartyId = value;
    }

    #endregion

    #region Reason Options

    public override ObservableCollection<string> LostDamagedReasonOptions { get; } =
    [
        "Damaged in transit",
        "Defective product",
        "Lost in warehouse",
        "Damaged during storage",
        "Customer damaged",
        "Other"
    ];

    public override ObservableCollection<string> ReturnReasonOptions { get; } =
    [
        "Customer return",
        "Wrong item sent",
        "Quality issues",
        "Not as Described",
        "Changed Mind",
        "Defective",
        "Other"
    ];

    public override ObservableCollection<string> UndoReasonOptions { get; } =
    [
        "Item found",
        "Damage was repairable",
        "Customer changed mind",
        "Incorrect status",
        "Administrative error",
        "Other"
    ];

    #endregion

    #region Data Loading

    protected override IEnumerable<CounterpartyOption> GetCounterpartyOptions() =>
        OptionLoader.Customers(App.CompanyManager?.CompanyData).AsOptions<CounterpartyOption>();

    #endregion

    #region Edit Modal

    public override void OpenEditModal(RevenueDisplayItem? item)
    {
        if (item == null) return;

        var revenue = App.CompanyManager?.CompanyData?.Revenues.FirstOrDefault(s => s.Id == item.Id);
        if (revenue == null) return;

        LoadCounterpartyOptions();
        LoadCategoryOptions();
        LoadProductOptions();

        EditingTransactionId = revenue.Id;
        IsEditMode = true;
        ModalTitle = $"Edit Revenue {revenue.Id}";
        SaveButtonText = "Save Changes";

        SelectedCustomer = CustomerOptions.FirstOrDefault(c => c.Id == revenue.CustomerId);
        ModalPaid = RevenueAggregator.IsCollected(revenue);
        PopulateFormFromTransaction(revenue);

        IsAddEditModalOpen = true;
    }

    #endregion

    #region Delete

    public async void OpenDeleteConfirm(RevenueDisplayItem? item)
    {
        try
        {
            if (item == null) return;

            var dialog = App.ConfirmationDialog;
            if (dialog == null) return;

            // Block deletion if revenue is linked to a portal-published invoice
            var companyData = App.CompanyManager?.CompanyData;
            if (!string.IsNullOrEmpty(item.InvoiceId))
            {
                var linkedInvoice = companyData?.Invoices.FirstOrDefault(i => i.Id == item.InvoiceId);
                if (linkedInvoice?.History.Any(h => h.Action == "Published to Portal") == true)
                {
                    await dialog.ShowAsync(new ConfirmationDialogOptions
                    {
                        Title = "Cannot Delete Revenue".Translate(),
                        Message = "This revenue is linked to an invoice that has been published to the payment portal and cannot be deleted.".Translate(),
                        PrimaryButtonText = "OK".Translate(),
                        CancelButtonText = null,
                        IsPrimaryDestructive = false
                    });
                    return;
                }
            }

            // Removing only the revenue would leave the rental marked paid with no money behind it.
            var rental = companyData?.Rentals.FirstOrDefault(r => r.RevenueId == item.Id);
            if (rental != null)
            {
                await dialog.ShowAsync(new ConfirmationDialogOptions
                {
                    Title = "Cannot Delete Revenue".Translate(),
                    Message = "This revenue was recorded when rental {0} was marked paid. To remove it, mark the rental unpaid on the Rental Records page.".TranslateFormat(rental.Id),
                    PrimaryButtonText = "OK".Translate(),
                    CancelButtonText = null,
                    IsPrimaryDestructive = false
                });
                return;
            }

            if (!await ConfirmDeleteAsync("Delete Revenue".Translate(),
                    "Are you sure you want to delete this revenue?\n\nID: {0}\nProduct: {1}\nAmount: {2}".TranslateFormat(item.Id, item.ProductDescription, item.TotalFormatted)))
                return;

            DeleteRevenue(item.Id);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Revenue.OpenDeleteConfirm");
        }
    }

    internal void DeleteRevenue(string revenueId)
    {
        var companyData = App.CompanyManager?.CompanyData;

        var revenue = companyData?.Revenues.FirstOrDefault(s => s.Id == revenueId);
        if (companyData == null || revenue == null) return;

        DeleteTransactionWithUndo(companyData, companyData.Revenues, revenue, isExpense: false);
    }

    #endregion

    #region Item Status Modal

    public void OpenMarkAsLostDamagedModal(RevenueDisplayItem? item)
    {
        OpenItemStatusModal(item, nameof(ArgoBooks.Core.Enums.ItemStatusAction.LostDamaged), "Mark as Lost / Damaged", "Mark as Lost / Damaged", false, LostDamagedReasonOptions);
    }

    public void OpenMarkAsReturnedModal(RevenueDisplayItem? item)
    {
        OpenItemStatusModal(item, nameof(ArgoBooks.Core.Enums.ItemStatusAction.Returned), "Mark as Returned", "Mark as Returned", false, ReturnReasonOptions);
    }

    public void OpenUndoLostDamagedModal(RevenueDisplayItem? item)
    {
        OpenItemStatusModal(item, nameof(ArgoBooks.Core.Enums.ItemStatusAction.UndoLostDamaged), "Undo Lost / Damaged Status", "Undo Status", true, UndoReasonOptions);
    }

    public void OpenUndoReturnedModal(RevenueDisplayItem? item)
    {
        OpenItemStatusModal(item, nameof(ArgoBooks.Core.Enums.ItemStatusAction.UndoReturned), "Undo Returned Status", "Undo Status", true, UndoReasonOptions);
    }

    protected override string GetItemStatusDescription(RevenueDisplayItem item)
    {
        return $"{item.Id} - {item.ProductDescription}";
    }

    protected override void ConfirmItemStatus()
    {
        if (ItemStatusItem == null)
        {
            CloseItemStatusModal();
            return;
        }

        if (string.IsNullOrEmpty(SelectedItemStatusReason))
        {
            HasItemStatusReasonError = true;
            ItemStatusReasonErrorMessage = "Please select a reason".Translate();
            return;
        }

        HasItemStatusReasonError = false;
        ItemStatusReasonErrorMessage = string.Empty;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            CloseItemStatusModal();
            return;
        }

        var revenue = companyData.Revenues.FirstOrDefault(s => s.Id == ItemStatusItem.Id);
        if (revenue == null)
        {
            CloseItemStatusModal();
            return;
        }

        if (!Enum.TryParse<ItemStatusAction>(ItemStatusAction, out var parsedAction))
        {
            CloseItemStatusModal();
            return;
        }

        switch (parsedAction)
        {
            case ArgoBooks.Core.Enums.ItemStatusAction.LostDamaged:
            {
                var record = CreateLostDamagedRecord(companyData, revenue);
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    $"Mark revenue '{revenue.Id}' as lost/damaged",
                    () =>
                    {
                        companyData.LostDamaged.Remove(record);
                        companyData.MarkAsModified();
                        RaiseTransactionSaved();
                    },
                    () =>
                    {
                        companyData.LostDamaged.Add(record);
                        companyData.MarkAsModified();
                        RaiseTransactionSaved();
                    }));
                break;
            }
            case ArgoBooks.Core.Enums.ItemStatusAction.Returned:
            {
                var record = CreateReturnRecord(companyData, revenue);
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    $"Mark revenue '{revenue.Id}' as returned",
                    () =>
                    {
                        companyData.Returns.Remove(record);
                        companyData.MarkAsModified();
                        RaiseTransactionSaved();
                    },
                    () =>
                    {
                        companyData.Returns.Add(record);
                        companyData.MarkAsModified();
                        RaiseTransactionSaved();
                    }));
                break;
            }
            case ArgoBooks.Core.Enums.ItemStatusAction.UndoLostDamaged:
            {
                var record = companyData.LostDamaged.FirstOrDefault(ld => ld.InventoryItemId == revenue.Id);
                if (record != null)
                {
                    companyData.LostDamaged.Remove(record);
                    App.UndoRedoManager.RecordAction(new DelegateAction(
                        $"Undo lost/damaged status for revenue '{revenue.Id}'",
                        () =>
                        {
                            companyData.LostDamaged.Add(record);
                            companyData.MarkAsModified();
                            RaiseTransactionSaved();
                        },
                        () =>
                        {
                            companyData.LostDamaged.Remove(record);
                            companyData.MarkAsModified();
                            RaiseTransactionSaved();
                        }));
                }
                break;
            }
            case ArgoBooks.Core.Enums.ItemStatusAction.UndoReturned:
            {
                var record = companyData.Returns.FirstOrDefault(r => r.OriginalTransactionId == revenue.Id);
                if (record != null)
                {
                    companyData.Returns.Remove(record);
                    App.UndoRedoManager.RecordAction(new DelegateAction(
                        $"Undo returned status for revenue '{revenue.Id}'",
                        () =>
                        {
                            companyData.Returns.Add(record);
                            companyData.MarkAsModified();
                            RaiseTransactionSaved();
                        },
                        () =>
                        {
                            companyData.Returns.Remove(record);
                            companyData.MarkAsModified();
                            RaiseTransactionSaved();
                        }));
                }
                break;
            }
        }

        App.CompanyManager?.MarkAsChanged();
        CloseItemStatusModal();
        RaiseTransactionSaved();
    }

    private LostDamaged CreateLostDamagedRecord(CompanyData companyData, Revenue revenue)
    {
        var reason = MapToLostDamagedReason(SelectedItemStatusReason ?? "Other");
        var productId = revenue.LineItems.FirstOrDefault()?.ProductId ?? "";
        // Use pre-tax subtotal: the actual product value, not tax/shipping/fees
        var valueLost = revenue.Subtotal > 0 ? revenue.Subtotal : revenue.Amount;

        var lostDamaged = new LostDamaged
        {
            Id = new IdGenerator(companyData).NextLostDamagedId(),
            ProductId = productId,
            InventoryItemId = revenue.Id,
            Quantity = (int)revenue.Quantity,
            Reason = reason,
            DateDiscovered = DateTime.UtcNow,
            ValueLost = valueLost,
            Notes = $"From revenue {revenue.Id}. {ItemStatusNotes}".Trim(),
            InsuranceClaim = false,
            CreatedAt = DateTime.UtcNow
        };

        companyData.LostDamaged.Add(lostDamaged);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.LostDamagedRecorded);
        return lostDamaged;
    }

    private Return CreateReturnRecord(CompanyData companyData, Revenue revenue)
    {
        var productId = revenue.LineItems.FirstOrDefault()?.ProductId ?? "";

        var returnRecord = new Return
        {
            Id = new IdGenerator(companyData).NextReturnId(),
            OriginalTransactionId = revenue.Id,
            ReturnType = "Customer",
            SupplierId = "",
            CustomerId = revenue.CustomerId ?? "",
            ReturnDate = DateTime.UtcNow,
            Items =
            [
                new ReturnItem
                {
                    ProductId = productId,
                    Quantity = (int)revenue.Quantity,
                    Reason = SelectedItemStatusReason ?? "Other"
                }
            ],
            // Refund the product amount + tax, excluding shipping/fees
            RefundAmount = (revenue.Subtotal > 0 ? revenue.Subtotal : revenue.Amount) + revenue.TaxAmount,
            RestockingFee = 0,
            Status = ReturnStatus.Completed,
            Notes = ItemStatusNotes,
            ProcessedBy = revenue.AccountantId ?? "",
            CreatedAt = DateTime.UtcNow
        };

        companyData.Returns.Add(returnRecord);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ReturnRecorded);
        return returnRecord;
    }

    #endregion

    #region Save Implementation

    protected override void SaveNewTransaction(CompanyData companyData)
    {
        var date = ModalDate?.DateTime ?? DateTime.Now;
        var revenueId = new Core.Data.IdGenerator(companyData).NextRevenueId(date);

        var (description, totalQuantity, averageUnitPrice) = GetLineItemSummary();
        var modelLineItems = CreateModelLineItems();

        var revenue = new Revenue
        {
            Id = revenueId,
            Date = date,
            CustomerId = SelectedCustomer?.Id,
            Description = description,
            LineItems = modelLineItems,
            Quantity = totalQuantity,
            UnitPrice = averageUnitPrice,
            Amount = Subtotal,
            TaxRate = Subtotal > 0 ? (TaxAmount / Subtotal) * 100 : 0,
            TaxAmount = TaxAmount,
            ShippingCost = ModalShipping,
            Discount = ModalDiscount,
            Fee = ModalFee,
            Total = Total,
            PaymentMethod = Enum.TryParse<PaymentMethod>(SelectedPaymentMethod.Replace(" ", ""), out var pm) ? pm : PaymentMethod.Cash,
            PaymentStatus = ModalPaid ? RevenuePaymentStatus.Paid : RevenuePaymentStatus.Unpaid,
            Notes = ModalNotes,
            ReferenceNumber = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            OriginalCurrency = SaveCurrency
        };
        UsdConversion.Apply(companyData, revenue, SaveRate);

        // Create Receipt if file was attached
        Receipt? receipt = null;
        if (!string.IsNullOrEmpty(ReceiptFilePath))
        {
            receipt = CreateReceipt(companyData, revenueId, "Revenue", SelectedCustomer?.Name ?? "");
            if (receipt != null)
            {
                revenue.ReceiptId = receipt.Id;
                companyData.Receipts.Add(receipt);
            }
        }

        companyData.Revenues.Add(revenue);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.RevenueCreated);

        // Adjust inventory for tracked products
        var inventoryResults = AdjustInventoryForLineItems(companyData, revenue, modelLineItems, isExpense: false);

        var capturedReceipt = receipt;
        var action = new DelegateAction(
            $"Add revenue {revenueId}",
            () =>
            {
                companyData.Revenues.RemoveRecord(revenue);
                UsdConversion.Set(companyData, UsdConversion.KeyOf(revenue), null);
                if (capturedReceipt != null)
                    companyData.Receipts.RemoveRecord(capturedReceipt);
                RevertInventoryAdjustments(companyData, inventoryResults);
                RaiseTransactionSaved();
            },
            () =>
            {
                companyData.Revenues.RestoreRecord(revenue);
                UsdConversion.Requeue(companyData, revenue);
                if (capturedReceipt != null)
                    companyData.Receipts.RestoreRecord(capturedReceipt);
                inventoryResults = AdjustInventoryForLineItems(companyData, revenue, modelLineItems, isExpense: false);
                RaiseTransactionSaved();
            });

        App.UndoRedoManager.RecordAction(action);
        App.CompanyManager?.MarkAsChanged();
        RaiseTransactionSaved();

        ShowInventoryNotifications(inventoryResults, isExpense: false);

        // Mark the setup checklist item as complete
        TutorialService.Instance.CompleteChecklistItem(TutorialService.ChecklistItems.RecordRevenue);
    }

    protected override void SaveEditedTransaction(CompanyData companyData)
    {
        var revenue = companyData.Revenues.FirstOrDefault(s => s.Id == EditingTransactionId);
        if (revenue == null) return;

        // Store original values for undo
        var original = CaptureTransactionState(revenue);
        var queueKey = UsdConversion.KeyOf(revenue);
        var originalQueued = UsdConversion.Snapshot(companyData, [queueKey]);

        var (description, totalQuantity, averageUnitPrice) = GetLineItemSummary();
        var modelLineItems = CreateModelLineItems();

        // Apply changes
        revenue.Date = ModalDate?.DateTime ?? DateTime.Now;
        revenue.CustomerId = SelectedCustomer?.Id;
        revenue.Description = description;
        revenue.LineItems = modelLineItems;
        revenue.Quantity = totalQuantity;
        revenue.UnitPrice = averageUnitPrice;
        revenue.Amount = Subtotal;
        revenue.TaxRate = Subtotal > 0 ? (TaxAmount / Subtotal) * 100 : 0;
        revenue.TaxAmount = TaxAmount;
        revenue.ShippingCost = ModalShipping;
        revenue.Discount = ModalDiscount;
        revenue.Fee = ModalFee;
        revenue.Total = Total;
        revenue.PaymentMethod = Enum.TryParse<PaymentMethod>(SelectedPaymentMethod.Replace(" ", ""), out var pm) ? pm : PaymentMethod.Cash;
        // Only a change to the Paid box changes the status, so an edit keeps Complete, Partial,
        // Pending or Overdue as they were.
        if (ModalPaid != RevenueAggregator.IsCollected(revenue))
            revenue.PaymentStatus = ModalPaid ? RevenuePaymentStatus.Paid : RevenuePaymentStatus.Unpaid;
        revenue.Notes = ModalNotes;
        revenue.UpdatedAt = DateTime.UtcNow;
        revenue.OriginalCurrency = SaveCurrency;
        UsdConversion.Apply(companyData, revenue, SaveRate);
        var editedQueued = UsdConversion.Snapshot(companyData, [queueKey]);

        // Handle receipt. The form loads an existing receipt's OriginalFilePath, so only a different
        // path means Change picked a new file.
        var currentReceipt = string.IsNullOrEmpty(original.ReceiptId)
            ? null
            : companyData.Receipts.FirstOrDefault(r => r.Id == original.ReceiptId);
        Receipt? newReceipt = null;
        Receipt? replacedReceipt = null;
        if (!string.IsNullOrEmpty(ReceiptFilePath) && ReceiptFilePath != currentReceipt?.OriginalFilePath)
        {
            newReceipt = CreateReceipt(companyData, revenue.Id, "Revenue", SelectedCustomer?.Name ?? "");
            if (newReceipt != null)
            {
                if (currentReceipt != null && companyData.Receipts.Remove(currentReceipt))
                    replacedReceipt = currentReceipt;
                revenue.ReceiptId = newReceipt.Id;
                companyData.Receipts.Add(newReceipt);
            }
        }

        // Adjust inventory with net diff (single adjustment per product)
        var editResults = AdjustInventoryForEdit(companyData, revenue, original.LineItems, modelLineItems, isExpense: false);

        var capturedNewReceipt = newReceipt;
        // Snapshot the NEW state so redo restores the edit itself.
        var edited = CaptureTransactionState(revenue);
        var action = new DelegateAction(
            $"Edit revenue {EditingTransactionId}",
            () =>
            {
                RestoreTransactionState(revenue, original);
                UsdConversion.Restore(companyData, [queueKey], originalQueued);
                if (capturedNewReceipt != null)
                    companyData.Receipts.RemoveRecord(capturedNewReceipt);
                if (replacedReceipt != null)
                    companyData.Receipts.RestoreRecord(replacedReceipt);
                RevertInventoryAdjustments(companyData, editResults);
                RaiseTransactionSaved();
            },
            () =>
            {
                RestoreTransactionState(revenue, edited);
                UsdConversion.Restore(companyData, [queueKey], editedQueued);
                if (replacedReceipt != null)
                    companyData.Receipts.RemoveRecord(replacedReceipt);
                if (capturedNewReceipt != null)
                    companyData.Receipts.RestoreRecord(capturedNewReceipt);
                editResults = AdjustInventoryForEdit(companyData, revenue, original.LineItems, modelLineItems, isExpense: false);
                RaiseTransactionSaved();
            });

        App.UndoRedoManager.RecordAction(action);
        App.CompanyManager?.MarkAsChanged();
        RaiseTransactionSaved();

        ShowEditInventoryNotifications(editResults);
    }

    private Receipt? CreateReceipt(CompanyData companyData, string transactionId, string transactionType, string supplier)
    {
        if (string.IsNullOrEmpty(ReceiptFilePath)) return null;

        var receiptId = new IdGenerator(companyData).NextReceiptId();
        var fileInfo = new FileInfo(ReceiptFilePath);
        var fileType = GetFileType(ReceiptFilePath);

        string? fileData = null;
        if (fileInfo.Exists)
        {
            try
            {
                var bytes = SharedFileReader.ReadAllBytes(ReceiptFilePath);
                fileData = Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to read receipt file");
                App.AddNotification("Warning".Translate(), "Could not attach receipt file: {0}".TranslateFormat(ex.Message), NotificationType.Warning);
            }
        }

        return new Receipt
        {
            Id = receiptId,
            TransactionId = transactionId,
            TransactionType = transactionType,
            FileName = fileInfo.Name,
            FileType = fileType,
            FileSize = fileInfo.Exists ? fileInfo.Length : 0,
            FileData = fileData,
            OriginalFilePath = ReceiptFilePath,
            Amount = Total,
            Date = ModalDate?.DateTime ?? DateTime.Now,
            Supplier = supplier,
            Source = "Manual",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static TransactionState CaptureTransactionState(Revenue revenue)
    {
        return new TransactionState
        {
            Date = revenue.Date,
            CounterpartyId = revenue.CustomerId,
            Description = revenue.Description,
            LineItems = revenue.LineItems.ToList(),
            Quantity = revenue.Quantity,
            UnitPrice = revenue.UnitPrice,
            Amount = revenue.Amount,
            TaxRate = revenue.TaxRate,
            TaxAmount = revenue.TaxAmount,
            ShippingCost = revenue.ShippingCost,
            Discount = revenue.Discount,
            Fee = revenue.Fee,
            Total = revenue.Total,
            PaymentMethod = revenue.PaymentMethod,
            PaymentStatus = revenue.PaymentStatus,
            Notes = revenue.Notes,
            ReferenceNumber = revenue.ReferenceNumber,
            ReceiptId = revenue.ReceiptId,
            IsPendingConversion = revenue.IsPendingConversion,
            OriginalCurrency = revenue.OriginalCurrency,
            TotalUSD = revenue.TotalUSD,
            UnitPriceUSD = revenue.UnitPriceUSD,
            ShippingCostUSD = revenue.ShippingCostUSD,
            TaxAmountUSD = revenue.TaxAmountUSD,
            DiscountUSD = revenue.DiscountUSD,
            FeeUSD = revenue.FeeUSD
        };
    }

    private static void RestoreTransactionState(Revenue revenue, TransactionState state)
    {
        revenue.Date = state.Date;
        revenue.CustomerId = state.CounterpartyId;
        revenue.Description = state.Description;
        revenue.LineItems = state.LineItems;
        revenue.Quantity = state.Quantity;
        revenue.UnitPrice = state.UnitPrice;
        revenue.Amount = state.Amount;
        revenue.TaxRate = state.TaxRate;
        revenue.TaxAmount = state.TaxAmount;
        revenue.ShippingCost = state.ShippingCost;
        revenue.Discount = state.Discount;
        revenue.Fee = state.Fee;
        revenue.Total = state.Total;
        revenue.PaymentMethod = state.PaymentMethod;
        revenue.PaymentStatus = state.PaymentStatus;
        revenue.Notes = state.Notes;
        revenue.ReferenceNumber = state.ReferenceNumber;
        revenue.ReceiptId = state.ReceiptId;
        revenue.IsPendingConversion = state.IsPendingConversion;
        revenue.OriginalCurrency = state.OriginalCurrency;
        revenue.TotalUSD = state.TotalUSD;
        revenue.UnitPriceUSD = state.UnitPriceUSD;
        revenue.ShippingCostUSD = state.ShippingCostUSD;
        revenue.TaxAmountUSD = state.TaxAmountUSD;
        revenue.DiscountUSD = state.DiscountUSD;
        revenue.FeeUSD = state.FeeUSD;
    }

    #endregion

}

/// <summary>
/// Line item for revenue form.
/// </summary>
public class RevenueLineItem : TransactionLineItemBase
{
}
