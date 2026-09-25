using ArgoBooks.Localization;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Services;
using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for product modals, shared between ProductsPage and AppShell.
/// </summary>
public partial class ProductModalsViewModel : ViewModelBase
{
    #region Modal State

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isAddModalOpen;

    /// <summary>Title of the Add modal. Reset to the default on each open; callers can override it
    /// (e.g. the bank importer customizing an AI-proposed product) after calling OpenAddModal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    private string _addModalTitle = "Add Product/Service";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isEditModalOpen;

    /// <summary>Add and edit share one form, open while either flag is set.</summary>
    public bool IsFormOpen => IsAddModalOpen || IsEditModalOpen;

    // Set when the form opens and kept on close, so the title doesn't change while it closes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(FormSaveText), nameof(ModalIdPlaceholder))]
    private bool _isEditMode;

    public string FormTitle => IsEditMode ? EditModalTitle : AddModalTitle;
    public string FormSaveText => IsEditMode
        ? "Save Changes".Translate()
        : IsProductSelected ? "Add Product".Translate() : "Add Service".Translate();

    // A blank ID is generated on add but rejected on edit, so only add hints at the format.
    public string ModalIdPlaceholder => IsEditMode ? string.Empty : "PRD-xxx".Translate();

    partial void OnIsAddModalOpenChanged(bool value)
    {
        if (value) IsEditMode = false;
    }

    partial void OnIsEditModalOpenChanged(bool value)
    {
        if (value) IsEditMode = true;
    }

    [RelayCommand]
    private Task RequestCloseFormAsync() => IsEditMode ? RequestCloseEditModalAsync() : RequestCloseAddModalAsync();

    [RelayCommand]
    private void SaveForm()
    {
        if (IsEditMode) SaveEditedProduct();
        else SaveNewProduct();
    }

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private string _modalId = string.Empty;

    [ObservableProperty]
    private string _modalProductName = string.Empty;

    [ObservableProperty]
    private string _modalDescription = string.Empty;

    [ObservableProperty]
    private string _modalItemType = "Product";

    /// <summary>
    /// Gets whether a Product is selected (not Service) - used for showing threshold inputs.
    /// </summary>
    public bool IsProductSelected => ModalItemType == "Product";
    public string EditModalTitle => IsProductSelected ? "Edit Product".Translate() : "Edit Service".Translate();

