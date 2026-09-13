using System.Collections.ObjectModel;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Services.PurchaseOrders;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for Purchase Orders modals (Add, Edit, View, Receive, Delete).
/// </summary>
public partial class PurchaseOrdersModalsViewModel : ViewModelBase
{
    #region Events

    /// <summary>
    /// Raised when a purchase order is saved (added or edited).
    /// </summary>
    public event EventHandler? OrderSaved;

    /// <summary>
    /// Raised when a purchase order is deleted.
    /// </summary>
    public event EventHandler? OrderDeleted;

    /// <summary>
    /// Raised when filters are applied.
    /// </summary>
    public event EventHandler? FiltersApplied;

    /// <summary>
    /// Raised when filters are cleared.
    /// </summary>
    public event EventHandler? FiltersCleared;

    #endregion

    #region Add/Edit Modal State

    [ObservableProperty]
    private bool _isAddModalOpen;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string? _editingOrderId;

    [ObservableProperty]
    private Supplier? _selectedSupplier;

    [ObservableProperty]
    private DateTimeOffset? _orderDate = new DateTimeOffset(DateTime.Today);

    [ObservableProperty]
    private DateTimeOffset? _expectedDeliveryDate = new DateTimeOffset(DateTime.Today.AddDays(7));

    [ObservableProperty]
    private string _shippingCost = "0";

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string? _addModalError;

    [ObservableProperty]
    private bool _hasSupplierError;

    // Original values for change detection in edit mode
    private Supplier? _originalSupplier;
    private DateTimeOffset? _originalOrderDate;
    private DateTimeOffset? _originalExpectedDeliveryDate;
    private string _originalShippingCost = "0";
    private string _originalNotes = string.Empty;
    private List<(string ProductId, string Quantity, string UnitCost)> _originalLineItems = [];

