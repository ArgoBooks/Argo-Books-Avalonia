using ArgoBooks.Localization;
using ArgoBooks.Services;
using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Assets;
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

    protected override string IdPlaceholderText => "PUR-xxx";

    protected override bool IsIdTaken(CompanyData companyData, string id) =>
        companyData.Expenses.Any(e => e.Id == id);
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
        if (item == null || LoadIntoForm(item) is not { } expense) return;

        EditingTransactionId = expense.Id;
        ModalId = expense.Id;
        IsEditMode = true;
        ModalTitle = $"Edit Expense {expense.Id}";
        SaveButtonText = "Save Changes";

        IsAddEditModalOpen = true;
    }

    /// <summary>Ticks "this is an asset" and fills in its type and life when the expense bought one.</summary>
    private void LoadAssetChoice(string expenseId)
    {
        var asset = App.CompanyManager?.CompanyData is { } data ? ExpenseAssetLink.AssetFor(data, expenseId) : null;
        PrepareAssetTypes();
        ModalIsAsset = asset != null;
        ModalAssetUsefulLife = asset?.UsefulLifeYears;
        ModalAssetUsesRate = asset?.Method == DepreciationMethod.DecliningBalance && string.IsNullOrEmpty(asset.CcaClass);
        if (asset == null)
        {
            ModalAssetType = DefaultAssetType();
            return;
        }

        var options = AssetTypeOptions.ToList();
        var type = CanadianAssetRates.Find(options, asset.Type, asset.CcaClass);
        AssetTypeOptions = options;
        ModalAssetType = type;
    }

    private ExpenseAssetLink.Change? ApplyAssetChoice(CompanyData companyData, Expense expense, decimal? preTaxBeforeEdit, string? currencyBeforeEdit) =>
        ExpenseAssetLink.Apply(companyData, expense, preTaxBeforeEdit, currencyBeforeEdit,
            ModalIsAsset, ModalAssetType?.Type ?? AssetType.Other, ModalAssetUsefulLife, AssetCcaChoice(expense.Date));

    protected override Transaction? LoadIntoForm(ExpenseDisplayItem item)
    {
        var expense = App.CompanyManager?.CompanyData?.Expenses.FirstOrDefault(p => p.Id == item.Id);
        if (expense == null) return null;

        LoadCounterpartyOptions();
        LoadCategoryOptions();
        LoadProductOptions();

        SelectedSupplier = SupplierOptions.FirstOrDefault(s => s.Id == expense.SupplierId);
        LoadAssetChoice(expense.Id);
        PopulateFormFromTransaction(expense);
        return expense;
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

    #region Bulk Edit

    protected override string BulkNoun(int count) => count == 1 ? "expense" : "expenses";

    protected override IEnumerable<Transaction> BulkRecords(CompanyData data) => data.Expenses;

    protected override void BulkRemove(CompanyData data, Transaction record) => data.Expenses.RemoveRecord((Expense)record);

    protected override void BulkRestore(CompanyData data, Transaction record) => data.Expenses.RestoreRecord((Expense)record);

    protected override string? CounterpartyIdOf(Transaction record) => ((Expense)record).SupplierId;

    protected override void SetCounterpartyId(Transaction record, string? id) => ((Expense)record).SupplierId = id;

    // Payroll wages stay as the pay run that made them recorded them.
    protected override Func<Transaction, bool> MadeElsewhere(CompanyData data)
    {
        var payrollIds = data.PayRuns.SelectMany(r => r.Lines)
            .Select(l => l.ExpenseId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();
        return record => payrollIds.Contains(record.Id);
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
                        companyData.LostDamaged.RemoveRecord(record);
                        companyData.MarkAsModified();
                        RaiseTransactionSaved();
                    },
                    () =>
                    {
                        companyData.LostDamaged.RestoreRecord(record);
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
                        companyData.Returns.RemoveRecord(record);
                        companyData.MarkAsModified();
                        RaiseTransactionSaved();
                    },
                    () =>
                    {
                        companyData.Returns.RestoreRecord(record);
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
                    companyData.LostDamaged.RemoveRecord(record);
                    RecordStatusUndo(new DelegateAction(
                        $"Undo lost/damaged status for expense '{purchase.Id}'",
                        () =>
                        {
                            companyData.LostDamaged.RestoreRecord(record);
                            companyData.MarkAsModified();
                            RaiseTransactionSaved();
                        },
                        () =>
                        {
                            companyData.LostDamaged.RemoveRecord(record);
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
                    companyData.Returns.RemoveRecord(record);
                    RecordStatusUndo(new DelegateAction(
                        $"Undo returned status for expense '{purchase.Id}'",
                        () =>
                        {
                            companyData.Returns.RestoreRecord(record);
                            companyData.MarkAsModified();
                            RaiseTransactionSaved();
                        },
                        () =>
                        {
                            companyData.Returns.RemoveRecord(record);
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
            RefundAmount = ReturnLossAmounts.RefundFor(purchase, purchase.Amount),
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
        var expenseId = RequestedId ?? new IdGenerator(companyData).NextExpenseId(date);

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
            PaymentMethod = PaymentMethodExtensions.ParseDisplayName(SelectedPaymentMethod),
            Notes = ModalNotes,
            ReferenceNumber = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            OriginalCurrency = SaveCurrency
        };
        UsdConversion.Apply(companyData, expense, SaveRate);

        var (addedReceipts, _) = ApplyAttachments(companyData, expense, "Expense", SelectedSupplier?.Name ?? "");

        companyData.Expenses.Add(expense);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ExpenseCreated);

        // Adjust inventory for tracked products
        var inventoryResults = AdjustInventoryForLineItems(companyData, expense, modelLineItems, isExpense: true);

        var assetChange = ApplyAssetChoice(companyData, expense, null, null);

        var capturedReceipts = addedReceipts;
        var action = new DelegateAction(
            $"Add expense {expenseId}",
            () =>
            {
                companyData.Expenses.RemoveRecord(expense);
                UsdConversion.Set(companyData, UsdConversion.KeyOf(expense), null);
                foreach (var r in capturedReceipts)
                    companyData.Receipts.RemoveRecord(r);
                RevertInventoryAdjustments(companyData, inventoryResults);
                assetChange?.Undo();
                RaiseTransactionSaved();
            },
            () =>
            {
                companyData.Expenses.RestoreRecord(expense);
                UsdConversion.Requeue(companyData, expense);
                foreach (var r in capturedReceipts)
                    companyData.Receipts.RestoreRecord(r);
                inventoryResults = AdjustInventoryForLineItems(companyData, expense, modelLineItems, isExpense: true);
                assetChange?.Redo();
                RaiseTransactionSaved();
            });

        App.UndoRedoManager.RecordAction(action);
        App.CompanyManager?.MarkAsChanged();
        RaiseTransactionSaved();

        ShowInventoryNotifications(inventoryResults, isExpense: true);

        // Mark the setup checklist item as complete
        TutorialService.Instance.CompleteChecklistItem(TutorialService.ChecklistItems.RecordExpense);
    }

    protected override bool SaveEditedTransaction(CompanyData companyData)
    {
        var expense = companyData.Expenses.FirstOrDefault(p => p.Id == EditingTransactionId);
        if (expense == null) return false;

        var oldId = expense.Id;
        var renamedId = RequestedId ?? oldId;
        if (renamedId != oldId)
        {
            try { App.CompanyManager?.ChangeExpenseId(expense, renamedId); }
            catch (Exception ex) { ModalIdError = ex.Message; return false; }
        }

        // Store original values for undo
        var original = CaptureTransactionState(expense);
        var preTaxBeforeEdit = AssetDepreciation.PreTaxCost(expense);
        var currencyBeforeEdit = expense.OriginalCurrency;
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
        expense.PaymentMethod = PaymentMethodExtensions.ParseDisplayName(SelectedPaymentMethod);
        expense.Notes = ModalNotes;
        expense.UpdatedAt = DateTime.UtcNow;
        expense.OriginalCurrency = SaveCurrency;
        UsdConversion.Apply(companyData, expense, SaveRate);
        var editedQueued = UsdConversion.Snapshot(companyData, [queueKey]);

        // Handle receipt. The form loads an existing receipt's OriginalFilePath, so only a different
        // path means Change picked a new file.
        var (addedReceipts, removedReceipts) = ApplyAttachments(companyData, expense, "Expense", SelectedSupplier?.Name ?? "");

        // Adjust inventory with net diff (single adjustment per product)
        var editResults = AdjustInventoryForEdit(companyData, expense, original.LineItems, modelLineItems, isExpense: true);

        var assetChange = ApplyAssetChoice(companyData, expense, preTaxBeforeEdit, currencyBeforeEdit);

        var capturedAdded = addedReceipts;
        var capturedRemoved = removedReceipts;
        // Snapshot the NEW state so redo restores the edit itself.
        var edited = CaptureTransactionState(expense);
        var action = new DelegateAction(
            $"Edit expense {EditingTransactionId}",
            () =>
            {
                if (renamedId != oldId) App.CompanyManager?.ChangeExpenseId(expense, oldId);
                RestoreTransactionState(expense, original);
                SyncDocumentsToRecord(companyData, expense, "Expense");
                UsdConversion.Restore(companyData, [queueKey], originalQueued);
                foreach (var r in capturedAdded)
                    companyData.Receipts.RemoveRecord(r);
                foreach (var r in capturedRemoved)
                    companyData.Receipts.RestoreRecord(r);
                RevertInventoryAdjustments(companyData, editResults);
                assetChange?.Undo();
                RaiseTransactionSaved();
            },
            () =>
            {
                if (renamedId != oldId) App.CompanyManager?.ChangeExpenseId(expense, renamedId);
                RestoreTransactionState(expense, edited);
                SyncDocumentsToRecord(companyData, expense, "Expense");
                UsdConversion.Restore(companyData, [queueKey], editedQueued);
                foreach (var r in capturedRemoved)
                    companyData.Receipts.RemoveRecord(r);
                foreach (var r in capturedAdded)
                    companyData.Receipts.RestoreRecord(r);
                editResults = AdjustInventoryForEdit(companyData, expense, original.LineItems, modelLineItems, isExpense: true);
                assetChange?.Redo();
                RaiseTransactionSaved();
            });

        App.UndoRedoManager.RecordAction(action);
        App.CompanyManager?.MarkAsChanged();
        RaiseTransactionSaved();

        ShowEditInventoryNotifications(editResults);
        return true;
    }

    protected override string CounterpartyNameOf(CompanyData companyData, Transaction transaction) =>
        transaction is Expense expense && !string.IsNullOrEmpty(expense.SupplierId)
            ? companyData.GetSupplier(expense.SupplierId)?.Name ?? string.Empty
            : string.Empty;

    private Receipt? CreateReceipt(CompanyData companyData, string transactionId, string transactionType, string supplier)
    {
        if (string.IsNullOrEmpty(ReceiptFilePath)) return null;

        var fileInfo = new FileInfo(ReceiptFilePath);
        var fileType = Core.Services.MimeTypes.For(ReceiptFilePath);

        // A receipt is only worth keeping with its file in it.
        string fileData;
        try
        {
            if (!fileInfo.Exists)
                throw new FileNotFoundException("The file could not be found.".Translate(), ReceiptFilePath);
            fileData = Convert.ToBase64String(SharedFileReader.ReadAllBytes(ReceiptFilePath));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to read receipt file");
            App.AddNotification("Warning".Translate(), "Could not attach receipt file: {0}".TranslateFormat(ex.Message), NotificationType.Warning);
            return null;
        }

        var receiptId = new IdGenerator(companyData).NextReceiptId();

        return new Receipt
        {
            Id = receiptId,
            TransactionId = transactionId,
            TransactionType = transactionType,
            FileName = fileInfo.Name,
            FileType = fileType,
            FileSize = fileInfo.Length,
            FileData = fileData,
            OriginalFilePath = ReceiptFilePath,
            Amount = Total,
            Date = ModalDate?.DateTime ?? DateTime.Now,
            Supplier = supplier,
            Source = "Manual",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static TransactionState CaptureTransactionState(Expense expense) =>
        TransactionState.Capture(expense, expense.SupplierId);

    private static void RestoreTransactionState(Expense expense, TransactionState state)
    {
        state.RestoreTo(expense);
        expense.SupplierId = state.CounterpartyId;
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
    public decimal Subtotal { get; set; }
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
    // The USD and currency fields are captured too, because dashboard totals read EffectiveTotalUSD and undo would leave them stale.
    public string OriginalCurrency { get; set; } = "USD";
    public decimal TotalUSD { get; set; }
    public decimal UnitPriceUSD { get; set; }
    public decimal ShippingCostUSD { get; set; }
    public decimal TaxAmountUSD { get; set; }
    public decimal DiscountUSD { get; set; }
    public decimal FeeUSD { get; set; }

    /// <summary>The fields an edit can change that every transaction has. The caller adds anything of its own.</summary>
    public static TransactionState Capture(Transaction transaction, string? counterpartyId) => new()
    {
        Date = transaction.Date,
        CounterpartyId = counterpartyId,
        Description = transaction.Description,
        LineItems = transaction.LineItems.ToList(),
        Quantity = transaction.Quantity,
        UnitPrice = transaction.UnitPrice,
        Amount = transaction.Amount,
        TaxRate = transaction.TaxRate,
        TaxAmount = transaction.TaxAmount,
        ShippingCost = transaction.ShippingCost,
        Discount = transaction.Discount,
        Fee = transaction.Fee,
        Total = transaction.Total,
        PaymentMethod = transaction.PaymentMethod,
        Notes = transaction.Notes,
        ReferenceNumber = transaction.ReferenceNumber,
        ReceiptId = transaction.ReceiptId,
        IsPendingConversion = transaction.IsPendingConversion,
        OriginalCurrency = transaction.OriginalCurrency,
        TotalUSD = transaction.TotalUSD,
        UnitPriceUSD = transaction.UnitPriceUSD,
        ShippingCostUSD = transaction.ShippingCostUSD,
        TaxAmountUSD = transaction.TaxAmountUSD,
        DiscountUSD = transaction.DiscountUSD,
        FeeUSD = transaction.FeeUSD
    };

    /// <summary>Puts the shared fields back. The caller restores its counterparty and anything of its own.</summary>
    public void RestoreTo(Transaction transaction)
    {
        transaction.Date = Date;
        transaction.Description = Description;
        transaction.LineItems = LineItems;
        transaction.Quantity = Quantity;
        transaction.UnitPrice = UnitPrice;
        transaction.Amount = Amount;
        transaction.TaxRate = TaxRate;
        transaction.TaxAmount = TaxAmount;
        transaction.ShippingCost = ShippingCost;
        transaction.Discount = Discount;
        transaction.Fee = Fee;
        transaction.Total = Total;
        transaction.PaymentMethod = PaymentMethod;
        transaction.Notes = Notes;
        transaction.ReferenceNumber = ReferenceNumber;
        transaction.ReceiptId = ReceiptId;
        transaction.IsPendingConversion = IsPendingConversion;
        transaction.OriginalCurrency = OriginalCurrency;
        transaction.TotalUSD = TotalUSD;
        transaction.UnitPriceUSD = UnitPriceUSD;
        transaction.ShippingCostUSD = ShippingCostUSD;
        transaction.TaxAmountUSD = TaxAmountUSD;
        transaction.DiscountUSD = DiscountUSD;
        transaction.FeeUSD = FeeUSD;
    }
}
