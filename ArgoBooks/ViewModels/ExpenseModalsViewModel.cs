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
/// ViewModel for expense modals (Add, Edit, Delete, Filter).
/// </summary>
public partial class ExpenseModalsViewModel : TransactionModalsViewModelBase<ExpenseDisplayItem, ExpenseLineItem>
{
    #region Abstract Property Implementations

    protected override string TransactionTypeName => "Expense";
    protected override string CounterpartyName => "Supplier";
    protected override CategoryType CategoryTypeFilter => CategoryType.Expense;
    protected override bool UseCostPrice => true;
    protected override bool AllowsTypedItems => true;

    #endregion

    #region Events (Expense-specific aliases)

    public event EventHandler? ExpenseSaved
    {
        add => TransactionSaved += value;
        remove => TransactionSaved -= value;
    }

    public event EventHandler? ExpenseDeleted
    {
        add => TransactionDeleted += value;
        remove => TransactionDeleted -= value;
    }

    #endregion

    #region Expense-Specific Properties

    // Supplier alias for counterparty
    public CounterpartyOption? SelectedSupplier
    {
        get => SelectedCounterparty;
        set => SelectedCounterparty = value;
    }

    public bool HasSupplierError
    {
        get => HasCounterpartyError;
        set => HasCounterpartyError = value;
    }

    // Notify SelectedSupplier when SelectedCounterparty changes so UI bindings update
    protected override void OnCounterpartyChanged(CounterpartyOption? value)
    {
        OnPropertyChanged(nameof(SelectedSupplier));
    }

    public ObservableCollection<CounterpartyOption> SupplierOptions => CounterpartyOptions;