    /// <summary>
    /// Returns true if any data has been entered in the Add modal (when not in edit mode).
    /// </summary>
    public bool HasAddModalEnteredData =>
        !IsEditMode && (
            SelectedSupplier != null ||
            ShippingCost != "0" ||
            !string.IsNullOrWhiteSpace(Notes) ||
            LineItems.Any(li => !string.IsNullOrWhiteSpace(li.ProductId)));

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges
    {
        get
        {
            if (!IsEditMode) return false;

            if (SelectedSupplier?.Id != _originalSupplier?.Id) return true;
            if (OrderDate != _originalOrderDate) return true;
            if (ExpectedDeliveryDate != _originalExpectedDeliveryDate) return true;
            if (ShippingCost != _originalShippingCost) return true;
            if (Notes != _originalNotes) return true;

            // Compare line items
            if (LineItems.Count != _originalLineItems.Count) return true;
            for (int i = 0; i < LineItems.Count; i++)
            {
                var current = LineItems[i];
                var original = _originalLineItems[i];
                if (current.ProductId != original.ProductId ||
                    current.Quantity != original.Quantity ||
                    current.UnitCost != original.UnitCost)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Line items for the order being created/edited.
    /// </summary>
    public ObservableCollection<OrderLineItemViewModel> LineItems { get; } = [];

    /// <summary>
    /// Available suppliers for selection.
    /// </summary>
    public ObservableCollection<Supplier> AvailableSuppliers { get; } = [];

    /// <summary>
    /// Available products for adding line items.
    /// </summary>
    public ObservableCollection<Product> AvailableProducts { get; } = [];

    /// <summary>
    /// Calculated subtotal.
    /// </summary>
    public decimal CalculatedSubtotal => LineItems.Sum(li => li.Total);

    /// <summary>
    /// Calculated total.
    /// </summary>
    public decimal CalculatedTotal => CalculatedSubtotal + (decimal.TryParse(ShippingCost, out var sc) ? sc : 0);

    /// <summary>
    /// Subtotal display string.
    /// </summary>
    public string SubtotalDisplay => CurrencyService.Format(CalculatedSubtotal);

    /// <summary>
    /// Total display string.
    /// </summary>
    public string TotalDisplay => CurrencyService.Format(CalculatedTotal);

    /// <summary>
    /// Modal title based on mode.
    /// </summary>
    public string AddModalTitle => IsEditMode ? "Edit Purchase Order" : "Create Purchase Order";

    partial void OnShippingCostChanged(string value)
    {
        OnPropertyChanged(nameof(CalculatedTotal));
        OnPropertyChanged(nameof(TotalDisplay));
    }

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(AddModalTitle));

    partial void OnSelectedSupplierChanged(Supplier? value)
    {
        if (value != null)
        {
            HasSupplierError = false;
        }
    }

    private void OnOrderLineItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateCalculatedTotals();

    private void UpdateCalculatedTotals()
    {
        OnPropertyChanged(nameof(CalculatedSubtotal));
        OnPropertyChanged(nameof(CalculatedTotal));
        OnPropertyChanged(nameof(SubtotalDisplay));
        OnPropertyChanged(nameof(TotalDisplay));
    }

    #endregion

    #region View Modal State

    [ObservableProperty]
    private bool _isViewModalOpen;

    [ObservableProperty]
    private PurchaseOrderDisplayItem? _viewingOrder;

    /// <summary>
    /// Line items for the order being viewed.
    /// </summary>
    public ObservableCollection<ViewLineItemDisplay> ViewLineItems { get; } = [];

    #endregion

    #region Receive Modal State

    [ObservableProperty]
    private bool _isReceiveModalOpen;

    [ObservableProperty]
    private PurchaseOrderDisplayItem? _receivingOrder;

    [ObservableProperty]
    private string? _receiveModalError;

    /// <summary>
    /// Line items for receiving.
    /// </summary>
    public ObservableCollection<ReceiveLineItemViewModel> ReceiveLineItems { get; } = [];

    #endregion

    #region Delete Confirmation State

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private PurchaseOrderDisplayItem? _deletingOrder;

    #endregion

    #region Add/Edit Modal Commands

    /// <summary>
    /// Opens the add purchase order modal.
    /// </summary>
    [RelayCommand]
    public void OpenAddModal()
    {
        LoadSuppliers();
        LoadProducts();
        ClearAddModalFields();
        IsEditMode = false;
        EditingOrderId = null;

        // Add a default empty line item
        AddLineItem();

        IsAddModalOpen = true;
    }

    /// <summary>
    /// Opens the edit purchase order modal.
    /// </summary>
    public void OpenEditModal(PurchaseOrderDisplayItem item)
    {
        LoadSuppliers();
        LoadProducts();
        ClearAddModalFields();

        var companyData = App.CompanyManager?.CompanyData;

        var order = companyData?.PurchaseOrders.FirstOrDefault(o => o.Id == item.Id);
        if (order == null) return;

        // Populate fields
        IsEditMode = true;
        EditingOrderId = order.Id;
        SelectedSupplier = AvailableSuppliers.FirstOrDefault(s => s.Id == order.SupplierId);
        OrderDate = new DateTimeOffset(order.OrderDate);
        ExpectedDeliveryDate = new DateTimeOffset(order.ExpectedDeliveryDate);
        ShippingCost = order.ShippingCost.ToString("F2");
        Notes = order.Notes;

        // Populate line items
        var products = companyData?.Products ?? [];
        foreach (var lineItem in order.LineItems)
        {
            var product = products.FirstOrDefault(p => p.Id == lineItem.ProductId);
            var vm = new OrderLineItemViewModel
            {
                ProductId = lineItem.ProductId,
                ProductName = product?.Name ?? "Unknown Product",
                Quantity = lineItem.Quantity.ToString(),
                UnitCost = lineItem.UnitCost.ToString("F2")
            };
            vm.PropertyChanged += OnOrderLineItemPropertyChanged;
            LineItems.Add(vm);
        }
        UpdateCalculatedTotals();

        // Store original values for change detection
        _originalSupplier = SelectedSupplier;
        _originalOrderDate = OrderDate;
        _originalExpectedDeliveryDate = ExpectedDeliveryDate;
        _originalShippingCost = ShippingCost;
        _originalNotes = Notes;
        _originalLineItems = LineItems.Select(li => (li.ProductId, li.Quantity, li.UnitCost)).ToList();

        IsAddModalOpen = true;
    }

    /// <summary>
    /// Closes the add/edit modal.
    /// </summary>
    [RelayCommand]
    private void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearAddModalFields();
    }

    /// <summary>
    /// Requests to close the Add/Edit modal, showing confirmation if data was entered or changes were made.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseAddModalAsync()
    {
        var hasChanges = IsEditMode ? HasEditModalChanges : HasAddModalEnteredData;
        if (hasChanges)
        {
            var confirmed = IsEditMode
                ? await ConfirmDiscardEditsAsync()
                : await ConfirmDiscardNewAsync();
            if (!confirmed)
                return;
        }

        CloseAddModal();
    }

    // One-shot handlers for the "create entity from this modal" flows. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _supplierSavedHandler;
    private EventHandler? _productSavedHandler;

    /// <summary>
    /// Opens the create supplier modal on top of the current modal.
    /// </summary>
    [RelayCommand]
    private void OpenCreateSupplier()
    {
        var supplierModals = App.SupplierModalsViewModel;
        if (supplierModals == null) return;

        CreateModalSubscription.RearmOnce(ref _supplierSavedHandler,
            h => supplierModals.SupplierSaved += h,
            h => supplierModals.SupplierSaved -= h,
            () =>
            {
                LoadSuppliers();

                // Auto-select the supplier the user just created.
                var newSupplier = AvailableSuppliers.FirstOrDefault(sup => sup.Id == supplierModals.LastSavedSupplierId);
                if (newSupplier != null)
                    SelectedSupplier = newSupplier;
            });
        supplierModals.OpenAddModal();
    }

    /// <summary>
    /// Opens the create product modal on top of the current modal.
    /// </summary>
    [RelayCommand]
    private void OpenCreateProduct(OrderLineItemViewModel? lineItem)
    {
        var productModals = App.ProductModalsViewModel;
        if (productModals == null) return;

        CreateModalSubscription.RearmOnce(ref _productSavedHandler,
            h => productModals.ProductSaved += h,
            h => productModals.ProductSaved -= h,
            () =>
            {
                LoadProducts();

                // Auto-select the new product into the line item whose dropdown launched the create.
                if (lineItem != null)
                {
                    var newProduct = AvailableProducts.FirstOrDefault(p => p.Id == productModals.LastSavedProductId);
                    if (newProduct != null)
                        lineItem.SelectedProduct = newProduct;
                }
            });
        productModals.OpenAddModal();
    }

    /// <summary>
    /// Adds a new line item to the order.
    /// </summary>
    [RelayCommand]
    private void AddLineItem()
    {
        var vm = new OrderLineItemViewModel();
        vm.PropertyChanged += OnOrderLineItemPropertyChanged;
        LineItems.Add(vm);
        UpdateCalculatedTotals();
    }

    /// <summary>
    /// Removes a line item from the order.
    /// </summary>
    [RelayCommand]
    private void RemoveLineItem(OrderLineItemViewModel? item)
    {
        if (item == null) return;
        item.PropertyChanged -= OnOrderLineItemPropertyChanged;
        LineItems.Remove(item);
        UpdateCalculatedTotals();
    }

    /// <summary>
    /// Saves the purchase order.
    /// </summary>
    [RelayCommand]
    private async Task SaveOrder()
    {
        var savedOrderId = await TrySaveOrder();
        if (savedOrderId == null) return;

        OrderSaved?.Invoke(this, EventArgs.Empty);
        CloseAddModal();
    }

    /// <summary>
    /// Saves the purchase order and immediately opens the Send modal so the user can preview
    /// the PDF and review the email before actually sending.
    /// </summary>
    [RelayCommand]
    private async Task SaveAndPreviewOrder()
    {
        var savedOrderId = await TrySaveOrder();
        if (savedOrderId == null) return;

        OrderSaved?.Invoke(this, EventArgs.Empty);

        var companyData = App.CompanyManager?.CompanyData;
        var savedOrder = companyData?.PurchaseOrders.FirstOrDefault(o => o.Id == savedOrderId);
        if (savedOrder == null)
        {
            CloseAddModal();
            return;
        }

        CloseAddModal();
        OpenSendModal(savedOrder);
    }

    /// <summary>
    /// Validates the Add/Edit form and persists the order. Returns the saved order ID on success,
    /// or null if validation failed (errors are already surfaced on the form).
    /// </summary>
    private async Task<string?> TrySaveOrder()
    {
        AddModalError = null;
        HasSupplierError = false;

        foreach (var li in LineItems)
        {
            li.HasProductError = false;
        }

        var hasErrors = false;

        if (SelectedSupplier == null)
        {
            HasSupplierError = true;
            hasErrors = true;
        }

        if (LineItems.Count == 0)
        {
            AddModalError = "Please add at least one line item.".Translate();
            hasErrors = true;
        }
        else
        {
            foreach (var li in LineItems)
            {
                if (string.IsNullOrWhiteSpace(li.ProductId))
                {
                    li.HasProductError = true;
                    hasErrors = true;
                }
                if (!decimal.TryParse(li.Quantity, out var qty) || qty <= 0)
                {
                    AddModalError = "Please enter valid quantities for all line items.".Translate();
                    hasErrors = true;
                }
                if (!decimal.TryParse(li.UnitCost, out var cost) || cost < 0)
                {
                    AddModalError = "Please enter valid unit costs for all line items.".Translate();
                    hasErrors = true;
                }
            }
        }

        if (!decimal.TryParse(ShippingCost, out var shipping) || shipping < 0)
        {
            AddModalError = "Please enter a valid shipping cost.".Translate();
            hasErrors = true;
        }

        if (hasErrors) return null;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return null;

        // Fetch the order-date rate up front (like manual entry) so the saved order shows its amount
        // immediately instead of a momentary "Pending".
        await CurrencyService.WarmRateForDateAsync(OrderDate?.DateTime ?? DateTime.Today);

        return IsEditMode && !string.IsNullOrEmpty(EditingOrderId)
            ? SaveEditedOrder(companyData, shipping)
            : SaveNewOrder(companyData, shipping);
    }

    private string SaveNewOrder(CompanyData companyData, decimal shipping)
    {
        // Generate ID
        companyData.IdCounters.PurchaseOrder++;
        var orderId = $"PO-{companyData.IdCounters.PurchaseOrder:D5}";
        var poNumber = $"#PO-{DateTime.Now.Year}-{companyData.IdCounters.PurchaseOrder:D3}";

        var lineItems = LineItems.Select(li => new PurchaseOrderLineItem
        {
            ProductId = li.ProductId,
            Quantity = decimal.Parse(li.Quantity),
            UnitCost = decimal.Parse(li.UnitCost),
            QuantityReceived = 0
        }).ToList();

        var subtotal = lineItems.Sum(li => li.Total);
        var total = subtotal + shipping;

        var order = new PurchaseOrder
        {
            Id = orderId,
            PoNumber = poNumber,
            SupplierId = SelectedSupplier!.Id,
            OrderDate = OrderDate?.DateTime ?? DateTime.Today,
            ExpectedDeliveryDate = ExpectedDeliveryDate?.DateTime ?? DateTime.Today.AddDays(7),
            LineItems = lineItems,
            Subtotal = subtotal,
            ShippingCost = shipping,
            Total = total,
            Status = PurchaseOrderStatus.Draft,
            Notes = Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        ApplyDisplayCurrency(companyData, order);

        companyData.PurchaseOrders.Add(order);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.PurchaseOrderCreated);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Create order '{poNumber}'",
            () =>
            {
                companyData.PurchaseOrders.Remove(order);
                companyData.MarkAsModified();
                OrderSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.PurchaseOrders.Add(order);
                companyData.MarkAsModified();
                OrderSaved?.Invoke(this, EventArgs.Empty);
            }));

