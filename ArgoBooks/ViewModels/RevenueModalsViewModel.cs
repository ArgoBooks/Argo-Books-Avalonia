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

    public bool HasCustomerError
    {
        get => HasCounterpartyError;
        set => HasCounterpartyError = value;
    }

    // Notify SelectedCustomer when SelectedCounterparty changes so UI bindings update
    protected override void OnCounterpartyChanged(CounterpartyOption? value)
    {
        OnPropertyChanged(nameof(SelectedCustomer));
    }

    public ObservableCollection<CounterpartyOption> CustomerOptions => CounterpartyOptions;

    // Filter aliases
    public CounterpartyOption? FilterSelectedCustomer
    {
        get => FilterSelectedCounterparty;
        set => FilterSelectedCounterparty = value;
    }

    public string? FilterCustomerId
    {
        get => FilterCounterpartyId;
        set => FilterCounterpartyId = value;
    }

    // Payment status
    [ObservableProperty]
    private bool _modalPaid = true;

    // Command aliases for AXAML bindings
    public IAsyncRelayCommand SaveRevenueCommand => SaveTransactionCommand;

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

    protected override void LoadCounterpartyOptions()
    {
        CounterpartyOptions.Clear();
        LoadCounterpartyOptionsInternal();
    }

    protected override void LoadCounterpartyOptionsInternal()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Customers == null)
            return;

        foreach (var customer in companyData.Customers.OrderBy(c => c.Name))
        {
            CounterpartyOptions.Add(new CounterpartyOption { Id = customer.Id, Name = customer.Name });
        }
    }

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
        ModalPaid = revenue.PaymentStatus == RevenuePaymentStatus.Paid;
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

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Revenue",
                Message = $"Are you sure you want to delete this revenue?\n\nID: {item.Id}\nProduct: {item.ProductDescription}\nAmount: {item.TotalFormatted}",
                PrimaryButtonText = "Delete",
                CancelButtonText = "Cancel",
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;

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

        // Find and remove associated receipt
        Receipt? deletedReceipt = null;
        if (!string.IsNullOrEmpty(revenue.ReceiptId))
        {
            deletedReceipt = companyData.Receipts.FirstOrDefault(r => r.Id == revenue.ReceiptId);
            if (deletedReceipt != null)
            {
                companyData.Receipts.Remove(deletedReceipt);
            }
        }

        // Put back the stock this sale took, as editing its lines down to nothing would
        var deleteResults = AdjustInventoryForEdit(companyData, revenue, revenue.LineItems, [], isExpense: false, reason: "Revenue deleted");

        var deletedRevenue = revenue;
        var capturedReceipt = deletedReceipt;
        var action = new DelegateAction(
            $"Delete revenue {revenue.Id}",
            () =>
            {
                companyData.Revenues.Add(deletedRevenue);
                if (capturedReceipt != null)
                    companyData.Receipts.Add(capturedReceipt);
                RevertInventoryAdjustments(companyData, deleteResults);
                RaiseTransactionDeleted();
            },
            () =>
            {
                companyData.Revenues.Remove(deletedRevenue);
                if (capturedReceipt != null)
                    companyData.Receipts.Remove(capturedReceipt);
                deleteResults = AdjustInventoryForEdit(companyData, deletedRevenue, deletedRevenue.LineItems, [], isExpense: false, reason: "Revenue deleted");
                RaiseTransactionDeleted();
            });

        companyData.Revenues.Remove(revenue);
        App.UndoRedoManager.RecordAction(action);
        App.CompanyManager?.MarkAsChanged();

        RaiseTransactionDeleted();
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
            Id = $"LOST-{++companyData.IdCounters.LostDamaged:D3}",
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
            Id = $"RET-{++companyData.IdCounters.Return:D3}",
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
            // USD conversion fields
            OriginalCurrency = ConvertedTotal?.OriginalCurrency ?? "USD",
            TotalUSD = ConvertedTotal?.AmountUSD ?? Total,
            TaxAmountUSD = ConvertedTaxAmount?.AmountUSD ?? TaxAmount,
            ShippingCostUSD = ConvertedShippingCost?.AmountUSD ?? ModalShipping,
            DiscountUSD = ConvertedDiscount?.AmountUSD ?? ModalDiscount,
            FeeUSD = ConvertedFee?.AmountUSD ?? ModalFee,
            UnitPriceUSD = ConvertedTotal != null && ConvertedTotal.OriginalCurrency != "USD" && Subtotal > 0 && Total != 0
                ? ConvertedTotal.AmountUSD / Total * averageUnitPrice
                : averageUnitPrice,
            IsPendingConversion = IsPendingConversion
        };

        // Queue for offline conversion if pending
        if (IsPendingConversion)
        {
            var pendingEntry = new PendingConversion
            {
                TransactionId = revenueId,
                TransactionType = "Revenue",
                OriginalCurrency = ConvertedTotal?.OriginalCurrency ?? "USD",
                TransactionDate = revenue.Date,
                Total = Total,
                TaxAmount = TaxAmount,
                ShippingCost = ModalShipping,
                Discount = ModalDiscount,
                Fee = ModalFee,
                UnitPrice = averageUnitPrice
            };
            companyData.PendingConversions.Add(pendingEntry);
            _ = PendingConversionService.Instance?.AddPendingConversionAsync(pendingEntry);
        }

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
                companyData.Revenues.Remove(revenue);
                if (capturedReceipt != null)
                    companyData.Receipts.Remove(capturedReceipt);
                RevertInventoryAdjustments(companyData, inventoryResults);
                RaiseTransactionSaved();
            },
            () =>
            {
                companyData.Revenues.Add(revenue);
                if (capturedReceipt != null)
                    companyData.Receipts.Add(capturedReceipt);
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
        var originalQueued = companyData.PendingConversions.Where(p => p.TransactionId == revenue.Id).ToList();

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
        revenue.PaymentStatus = ModalPaid ? RevenuePaymentStatus.Paid : RevenuePaymentStatus.Unpaid;
        revenue.Notes = ModalNotes;
        revenue.UpdatedAt = DateTime.UtcNow;
        // USD conversion fields
        revenue.OriginalCurrency = ConvertedTotal?.OriginalCurrency ?? "USD";
        revenue.TotalUSD = ConvertedTotal?.AmountUSD ?? Total;
        revenue.TaxAmountUSD = ConvertedTaxAmount?.AmountUSD ?? TaxAmount;
        revenue.ShippingCostUSD = ConvertedShippingCost?.AmountUSD ?? ModalShipping;
        revenue.DiscountUSD = ConvertedDiscount?.AmountUSD ?? ModalDiscount;
        revenue.FeeUSD = ConvertedFee?.AmountUSD ?? ModalFee;
        revenue.UnitPriceUSD = ConvertedTotal != null && ConvertedTotal.OriginalCurrency != "USD" && Subtotal > 0 && Total != 0
            ? ConvertedTotal.AmountUSD / Total * averageUnitPrice
            : averageUnitPrice;
        revenue.IsPendingConversion = IsPendingConversion;

        // Queue for offline conversion if pending; a row that converted leaves the queue
        List<PendingConversion> editedQueued = [];
        if (IsPendingConversion)
        {
            editedQueued.Add(new PendingConversion
            {
                TransactionId = revenue.Id,
                TransactionType = "Revenue",
                OriginalCurrency = ConvertedTotal?.OriginalCurrency ?? "USD",
                TransactionDate = revenue.Date,
                Total = Total,
                TaxAmount = TaxAmount,
                ShippingCost = ModalShipping,
                Discount = ModalDiscount,
                Fee = ModalFee,
                UnitPrice = averageUnitPrice
            });
        }
        var queueTouched = originalQueued.Count > 0 || editedQueued.Count > 0;
        if (queueTouched)
            SetQueuedConversions(companyData, revenue.Id, editedQueued);

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
                if (queueTouched)
                    SetQueuedConversions(companyData, revenue.Id, originalQueued);
                if (capturedNewReceipt != null)
                    companyData.Receipts.Remove(capturedNewReceipt);
                if (replacedReceipt != null && !companyData.Receipts.Contains(replacedReceipt))
                    companyData.Receipts.Add(replacedReceipt);
                RevertInventoryAdjustments(companyData, editResults);
                RaiseTransactionSaved();
            },
            () =>
            {
                RestoreTransactionState(revenue, edited);
                if (queueTouched)
                    SetQueuedConversions(companyData, revenue.Id, editedQueued);
                if (replacedReceipt != null)
                    companyData.Receipts.Remove(replacedReceipt);
                if (capturedNewReceipt != null && !companyData.Receipts.Contains(capturedNewReceipt))
                    companyData.Receipts.Add(capturedNewReceipt);
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

        companyData.IdCounters.Receipt++;
        var receiptId = $"RCP-{DateTime.Now:yyyy}-{companyData.IdCounters.Receipt:D5}";
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

    #region Navigation Aliases

    [RelayCommand]
    private void OpenCreateCustomer() => OpenCreateCounterparty();

    #endregion

}

/// <summary>
/// Line item for revenue form.
/// </summary>
public class RevenueLineItem : TransactionLineItemBase
{
}