    public string? FilterSupplierId
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
        "Expired",
        "Other"
    ];

    public override ObservableCollection<string> ReturnReasonOptions { get; } =
    [
        "Wrong item received",
        "Quality issues",
        "Not as Described",
        "Duplicate order",
        "Changed Mind",
        "Better price elsewhere",
        "Other"
    ];

    public override ObservableCollection<string> UndoReasonOptions { get; } =
    [
        "Item found",
        "Damage was repairable",
        "Incorrect status",
        "Administrative error",
        "Other"
    ];

    #endregion

    #region Data Loading

    protected override IEnumerable<CounterpartyOption> GetCounterpartyOptions() =>
        OptionLoader.Suppliers(App.CompanyManager?.CompanyData).AsOptions<CounterpartyOption>();

    #endregion

    #region Edit Modal

    public override void OpenEditModal(ExpenseDisplayItem? item)
    {
        if (item == null) return;

        var expense = App.CompanyManager?.CompanyData?.Expenses.FirstOrDefault(p => p.Id == item.Id);
        if (expense == null) return;

        LoadCounterpartyOptions();
        LoadCategoryOptions();
        LoadProductOptions();

        EditingTransactionId = expense.Id;
        IsEditMode = true;
        ModalTitle = $"Edit Expense {expense.Id}";
        SaveButtonText = "Save Changes";

        SelectedSupplier = SupplierOptions.FirstOrDefault(s => s.Id == expense.SupplierId);
        PopulateFormFromTransaction(expense);

        IsAddEditModalOpen = true;
    }

    #endregion

    #region Delete

    public async void OpenDeleteConfirm(ExpenseDisplayItem? item)
    {
        try
        {
            if (item == null) return;

            if (!await ConfirmDeleteAsync("Delete Expense".Translate(),
                    "Are you sure you want to delete this expense?\n\nID: {0}\nProduct: {1}\nAmount: {2}".TranslateFormat(item.Id, item.ProductDescription, item.TotalFormatted)))
                return;

            DeleteExpense(item.Id);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Expense.OpenDeleteConfirm");
        }
    }

    internal void DeleteExpense(string expenseId)
    {
        var companyData = App.CompanyManager?.CompanyData;

        var expense = companyData?.Expenses.FirstOrDefault(p => p.Id == expenseId);
        if (companyData == null || expense == null) return;

        DeleteTransactionWithUndo(companyData, companyData.Expenses, expense, isExpense: true);
    }

    #endregion

    #region Item Status Modal

    public void OpenMarkAsLostDamagedModal(ExpenseDisplayItem? item)
    {
        OpenItemStatusModal(item, nameof(ArgoBooks.Core.Enums.ItemStatusAction.LostDamaged), "Mark as Lost / Damaged", "Mark as Lost / Damaged", false, LostDamagedReasonOptions);
    }

    public void OpenMarkAsReturnedModal(ExpenseDisplayItem? item)
    {
        OpenItemStatusModal(item, nameof(ArgoBooks.Core.Enums.ItemStatusAction.Returned), "Mark as Returned", "Mark as Returned", false, ReturnReasonOptions);
    }

    public void OpenUndoLostDamagedModal(ExpenseDisplayItem? item)
    {
        OpenItemStatusModal(item, nameof(ArgoBooks.Core.Enums.ItemStatusAction.UndoLostDamaged), "Undo Lost / Damaged Status", "Undo Status", true, UndoReasonOptions);
    }

    public void OpenUndoReturnedModal(ExpenseDisplayItem? item)
    {
        OpenItemStatusModal(item, nameof(ArgoBooks.Core.Enums.ItemStatusAction.UndoReturned), "Undo Returned Status", "Undo Status", true, UndoReasonOptions);
    }

    protected override string GetItemStatusDescription(ExpenseDisplayItem item)
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

        var purchase = companyData.Expenses.FirstOrDefault(p => p.Id == ItemStatusItem.Id);
        if (purchase == null)
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
                var record = CreateLostDamagedRecord(companyData, purchase);
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    $"Mark expense '{purchase.Id}' as lost/damaged",
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
                var record = CreateReturnRecord(companyData, purchase);
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    $"Mark expense '{purchase.Id}' as returned",
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
                var record = companyData.LostDamaged.FirstOrDefault(ld => ld.InventoryItemId == purchase.Id);
                if (record != null)
                {
                    companyData.LostDamaged.Remove(record);
                    App.UndoRedoManager.RecordAction(new DelegateAction(
                        $"Undo lost/damaged status for expense '{purchase.Id}'",
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
                var record = companyData.Returns.FirstOrDefault(r => r.OriginalTransactionId == purchase.Id);
                if (record != null)
                {
                    companyData.Returns.Remove(record);
                    App.UndoRedoManager.RecordAction(new DelegateAction(
                        $"Undo returned status for expense '{purchase.Id}'",
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

    private LostDamaged CreateLostDamagedRecord(CompanyData companyData, Expense purchase)
    {
        var reason = MapToLostDamagedReason(SelectedItemStatusReason ?? "Other");
        var productId = purchase.LineItems.FirstOrDefault()?.ProductId ?? "";
        var valueLost = purchase.Total;

        var lostDamaged = new LostDamaged
        {
            Id = new IdGenerator(companyData).NextLostDamagedId(),
            ProductId = productId,
            InventoryItemId = purchase.Id,
            Quantity = (int)purchase.Quantity,
            Reason = reason,
            DateDiscovered = DateTime.UtcNow,
            ValueLost = valueLost,
            Notes = $"From purchase {purchase.Id}. {ItemStatusNotes}".Trim(),
            InsuranceClaim = false,
            CreatedAt = DateTime.UtcNow
        };

        companyData.LostDamaged.Add(lostDamaged);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.LostDamagedRecorded);
        return lostDamaged;
    }

    private Return CreateReturnRecord(CompanyData companyData, Expense purchase)
    {
        var productId = purchase.LineItems.FirstOrDefault()?.ProductId ?? "";

        var returnRecord = new Return
        {
            Id = new IdGenerator(companyData).NextReturnId(),
            OriginalTransactionId = purchase.Id,
            ReturnType = "Expense",
            SupplierId = purchase.SupplierId ?? "",
            CustomerId = "",
            ReturnDate = DateTime.UtcNow,
            Items =
            [
                new ReturnItem
                {
                    ProductId = productId,
                    Quantity = (int)purchase.Quantity,
                    Reason = SelectedItemStatusReason ?? "Other"
                }
            ],
            RefundAmount = purchase.Total,
            RestockingFee = 0,
            Status = ReturnStatus.Completed,
            Notes = ItemStatusNotes,
            ProcessedBy = purchase.AccountantId ?? "",
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
        var expenseId = new Core.Data.IdGenerator(companyData).NextExpenseId(date);

        var (description, totalQuantity, averageUnitPrice) = GetLineItemSummary();
        var modelLineItems = CreateModelLineItems();

        var expense = new Expense
        {
            Id = expenseId,
            Date = date,
            SupplierId = SelectedSupplier?.Id,
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
            Notes = ModalNotes,
            ReferenceNumber = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            OriginalCurrency = SaveCurrency
        };
        UsdConversion.Apply(companyData, expense, SaveRate);

        // Create Receipt if file was attached
        Receipt? receipt = null;
        if (!string.IsNullOrEmpty(ReceiptFilePath))
        {
            receipt = CreateReceipt(companyData, expenseId, "Expense", SelectedSupplier?.Name ?? "");
            if (receipt != null)
            {
                expense.ReceiptId = receipt.Id;
                companyData.Receipts.Add(receipt);
            }
        }

        companyData.Expenses.Add(expense);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ExpenseCreated);

        // Adjust inventory for tracked products
        var inventoryResults = AdjustInventoryForLineItems(companyData, expense, modelLineItems, isExpense: true);

        var capturedReceipt = receipt;
        var action = new DelegateAction(
            $"Add expense {expenseId}",
            () =>
            {
                companyData.Expenses.RemoveRecord(expense);
                UsdConversion.Set(companyData, UsdConversion.KeyOf(expense), null);
                if (capturedReceipt != null)
                    companyData.Receipts.RemoveRecord(capturedReceipt);
                RevertInventoryAdjustments(companyData, inventoryResults);
                RaiseTransactionSaved();
            },
            () =>
            {
                companyData.Expenses.RestoreRecord(expense);
                UsdConversion.Requeue(companyData, expense);
                if (capturedReceipt != null)
                    companyData.Receipts.RestoreRecord(capturedReceipt);
                inventoryResults = AdjustInventoryForLineItems(companyData, expense, modelLineItems, isExpense: true);
                RaiseTransactionSaved();
            });

        App.UndoRedoManager.RecordAction(action);
        App.CompanyManager?.MarkAsChanged();
        RaiseTransactionSaved();

        ShowInventoryNotifications(inventoryResults, isExpense: true);

        // Mark the setup checklist item as complete
        TutorialService.Instance.CompleteChecklistItem(TutorialService.ChecklistItems.RecordExpense);
    }

    protected override void SaveEditedTransaction(CompanyData companyData)
    {
        var expense = companyData.Expenses.FirstOrDefault(p => p.Id == EditingTransactionId);
        if (expense == null) return;

        // Store original values for undo
        var original = CaptureTransactionState(expense);
        var queueKey = UsdConversion.KeyOf(expense);
        var originalQueued = UsdConversion.Snapshot(companyData, [queueKey]);

        var (description, totalQuantity, averageUnitPrice) = GetLineItemSummary();
        var modelLineItems = CreateModelLineItems();

        // Apply changes
        expense.Date = ModalDate?.DateTime ?? DateTime.Now;
        expense.SupplierId = SelectedSupplier?.Id;
        expense.Description = description;
        expense.LineItems = modelLineItems;
        expense.Quantity = totalQuantity;
        expense.UnitPrice = averageUnitPrice;
        expense.Amount = Subtotal;
        expense.TaxRate = Subtotal > 0 ? (TaxAmount / Subtotal) * 100 : 0;
        expense.TaxAmount = TaxAmount;
        expense.ShippingCost = ModalShipping;
        expense.Discount = ModalDiscount;
        expense.Fee = ModalFee;
        expense.Total = Total;
        expense.PaymentMethod = Enum.TryParse<PaymentMethod>(SelectedPaymentMethod.Replace(" ", ""), out var pm) ? pm : PaymentMethod.Cash;
        expense.Notes = ModalNotes;
        expense.UpdatedAt = DateTime.UtcNow;
        expense.OriginalCurrency = SaveCurrency;
        UsdConversion.Apply(companyData, expense, SaveRate);
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
            newReceipt = CreateReceipt(companyData, expense.Id, "Expense", SelectedSupplier?.Name ?? "");
            if (newReceipt != null)
            {
                if (currentReceipt != null && companyData.Receipts.Remove(currentReceipt))
                    replacedReceipt = currentReceipt;
                expense.ReceiptId = newReceipt.Id;
                companyData.Receipts.Add(newReceipt);
            }
        }

        // Adjust inventory with net diff (single adjustment per product)
        var editResults = AdjustInventoryForEdit(companyData, expense, original.LineItems, modelLineItems, isExpense: true);

        var capturedNewReceipt = newReceipt;
        // Snapshot the NEW state so redo restores the edit itself.
        var edited = CaptureTransactionState(expense);
        var action = new DelegateAction(
            $"Edit expense {EditingTransactionId}",
            () =>
            {
                RestoreTransactionState(expense, original);
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
                RestoreTransactionState(expense, edited);
                UsdConversion.Restore(companyData, [queueKey], editedQueued);
                if (replacedReceipt != null)
                    companyData.Receipts.RemoveRecord(replacedReceipt);
                if (capturedNewReceipt != null)
                    companyData.Receipts.RestoreRecord(capturedNewReceipt);
                editResults = AdjustInventoryForEdit(companyData, expense, original.LineItems, modelLineItems, isExpense: true);
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

    private static TransactionState CaptureTransactionState(Expense expense)
    {
        return new TransactionState
        {
            Date = expense.Date,
            CounterpartyId = expense.SupplierId,
            Description = expense.Description,
            LineItems = expense.LineItems.ToList(),
            Quantity = expense.Quantity,
            UnitPrice = expense.UnitPrice,
            Amount = expense.Amount,
            TaxRate = expense.TaxRate,
            TaxAmount = expense.TaxAmount,
            ShippingCost = expense.ShippingCost,
            Discount = expense.Discount,
            Fee = expense.Fee,
            Total = expense.Total,
            PaymentMethod = expense.PaymentMethod,
            Notes = expense.Notes,
            ReferenceNumber = expense.ReferenceNumber,
            ReceiptId = expense.ReceiptId,
            IsPendingConversion = expense.IsPendingConversion,
            OriginalCurrency = expense.OriginalCurrency,
            TotalUSD = expense.TotalUSD,
            UnitPriceUSD = expense.UnitPriceUSD,
            ShippingCostUSD = expense.ShippingCostUSD,
            TaxAmountUSD = expense.TaxAmountUSD,
            DiscountUSD = expense.DiscountUSD,
            FeeUSD = expense.FeeUSD
        };
    }

    private static void RestoreTransactionState(Expense expense, TransactionState state)
    {
        expense.Date = state.Date;
        expense.SupplierId = state.CounterpartyId;
        expense.Description = state.Description;
        expense.LineItems = state.LineItems;
        expense.Quantity = state.Quantity;
        expense.UnitPrice = state.UnitPrice;
        expense.Amount = state.Amount;
        expense.TaxRate = state.TaxRate;
        expense.TaxAmount = state.TaxAmount;
        expense.ShippingCost = state.ShippingCost;
        expense.Discount = state.Discount;
        expense.Fee = state.Fee;
        expense.Total = state.Total;
        expense.PaymentMethod = state.PaymentMethod;
        expense.Notes = state.Notes;
        expense.ReferenceNumber = state.ReferenceNumber;
        expense.ReceiptId = state.ReceiptId;
        expense.IsPendingConversion = state.IsPendingConversion;
        expense.OriginalCurrency = state.OriginalCurrency;
        expense.TotalUSD = state.TotalUSD;
        expense.UnitPriceUSD = state.UnitPriceUSD;
        expense.ShippingCostUSD = state.ShippingCostUSD;
        expense.TaxAmountUSD = state.TaxAmountUSD;
        expense.DiscountUSD = state.DiscountUSD;
        expense.FeeUSD = state.FeeUSD;
    }

    #endregion

    #region Navigation Aliases

    [RelayCommand]
    private void OpenCreateSupplier() => OpenCreateCounterparty();

    #endregion

}

/// <summary>
/// Line item for expense form.
/// </summary>
public class ExpenseLineItem : TransactionLineItemBase
{
}

/// <summary>
/// Helper class for capturing transaction state for undo/redo.
/// </summary>
internal class TransactionState
{
    public DateTime Date { get; set; }
    public string? CounterpartyId { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<LineItem> LineItems { get; set; } = [];
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Discount { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public RevenuePaymentStatus PaymentStatus { get; set; } = RevenuePaymentStatus.Paid;
    public string Notes { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public string? ReceiptId { get; set; }
    public bool IsPendingConversion { get; set; }
    // USD-normalized + currency fields must also be captured. Dashboard
    // aggregations read EffectiveTotalUSD which falls back to TotalUSD for
    // non-USD companies. If undo restores only native fields, those
    // companies see stale (post-edit) values even after the revert.
    public string OriginalCurrency { get; set; } = "USD";
    public decimal TotalUSD { get; set; }
    public decimal UnitPriceUSD { get; set; }
    public decimal ShippingCostUSD { get; set; }
    public decimal TaxAmountUSD { get; set; }
    public decimal DiscountUSD { get; set; }
    public decimal FeeUSD { get; set; }
}