    partial void OnModalItemTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsProductSelected));
        OnPropertyChanged(nameof(ShowTrackInventoryOption));
        OnPropertyChanged(nameof(EditModalTitle));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(FormSaveText));
    }

    /// <summary>What is typed in the Category box, so a name matching nothing becomes a new category on save.</summary>
    [ObservableProperty]
    private string? _modalCategoryText;

    /// <summary>What is typed in the Supplier box, so a name matching nothing becomes a new supplier on save.</summary>
    [ObservableProperty]
    private string? _modalSupplierText;

    [ObservableProperty]
    private CategoryOption? _modalCategory;

    [ObservableProperty]
    private string? _modalCategoryId;

    partial void OnModalCategoryIdChanged(string? value)
    {
        ModalCategory = value != null ? AvailableCategories.FirstOrDefault(c => c.Id == value) : null;
    }

    [ObservableProperty]
    private SupplierOption? _modalSupplier;

    [ObservableProperty]
    private bool _modalTrackInventory;

    /// <summary>
    /// Set when the stock form opens this to create a product it can stock. That form only lists
    /// tracked products, so tracking is forced on and neither the checkbox nor Service is offered.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTrackInventoryOption))]
    private bool _isForInventory;

    public bool ShowTrackInventoryOption => IsProductSelected && !IsForInventory;

    [ObservableProperty]
    private string _modalUnitOfMeasure = StockUnits.Each;

    /// <summary>Units a tracked product can be stocked in.</summary>
    public IReadOnlyList<string> UnitOptions => StockUnits.All;

    [ObservableProperty]
    private string _modalReorderPoint = string.Empty;

    [ObservableProperty]
    private string _modalOverstockThreshold = string.Empty;

    [ObservableProperty]
    private string _modalUnitPrice = string.Empty;

    [ObservableProperty]
    private string _modalCostPrice = string.Empty;

    [ObservableProperty]
    private string _modalSku = string.Empty;

    [ObservableProperty]
    private string? _modalError;

    [ObservableProperty]
    private string? _modalIdError;

    [ObservableProperty]
    private string? _modalProductNameError;

    [ObservableProperty]
    private string? _modalCategoryError;

    partial void OnModalIdChanged(string value)
    {
        ModalIdError = null;
    }

    partial void OnModalProductNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ModalProductNameError = null;
        }
    }

    partial void OnModalCategoryChanged(CategoryOption? value)
    {
        if (value != null)
        {
            ModalCategoryError = null;
        }
    }

    /// <summary>
    /// The product being edited (null for add).
    /// </summary>
    private Product? _editingProduct;

    /// <summary>
    /// Whether we're in expenses tab (purchase) or revenue tab (sales).
    /// Used to show the relevant price field (Cost Price for expenses, Unit Price for revenue).
    /// </summary>
    [ObservableProperty]
    private bool _isExpensesTab = true;

    private sealed record EditState(
        string Id, string Name, string Description, string ItemType, string? CategoryId, string? SupplierId,
        bool TrackInventory, string UnitOfMeasure, string ReorderPoint, string OverstockThreshold,
        string UnitPrice, string CostPrice, string Sku);

    // The form as the edit modal opened, for change detection.
    private EditState? _original;

    private EditState Capture() => new(
        ModalId.Trim(), ModalProductName, ModalDescription, ModalItemType, ModalCategory?.Id, ModalSupplier?.Id,
        ModalTrackInventory, ModalUnitOfMeasure, ModalReorderPoint, ModalOverstockThreshold,
        ModalUnitPrice, ModalCostPrice, ModalSku);

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasAddModalEnteredData =>
        !string.IsNullOrWhiteSpace(ModalProductName) ||
        !string.IsNullOrWhiteSpace(ModalDescription) ||
        ModalCategory != null ||
        ModalSupplier != null ||
        !string.IsNullOrWhiteSpace(ModalReorderPoint) ||
        !string.IsNullOrWhiteSpace(ModalOverstockThreshold) ||
        !string.IsNullOrWhiteSpace(ModalUnitPrice) ||
        !string.IsNullOrWhiteSpace(ModalCostPrice) ||
        !string.IsNullOrWhiteSpace(ModalSku);

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges => Capture() != _original;

    #endregion

    #region Filter Fields

    [ObservableProperty]
    private string _filterItemType = "All";

    // The dropdowns select option objects, so these hold the option itself; null means All.
    [ObservableProperty]
    private CategoryOption? _filterCategory;

    [ObservableProperty]
    private SupplierOption? _filterSupplier;

    private sealed record FilterValues(string ItemType, string? CategoryId, string? SupplierId)
    {
        public static readonly FilterValues Default = new("All", null, null);
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterItemType, FilterCategory?.Id, FilterSupplier?.Id),
        v =>
        {
            FilterItemType = v.ItemType;
            FilterCategory = v.CategoryId == null ? null : AvailableCategories.FirstOrDefault(c => c.Id == v.CategoryId);
            FilterSupplier = v.SupplierId == null ? null : AvailableSuppliers.FirstOrDefault(s => s.Id == v.SupplierId);
        });

    #endregion

    #region Dropdown Options

    public ObservableCollection<CategoryOption> AvailableCategories { get; } = [];
    public ObservableCollection<CategoryItem> CategoryItems { get; } = [];
    public ObservableCollection<SupplierOption> AvailableSuppliers { get; } = [];
    public ObservableCollection<string> ItemTypes { get; } = ["Product", "Service"];
    public ObservableCollection<string> ItemTypeOptions { get; } = ["All", "Product", "Service"];

    public bool HasCategories => CategoryItems.Count > 0;

    #endregion

    #region Events

    public event EventHandler? ProductSaved;
    public event EventHandler? ProductDeleted;

    /// <summary>
    /// The Id of the product most recently created via the Add modal. Lets a caller that
    /// opened "create product" from another modal auto-select the new product after save.
    /// </summary>
    public string? LastSavedProductId { get; private set; }
    public event EventHandler? FiltersApplied;
    public event EventHandler? FiltersCleared;

    #endregion

    #region Add Product

    [RelayCommand]
    public void OpenAddModal()
    {
        _editingProduct = null;
        ClearModalFields();
        UpdateDropdownOptions();
        AddModalTitle = "Add Product/Service".Translate();
        IsAddModalOpen = true;
    }

    public void OpenAddModal(bool isExpensesTab)
    {
        IsExpensesTab = isExpensesTab;
        OpenAddModal();
    }

    /// <summary>Opens the Add modal for a product that goes straight into stock.</summary>
    public void OpenAddInventoryProductModal()
    {
        OpenAddModal();
        IsForInventory = true;
        ModalTrackInventory = true;
        AddModalTitle = "Add Product".Translate();
    }

    [RelayCommand]
    public void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Add modal, showing confirmation if data was entered.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseAddModalAsync()
    {
        if (HasAddModalEnteredData)
        {
            if (!await ConfirmDiscardNewAsync())
                return;
        }

        CloseAddModal();
    }

    // One-shot handlers for the "create entity from this modal" flows. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _categorySavedHandler;
    private EventHandler? _supplierSavedHandler;

    [RelayCommand]
    public void OpenCategoriesWithAddModal()
    {
        var categoryModals = App.CategoryModalsViewModel;
        if (categoryModals == null) return;

        var isExpense = IsExpensesTab;
        CreateModalSubscription.RearmOnce(ref _categorySavedHandler,
            h => categoryModals.CategorySaved += h,
            h => categoryModals.CategorySaved -= h,
            () =>
            {
                UpdateDropdownOptions();

                // Auto-select the category the user just created.
                var newCategory = AvailableCategories.FirstOrDefault(c => c.Id == categoryModals.LastSavedCategoryId);
                if (newCategory != null)
                {
                    ModalCategory = newCategory;
                    ModalCategoryId = newCategory.Id;
                }
            });
        categoryModals.OpenAddModal(isExpense);
    }

    [RelayCommand]
    public void OpenSuppliersWithAddModal()
    {
        var supplierModals = App.SupplierModalsViewModel;
        if (supplierModals == null) return;

        CreateModalSubscription.RearmOnce(ref _supplierSavedHandler,
            h => supplierModals.SupplierSaved += h,
            h => supplierModals.SupplierSaved -= h,
            () =>
            {
                UpdateDropdownOptions();

                // Auto-select the supplier the user just created.
                var newSupplier = AvailableSuppliers.FirstOrDefault(s => s.Id == supplierModals.LastSavedSupplierId);
                if (newSupplier != null)
                    ModalSupplier = newSupplier;
            });
        supplierModals.OpenAddModal();
    }

    [RelayCommand]
    public void SaveNewProduct()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        string newId;
        if (!string.IsNullOrWhiteSpace(ModalId))
        {
            newId = ModalId.Trim();
        }
        else
        {
            newId = new Core.Data.IdGenerator(companyData).NextProductId();
        }

        var reorderPoint = decimal.TryParse(ModalReorderPoint, out var rp) ? rp : 0;
        var overstockThreshold = decimal.TryParse(ModalOverstockThreshold, out var ot) ? ot : 0;

        var newProduct = new Product
        {
            Id = newId,
            Name = ModalProductName.Trim(),
            Description = string.IsNullOrWhiteSpace(ModalDescription) ? string.Empty : ModalDescription.Trim(),
            Sku = string.IsNullOrWhiteSpace(ModalSku) ? newId : ModalSku.Trim(),
            ItemType = ModalItemType,
            CategoryId = ModalCategory?.Id,
            SupplierId = ModalSupplier?.Id,
            UnitPrice = decimal.TryParse(ModalUnitPrice, out var unitPrice) ? unitPrice : 0,
            CostPrice = decimal.TryParse(ModalCostPrice, out var costPrice) ? costPrice : 0,
            TrackInventory = ModalTrackInventory,
            UnitOfMeasure = ModalUnitOfMeasure,
            ReorderPoint = reorderPoint,
            OverstockThreshold = overstockThreshold,
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        companyData.Products.Add(newProduct);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ProductCreated);
        companyData.MarkAsModified();

        var productToUndo = newProduct;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add product '{newProduct.Name}'",
            () =>
            {
                companyData.Products.Remove(productToUndo);
                companyData.MarkAsModified();
                ProductSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Products.Add(productToUndo);
                companyData.MarkAsModified();
                ProductSaved?.Invoke(this, EventArgs.Empty);
            }));

        LastSavedProductId = newProduct.Id;
        ProductSaved?.Invoke(this, EventArgs.Empty);

        CloseAddModal();
    }

    #endregion

    #region Edit Product

    public void OpenEditModal(ProductDisplayItem? item)
    {
        if (item == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var product = companyData?.Products.FirstOrDefault(p => p.Id == item.Id);
        if (product == null)
            return;

        _editingProduct = product;
        UpdateDropdownOptions();

        ModalId = product.Id;
        ModalProductName = product.Name;
        ModalDescription = product.Description;
        ModalSku = product.Sku;
        ModalUnitPrice = product.UnitPrice.ToString("0.00");
        ModalCostPrice = product.CostPrice.ToString("0.00");

        // The product's own ItemType is the source of truth (it's what the
        // list displays and what imports write), not the category's.
        ModalItemType = product.ItemType;

        if (companyData != null)
        {
            var category = companyData.Categories.FirstOrDefault(c => c.Id == product.CategoryId);
            if (category != null)
            {
                ModalCategory = AvailableCategories.FirstOrDefault(c => c.Id == category.Id);
                ModalCategoryId = category.Id;
            }

            ModalSupplier = AvailableSuppliers.FirstOrDefault(s => s.Id == product.SupplierId);
        }

        ModalTrackInventory = product.TrackInventory;
        ModalUnitOfMeasure = string.IsNullOrEmpty(product.UnitOfMeasure) ? StockUnits.Each : product.UnitOfMeasure;
        ModalReorderPoint = product.ReorderPoint > 0 ? product.ReorderPoint.ToString() : string.Empty;
        ModalOverstockThreshold = product.OverstockThreshold > 0 ? product.OverstockThreshold.ToString() : string.Empty;

        _original = Capture();

        ModalError = null;
        IsEditModalOpen = true;
    }

    public void OpenEditModal(ProductDisplayItem? item, bool isExpensesTab)
    {
        IsExpensesTab = isExpensesTab;
        OpenEditModal(item);
    }

    [RelayCommand]
    public void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingProduct = null;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Edit modal, showing confirmation if changes were made.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseEditModalAsync()
    {
        if (HasEditModalChanges)
        {
            if (!await ConfirmDiscardEditsAsync())
                return;
        }

        CloseEditModal();
    }

    [RelayCommand]
    public void SaveEditedProduct()
    {
        if (!ValidateModal() || _editingProduct == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var oldId = _editingProduct.Id;
        var oldName = _editingProduct.Name;
        var oldDescription = _editingProduct.Description;
        var oldSku = _editingProduct.Sku;
        var oldItemType = _editingProduct.ItemType;
        var oldCategoryId = _editingProduct.CategoryId;
        var oldSupplierId = _editingProduct.SupplierId;
        var oldUnitPrice = _editingProduct.UnitPrice;
        var oldCostPrice = _editingProduct.CostPrice;
        var oldTrackInventory = _editingProduct.TrackInventory;
        var oldUnitOfMeasure = _editingProduct.UnitOfMeasure;
        var oldReorderPoint = _editingProduct.ReorderPoint;
        var oldOverstockThreshold = _editingProduct.OverstockThreshold;

        var newId = ModalId.Trim();
        var newName = ModalProductName.Trim();
        var newDescription = string.IsNullOrWhiteSpace(ModalDescription) ? string.Empty : ModalDescription.Trim();
        // SKU defaults to the product's *new* Id when blank, so the fallback follows a rename.
        var newSku = string.IsNullOrWhiteSpace(ModalSku) ? newId : ModalSku.Trim();
        var newItemType = ModalItemType;
        var newCategoryId = ModalCategory?.Id;
        var newSupplierId = ModalSupplier?.Id;
        var newUnitPrice = decimal.TryParse(ModalUnitPrice, out var unitPrice) ? unitPrice : 0;
        var newCostPrice = decimal.TryParse(ModalCostPrice, out var costPrice) ? costPrice : 0;
        var newReorderPoint = decimal.TryParse(ModalReorderPoint, out var rp) ? rp : 0;
        var newOverstockThreshold = decimal.TryParse(ModalOverstockThreshold, out var ot) ? ot : 0;
        var newTrackInventory = ModalTrackInventory;
        var newUnitOfMeasure = ModalUnitOfMeasure;

        // Check if anything actually changed
        var hasIdChange = oldId != newId;
        var hasChanges = hasIdChange ||
                         oldName != newName ||
                         oldDescription != newDescription ||
                         oldSku != newSku ||
                         oldItemType != newItemType ||
                         oldCategoryId != newCategoryId ||
                         oldSupplierId != newSupplierId ||
                         oldUnitPrice != newUnitPrice ||
                         oldCostPrice != newCostPrice ||
                         oldTrackInventory != newTrackInventory ||
                         oldReorderPoint != newReorderPoint ||
                         oldOverstockThreshold != newOverstockThreshold;

        // If nothing changed, just close the modal without recording an action
        if (!hasChanges)
        {
            CloseEditModal();
            return;
        }

        var productToEdit = _editingProduct;
        var changes = new Dictionary<string, FieldChange>();
        if (hasIdChange) changes["ID"] = new FieldChange { OldValue = oldId, NewValue = newId };
        if (oldName != newName) changes["Name"] = new FieldChange { OldValue = oldName, NewValue = newName };
        if (oldDescription != newDescription) changes["Description"] = new FieldChange { OldValue = oldDescription, NewValue = newDescription };
        if (oldSku != newSku) changes["SKU"] = new FieldChange { OldValue = oldSku, NewValue = newSku };
        if (oldItemType != newItemType) changes["Item Type"] = new FieldChange { OldValue = oldItemType, NewValue = newItemType };
        if (oldUnitPrice != newUnitPrice) changes["Unit Price"] = new FieldChange { OldValue = oldUnitPrice.ToString("F2"), NewValue = newUnitPrice.ToString("F2") };
        if (oldCostPrice != newCostPrice) changes["Cost Price"] = new FieldChange { OldValue = oldCostPrice.ToString("F2"), NewValue = newCostPrice.ToString("F2") };
        if (oldTrackInventory != newTrackInventory) changes["Track Inventory"] = new FieldChange { OldValue = oldTrackInventory.ToString(), NewValue = newTrackInventory.ToString() };
        if (oldUnitOfMeasure != newUnitOfMeasure) changes["Unit"] = new FieldChange { OldValue = oldUnitOfMeasure, NewValue = newUnitOfMeasure };
        if (oldReorderPoint != newReorderPoint) changes["Reorder Point"] = new FieldChange { OldValue = oldReorderPoint.ToString(), NewValue = newReorderPoint.ToString() };
        if (oldOverstockThreshold != newOverstockThreshold) changes["Overstock Threshold"] = new FieldChange { OldValue = oldOverstockThreshold.ToString(), NewValue = newOverstockThreshold.ToString() };
        if (changes.Count > 0) App.EventLogService?.SetPendingChanges(changes);

        // Apply the Id rename FIRST so a failure doesn't leave the product with new
        // field values but the old Id. Validation already prevents conflicts; this
        // ordering is defense-in-depth.
        if (hasIdChange)
        {
            try
            {
                App.CompanyManager?.ChangeProductId(productToEdit, newId);
            }
            catch (Exception ex)
            {
                App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Product.ChangeId");
                ModalIdError = ex.Message;
                return;
            }
        }

        productToEdit.Name = newName;
        productToEdit.Description = newDescription;
        productToEdit.Sku = newSku;
        productToEdit.ItemType = newItemType;
        productToEdit.CategoryId = newCategoryId;
        productToEdit.SupplierId = newSupplierId;
        productToEdit.UnitPrice = newUnitPrice;
        productToEdit.CostPrice = newCostPrice;
        productToEdit.TrackInventory = newTrackInventory;
        productToEdit.UnitOfMeasure = newUnitOfMeasure;
        productToEdit.ReorderPoint = newReorderPoint;
        productToEdit.OverstockThreshold = newOverstockThreshold;
        productToEdit.UpdatedAt = DateTime.UtcNow;

        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit product '{newName}'",
            () =>
            {
                if (hasIdChange) App.CompanyManager?.ChangeProductId(productToEdit, oldId);
                productToEdit.Name = oldName;
                productToEdit.Description = oldDescription;
                productToEdit.Sku = oldSku;
                productToEdit.ItemType = oldItemType;
                productToEdit.CategoryId = oldCategoryId;
                productToEdit.SupplierId = oldSupplierId;
                productToEdit.UnitPrice = oldUnitPrice;
                productToEdit.CostPrice = oldCostPrice;
                productToEdit.TrackInventory = oldTrackInventory;
                productToEdit.UnitOfMeasure = oldUnitOfMeasure;
                productToEdit.ReorderPoint = oldReorderPoint;
                productToEdit.OverstockThreshold = oldOverstockThreshold;
                companyData.MarkAsModified();
                ProductSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                if (hasIdChange) App.CompanyManager?.ChangeProductId(productToEdit, newId);
                productToEdit.Name = newName;
                productToEdit.Description = newDescription;
                productToEdit.Sku = newSku;
                productToEdit.ItemType = newItemType;
                productToEdit.CategoryId = newCategoryId;
                productToEdit.SupplierId = newSupplierId;
                productToEdit.UnitPrice = newUnitPrice;
                productToEdit.CostPrice = newCostPrice;
                productToEdit.TrackInventory = newTrackInventory;
                productToEdit.UnitOfMeasure = newUnitOfMeasure;
                productToEdit.ReorderPoint = newReorderPoint;
                productToEdit.OverstockThreshold = newOverstockThreshold;
                companyData.MarkAsModified();
                ProductSaved?.Invoke(this, EventArgs.Empty);
            }));

        ProductSaved?.Invoke(this, EventArgs.Empty);
        CloseEditModal();
    }

    #endregion

    #region Delete Product

    public async void OpenDeleteConfirm(ProductDisplayItem? item)
    {
        try
        {
            if (item == null)
                return;

            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null)
                return;

            if (await BlockIfInUseAsync(
                    usages => "This product cannot be deleted because it is referenced by one or more: {0}.".TranslateFormat(usages),
                    (companyData.Revenues.Any(r => r.LineItems.Any(li => li.ProductId == item.Id)), "Revenue".Translate()),
                    (companyData.Expenses.Any(e => e.LineItems.Any(li => li.ProductId == item.Id)), "Expense".Translate()),
                    (companyData.Invoices.Any(i => i.LineItems.Any(li => li.ProductId == item.Id)), "Invoice".Translate()),
                    (companyData.Inventory.Any(i => i.ProductId == item.Id), "Inventory".Translate()),
                    (companyData.PurchaseOrders.Any(po => po.LineItems.Any(li => li.ProductId == item.Id)), "Purchase Order".Translate()),
                    (companyData.Returns.Any(r => r.Items.Any(ri => ri.ProductId == item.Id)), "Return".Translate()),
                    (companyData.LostDamaged.Any(ld => ld.ProductId == item.Id), "Lost / Damaged".Translate()),
                    // Both kinds of schedule clone their template's line items into every
                    // occurrence, so a product either of them names is still in use.
                    (companyData.RecurringInvoices.Any(ri => ri.Template != null
                        && ri.Template.LineItems.Any(li => li.ProductId == item.Id)), "Recurring Invoice".Translate()),
                    (RecurringTransactionService.IsProductInUse(companyData, item.Id), "Recurring Transaction".Translate())))
                return;

            if (!await ConfirmDeleteAsync("Delete Product".Translate(),
                    "Are you sure you want to delete this product?\n\n{0}".TranslateFormat(item.Name)))
                return;

            var product = companyData.Products.FirstOrDefault(p => p.Id == item.Id);
            if (product == null)
            {
                ProductDeleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            RemoveWithUndo(companyData, companyData.Products, product, $"Delete product '{product.Name}'",
                () => ProductDeleted?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Product.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Filter Modal

    [RelayCommand]
    public void OpenFilterModal()
    {
        var current = Filters.Current;
        UpdateDropdownOptions();
        // The reload replaced the option objects (and the category list follows the tab), so point
        // the selections at the new ones; a category from the other tab drops back to All.
        Filters.Set(current);
        Filters.Capture();
        IsFilterModalOpen = true;
    }

    public void OpenFilterModal(bool isExpensesTab)
    {
        IsExpensesTab = isExpensesTab;
        OpenFilterModal();
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

    [RelayCommand]
    public void ApplyFilters()
    {
        ActiveFilterCount = Filters.ActiveCount;
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        Filters.Reset();
        ActiveFilterCount = 0;
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    #endregion

    #region Modal Helpers

    private void UpdateDropdownOptions()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var categories = OptionLoader.Categories(companyData, IsExpensesTab ? CategoryType.Expense : CategoryType.Revenue).ToList();
        OptionLoader.Fill(AvailableCategories, categories.AsOptions<CategoryOption>());
        OptionLoader.Fill(CategoryItems, categories.Select(cat => new CategoryItem { Id = cat.Id, Name = cat.Name }));
        OnPropertyChanged(nameof(HasCategories));

        OptionLoader.Fill(AvailableSuppliers, OptionLoader.Suppliers(companyData).AsOptions<SupplierOption>());
    }

    private void ClearModalFields()
    {
        ModalId = string.Empty;
        ModalIdError = null;
        ModalProductName = string.Empty;
        ModalDescription = string.Empty;
        ModalItemType = "Product";
        ModalCategory = null;
        ModalCategoryId = null;
        ModalCategoryText = null;
        ModalSupplierText = null;
        ModalSupplier = null;
        ModalTrackInventory = false;
        IsForInventory = false;
        ModalUnitOfMeasure = StockUnits.Each;
        ModalReorderPoint = string.Empty;
        ModalOverstockThreshold = string.Empty;
        ModalUnitPrice = string.Empty;
        ModalCostPrice = string.Empty;
        ModalSku = string.Empty;
        ModalError = null;
        ModalProductNameError = null;
        ModalCategoryError = null;
    }

    private bool ValidateModal()
    {
        ModalError = null;
        ModalIdError = null;
        ModalProductNameError = null;
        ModalCategoryError = null;

        var isValid = true;

        var companyDataForId = App.CompanyManager?.CompanyData;
        if (companyDataForId != null)
        {
            var trimmedId = ModalId.Trim();
            if (_editingProduct != null && string.IsNullOrEmpty(trimmedId))
            {
                ModalIdError = "ID cannot be empty.".Translate();
                isValid = false;
            }
            else if (!string.IsNullOrEmpty(trimmedId))
            {
                var existingWithSameId = companyDataForId.Products.Any(p =>
                    p.Id == trimmedId &&
                    (_editingProduct == null || !ReferenceEquals(p, _editingProduct)));
                if (existingWithSameId)
                {
                    ModalIdError = "A product with this ID already exists.".Translate();
                    isValid = false;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(ModalProductName))
        {
            ModalProductNameError = "Product name is required.".Translate();
            isValid = false;
        }
        else
        {
            var companyData = App.CompanyManager?.CompanyData;
            var existingWithSameName = companyData?.Products.Any(p =>
                p.Name.Equals(ModalProductName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (_editingProduct == null || p.Id != _editingProduct.Id)) ?? false;

            if (existingWithSameName)
            {
                ModalProductNameError = "A product with this name already exists.".Translate();
                isValid = false;
            }
        }

        ResolveTypedPickers();

        if (string.IsNullOrEmpty(ModalCategoryId))
        {
            ModalCategoryError = HasCategories
                ? "Category is required.".Translate()
                : "Please create a category first.".Translate();
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Creates the category or supplier the user typed but never picked from the list, so a name that
    /// isn't on the list yet saves instead of failing validation.
    /// </summary>
    private void ResolveTypedPickers()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var type = IsExpensesTab ? CategoryType.Expense : CategoryType.Revenue;
        var category = ModalCategory == null ? QuickCreate.EnsureCategory(companyData, type, ModalCategoryText) : null;
        var supplier = ModalSupplier == null ? QuickCreate.EnsureSupplier(companyData, ModalSupplierText) : null;
        if (category == null && supplier == null)
            return;

        UpdateDropdownOptions();
        if (category != null)
            ModalCategoryId = category.Id;
        if (supplier != null)
            ModalSupplier = AvailableSuppliers.FirstOrDefault(s => s.Id == supplier.Id);
    }

    #endregion
}