        return order.Id;
    }

    /// <summary>
    /// Tags a purchase order with the company's display currency and its USD total, so a non-USD
    /// company's PO rows show the amount instead of "Pending". Converts at the order date when the
    /// exact-date rate is cached; otherwise marks it pending and queues it for the self-heal. The
    /// display is correct immediately because the original currency matches the display currency.
    /// </summary>
    private static void ApplyDisplayCurrency(CompanyData companyData, PurchaseOrder order)
    {
        var currency = CurrencyService.CurrentCurrencyCode;
        order.OriginalCurrency = currency;

        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            order.TotalUSD = order.Total;
            order.IsPendingConversion = false;
            return;
        }

        var rates = Core.Services.ExchangeRateService.Instance;
        if (rates != null && rates.TryConvertToUsdBase(order.Total, currency, order.OrderDate, out var usd))
        {
            order.TotalUSD = usd;
            order.IsPendingConversion = false;
            return;
        }

        // Exact-date rate not cached: the display is already correct (original currency == display
        // currency); defer the USD total to the self-heal queue.
        order.IsPendingConversion = true;
        var entry = new Core.Models.Common.PendingConversion
        {
            TransactionId = order.Id,
            TransactionType = "PurchaseOrder",
            OriginalCurrency = currency,
            TransactionDate = order.OrderDate,
            Total = order.Total
        };
        // Drop any earlier pending entry for this order first, so re-editing an offline/future-dated
        // PO doesn't accumulate duplicates in the saved file (matches the manual edit path).
        companyData.PendingConversions.RemoveAll(p => p.TransactionId == order.Id);
        companyData.PendingConversions.Add(entry);
        _ = Core.Services.PendingConversionService.Instance?.AddPendingConversionAsync(entry);
    }

    private string? SaveEditedOrder(CompanyData companyData, decimal shipping)
    {
        var order = companyData.PurchaseOrders.FirstOrDefault(o => o.Id == EditingOrderId);
        if (order == null) return null;

        // Store old values for undo
        var oldSupplierId = order.SupplierId;
        var oldOrderDate = order.OrderDate;
        var oldExpectedDate = order.ExpectedDeliveryDate;
        var oldLineItems = order.LineItems.ToList();
        var oldSubtotal = order.Subtotal;
        var oldShipping = order.ShippingCost;
        var oldTotal = order.Total;
        var oldNotes = order.Notes;
        // Currency-conversion fields are mutated by ApplyDisplayCurrency below (and it may add a
        // PendingConversions entry). Capture them so undo/redo restore them symmetrically instead of
        // leaving a stale USD total / pending flag and an orphaned self-heal entry.
        var oldOriginalCurrency = order.OriginalCurrency;
        var oldTotalUSD = order.TotalUSD;
        var oldIsPendingConversion = order.IsPendingConversion;
        var oldPending = companyData.PendingConversions.Where(p => p.TransactionId == order.Id).ToList();

        // Update order
        order.SupplierId = SelectedSupplier!.Id;
        order.OrderDate = OrderDate?.DateTime ?? DateTime.Today;
        order.ExpectedDeliveryDate = ExpectedDeliveryDate?.DateTime ?? DateTime.Today.AddDays(7);
        order.LineItems = LineItems.Select(li => new PurchaseOrderLineItem
        {
            ProductId = li.ProductId,
            Quantity = decimal.Parse(li.Quantity),
            UnitCost = decimal.Parse(li.UnitCost),
            QuantityReceived = 0
        }).ToList();
        order.Subtotal = order.LineItems.Sum(li => li.Total);
        order.ShippingCost = shipping;
        order.Total = order.Subtotal + shipping;
        order.Notes = Notes;
        order.UpdatedAt = DateTime.UtcNow;
        ApplyDisplayCurrency(companyData, order);
        companyData.MarkAsModified();

        // Record undo action. Capture the NEW values as locals so redo restores the edit itself.
        var editedOrder = order;
        var newLineItems = order.LineItems.ToList();
        var newSupplierId = order.SupplierId;
        var newOrderDate = order.OrderDate;
        var newExpectedDate = order.ExpectedDeliveryDate;
        var newSubtotal = order.Subtotal;
        var newShipping = order.ShippingCost;
        var newTotal = order.Total;
        var newNotes = order.Notes;
        // Post-ApplyDisplayCurrency currency state, captured so redo restores the edited conversion.
        var newOriginalCurrency = order.OriginalCurrency;
        var newTotalUSD = order.TotalUSD;
        var newIsPendingConversion = order.IsPendingConversion;
        var newPending = companyData.PendingConversions.Where(p => p.TransactionId == order.Id).ToList();
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit order '{order.PoNumber}'",
            () =>
            {
                editedOrder.SupplierId = oldSupplierId;
                editedOrder.OrderDate = oldOrderDate;
                editedOrder.ExpectedDeliveryDate = oldExpectedDate;
                editedOrder.LineItems = oldLineItems;
                editedOrder.Subtotal = oldSubtotal;
                editedOrder.ShippingCost = oldShipping;
                editedOrder.Total = oldTotal;
                editedOrder.Notes = oldNotes;
                editedOrder.OriginalCurrency = oldOriginalCurrency;
                editedOrder.TotalUSD = oldTotalUSD;
                editedOrder.IsPendingConversion = oldIsPendingConversion;
                companyData.PendingConversions.RemoveAll(p => p.TransactionId == editedOrder.Id);
                companyData.PendingConversions.AddRange(oldPending);
                companyData.MarkAsModified();
                OrderSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                editedOrder.SupplierId = newSupplierId;
                editedOrder.OrderDate = newOrderDate;
                editedOrder.ExpectedDeliveryDate = newExpectedDate;
                editedOrder.LineItems = newLineItems;
                editedOrder.Subtotal = newSubtotal;
                editedOrder.ShippingCost = newShipping;
                editedOrder.Total = newTotal;
                editedOrder.Notes = newNotes;
                editedOrder.OriginalCurrency = newOriginalCurrency;
                editedOrder.TotalUSD = newTotalUSD;
                editedOrder.IsPendingConversion = newIsPendingConversion;
                companyData.PendingConversions.RemoveAll(p => p.TransactionId == editedOrder.Id);
                companyData.PendingConversions.AddRange(newPending);
                companyData.MarkAsModified();
                OrderSaved?.Invoke(this, EventArgs.Empty);
            }));

        return order.Id;
    }

    private void LoadSuppliers()
    {
        AvailableSuppliers.Clear();
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Suppliers == null) return;

        foreach (var supplier in companyData.Suppliers.OrderBy(s => s.Name))
        {
            AvailableSuppliers.Add(supplier);
        }
    }

    private void LoadProducts()
    {
        AvailableProducts.Clear();
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Products == null) return;

        foreach (var product in companyData.Products.OrderBy(p => p.Name))
        {
            AvailableProducts.Add(product);
        }
    }

    private void ClearAddModalFields()
    {
        SelectedSupplier = null;
        OrderDate = new DateTimeOffset(DateTime.Today);
        ExpectedDeliveryDate = new DateTimeOffset(DateTime.Today.AddDays(7));
        ShippingCost = "0";
        Notes = string.Empty;
        AddModalError = null;
        HasSupplierError = false;
        foreach (var li in LineItems)
            li.PropertyChanged -= OnOrderLineItemPropertyChanged;
        LineItems.Clear();
        IsEditMode = false;
        EditingOrderId = null;
    }

    #endregion

    #region View Modal Commands

    /// <summary>
    /// Opens the view order modal.
    /// </summary>
    public void OpenViewModal(PurchaseOrderDisplayItem item)
    {
        ViewingOrder = item;
        LoadViewLineItems(item.Id);
        IsViewModalOpen = true;
    }

    /// <summary>
    /// Closes the view modal.
    /// </summary>
    [RelayCommand]
    private void CloseViewModal()
    {
        IsViewModalOpen = false;
        ViewingOrder = null;
        ViewLineItems.Clear();
    }

    private void LoadViewLineItems(string orderId)
    {
        ViewLineItems.Clear();
        var companyData = App.CompanyManager?.CompanyData;

        var order = companyData?.PurchaseOrders.FirstOrDefault(o => o.Id == orderId);
        if (order == null) return;

        var products = companyData?.Products ?? [];
        foreach (var li in order.LineItems)
        {
            var product = products.FirstOrDefault(p => p.Id == li.ProductId);
            ViewLineItems.Add(new ViewLineItemDisplay
            {
                ProductName = product?.Name ?? "Unknown Product",
                ProductSku = product?.Sku ?? "-",
                Quantity = li.Quantity,
                QuantityReceived = li.QuantityReceived,
                UnitCost = li.UnitCost,
                Total = li.Total
            });
        }
    }

    #endregion

    #region Receive Modal Commands

    /// <summary>
    /// Opens the receive order modal.
    /// </summary>
    public void OpenReceiveModal(PurchaseOrderDisplayItem item)
    {
        ReceivingOrder = item;
        LoadReceiveLineItems(item.Id);
        ReceiveModalError = null;
        IsReceiveModalOpen = true;
    }

    /// <summary>
    /// Closes the receive modal.
    /// </summary>
    [RelayCommand]
    private void CloseReceiveModal()
    {
        IsReceiveModalOpen = false;
        ReceivingOrder = null;
        ReceiveLineItems.Clear();
        ReceiveModalError = null;
    }

    /// <summary>
    /// Confirms receiving items.
    /// </summary>
    [RelayCommand]
    private void ConfirmReceive()
    {
        ReceiveModalError = null;

        if (ReceivingOrder == null) return;

        // Validate quantities
        foreach (var li in ReceiveLineItems)
        {
            if (!decimal.TryParse(li.ReceivingQuantity, out var qty) || qty < 0)
            {
                ReceiveModalError = "Please enter valid quantities.".Translate();
                return;
            }
            if (qty > li.Remaining)
            {
                ReceiveModalError = $"Cannot receive more than remaining quantity ({li.Remaining}) for {li.ProductName}.";
                return;
            }
        }

        // Nothing to do if no quantities were entered; skip work and undo record.
        if (ReceiveLineItems.All(li => !decimal.TryParse(li.ReceivingQuantity, out var q) || q == 0))
        {
            CloseReceiveModal();
            return;
        }

        var companyData = App.CompanyManager?.CompanyData;

        var order = companyData?.PurchaseOrders.FirstOrDefault(o => o.Id == ReceivingOrder.Id);
        if (companyData == null || order == null) return;

        // Store old values for undo
        var oldLineItemReceived = order.LineItems.Select(li => li.QuantityReceived).ToList();
        var oldStatus = order.Status;
        var stockChanges = new List<ReceivedStockChange>();

        // Apply received quantities
        for (var i = 0; i < ReceiveLineItems.Count && i < order.LineItems.Count; i++)
        {
            if (decimal.TryParse(ReceiveLineItems[i].ReceivingQuantity, out var qty) && qty > 0)
            {
                order.LineItems[i].QuantityReceived += qty;

                var change = ReceiveIntoStock(companyData, order.LineItems[i], qty, order.PoNumber);
                if (change != null)
                    stockChanges.Add(change);
            }
        }

        // Update order status
        if (order.IsFullyReceived)
        {
            order.Status = PurchaseOrderStatus.Received;
        }
        else if (order.LineItems.Any(li => li.QuantityReceived > 0))
        {
            order.Status = PurchaseOrderStatus.PartiallyReceived;
        }

        order.UpdatedAt = DateTime.UtcNow;
        companyData.MarkAsModified();

        // Record undo action
        var receivedOrder = order;
        var newLineItemReceived = order.LineItems.Select(li => li.QuantityReceived).ToList();
        var newStatus = order.Status;

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Receive items for '{order.PoNumber}'",
            () =>
            {
                for (var i = 0; i < receivedOrder.LineItems.Count; i++)
                    receivedOrder.LineItems[i].QuantityReceived = oldLineItemReceived[i];

                // Newest first, so an item received on two lines ends at its original stock
                for (var i = stockChanges.Count - 1; i >= 0; i--)
                {
                    var change = stockChanges[i];
                    var stockBeforeUndo = change.Item.InStock;
                    change.Item.InStock = change.OldStock;
                    change.Item.Status = change.Item.CalculateStatus();
                    change.Item.LastUpdated = DateTime.UtcNow;
                    if (change.WasCreated)
                        companyData.Inventory.Remove(change.Item);
                    else
                        App.CheckAndNotifyStockStatus(change.Item, stockBeforeUndo);
                    companyData.StockAdjustments.Remove(change.Adjustment);
                }
                receivedOrder.Status = oldStatus;
                companyData.MarkAsModified();
                OrderSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                for (var i = 0; i < receivedOrder.LineItems.Count; i++)
                    receivedOrder.LineItems[i].QuantityReceived = newLineItemReceived[i];

                foreach (var change in stockChanges)
                {
                    if (change.WasCreated)
                        companyData.Inventory.Add(change.Item);
                    change.Item.InStock += change.Adjustment.Quantity;
                    change.Item.Status = change.Item.CalculateStatus();
                    change.Item.LastUpdated = DateTime.UtcNow;
                    companyData.StockAdjustments.Add(change.Adjustment);
                }
                receivedOrder.Status = newStatus;
                companyData.MarkAsModified();
                OrderSaved?.Invoke(this, EventArgs.Empty);
            }));

        OrderSaved?.Invoke(this, EventArgs.Empty);
        CloseReceiveModal();
    }

    private sealed record ReceivedStockChange(InventoryItem Item, decimal OldStock, StockAdjustment Adjustment, bool WasCreated);

    /// <summary>
    /// Adds received units to stock and records them in the stock ledger, which historical inventory
    /// valuations roll back from. A tracked product with no inventory row gets one, as an expense does.
    /// </summary>
    private static ReceivedStockChange? ReceiveIntoStock(CompanyData companyData, PurchaseOrderLineItem line, decimal qty, string reference)
    {
        var inventoryItem = InventoryStockService.FindStockItem(companyData, line.ProductId, null);
        var wasCreated = false;
        if (inventoryItem == null)
        {
            var product = companyData.Products.FirstOrDefault(p => p.Id == line.ProductId);
            if (product is not { TrackInventory: true }) return null;

            companyData.IdCounters.InventoryItem++;
            inventoryItem = new InventoryItem
            {
                Id = $"INV-ITM-{companyData.IdCounters.InventoryItem:D5}",
                ProductId = product.Id,
                Sku = product.Sku,
                LocationId = companyData.Locations.FirstOrDefault()?.Id ?? InventoryStockService.NoLocationId,
                UnitOfMeasure = product.UnitOfMeasure,
                UnitCost = line.UnitCost,
                LastUpdated = DateTime.UtcNow
            };
            companyData.Inventory.Add(inventoryItem);
            wasCreated = true;
        }

        var oldStock = inventoryItem.InStock;
        inventoryItem.InStock += qty;
        inventoryItem.Status = inventoryItem.CalculateStatus();
        inventoryItem.LastUpdated = DateTime.UtcNow;

        companyData.IdCounters.StockAdjustment++;
        var adjustment = new StockAdjustment
        {
            Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
            InventoryItemId = inventoryItem.Id,
            AdjustmentType = AdjustmentType.Add,
            Quantity = qty,
            PreviousStock = oldStock,
            NewStock = inventoryItem.InStock,
            Reason = "Purchase order received",
            ReferenceNumber = reference,
            Timestamp = DateTime.UtcNow,
            IsAutoGenerated = true
        };
        companyData.StockAdjustments.Add(adjustment);

        return new ReceivedStockChange(inventoryItem, oldStock, adjustment, wasCreated);
    }

    private void LoadReceiveLineItems(string orderId)
    {
        ReceiveLineItems.Clear();
        var companyData = App.CompanyManager?.CompanyData;

        var order = companyData?.PurchaseOrders.FirstOrDefault(o => o.Id == orderId);
        if (order == null) return;

        var products = companyData?.Products ?? [];
        foreach (var li in order.LineItems)
        {
            var product = products.FirstOrDefault(p => p.Id == li.ProductId);
            ReceiveLineItems.Add(new ReceiveLineItemViewModel
            {
                ProductId = li.ProductId,
                ProductName = product?.Name ?? "Unknown Product",
                Ordered = li.Quantity,
                Received = li.QuantityReceived,
                Remaining = li.Quantity - li.QuantityReceived,
                ReceivingQuantity = "0"
            });
        }
    }

    #endregion

    #region Delete Confirmation Commands

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    public async void OpenDeleteConfirm(PurchaseOrderDisplayItem item)
    {
        try
        {
            var dialog = App.ConfirmationDialog;
            if (dialog == null) return;

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Purchase Order".Translate(),
                Message = "Are you sure you want to delete this purchase order?\n\nPO #: {0}\nTotal: {1}".TranslateFormat(item.PoNumber, item.TotalDisplay),
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;

            var companyData = App.CompanyManager?.CompanyData;

            var order = companyData?.PurchaseOrders.FirstOrDefault(o => o.Id == item.Id);
            if (order == null) return;

            companyData?.PurchaseOrders.Remove(order);
            companyData?.MarkAsModified();

            // Record undo action
            var orderPoNumber = item.PoNumber;
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Delete order '{orderPoNumber}'",
                () =>
                {
                    companyData?.PurchaseOrders.Add(order);
                    companyData?.MarkAsModified();
                    OrderDeleted?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    companyData?.PurchaseOrders.Remove(order);
                    companyData?.MarkAsModified();
                    OrderDeleted?.Invoke(this, EventArgs.Empty);
                }));

            OrderDeleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "PurchaseOrder.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Send Modal State

    [ObservableProperty]
    private bool _isSendModalOpen;

    [ObservableProperty]
    private bool _isSendFullscreen;

    [ObservableProperty]
    private PurchaseOrder? _sendingOrder;

    /// <summary>
    /// Modal width. The compact post-send / in-flight states drop to a small
    /// "confirmation card" size so the success/spinner content isn't swimming
    /// in a 1080x840 frame. Fullscreen wins (NaN = stretch).
    /// </summary>
    public double SendModalWidth => IsSendFullscreen
        ? double.NaN
        : ((IsSendSuccess || IsSending) ? 420 : 1080);

    public double SendModalHeight => IsSendFullscreen
        ? double.NaN
        : ((IsSendSuccess || IsSending) ? 360 : 840);

    partial void OnIsSendFullscreenChanged(bool value)
    {
        OnPropertyChanged(nameof(SendModalWidth));
        OnPropertyChanged(nameof(SendModalHeight));
    }

    partial void OnIsSendSuccessChanged(bool value)
    {
        if (value) IsSendFullscreen = false;
        OnPropertyChanged(nameof(SendModalWidth));
        OnPropertyChanged(nameof(SendModalHeight));
        OnPropertyChanged(nameof(ShowSendControls));
    }

    partial void OnIsSendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SendModalWidth));
        OnPropertyChanged(nameof(SendModalHeight));
        OnPropertyChanged(nameof(ShowSendControls));
    }

    [ObservableProperty]
    private string _sendRecipientEmail = string.Empty;

    [ObservableProperty]
    private string _sendCcEmail = string.Empty;

    [ObservableProperty]
    private string _sendBccEmail = string.Empty;

    [ObservableProperty]
    private bool _isSendCcBccExpanded;

    [ObservableProperty]
    private string _sendSubject = string.Empty;

    [ObservableProperty]
    private string _sendBody = string.Empty;

    [ObservableProperty]
    private string _sendPdfFilename = string.Empty;

    [ObservableProperty]
    private Bitmap? _sendPdfPreview;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private string? _sendError;

    [ObservableProperty]
    private bool _isSendSuccess;

    /// <summary>
    /// Raw PDF bytes for the order being sent. Held so the Download button and the
    /// actual send call reuse the same render output rather than regenerating.
    /// </summary>
    private byte[]? _sendPdfBytes;

    /// <summary>
    /// Cancellation source for an in-flight send. The Cancel button trips this so the
    /// HTTP request aborts instead of hanging the user behind the sending overlay.
    /// </summary>
    private CancellationTokenSource? _sendCts;

    public bool HasSendError => !string.IsNullOrEmpty(SendError);

    /// <summary>
    /// Visibility helper for the footer controls that only make sense while the user is
    /// composing the email (fullscreen toggle, Download PDF, Send). Hidden while the send
    /// is in flight (replaced by Cancel) and after success (replaced by Close).
    /// </summary>
    public bool ShowSendControls => !IsSendSuccess && !IsSending;

    partial void OnSendErrorChanged(string? value) => OnPropertyChanged(nameof(HasSendError));

    #endregion

    #region Send Modal Commands

    /// <summary>
    /// Opens the Send modal for the given display item.
    /// </summary>
    public void OpenSendModal(PurchaseOrderDisplayItem item)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var order = companyData?.PurchaseOrders.FirstOrDefault(o => o.Id == item.Id);
        if (order == null) return;
        OpenSendModal(order);
    }

    /// <summary>
    /// Opens the Send modal with the given order pre-filled.
    /// </summary>
    public void OpenSendModal(PurchaseOrder order)
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        SendingOrder = order;
        SendError = null;
        IsSendSuccess = false;
        IsSending = false;
        IsSendCcBccExpanded = false;

        var settings = companyData.Settings.PurchaseOrderEmail;
        var symbol = CurrencyService.CurrentSymbol;

        var supplier = companyData.GetSupplier(order.SupplierId);
        SendRecipientEmail = supplier?.Email ?? string.Empty;
        SendCcEmail = string.Empty;
        SendBccEmail = settings.BccEmail;
        SendSubject = PurchaseOrderEmailService.FillTemplate(settings.SubjectTemplate, order, companyData, symbol);
        SendBody = PurchaseOrderEmailService.FillTemplate(settings.BodyTemplate, order, companyData, symbol);
        SendPdfFilename = $"{SanitizePoFilename(order.PoNumber)}.pdf";

        SendPdfPreview?.Dispose();
        SendPdfPreview = null;
        _sendPdfBytes = null;

        IsSendModalOpen = true;

        _ = GenerateSendPreviewAsync(order, companyData, symbol);
    }

    [RelayCommand]
    private void CloseSendModal()
    {
        // If a send is in flight when the modal closes, abort it so we don't leak
        // a hanging HTTP call that flips status to Sent after the user moved on.
        _sendCts?.Cancel();
        _sendCts?.Dispose();
        _sendCts = null;

        IsSendModalOpen = false;
        IsSendFullscreen = false;
        SendingOrder = null;
        SendError = null;
        IsSendSuccess = false;
        IsSending = false;
        SendPdfPreview?.Dispose();
        SendPdfPreview = null;
        _sendPdfBytes = null;
    }

    /// <summary>
    /// Aborts the in-flight send. The footer swaps to a single Cancel button while IsSending
    /// is true, and this is what that button calls.
    /// </summary>
    [RelayCommand]
    private void CancelSend()
    {
        _sendCts?.Cancel();
    }

    [RelayCommand]
    private void ToggleSendFullscreen() => IsSendFullscreen = !IsSendFullscreen;

    [RelayCommand]
    private void ToggleCcBcc() => IsSendCcBccExpanded = !IsSendCcBccExpanded;

    [RelayCommand]
    private void DismissSendError() => SendError = null;

    [RelayCommand]
    private async Task DownloadSendPdfAsync()
    {
        try
        {
            if (SendingOrder == null) return;

            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null) return;

            _sendPdfBytes ??= PurchaseOrderPdfRenderer.Render(SendingOrder, companyData, CurrencyService.CurrentSymbol);

            var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel?.StorageProvider == null) return;

            var saved = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Purchase Order PDF".Translate(),
                SuggestedFileName = SendPdfFilename,
                DefaultExtension = "pdf",
                FileTypeChoices =
                [
                    new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }
                ]
            });

            if (saved == null) return;

            await File.WriteAllBytesAsync(saved.Path.LocalPath, _sendPdfBytes);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "PurchaseOrder.DownloadPdf");
            SendError = "Could not save the PDF: {0}".TranslateFormat(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SendOrderAsync()
    {
        SendError = null;

        if (SendingOrder == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var recipient = SendRecipientEmail?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(recipient) || !recipient.Contains('@'))
        {
            SendError = "Please enter a valid recipient email address.".Translate();
            return;
        }

        var subject = SendSubject?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(subject))
        {
            SendError = "Please enter an email subject.".Translate();
            return;
        }

        // If the recipient differs from the supplier's saved email, ask the user
        // whether to update the saved address. Three-way choice so users can:
        //   - Update & Send (this becomes the supplier's new email going forward)
        //   - Send without saving (one-off override, leave the saved address alone)
        //   - Cancel (something is wrong, let me re-check)
        var supplier = companyData.GetSupplier(SendingOrder.SupplierId);
        var shouldSaveSupplierEmail = false;
        if (supplier != null)
        {
            var savedEmail = supplier.Email?.Trim() ?? string.Empty;
            if (!string.Equals(savedEmail, recipient, StringComparison.OrdinalIgnoreCase))
            {
                var dialog = App.ConfirmationDialog;
                if (dialog != null)
                {
                    var hasSavedEmail = !string.IsNullOrEmpty(savedEmail);
                    var result = await dialog.ShowAsync(new ConfirmationDialogOptions
                    {
                        Title = (hasSavedEmail ? "Update supplier email?" : "Save supplier email?").Translate(),
                        Message = hasSavedEmail
                            ? "You're sending to a different address than {0}'s saved email ({1}). Update the saved address to {2}? The order will be sent either way.".TranslateFormat(supplier.Name, savedEmail, recipient)
                            : "{0} doesn't have a saved email. Save {1} for next time? The order will be sent either way.".TranslateFormat(supplier.Name, recipient),
                        PrimaryButtonText = (hasSavedEmail ? "Update" : "Save").Translate(),
                        SecondaryButtonText = (hasSavedEmail ? "Don't update" : "Don't save").Translate(),
                        CancelButtonText = "Cancel".Translate()
                    });

                    if (result == ConfirmationResult.Cancel || result == ConfirmationResult.None) return;
                    shouldSaveSupplierEmail = result == ConfirmationResult.Primary;
                }
            }
        }

        if (_sendPdfBytes == null)
        {
            try
            {
                _sendPdfBytes = PurchaseOrderPdfRenderer.Render(SendingOrder, companyData, CurrencyService.CurrentSymbol);
            }
            catch (Exception ex)
            {
                App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "PurchaseOrder.RenderPdf");
                SendError = "Could not generate the PDF: {0}".TranslateFormat(ex.Message);
                return;
            }
        }

        _sendCts?.Dispose();
        _sendCts = new CancellationTokenSource();
        var ct = _sendCts.Token;

        IsSending = true;
        try
        {
            using var service = new PurchaseOrderEmailService();
            var response = await service.SendAsync(
                SendingOrder,
                companyData,
                companyData.Settings.PurchaseOrderEmail,
                recipient,
                subject,
                SendBody ?? string.Empty,
                string.IsNullOrWhiteSpace(SendCcEmail) ? null : SendCcEmail.Trim(),
                string.IsNullOrWhiteSpace(SendBccEmail) ? null : SendBccEmail.Trim(),
                _sendPdfBytes,
                ct);

            // User clicked Cancel while the request was in flight. If the server already
            // accepted the email we still treat that as a successful send (cannot un-send);
            // only swallow the failure path silently.
            if (!response.Success && ct.IsCancellationRequested) return;

            if (!response.Success)
            {
                SendError = response.Message;
                return;
            }

            var oldStatus = SendingOrder.Status;
            SendingOrder.Status = PurchaseOrderStatus.Sent;
            SendingOrder.UpdatedAt = DateTime.UtcNow;
            companyData.MarkAsModified();

            var sentOrder = SendingOrder;
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Send order '{sentOrder.PoNumber}'",
                () =>
                {
                    sentOrder.Status = oldStatus;
                    companyData.MarkAsModified();
                    OrderSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    sentOrder.Status = PurchaseOrderStatus.Sent;
                    companyData.MarkAsModified();
                    OrderSaved?.Invoke(this, EventArgs.Empty);
                }));

            if (shouldSaveSupplierEmail && supplier != null)
            {
                var supplierToUpdate = supplier;
                var oldEmail = supplierToUpdate.Email ?? string.Empty;
                var newEmail = recipient;
                supplierToUpdate.Email = newEmail;
                supplierToUpdate.UpdatedAt = DateTime.UtcNow;
                companyData.MarkAsModified();

                App.UndoRedoManager.RecordAction(new DelegateAction(
                    $"Update supplier '{supplierToUpdate.Name}' email",
                    () =>
                    {
                        supplierToUpdate.Email = oldEmail;
                        companyData.MarkAsModified();
                    },
                    () =>
                    {
                        supplierToUpdate.Email = newEmail;
                        companyData.MarkAsModified();
                    }));
            }

            OrderSaved?.Invoke(this, EventArgs.Empty);
            IsSendSuccess = true;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "PurchaseOrder.Send");
            SendError = "Failed to send: {0}".TranslateFormat(ex.Message);
        }
        finally
        {
            IsSending = false;
        }
    }

    private async Task GenerateSendPreviewAsync(PurchaseOrder order, CompanyData companyData, string symbol)
    {
        try
        {
            var bytes = await Task.Run(() => PurchaseOrderPdfRenderer.Render(order, companyData, symbol));
            _sendPdfBytes = bytes;

            var rendered = await PdfThumbnailService.Instance.RenderPdfFirstPageAsync(bytes);
            if (rendered == null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsSendModalOpen || SendingOrder?.Id != order.Id) return;
                try
                {
                    using var ms = new MemoryStream(rendered.Value.Image);
                    SendPdfPreview = new Bitmap(ms);
                }
                catch (Exception ex)
                {
                    App.ErrorLogger?.LogWarning($"Send PDF preview decode failed: {ex.Message}", "PurchaseOrder.SendPreview");
                }
            });
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "PurchaseOrder.GenerateSendPreview");
        }
    }

    private static string SanitizePoFilename(string poNumber)
    {
        if (string.IsNullOrWhiteSpace(poNumber)) return "PurchaseOrder";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = poNumber.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray();
        var result = new string(chars).Trim('-');
        return string.IsNullOrEmpty(result) ? "PurchaseOrder" : result;
    }

    #endregion

    #region Filter Modal

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private DateTimeOffset? _filterStartDate;

    [ObservableProperty]
    private DateTimeOffset? _filterEndDate;

    [ObservableProperty]
    private string _filterSupplier = "All";

    [ObservableProperty]
    private string _filterStatus = "All";

    /// <summary>
    /// Supplier options for filter dropdown.
    /// </summary>
    public ObservableCollection<string> FilterSupplierOptions { get; } = ["All"];

    /// <summary>
    /// Status options for filter dropdown.
    /// </summary>
    public ObservableCollection<string> FilterStatusOptions { get; } =
        new(PurchaseOrderStatusExtensions.GetFilterOptions());

    // Original filter values for change detection
    private DateTimeOffset? _originalFilterStartDate;
    private DateTimeOffset? _originalFilterEndDate;
    private string _originalFilterSupplier = "All";
    private string _originalFilterStatus = "All";

    /// <summary>
    /// Returns true if any filter has been changed from its original value when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterStartDate != _originalFilterStartDate ||
        FilterEndDate != _originalFilterEndDate ||
        FilterSupplier != _originalFilterSupplier ||
        FilterStatus != _originalFilterStatus;

    /// <summary>
    /// Captures the current filter values as the original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterStartDate = FilterStartDate;
        _originalFilterEndDate = FilterEndDate;
        _originalFilterSupplier = FilterSupplier;
        _originalFilterStatus = FilterStatus;
    }

    /// <summary>
    /// Restores filter values to their original values when the modal was opened.
    /// </summary>
    private void RestoreOriginalFilterValues()
    {
        FilterStartDate = _originalFilterStartDate;
        FilterEndDate = _originalFilterEndDate;
        FilterSupplier = _originalFilterSupplier;
        FilterStatus = _originalFilterStatus;
    }

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    public void OpenFilterModal()
    {
        LoadFilterSupplierOptions();
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
    /// Requests to close the filter modal, showing confirmation if filters have been changed.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (HasFilterModalChanges)
        {
            if (!await ConfirmDiscardFiltersAsync())
                return;

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

    private void ResetFilterDefaults()
    {
        FilterStartDate = null;
        FilterEndDate = null;
        FilterSupplier = "All";
        FilterStatus = "All";
    }

    private void LoadFilterSupplierOptions()
    {
        FilterSupplierOptions.Clear();
        FilterSupplierOptions.Add("All");

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Suppliers == null) return;

        foreach (var supplier in companyData.Suppliers.OrderBy(s => s.Name))
        {
            FilterSupplierOptions.Add(supplier.Name);
        }
    }

    #endregion
}

