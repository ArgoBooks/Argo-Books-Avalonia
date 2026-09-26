using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Services.InvoiceTemplates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>A line on the editable document paper.</summary>
public interface IPaperLine
{
    string Description { get; set; }
    decimal? Quantity { get; set; }
    decimal? UnitPrice { get; set; }
    ProductOption? SelectedProduct { get; set; }
}

/// <summary>What the editable document paper calls back into, whichever document it shows.</summary>
public interface IPaperDocumentEditor
{
    void ApplyPaperEdit(string field, int? index, string value);
    void ToggleTotalsMode(string which);
    void SelectProductForLine(int index, string productId);
    void CreateProductForLine(int index);
    void AddLineFromPaper();
    void RemoveLineFromPaper(int index);
    void SelectCustomerFromPaper(string customerId);
    void CreateCustomerFromPaper();
    void SetDateFromPaper(string field, string iso);
    void SetLogoFromPaper(string base64);
    void DeleteLogoFromPaper();
}

/// <summary>
/// The editor behind the editable document paper, shared by invoices and quotes: the fields the
/// paper edits in place, its pickers, the company-wide logo, and the create-customer/product
/// modals opened over it. Each document supplies how it renders, adds and removes lines, and
/// which dates the paper's two date slots are.
/// </summary>
public abstract partial class PaperDocumentEditorViewModelBase<TLine> : ViewModelBase, IPaperDocumentEditor
    where TLine : class, IPaperLine
{
    [ObservableProperty]
    private CustomerOption? _selectedCustomer;

    [ObservableProperty]
    private bool _hasCustomerError;

    [ObservableProperty]
    private InvoiceTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    // Totals fields, all edited directly in the totals block on the paper.
    [ObservableProperty]
    private decimal _taxRate;

    [ObservableProperty]
    private bool _taxIsFixed;

    [ObservableProperty]
    private decimal _shippingAmount;

    [ObservableProperty]
    private decimal _discountAmount;

    [ObservableProperty]
    private bool _discountIsPercent;

    [ObservableProperty]
    private string _customFeeLabel = string.Empty;

    [ObservableProperty]
    private decimal _customFeeAmount;

    [ObservableProperty]
    private bool _customFeeIsPercent;

    /// <summary>
    /// Preview mode renders the paper clean (no edit outlines, no add-line, no pickers) so the user
    /// sees exactly what the customer gets before sending.
    /// </summary>
    [ObservableProperty]
    private bool _isEditorPreviewing;

    /// <summary>
    /// True while a modal is open over the editor. The paper is a native web view that draws above
    /// Avalonia content and would cover that modal, so it is hidden meanwhile.
    /// </summary>
    [ObservableProperty]
    private bool _isNestedModalOpen;

    /// <summary>Line items, edited in place on the paper.</summary>
    public ObservableCollection<TLine> LineItems { get; } = [];

    public ObservableCollection<CustomerOption> CustomerOptions { get; } = [];

    public ObservableCollection<ProductOption> ProductOptions { get; } = [];

    /// <summary>The invoice templates, which quotes share so both documents look the same.</summary>
    public ObservableCollection<InvoiceTemplate> TemplateOptions { get; } = [];

    /// <summary>Config the paper's live totals recompute needs: currency, deposit, paid, fee.</summary>
    public abstract string TotalsConfigJson { get; }

    /// <summary>
    /// Re-renders the paper from current state. Structured changes (customer, dates, template,
    /// totals modes, lines added or removed) call this. Text typed into the paper must NOT: a
    /// reload would interrupt typing.
    /// </summary>
    protected abstract void RegeneratePaper();

    protected abstract void AddBlankLine();

    protected abstract void RemoveLineAt(int index);

    /// <param name="field">"issueDate", or "dueDate" for the paper's second date slot.</param>
    protected abstract void ApplyPaperDate(string field, DateTimeOffset date);

    protected virtual void OnTemplateChanged(InvoiceTemplate? template) => RegeneratePaper();

    // Amounts are typed straight into the paper, so they must NOT re-render: rebuilding the page
    // mid-keystroke recreates the field the caret is in and the next character goes nowhere.
    protected virtual void OnTotalsAmountChanged() { }

    // Percent against fixed is a click on the swap button, so a re-render puts the new symbol on the paper.
    protected virtual void OnTotalsModeChanged() => RegeneratePaper();

    partial void OnSelectedCustomerChanged(CustomerOption? value)
    {
        if (value != null && !string.IsNullOrEmpty(value.Id))
            HasCustomerError = false;
        RegeneratePaper();
    }

    partial void OnSelectedTemplateChanged(InvoiceTemplate? value) => OnTemplateChanged(value);
    partial void OnTaxRateChanged(decimal value) => OnTotalsAmountChanged();
    partial void OnShippingAmountChanged(decimal value) => OnTotalsAmountChanged();
    partial void OnDiscountAmountChanged(decimal value) => OnTotalsAmountChanged();
    partial void OnCustomFeeAmountChanged(decimal value) => OnTotalsAmountChanged();
    partial void OnTaxIsFixedChanged(bool value) => OnTotalsModeChanged();
    partial void OnDiscountIsPercentChanged(bool value) => OnTotalsModeChanged();
    partial void OnCustomFeeIsPercentChanged(bool value) => OnTotalsModeChanged();

    /// <summary>
    /// Applies a single edit made directly on the paper back into the editor state. Numbers parse
    /// leniently, because the paper shows formatted, currency-prefixed values.
    /// </summary>
    public void ApplyPaperEdit(string field, int? index, string value)
    {
        switch (field)
        {
            case "notes":
                // The paper falls back to the template's footer when the document has no notes of
                // its own, so a commit handing that same text back is the fallback, not typing.
                // Taking it would make an untouched document look edited and stop it following
                // the template.
                if (value != (SelectedTemplate?.FooterText ?? string.Empty))
                    ModalNotes = value;
                break;
            case "description":
                if (index is int di && di >= 0 && di < LineItems.Count)
                    LineItems[di].Description = value;
                break;
            case "quantity":
                if (index is int qi && qi >= 0 && qi < LineItems.Count && TryParsePaperNumber(value, out var q))
                    LineItems[qi].Quantity = q;
                break;
            case "rate":
                if (index is int ri && ri >= 0 && ri < LineItems.Count && TryParsePaperNumber(value, out var r))
                    LineItems[ri].UnitPrice = r;
                break;
            // An empty box means the placeholder is showing, i.e. zero (not "leave unchanged").
            case "taxValue":
                if (string.IsNullOrWhiteSpace(value)) TaxRate = 0;
                else if (TryParsePaperNumber(value, out var tax)) TaxRate = tax;
                break;
            case "shippingValue":
                if (string.IsNullOrWhiteSpace(value)) ShippingAmount = 0;
                else if (TryParsePaperNumber(value, out var ship)) ShippingAmount = ship;
                break;
            case "discountValue":
                if (string.IsNullOrWhiteSpace(value)) DiscountAmount = 0;
                else if (TryParsePaperNumber(value, out var disc)) DiscountAmount = disc;
                break;
            case "feeValue":
                if (string.IsNullOrWhiteSpace(value)) CustomFeeAmount = 0;
                else if (TryParsePaperNumber(value, out var fee)) CustomFeeAmount = fee;
                break;
        }
    }

    /// <summary>Toggles a totals field between percent and fixed from the paper's swap button.</summary>
    public void ToggleTotalsMode(string which)
    {
        switch (which)
        {
            case "tax": TaxIsFixed = !TaxIsFixed; break;
            case "discount": DiscountIsPercent = !DiscountIsPercent; break;
            case "fee": CustomFeeIsPercent = !CustomFeeIsPercent; break;
        }
    }

    // Pulls a number out of a value that may carry a currency symbol / thousands separators.
    private static bool TryParsePaperNumber(string raw, out decimal result)
    {
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    /// <summary>The product options, as JSON, for the line picker built inside the paper.</summary>
    public string ProductsJson =>
        System.Text.Json.JsonSerializer.Serialize(
            ProductOptions.Select(p => new { id = p.Id, name = p.Name, price = p.UnitPrice }));

    /// <summary>The customer options, as JSON, for the Bill To picker built inside the paper.</summary>
    public string CustomersJson =>
        System.Text.Json.JsonSerializer.Serialize(CustomerOptions.Select(c => new { id = c.Id, name = c.Name }));

    /// <summary>
    /// Fills a line from the paper's product picker. Setting SelectedProduct fills the description
    /// and price through the line's own handler; then the paper re-renders.
    /// </summary>
    public void SelectProductForLine(int index, string productId)
    {
        if (index < 0 || index >= LineItems.Count) return;
        var product = ProductOptions.FirstOrDefault(p => p.Id == productId);
        if (product == null) return;
        LineItems[index].SelectedProduct = product;
        RegeneratePaper();
    }

    /// <summary>Opens the create-product modal from the paper picker's "create new".</summary>
    public void CreateProductForLine(int index)
    {
        if (index < 0 || index >= LineItems.Count) return;
        OpenCreateProduct(LineItems[index]);
    }

    /// <summary>Adds a blank line from the paper's "+ Add line item" and re-renders.</summary>
    public void AddLineFromPaper()
    {
        AddBlankLine();
        RegeneratePaper();
    }

    /// <summary>Removes a line from the paper's "x" and re-renders (keeps at least one).</summary>
    public void RemoveLineFromPaper(int index)
    {
        if (index < 0 || index >= LineItems.Count || LineItems.Count <= 1) return;
        RemoveLineAt(index);
        RegeneratePaper();
    }

    /// <summary>Selects a customer from the paper's Bill To picker; re-renders via the change handler.</summary>
    public void SelectCustomerFromPaper(string customerId)
    {
        var customer = CustomerOptions.FirstOrDefault(c => c.Id == customerId);
        if (customer != null) SelectedCustomer = customer;
    }

    /// <summary>Opens the create-customer modal from the paper's Bill To picker.</summary>
    public void CreateCustomerFromPaper() => OpenCreateCustomer();

    /// <summary>Sets a date from the paper's date editor (yyyy-MM-dd); re-renders via the change handler.</summary>
    public void SetDateFromPaper(string field, string iso)
    {
        if (!DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return;
        ApplyPaperDate(field, new DateTimeOffset(date));
    }

    // The logo the user set on the paper this session. null = untouched (leave each template's own
    // logo alone); "" = explicitly removed; otherwise the raw base64 to carry across template switches.
    protected string? PaperLogo { get; set; }

    /// <summary>Sets the template's logo (raw base64) from the paper's logo click, re-rendering.</summary>
    public void SetLogoFromPaper(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return;
        PaperLogo = base64;
        ApplyPaperLogo();
        RegeneratePaper();
    }

    /// <summary>Removes the logo when the user clicks the hover "x" on the paper, re-rendering.</summary>
    public void DeleteLogoFromPaper()
    {
        PaperLogo = string.Empty;
        ApplyPaperLogo();
        RegeneratePaper();
    }

    // The logo is a single company-wide choice shared by invoices and quotes: apply it to every
    // template (and persist) so it shows on all of them and survives closing the editor.
    private void ApplyPaperLogo()
    {
        if (PaperLogo == null) return;
        var remove = PaperLogo.Length == 0;
        // TemplateOptions holds the company's actual templates, so mutating them here updates the
        // persisted objects directly.
        var companyData = App.CompanyManager?.CompanyData;
        foreach (var template in TemplateOptions)
        {
            // Anything already sent under the outgoing logo keeps it.
            if (companyData != null)
                LogoHistory.RetireLogo(companyData, template, remove ? null : PaperLogo);

            if (remove)
            {
                template.LogoBase64 = null;
                template.ShowLogo = false;
            }
            else
            {
                template.LogoBase64 = PaperLogo;
                template.LogoWidth = 150;
                template.ShowLogo = true;
            }
        }
        // Mark dirty so the logo is saved even if the user cancels this document (it's a company setting).
        App.CompanyManager?.MarkAsChanged();
    }

    protected void LoadCustomerOptions() =>
        OptionLoader.Fill(CustomerOptions,
            OptionLoader.Customers(App.CompanyManager?.CompanyData).AsOptions<CustomerOption>());

    // Revenue products only: both documents offer something to a customer, not something bought.
    protected void LoadProductOptions()
    {
        ProductOptions.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Products == null) return;

        foreach (var product in companyData.Products
                     .Where(p => p.Type == CategoryType.Revenue)
                     .OrderBy(p => p.Name))
        {
            ProductOptions.Add(new ProductOption
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                UnitPrice = product.UnitPrice
            });
        }
    }

    // One-shot handlers for the "create entity from this modal" flows. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _customerSavedHandler;
    private EventHandler? _productSavedHandler;

    // Hide the paper while a modal is open on top of it; restore when it closes (its open flag flips
    // back to false, on save or cancel). Only the named open-flag property is watched: OpenAddModal
    // resets other fields first (firing PropertyChanged while the flag is still false), and reacting
    // to those would clear this before the modal is even shown.
    protected void HideWebViewWhileModalOpen(System.ComponentModel.INotifyPropertyChanged modalVm, string openFlagName, Func<bool> isOpen)
    {
        IsNestedModalOpen = true;
        System.ComponentModel.PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == openFlagName && !isOpen())
            {
                IsNestedModalOpen = false;
                modalVm.PropertyChanged -= handler;
            }
        };
        modalVm.PropertyChanged += handler;
    }

    /// <summary>Opens the create customer modal on top of the editor.</summary>
    [RelayCommand]
    private void OpenCreateCustomer()
    {
        var customerModals = App.CustomerModalsViewModel;
        if (customerModals == null) return;

        CreateModalSubscription.RearmOnce(ref _customerSavedHandler,
            h => customerModals.CustomerSaved += h,
            h => customerModals.CustomerSaved -= h,
            () =>
            {
                LoadCustomerOptions();
                OnPropertyChanged(nameof(CustomersJson));

                // Auto-select the customer the user just created.
                var newCustomer = CustomerOptions.FirstOrDefault(c => c.Id == customerModals.LastSavedCustomerId);
                if (newCustomer != null)
                    SelectedCustomer = newCustomer;
            });
        HideWebViewWhileModalOpen(customerModals, nameof(customerModals.IsAddModalOpen), () => customerModals.IsAddModalOpen);
        customerModals.OpenAddModal();
    }

    /// <summary>Opens the create product modal on top of the editor.</summary>
    private void OpenCreateProduct(TLine? line)
    {
        var productModals = App.ProductModalsViewModel;
        if (productModals == null) return;

        CreateModalSubscription.RearmOnce(ref _productSavedHandler,
            h => productModals.ProductSaved += h,
            h => productModals.ProductSaved -= h,
            () =>
            {
                LoadProductOptions();
                OnPropertyChanged(nameof(ProductsJson));

                // Auto-select the new product into the line whose picker launched the create.
                if (line != null)
                {
                    var newProduct = ProductOptions.FirstOrDefault(p => p.Id == productModals.LastSavedProductId);
                    if (newProduct != null)
                        line.SelectedProduct = newProduct;
                }
                RegeneratePaper();
            });
        HideWebViewWhileModalOpen(productModals, nameof(productModals.IsAddModalOpen), () => productModals.IsAddModalOpen);
        productModals.OpenAddModal();
    }
}