/// <summary>
/// ViewModel for a line item in the add/edit order modal.
/// </summary>
public partial class OrderLineItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _productName = string.Empty;

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _unitCost = "0.00";

    [ObservableProperty]
    private bool _hasProductError;

    /// <summary>
    /// Available products for the dropdown.
    /// </summary>
    public ObservableCollection<Product> AvailableProducts => App.PurchaseOrdersModalsViewModel?.AvailableProducts ?? [];

    /// <summary>
    /// Selected product.
    /// </summary>
    public Product? SelectedProduct
    {
        get => AvailableProducts.FirstOrDefault(p => p.Id == ProductId);
        set
        {
            if (value != null)
            {
                ProductId = value.Id;
                ProductName = value.Name;
                if (string.IsNullOrEmpty(UnitCost) || UnitCost == "0.00")
                    UnitCost = value.CostPrice.ToString("F2");
                HasProductError = false;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Calculated line total.
    /// </summary>
    public decimal Total
    {
        get
        {
            if (decimal.TryParse(Quantity, out var qty) && decimal.TryParse(UnitCost, out var cost))
                return qty * cost;
            return 0;
        }
    }

    /// <summary>
    /// Total display string.
    /// </summary>
    public string TotalDisplay => CurrencyService.Format(Total);

    partial void OnQuantityChanged(string value)
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalDisplay));
    }

    partial void OnUnitCostChanged(string value)
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalDisplay));
    }
}

/// <summary>
/// Display model for view modal line items.
/// </summary>
public class ViewLineItemDisplay
{
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Total { get; set; }
    public string UnitCostDisplay => CurrencyService.Format(UnitCost);
    public string TotalDisplay => CurrencyService.Format(Total);
    public string QuantityDisplay => $"{StockUnits.Format(QuantityReceived)}/{StockUnits.Format(Quantity)}";
}

/// <summary>
/// ViewModel for a line item in the receive modal.
/// </summary>
public partial class ReceiveLineItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _productName = string.Empty;

    [ObservableProperty]
    private decimal _ordered;

    [ObservableProperty]
    private decimal _received;

    [ObservableProperty]
    private decimal _remaining;

    [ObservableProperty]
    private string _receivingQuantity = "0";
}
