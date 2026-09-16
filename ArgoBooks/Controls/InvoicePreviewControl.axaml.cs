using System.Windows.Input;
using ArgoBooks.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;

namespace ArgoBooks.Controls;

/// <summary>
/// Cross-platform invoice preview control.
/// On Windows/macOS: Uses NativeWebView for high-quality HTML rendering with zoom/pan.
/// On Linux: Shows a fallback with option to view in browser (NativeWebView doesn't embed inline on Linux).
/// </summary>
public partial class InvoicePreviewControl : UserControl
{
    /// <summary>
    /// Defines the Html property for binding HTML content.
    /// </summary>
    public static readonly StyledProperty<string?> HtmlProperty =
        AvaloniaProperty.Register<InvoicePreviewControl, string?>(nameof(Html));

    /// <summary>
    /// Defines the OpenInBrowserCommand property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> OpenInBrowserCommandProperty =
        AvaloniaProperty.Register<InvoicePreviewControl, ICommand?>(nameof(OpenInBrowserCommand));

    /// <summary>
    /// Gets or sets the HTML content to display.
    /// </summary>
    public string? Html
    {
        get => GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to execute when "Open in Browser" is clicked.
    /// </summary>
    public ICommand? OpenInBrowserCommand
    {
        get => GetValue(OpenInBrowserCommandProperty);
        set => SetValue(OpenInBrowserCommandProperty, value);
    }

    /// <summary>
    /// When true, the invoice's [data-field] elements become directly editable on the page and each
    /// edit is raised via <see cref="InvoiceEdited"/>. Off for the customer-facing / view-only render.
    /// </summary>
    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<InvoicePreviewControl, bool>(nameof(IsEditable));

    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>Raised when the user edits a [data-field] element directly on the invoice.</summary>
    public event EventHandler<InvoiceEditEventArgs>? InvoiceEdited;

    /// <summary>
    /// JSON array of products [{id,name,price}] used to build the line-item product dropdown on the
    /// paper (only used in editable mode). Bound from the view-model.
    /// </summary>
    public static readonly StyledProperty<string?> ProductsJsonProperty =
        AvaloniaProperty.Register<InvoicePreviewControl, string?>(nameof(ProductsJson));

    public string? ProductsJson
    {
        get => GetValue(ProductsJsonProperty);
        set => SetValue(ProductsJsonProperty, value);
    }

    /// <summary>JSON array of customers [{id,name}] for the Bill To dropdown on the paper.</summary>
    public static readonly StyledProperty<string?> CustomersJsonProperty =
        AvaloniaProperty.Register<InvoicePreviewControl, string?>(nameof(CustomersJson));

    public string? CustomersJson
    {
        get => GetValue(CustomersJsonProperty);
        set => SetValue(CustomersJsonProperty, value);
    }

    /// <summary>
    /// JSON config the paper's live totals recompute needs (currency symbol/code, security deposit,
    /// whether the payment portal is configured for the processing-fee row). Lets typing update the
    /// amounts in the browser without a C# round-trip.
    /// </summary>
    public static readonly StyledProperty<string?> TotalsConfigJsonProperty =
        AvaloniaProperty.Register<InvoicePreviewControl, string?>(nameof(TotalsConfigJson));

    public string? TotalsConfigJson
    {
        get => GetValue(TotalsConfigJsonProperty);
        set => SetValue(TotalsConfigJsonProperty, value);
    }

    /// <summary>Raised when a product is chosen from a line item's dropdown on the paper.</summary>
    public event EventHandler<ProductPickEventArgs>? ProductPicked;

    /// <summary>Raised when "create new product" is chosen from a line item's dropdown (line index).</summary>
    public event EventHandler<int>? CreateProductRequested;

    /// <summary>Raised when "+ Add line item" is clicked on the paper.</summary>
    public event EventHandler? AddLineRequested;

    /// <summary>Raised when a line item's remove "x" is clicked on the paper (line index).</summary>
    public event EventHandler<int>? RemoveLineRequested;

    /// <summary>Raised when a customer is chosen from the Bill To dropdown on the paper (customer id).</summary>
    public event EventHandler<string>? CustomerPicked;

    /// <summary>Raised when "create new customer" is chosen from the Bill To dropdown.</summary>
    public event EventHandler? CreateCustomerRequested;

    /// <summary>Raised when an issue/due date is edited on the paper (field name and yyyy-MM-dd value).</summary>
    public event EventHandler<(string Field, string Value)>? DateEdited;

    /// <summary>Raised when the logo on the paper is clicked to change it.</summary>
    public event EventHandler? PickLogoRequested;

    /// <summary>Raised when the logo's hover "x" is clicked to remove it.</summary>
    public event EventHandler? DeleteLogoRequested;

    /// <summary>Raised when a totals swap button toggles a field between percent and fixed. Arg is the field key.</summary>
    public event EventHandler<string>? TotalsModeToggled;

    private NativeWebView? _webView;
    private Panel? _rootPanel;
    private Border? _fallbackPanel;
    private Border? _zoomToolbar;
    private TextBlock? _zoomPercentageText;
    private bool _isInitialized;
    private bool _webViewReady;
    private double _currentZoom = 1.0;
    // Set once the user zooms by hand, so re-renders keep their zoom; until then every render fits the
    // paper. Opening an invoice re-renders several times as the form fills in, faster than the page
    // reports its fitted zoom back, so restoring a remembered number opened it at 100%.
    private bool _userZoomed;
    private double _pendingScrollX;
    private double _pendingScrollY;
    private bool _hasPendingScroll;

    private const double ZoomStep = 0.1;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;

    // Injected only in editable mode: makes [data-field] elements contenteditable, posts edits back
    // via the zoom feature's postMessage channel, and builds a product dropdown on description fields
    // that mirrors the app's SearchableDropdown (filter, keyboard nav, create-new, empty state).
    private string BuildEditingScript()
    {
        var products = string.IsNullOrWhiteSpace(ProductsJson) ? "[]" : ProductsJson;
        var customers = string.IsNullOrWhiteSpace(CustomersJson) ? "[]" : CustomersJson;
        var totalsConfig = string.IsNullOrWhiteSpace(TotalsConfigJson) ? "{}" : TotalsConfigJson;
        return EditingScriptTemplate
            .Replace("__PRODUCTS_JSON__", products)
            .Replace("__CUSTOMERS_JSON__", customers)
            .Replace("__TOTALS_CONFIG__", totalsConfig);
    }

    private const string EditingScriptTemplate = @"
<script>
window.__invProducts = __PRODUCTS_JSON__;
window.__invCustomers = __CUSTOMERS_JSON__;
window.__totalsConfig = __TOTALS_CONFIG__;
(function() {
    if (window.__editHandlersInstalled) return;
    window.__editHandlersInstalled = true;

    var style = document.createElement('style');
    style.textContent =
        '[data-field]{display:inline-block;outline:2px dashed rgba(47,107,255,0.6);outline-offset:3px;border-radius:4px;padding:3px 7px;cursor:text;background:rgba(47,107,255,0.05)}' +
        '[data-field]:empty{min-width:60px;min-height:1.15em}' +
        '[data-field]:hover{background:rgba(47,107,255,0.12)}' +
        '[data-field]:focus{outline:2px solid rgba(47,107,255,0.95);background:rgba(47,107,255,0.14)}' +
        '#__prodDrop{position:fixed;z-index:99999;background:#fff;border:1px solid #d0d5dd;border-radius:8px;box-shadow:0 6px 20px rgba(0,0,0,0.15);max-height:280px;overflow-y:auto;min-width:240px;font-family:inherit;font-size:13px;color:#1a1f2b}' +
        '#__prodDrop .it{padding:9px 12px;cursor:pointer}' +
        '#__prodDrop .it:hover,#__prodDrop .it.hl{background:#eff4ff}' +
        '#__prodDrop .empty{padding:12px;color:#9ca3af}' +
        '#__prodDrop .add{padding:10px 12px;color:#2f6bff;font-weight:600;cursor:pointer;border-top:1px solid #eef1f5}' +
        '#__prodDrop .add:hover{background:#f5f8ff}' +
        // A zero tax/shipping/discount/fee value shows as a faded placeholder instead of a literal 0.
        '[data-total-input]:empty:before{content:attr(data-ph);color:#b0b7c3;pointer-events:none}';
    document.head.appendChild(style);

    function post(obj) {
        window.__argoPost(JSON.stringify(obj));
    }

    // Restrict a numeric field to digits and a single decimal point, blocking the keystroke or paste
    // outright so junk never lands in a price box in the first place.
    function attachNumericFilter(el) {
        el.addEventListener('beforeinput', function(e) {
            if (!e.inputType || e.inputType.indexOf('insert') !== 0) return; // deletions/formatting: allow
            var text = e.data;
            if (text == null && e.dataTransfer) text = e.dataTransfer.getData('text'); // paste/drop
            if (text == null || text === '') return; // composition/unknown insert: leave to the backstop
            if (/[^0-9.]/.test(text)) { e.preventDefault(); return; } // anything but a digit or a point
            if (text.indexOf('.') !== -1) {
                // Allow a point only if the field would still have at most one (accounting for a replaced selection).
                var selText = (window.getSelection && window.getSelection().toString()) || '';
                var curDots = ((el.textContent || '').match(/[.]/g) || []).length;
                var selDots = (selText.match(/[.]/g) || []).length;
                var addDots = (text.match(/[.]/g) || []).length;
                if ((curDots - selDots) + addDots > 1) e.preventDefault();
            }
        });
    }

    // qty, rate and notes are free-text contenteditable. customer and description are strict pickers
    // (below): typed text only searches the dropdown and never commits as free text, so a line item can
    // only hold a real product picked from the list (or one made via '+ Create new product'). Dates are
    // pickers too. This keeps an invalid product from sticking, mirroring the customer field.
    var pickers = { customer: 1, description: 1, issueDate: 1, dueDate: 1 };
    var timers = {};
    document.querySelectorAll('[data-field]').forEach(function(el) {
        if (pickers[el.dataset.field]) return;
        el.setAttribute('contenteditable', 'true');
        var single = el.dataset.field !== 'notes' && el.dataset.field !== 'description';
        el.addEventListener('input', function() {
            var field = el.dataset.field;
            var index = (el.dataset.lineIndex != null) ? parseInt(el.dataset.lineIndex, 10) : null;
            var key = field + ':' + index;
            clearTimeout(timers[key]);
            timers[key] = setTimeout(function() { post({ type:'invoiceEdit', field:field, index:index, value: el.textContent }); }, 150);
        });
        if (single) {
            el.addEventListener('keydown', function(e) { if (e.key === 'Enter') { e.preventDefault(); el.blur(); } });
        }
        if (el.dataset.field === 'quantity' || el.dataset.field === 'rate') attachNumericFilter(el);
    });

    // ---- shared entity dropdown: products (per line) and the customer on Bill To ----
    var drop = null, target = null, hl = -1, filtered = [], mode = null;
    function closeDrop() { if (drop) { drop.remove(); drop = null; } target = null; hl = -1; filtered = []; mode = null; }
    function positionDrop() {
        if (!drop || !target) return;
        var r = target.getBoundingClientRect();
        drop.style.left = r.left + 'px';
        drop.style.top = (r.bottom + 4) + 'px';
        drop.style.minWidth = Math.max(240, r.width) + 'px';
    }
    function sourceItems() { return mode === 'customer' ? (window.__invCustomers || []) : (window.__invProducts || []); }
    function pick(p) {
        if (!target) return;
        target.textContent = p.name;
        if (mode === 'customer') { closeDrop(); post({ type:'customerSelected', id: p.id }); }
        else { var idx = parseInt(target.dataset.lineIndex, 10); closeDrop(); post({ type:'productSelected', index: idx, id: p.id }); }
    }
    function render() {
        if (!drop) return;
        drop.innerHTML = '';
        if (filtered.length === 0) {
            var em = document.createElement('div'); em.className = 'empty';
            em.textContent = mode === 'customer' ? 'No customers found.' : 'No products found.';
            drop.appendChild(em);
        } else {
            filtered.forEach(function(p, i) {
                var it = document.createElement('div');
                it.className = 'it' + (i === hl ? ' hl' : '');
                it.textContent = p.name;
                it.addEventListener('mousedown', function(e) { e.preventDefault(); pick(p); });
                drop.appendChild(it);
            });
        }
        var add = document.createElement('div'); add.className = 'add';
        add.textContent = mode === 'customer' ? '+ Create new customer' : '+ Create new product';
        add.addEventListener('mousedown', function(e) {
            e.preventDefault();
            if (mode === 'customer') { closeDrop(); post({ type:'createCustomer' }); }
            else { var idx = target ? parseInt(target.dataset.lineIndex, 10) : -1; closeDrop(); post({ type:'createProduct', index: idx }); }
        });
        drop.appendChild(add);
    }
    function filter(text) {
        var t = (text || '').toLowerCase();
        filtered = sourceItems().filter(function(p) { return (p.name || '').toLowerCase().indexOf(t) !== -1; });
        // Both customer and product (description) are strict pickers, so highlight the top match for
        // quick Enter-to-select. Arrowing up/down still moves the highlight.
        hl = filtered.length ? 0 : -1;
    }
    function openFor(el, m) {
        target = el; mode = m;
        if (!drop) { drop = document.createElement('div'); drop.id = '__prodDrop'; document.body.appendChild(drop); }
        filter(el.textContent); render(); positionDrop();
    }
    function wirePicker(el, m) {
        el.setAttribute('contenteditable', 'true');
        el.addEventListener('focus', function() { openFor(el, m); });
        el.addEventListener('click', function() { openFor(el, m); });
        el.addEventListener('input', function() { if (target !== el) openFor(el, m); else { filter(el.textContent); render(); positionDrop(); } });
        el.addEventListener('blur', function() { setTimeout(closeDrop, 150); });
        el.addEventListener('keydown', function(e) {
            if (!drop) return;
            if (e.key === 'ArrowDown') { e.preventDefault(); hl = Math.min(hl + 1, filtered.length - 1); render(); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); hl = Math.max(hl - 1, 0); render(); }
            else if (e.key === 'Enter') { e.preventDefault(); if (hl >= 0 && filtered[hl]) pick(filtered[hl]); else el.blur(); }
            else if (e.key === 'Escape') { closeDrop(); }
        });
    }
    document.querySelectorAll(""[data-field='description']"").forEach(function(el) { wirePicker(el, 'product'); });
    document.querySelectorAll(""[data-field='customer']"").forEach(function(el) { wirePicker(el, 'customer'); });
    window.addEventListener('scroll', positionDrop, true);

    // ---- date editors (issue / due): click opens a native date input ----
    document.querySelectorAll(""[data-field='issueDate'],[data-field='dueDate']"").forEach(function(el) {
        el.style.cursor = 'pointer';
        el.addEventListener('click', function() {
            var prev = document.getElementById('__dateInput'); if (prev) prev.remove();
            var inp = document.createElement('input'); inp.type = 'date'; inp.id = '__dateInput';
            inp.value = el.dataset.iso || '';
            var r = el.getBoundingClientRect();
            inp.style.cssText = 'position:fixed;z-index:99999;left:' + r.left + 'px;top:' + r.top + 'px;font-size:13px;padding:2px 4px';
            document.body.appendChild(inp);
            inp.focus(); if (inp.showPicker) { try { inp.showPicker(); } catch(e) {} }
            var field = el.dataset.field;
            inp.addEventListener('change', function() { post({ type:'dateEdit', field: field, value: inp.value }); if (inp.parentNode) inp.remove(); });
            inp.addEventListener('blur', function() { setTimeout(function() { if (inp.parentNode) inp.remove(); }, 200); });
        });
    });

    // ---- '+ Add line item' affordance, injected after the line-items table ----
    var firstRow = document.querySelector('[data-line-index]');
    var itemsTable = firstRow ? firstRow.closest('table') : null;
    if (itemsTable && itemsTable.parentNode) {
        var addWrap = document.createElement('div');
        addWrap.style.cssText = 'padding:8px 0';
        var addLine = document.createElement('span');
        addLine.textContent = '+ Add line item';
        addLine.style.cssText = 'display:inline-block;color:#2f6bff;cursor:pointer;padding:4px 2px;font-weight:600;font-family:inherit;font-size:13px';
        addLine.addEventListener('click', function() { post({ type:'addLine' }); });
        addWrap.appendChild(addLine);
        itemsTable.parentNode.insertBefore(addWrap, itemsTable.nextSibling);
    }

    // ---- remove-row 'x' per line item, only when there is more than one row ----
    var rows = document.querySelectorAll(""[data-field='description']"");
    if (rows.length > 1) {
        rows.forEach(function(sp) {
            var tr = sp.closest('tr');
            if (!tr) return;
            var lastCell = tr.lastElementChild;
            if (!lastCell) return;
            lastCell.style.position = 'relative';
            var x = document.createElement('span');
            x.textContent = '×';
            x.title = 'Remove line';
            x.style.cssText = 'position:absolute;right:-22px;top:50%;transform:translateY(-50%);cursor:pointer;color:#c4ccd6;font-size:18px;line-height:1;padding:2px 4px';
            x.addEventListener('click', function() { post({ type:'removeLine', index: parseInt(sp.dataset.lineIndex, 10) }); });
            lastCell.appendChild(x);
        });
    }

    // ---- logo: click to change; hover shows an 'x' to delete; a square prompt when there is none ----
    var logoEl = document.querySelector('[data-logo]');
    if (logoEl) {
        logoEl.style.cursor = 'pointer'; logoEl.title = 'Click to change logo';
        logoEl.addEventListener('click', function() { post({ type:'pickLogo' }); });
        // Wrap the logo so a delete 'x' can be positioned over its top-right corner. The wrapper
        // carries the spacing margin (not the image) so the 'x' hugs the image corner, not the gap.
        var wrap = document.createElement('span');
        wrap.style.cssText = 'position:relative;display:inline-block;vertical-align:middle;line-height:0;margin-right:14px;';
        logoEl.parentNode.insertBefore(wrap, logoEl);
        wrap.appendChild(logoEl);
        logoEl.style.margin = '0';
        logoEl.style.display = 'block';
        var del = document.createElement('div'); del.textContent = '×'; del.title = 'Remove logo';
        del.style.cssText = 'position:absolute;top:-7px;right:-7px;width:17px;height:17px;border-radius:50%;background:#e5484d;color:#fff;font-size:12px;line-height:17px;text-align:center;cursor:pointer;font-family:sans-serif;display:none;box-shadow:0 1px 3px rgba(0,0,0,0.3);';
        wrap.appendChild(del);
        wrap.addEventListener('mouseenter', function() { del.style.display = 'block'; });
        wrap.addEventListener('mouseleave', function() { del.style.display = 'none'; });
        del.addEventListener('click', function(e) { e.stopPropagation(); post({ type:'deleteLogo' }); });
    } else {
        // Show a clickable square where the logo would sit, next to the company name slot.
        var square = document.createElement('div'); square.textContent = '+ Logo';
        square.title = 'Click to add a logo';
        square.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;vertical-align:middle;width:72px;height:72px;border:2px dashed #b6bfca;border-radius:8px;color:#5b6472;font-weight:600;font-size:12px;cursor:pointer;font-family:sans-serif;margin-right:14px;background:#ffffff;';
        square.addEventListener('click', function() { post({ type:'pickLogo' }); });
        var slot = document.querySelector('[data-logo-slot]');
        if (slot) { slot.parentNode.insertBefore(square, slot); }
        else {
            square.style.position = 'fixed'; square.style.left = '14px'; square.style.top = '14px'; square.style.zIndex = '99998';
            document.body.appendChild(square);
        }
    }

    // ---- editable totals: tax (%/fixed), shipping ($), discount (%/fixed), custom fee (%/fixed) ----
    // Each marked cell shows the computed amount in the customer view; here we replace it with an
    // inline value editor plus a swap button, mirroring the website's totals controls.
    document.querySelectorAll('[data-total]').forEach(function(cell) {
        var which = cell.dataset.total;              // tax | shipping | discount | fee
        var mode = cell.dataset.totalMode || '';     // percent | fixed | '' (shipping has no mode)
        var sym = cell.dataset.totalSymbol || '$';
        var raw = cell.dataset.totalRaw || '0';
        var fieldName = which + 'Value';
        var isPercent = mode === 'percent';

        cell.textContent = '';

        // One bordered rounded box holding: optional currency prefix, the number, a %/currency
        // affix, and (for togglable fields) a swap button, matching the website's totals inputs.
        var box = document.createElement('span');
        // Fixed width so tax, shipping and discount boxes line up the same, with or without a swap button.
        box.style.cssText = 'display:inline-flex;align-items:stretch;height:30px;width:160px;border:1px solid #d0d5dd;border-radius:6px;background:#fff;overflow:hidden;font-size:13px;font-family:inherit;vertical-align:middle';

        if (!isPercent) {
            var pre = document.createElement('span');
            pre.textContent = sym;
            pre.style.cssText = 'display:flex;align-items:center;padding:0 8px;color:#8a94a3';
            box.appendChild(pre);
        }

        var val = document.createElement('span');
        val.setAttribute('contenteditable', 'true');
        val.setAttribute('data-total-input', fieldName);
        // A zero value renders empty so the '0' shows as a faded placeholder (see CSS) instead of real text.
        val.setAttribute('data-ph', '0');
        var isZeroVal = !raw || parseFloat(raw) === 0;
        val.textContent = isZeroVal ? '' : raw;
        // Vertically center via line-height (matches the 30px box) rather than display:flex - an empty
        // contenteditable flex box renders the caret at the top instead of centered.
        val.style.cssText = 'line-height:30px;text-align:right;padding:0 6px;min-width:44px;flex:1 1 auto;outline:none;white-space:nowrap;overflow:hidden';
        attachNumericFilter(val);
        box.appendChild(val);

        if (isPercent) {
            var suf = document.createElement('span');
            suf.textContent = '%';
            suf.style.cssText = 'display:flex;align-items:center;padding:0 8px;color:#8a94a3';
            box.appendChild(suf);
        }

        box.addEventListener('focusin', function() { box.style.borderColor = '#2f6bff'; });
        box.addEventListener('focusout', function() { box.style.borderColor = '#d0d5dd'; });

        var t;
        val.addEventListener('input', function() {
            // Keep the element truly empty when cleared so the :empty placeholder shows (browsers can
            // leave a stray <br> behind after deleting the last character).
            if (!val.textContent.trim()) val.innerHTML = '';
            clearTimeout(t);
            t = setTimeout(function() { post({ type:'invoiceEdit', field: fieldName, index: null, value: val.textContent }); }, 150);
        });
        val.addEventListener('keydown', function(e) { if (e.key === 'Enter') { e.preventDefault(); val.blur(); } });
        val.addEventListener('blur', function() {
            // Flush the value to the model. The displayed Total updates live in JS (see the recompute
            // block below), so there is no paper re-render here, which is what kept the preview from
            // resetting its zoom while the user typed.
            clearTimeout(t);
            post({ type:'invoiceEdit', field: fieldName, index: null, value: val.textContent });
        });

        if (mode) {
            var swap = document.createElement('button');
            swap.type = 'button';
            swap.innerHTML = '&#x21c4;';
            swap.title = 'Switch between percent and fixed amount';
            swap.style.cssText = 'border:none;border-left:1px solid #d0d5dd;background:transparent;cursor:pointer;color:#5b6472;font-size:13px;line-height:1;padding:0 8px';
            // mousedown + preventDefault so the value field doesn't blur (which would double-commit).
            swap.addEventListener('mousedown', function(e) { e.preventDefault(); post({ type:'totalsToggle', which: which }); });
            box.appendChild(swap);
        }
        cell.appendChild(box);
    });

    // ---- live totals: recompute amounts/subtotal/total/fee in the browser as the user types, so
    // ---- the paper never has to re-render (which would reset the zoom). Mirrors the C# math; the
    // ---- authoritative values are recomputed in C# on preview/save.
    (function() {
        var cfg = window.__totalsConfig || {};
        var sym = cfg.symbol || '$';
        var code = cfg.code || '';
        var deposit = Number(cfg.deposit) || 0;
        var portal = !!cfg.portal;
        var passFee = cfg.passFee !== false;
        var paid = Number(cfg.paid) || 0;
        // The currency's decimals, not a fixed two: yen has no subunit.
        var dec = (typeof cfg.decimals === 'number') ? cfg.decimals : 2;

        function num(el) { return el ? (parseFloat((el.textContent || '').replace(/[^0-9.\-]/g, '')) || 0) : 0; }
        function money(a) { return sym + a.toLocaleString('en-US', { minimumFractionDigits: dec, maximumFractionDigits: dec }); }
        function due(a) { return money(a) + (code ? ' ' + code : ''); }
        function setOut(name, text) {
            document.querySelectorAll('[data-out=""' + name + '""]').forEach(function(o) { o.textContent = text; });
        }
        function totalVal(which) {
            var el = document.querySelector('[data-total=""' + which + '""] [data-total-input]');
            return el ? (parseFloat((el.textContent || '').replace(/[^0-9.\-]/g, '')) || 0) : 0;
        }
        function totalMode(which) {
            var el = document.querySelector('[data-total=""' + which + '""]');
            return el ? (el.dataset.totalMode || '') : '';
        }

        window.__recomputeTotals = function() {
            var subtotal = 0;
            document.querySelectorAll('[data-field=""rate""]').forEach(function(rateEl) {
                var idx = rateEl.dataset.lineIndex;
                var qtyEl = document.querySelector('[data-field=""quantity""][data-line-index=""' + idx + '""]');
                var amtOut = document.querySelector('[data-out=""lineAmount""][data-line-index=""' + idx + '""]');
                // The line's own discount has no editor on the paper, so it rides along on the amount
                // cell. Floored at zero to match LineItem.Subtotal, which is what gets saved.
                var lineDiscount = amtOut ? (parseFloat(amtOut.dataset.lineDiscount) || 0) : 0;
                var amt = Math.round(Math.max(0, num(qtyEl) * num(rateEl) - lineDiscount) * 100) / 100;
                subtotal += amt;
                if (amtOut) amtOut.textContent = money(amt);
            });

            var discount = totalVal('discount'), discMode = totalMode('discount');
            var fee = totalVal('fee'), feeMode = totalMode('fee');
            var shipping = totalVal('shipping');
            var tax = totalVal('tax'), taxMode = totalMode('tax');

            // Mirrors InvoiceMath: the discount is worth at most the subtotal it comes off, so no
            // figure on the paper can go negative however large a discount is typed.
            var discountCalc = discMode === 'percent' ? subtotal * discount / 100 : discount;
            discountCalc = Math.min(Math.max(0, discountCalc), Math.max(0, subtotal));
            var feeCalc = Math.max(0, feeMode === 'percent' ? subtotal * fee / 100 : fee);
            var taxableBase = Math.max(0, subtotal - discountCalc + feeCalc + shipping);
            var taxAmount = Math.max(0, taxMode === 'fixed' ? tax : taxableBase * tax / 100);
            var total = Math.max(0, taxableBase + taxAmount + deposit);

            // Less whatever has already been paid. Floored at zero so an overpayment does not
            // render a negative.
            var balance = Math.max(0, total - paid);
            var procFee = (portal && passFee && balance > 0) ? Math.round((balance * 2.9 / 100 + 0.30) * 100) / 100 : 0;

            // The (x%) suffix on the tax label is server-rendered, so it has to be
            // rewritten here or it keeps showing the rate the paper loaded with.
            // Trims trailing zeros to match the C# ""0.##"" format.
            setOut('taxRateLabel', taxMode === 'fixed' ? '' : ' (' + String(parseFloat(tax.toFixed(2))) + '%)');
            setOut('subtotal', money(subtotal));
            setOut('processingFee', money(procFee));
            setOut('amountToPay', due(balance + procFee));
        };

        // Rate placeholder: show a greyed hint (e.g. ""$0.00"") instead of a literal zero, so clicking
        // the field leaves an empty box to type into.
        var phStyle = document.createElement('style');
        phStyle.textContent = '[data-field=""rate""][data-ph]:empty::before{content:attr(data-ph);color:#9ca3af;}';
        document.head.appendChild(phStyle);
        document.querySelectorAll('[data-field=""rate""]').forEach(function(el) {
            var raw = (el.textContent || '').trim();
            if ((parseFloat(raw.replace(/[^0-9.\-]/g, '')) || 0) === 0) {
                el.dataset.ph = raw || (sym + '0.00');
                el.textContent = '';
            }
            // Keep the node truly empty when cleared so :empty (and the placeholder) apply.
            el.addEventListener('input', function() { if (!el.textContent.trim()) el.innerHTML = ''; });
        });

        // Any edit to a line or totals field triggers a live recompute.
        document.addEventListener('input', function() { window.__recomputeTotals(); });
        window.__recomputeTotals();
    })();
})();
</script>";

    /// <summary>
    /// Whether the current platform supports inline WebView embedding.
    /// Avalonia 12 NativeWebView supports Windows (WebView2), macOS (WKWebView), and Linux (WebKitGTK).
    /// </summary>
    private static bool PlatformSupportsInlineWebView =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    public InvoicePreviewControl()
    {
        InitializeComponent();

        // The web view is declared in XAML, so it attaches as soon as this control does, and
        // attaching is what creates the WebView2 environment. OnLoaded already runs too late to
        // influence that, so the lookup is duplicated here rather than moved.
        if (this.FindControl<NativeWebView>("WebView") is { } webView)
        {
            WebViewEnvironment.Configure(webView);
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Subscribed on every load, above the initialised check, so it stays paired with the
        // unsubscribe in OnUnloaded. A control that is unloaded and loaded again skips the
        // block below, and would otherwise come back without a subscription.
        WebViewEnvironment.Failed -= OnWebViewFailed;
        WebViewEnvironment.Failed += OnWebViewFailed;

        if (_isInitialized)
            return;

        _rootPanel = this.FindControl<Panel>("RootPanel");
        _fallbackPanel = this.FindControl<Border>("FallbackPanel");
        _zoomToolbar = this.FindControl<Border>("ZoomToolbar");
        _zoomPercentageText = this.FindControl<TextBlock>("ZoomPercentageText");
        _webView = this.FindControl<NativeWebView>("WebView");

        _isInitialized = true;

        // IMPORTANT: Do NOT create/show WebView here!
        // OnLoaded fires before bindings evaluate, so IsVisible may incorrectly be true.
        // We must only activate the WebView when IsVisible explicitly changes to true.
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if ((change.Property == HtmlProperty || change.Property == IsEditableProperty
                || change.Property == ProductsJsonProperty || change.Property == CustomersJsonProperty
                || change.Property == TotalsConfigJsonProperty) && _isInitialized)
        {
            // If this call activates the WebView, ActivateWebView already renders the current content;
            // rendering again here would race that first render and make the initial auto-fit lose to
            // the restore-zoom path (opening zoomed-in instead of fitted).
            if (!EnsureWebViewActiveIfVisible())
                _ = UpdateWebViewContent();
        }

        if (change.Property == IsVisibleProperty && _isInitialized)
        {
            bool wasVisible = change.OldValue is bool oldVal && oldVal;
            bool isNowVisible = change.NewValue is bool newVal && newVal;

            if (isNowVisible && !wasVisible)
            {
                InitializePlatformPreview();
            }
            else if (!isNowVisible && wasVisible)
            {
                DeactivateWebView();
            }
        }
    }

    private void InitializePlatformPreview()
    {
        // A web view that already failed to start this session fails the same way again, and it
        // fails silently from here: the exception never reaches this call. Without the check the
        // preview would show an empty panel, so treat it exactly like a platform that cannot
        // embed one and offer the browser instead.
        if (PlatformSupportsInlineWebView && !WebViewEnvironment.HasFailed)
        {
            ActivateWebView();
        }
        else
        {
            ShowFallback();
        }
    }

    /// <summary>
    /// The web view died after this control activated it, so the preview is showing nothing.
    /// Swap it for the fallback the Linux path already uses.
    /// </summary>
    private void OnWebViewFailed(object? sender, EventArgs e)
    {
        DeactivateWebView();
        ShowFallback();
    }

    /// <summary>Activates the WebView (or fallback) if it should be showing but isn't yet.
    /// Returns true if it activated the inline WebView, which itself renders the current content.</summary>
    private bool EnsureWebViewActiveIfVisible()
    {
        if (PlatformSupportsInlineWebView && _webView != null && !_webView.IsVisible
            && IsEffectivelyVisible && !string.IsNullOrEmpty(Html))
        {
            InitializePlatformPreview();
            return true;
        }
        if (!PlatformSupportsInlineWebView && IsEffectivelyVisible)
        {
            ShowFallback();
        }
        return false;
    }

    private void ActivateWebView()
    {
        if (_webView == null || _webViewReady)
            return;

        _webView.IsVisible = true;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.WebMessageReceived += OnWebMessageReceived;

        if (_zoomToolbar != null)
            _zoomToolbar.IsVisible = true;

        _webViewReady = true;
        _ = UpdateWebViewContent();
    }

    private void DeactivateWebView()
    {
        if (_webView != null)
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.WebMessageReceived -= OnWebMessageReceived;
            _webView.IsVisible = false;
        }

        StopOutboxPolling();

        _webViewReady = false;
        // Next activation should auto-fit fresh rather than restore a stale zoom.
        _userZoomed = false;
        _currentZoom = 1.0;
        // Drop any scroll capture that never completed, so reactivation doesn't restore a stale
        // scroll position from the previous viewing onto the freshly shown invoice.
        _hasPendingScroll = false;

        if (_zoomToolbar != null)
            _zoomToolbar.IsVisible = false;
    }

    private void ShowFallback()
    {
        if (_fallbackPanel != null)
            _fallbackPanel.IsVisible = true;
    }

    /// <summary>
    /// How often the page is asked for anything it queued. Zoom steps and field edits are both
    /// things the user is watching for, so this is short enough to read as immediate while
    /// staying far cheaper than the render it sits beside.
    /// </summary>
    private static readonly TimeSpan OutboxPollInterval = TimeSpan.FromMilliseconds(150);

    private DispatcherTimer? _outboxTimer;
    private bool _draining;

    /// <summary>
    /// Starts polling only where the page had to queue instead of post.
    ///
    /// The page decides that at load and reports it through __argoNative, so a platform whose
    /// bridge works is never polled and no message is delivered twice. See
    /// <see cref="Services.WebViewOutbox"/> for why the bridge is missing on macOS.
    /// </summary>
    private async Task StartOutboxPollingIfNeededAsync()
    {
        if (_outboxTimer != null || _webView == null)
            return;

        bool usesNativeBridge;
        try
        {
            var answer = await _webView.InvokeScript(Services.WebViewOutbox.NativeProbe);
            usesNativeBridge = answer != null && answer.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // The page is not up yet, or the view is already gone. Either way the next
            // activation asks again, and polling a dead view would answer nothing anyway.
            return;
        }

        if (usesNativeBridge || _webView == null)
            return;

        _outboxTimer = new DispatcherTimer { Interval = OutboxPollInterval };
        _outboxTimer.Tick += async (_, _) => await DrainOutboxAsync();
        _outboxTimer.Start();
    }

    private void StopOutboxPolling()
    {
        _outboxTimer?.Stop();
        _outboxTimer = null;
        _draining = false;
    }

    private async Task DrainOutboxAsync()
    {
        // A drain that outruns its own reply would interleave two reads of the same queue and
        // deliver an edit twice, so a tick that arrives mid-drain is skipped instead.
        if (_draining || _webView == null)
            return;

        _draining = true;
        try
        {
            var raw = await _webView.InvokeScript(Services.WebViewOutbox.Drain);
            foreach (var message in Services.WebViewOutbox.Parse(raw))
                DispatchMessage(message);
        }
        catch (Exception)
        {
            // Navigating, reloading or tearing down: nothing to report and nothing to fix.
        }
        finally
        {
            _draining = false;
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        // Only now does the page exist to be asked which channel it chose. Asking in
        // ActivateWebView instead reads __argoNative off a page that has not loaded, which
        // answers "no native bridge" everywhere and would start polling on Windows too,
        // delivering every edit twice: once by postMessage and again from the drain.
        _ = StartOutboxPollingIfNeededAsync();

        if (_hasPendingScroll && _webView != null)
        {
            var sx = _pendingScrollX.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sy = _pendingScrollY.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = _webView.InvokeScript($"window.scrollTo({sx}, {sy})");
            _hasPendingScroll = false;
        }
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e) =>
        DispatchMessage(e.Body);

    /// <summary>
    /// Handles one message from the page, whether it arrived by postMessage or was polled out
    /// of the outbox.
    /// </summary>
    private void DispatchMessage(string? message)
    {
        try
        {
            if (string.IsNullOrEmpty(message))
                return;

            var json = System.Text.Json.JsonDocument.Parse(message);
            var root = json.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
                return;

            var messageType = typeElement.GetString();

            if (messageType == "zoomUpdate" && root.TryGetProperty("zoom", out var zoomElement))
            {
                _currentZoom = zoomElement.GetDouble();
                _userZoomed = !(root.TryGetProperty("fit", out var fitElement) && fitElement.ValueKind == System.Text.Json.JsonValueKind.True);
                Avalonia.Threading.Dispatcher.UIThread.Post(UpdateZoomDisplay);
            }
            else if (messageType == "invoiceEdit")
            {
                var field = root.TryGetProperty("field", out var f) ? f.GetString() : null;
                if (string.IsNullOrEmpty(field))
                    return;
                int? index = null;
                if (root.TryGetProperty("index", out var idx) && idx.ValueKind == System.Text.Json.JsonValueKind.Number)
                    index = idx.GetInt32();
                var value = root.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : string.Empty;
                var args = new InvoiceEditEventArgs(field, index, value);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => InvoiceEdited?.Invoke(this, args));
            }
            else if (messageType == "productSelected")
            {
                if (root.TryGetProperty("index", out var pi) && pi.ValueKind == System.Text.Json.JsonValueKind.Number
                    && root.TryGetProperty("id", out var pid))
                {
                    var lineIndex = pi.GetInt32();
                    var productId = pid.GetString() ?? string.Empty;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ProductPicked?.Invoke(this, new ProductPickEventArgs(lineIndex, productId)));
                }
            }
            else if (messageType == "createProduct")
            {
                if (root.TryGetProperty("index", out var ci) && ci.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    var lineIndex = ci.GetInt32();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => CreateProductRequested?.Invoke(this, lineIndex));
                }
            }
            else if (messageType == "addLine")
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AddLineRequested?.Invoke(this, EventArgs.Empty));
            }
            else if (messageType == "removeLine")
            {
                if (root.TryGetProperty("index", out var ri) && ri.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    var lineIndex = ri.GetInt32();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => RemoveLineRequested?.Invoke(this, lineIndex));
                }
            }
            else if (messageType == "customerSelected")
            {
                if (root.TryGetProperty("id", out var cid))
                {
                    var customerId = cid.GetString() ?? string.Empty;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => CustomerPicked?.Invoke(this, customerId));
                }
            }
            else if (messageType == "createCustomer")
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => CreateCustomerRequested?.Invoke(this, EventArgs.Empty));
            }
            else if (messageType == "dateEdit")
            {
                var dField = root.TryGetProperty("field", out var df) ? df.GetString() ?? string.Empty : string.Empty;
                var dValue = root.TryGetProperty("value", out var dv) ? dv.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrEmpty(dField) && !string.IsNullOrEmpty(dValue))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => DateEdited?.Invoke(this, (dField, dValue)));
            }
            else if (messageType == "pickLogo")
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => PickLogoRequested?.Invoke(this, EventArgs.Empty));
            }
            else if (messageType == "deleteLogo")
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => DeleteLogoRequested?.Invoke(this, EventArgs.Empty));
            }
            else if (messageType == "totalsToggle")
            {
                var which = root.TryGetProperty("which", out var we) ? we.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrEmpty(which))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => TotalsModeToggled?.Invoke(this, which));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"InvoicePreview message error: {ex.Message}");
        }
    }

    private void UpdateZoomDisplay()
    {
        if (_zoomPercentageText != null)
        {
            var percentage = (int)Math.Round(_currentZoom * 100);
            _zoomPercentageText.Text = $"{percentage}%";
        }
    }

    private async Task UpdateWebViewContent()
    {
        if (!_webViewReady || _webView == null || string.IsNullOrEmpty(Html))
            return;

        // Capture current scroll position so NavigationCompleted can restore it
        // after NavigateToString resets the page. Skip re-capture while a prior
        // navigation is still in flight, the live page is mid-reload and would
        // report scroll=0, clobbering the position we're trying to preserve.
        if (!_hasPendingScroll)
        {
            try
            {
                var result = await _webView.InvokeScript("JSON.stringify([window.scrollX||0,window.scrollY||0])");
                if (TryParseScrollResult(result, out var sx, out var sy))
                {
                    _pendingScrollX = sx;
                    _pendingScrollY = sy;
                    _hasPendingScroll = true;
                }
            }
            catch
            {
                // No saved scroll; reload will keep scroll at 0
            }
        }

        // Inject interaction scripts for zoom and pan handling
        var interactionScript = @"
<script>
(function() {
    if (window.__interactionHandlersInstalled) return;
    window.__interactionHandlersInstalled = true;

    var zoomWrapper = document.getElementById('__zoomWrapper');
    if (!zoomWrapper) {
        zoomWrapper = document.createElement('div');
        zoomWrapper.id = '__zoomWrapper';
        zoomWrapper.style.cssText = 'transform-origin: 0 0; min-height: 100%; will-change: transform;';
        zoomWrapper.dataset.scale = '1';
        while (document.body.firstChild) {
            zoomWrapper.appendChild(document.body.firstChild);
        }
        document.body.appendChild(zoomWrapper);
        document.body.style.overflow = 'auto';
    }

    window.__isInFitMode = false;

    // Horizontal centering: when the scaled wrapper is narrower than the
    // viewport, shift it right by half the empty space. Without this,
    // transform-origin: 0 0 leaves zoomed-out content pinned to the
    // viewport's left edge.
    function centerOffsetX(scale) {
        var wrapper = document.getElementById('__zoomWrapper');
        if (!wrapper) return 0;
        var contentWidth = wrapper.scrollWidth || wrapper.offsetWidth;
        var viewportWidth = window.innerWidth;
        var scaledWidth = contentWidth * scale;
        return scaledWidth < viewportWidth ? (viewportWidth - scaledWidth) / 2 : 0;
    }

    // Single source of truth for the wrapper transform. Form is
    // ""translate(tx, ty) scale(s)"", translate is applied in screen
    // pixels (CSS rule: outer transform applies last). tx already
    // accounts for centering; panOffset adds the rubber-band overscroll.
    function applyTransform(scale, panOffsetX, panOffsetY) {
        var wrapper = document.getElementById('__zoomWrapper');
        if (!wrapper) return;
        var tx = centerOffsetX(scale) + (panOffsetX || 0);
        var ty = panOffsetY || 0;
        wrapper.style.transform = 'translate(' + tx + 'px, ' + ty + 'px) scale(' + scale + ')';
    }

    function notifyZoom(scale, fit) {
        window.__argoPost(JSON.stringify({ type: 'zoomUpdate', zoom: scale, fit: fit }));
    }

    function updateZoom(newScale, originX, originY) {
        var wrapper = document.getElementById('__zoomWrapper');
        var oldScale = parseFloat(wrapper.dataset.scale || '1');
        newScale = Math.max(0.25, Math.min(5.0, newScale));
        wrapper.dataset.scale = newScale;
        window.__isInFitMode = false;
        // Show scrollbars only if the SCALED content actually overflows the viewport. transform:scale
        // doesn't shrink the layout box, so a plain overflow:auto shows phantom scrollbars (e.g. at
        // 1:1 when the paper visibly fits). Compare the scaled size to the viewport instead.
        var scaledW = wrapper.scrollWidth * newScale;
        var scaledH = wrapper.scrollHeight * newScale;
        var fits = scaledW <= window.innerWidth + 1 && scaledH <= window.innerHeight + 1;
        document.body.style.overflow = fits ? 'hidden' : 'auto';

        // Calculate scroll adjustment to keep the point under cursor.
        // Assumes centering offset doesn't change much across the zoom
        // step, true once content is wider than viewport, slightly
        // approximate around the fit-to-window boundary.
        var scrollX = window.scrollX || 0;
        var scrollY = window.scrollY || 0;
        var docX = scrollX + (originX / oldScale);
        var docY = scrollY + (originY / oldScale);

        applyTransform(newScale);

        var newScrollX = docX - (originX / newScale);
        var newScrollY = docY - (originY / newScale);
        window.scrollTo(newScrollX, newScrollY);

        notifyZoom(newScale, false);
    }

    // Expose for C# InvokeScript calls
    window.__setZoom = function(newScale) {
        var vw = window.innerWidth / 2;
        var vh = window.innerHeight / 2;
        updateZoom(newScale, vw, vh);
    };

    window.__fitToWindow = function() {
        var wrapper = document.getElementById('__zoomWrapper');
        // Reset to 1 first to measure natural size
        wrapper.style.transform = '';
        wrapper.dataset.scale = '1';
        // Hide scrollbars BEFORE measuring the viewport. transform: scale() doesn't shrink the
        // wrapper's layout box, so the body reports overflow and shows scrollbars even though the
        // scaled content fits; we hide them while in fit mode. Doing it first matters: if we
        // measured with the scrollbar present and hid it afterwards, removing the scrollbar would
        // widen the viewport and fire a resize, which re-fit to the new width and nudged the paper
        // a few px right - the shift the user saw a moment after the paper appeared. Measuring after
        // the hide means the fit (and any resize-triggered re-fit) use the same final viewport.
        document.body.style.overflow = 'hidden';
        var contentWidth = wrapper.scrollWidth;
        var contentHeight = wrapper.scrollHeight;
        var viewportWidth = window.innerWidth;
        var viewportHeight = window.innerHeight;
        if (contentWidth <= 0 || contentHeight <= 0) return;
        var fitScale = Math.min(viewportWidth / contentWidth, viewportHeight / contentHeight);
        fitScale = Math.max(0.25, Math.min(5.0, fitScale));

        wrapper.dataset.scale = fitScale;
        applyTransform(fitScale);
        window.scrollTo(0, 0);
        window.__isInFitMode = true;
        // Remember the viewport we fit to so the resize handler can tell a genuine
        // window/DPI change from the scrollbar-toggle resize our own fit just fired.
        window.__fitVW = viewportWidth;
        window.__fitVH = viewportHeight;
        notifyZoom(fitScale, true);
    };

    window.__getZoom = function() {
        var wrapper = document.getElementById('__zoomWrapper');
        return parseFloat(wrapper.dataset.scale || '1');
    };

    // First load and DPI/window-resize handling. WebView2 fires
    // ""resize"" when the parent moves to a monitor with different DPI
    // (window.innerWidth changes inversely with devicePixelRatio).
    // If the user hasn't manually zoomed, re-fit so the preview lays
    // out cleanly on the new monitor instead of staying ""zoomed in"".
    var resizeTimer = null;
    window.addEventListener('resize', function() {
        if (!window.__isInFitMode) return;
        // Ignore the spurious resize our own fit fires when it toggles the scrollbar (the viewport
        // barely changes). Only re-fit on a real window/monitor/DPI change, otherwise we re-fit in a
        // loop and nudge the paper a few px each pass. A scrollbar is ~17px, so 24px is a safe cutoff.
        if (Math.abs(window.innerWidth - (window.__fitVW || 0)) < 24
            && Math.abs(window.innerHeight - (window.__fitVH || 0)) < 24) return;
        if (resizeTimer) clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function() { window.__fitToWindow(); }, 50);
    });

    // Fit the paper to the window when it shows, unless C# passes the zoom the user chose by hand.
    function maybeInitialFit() {
        if (typeof window.__restoreScale === 'number' && window.__restoreScale > 0) {
            window.__setZoom(window.__restoreScale);
            return;
        }
        if (document.getElementById('__zoomWrapper'))
            window.__fitToWindow();
    }
    // Apply the initial fit synchronously - before the browser's first paint - so the content
    // appears already fitted and centered. This script runs at the end of <body>, so the DOM is
    // parsed and measurable here. Deferring to setTimeout painted the page at natural scale 1
    // (left-aligned) first, then shifted it right to center a frame later: the ""shifts right a
    // second later"" jump the user saw on every show. Fall back to a deferred pass only if the
    // viewport isn't measurable yet (innerWidth 0 during an early navigation).
    function runInitialFit() {
        if (window.innerWidth > 0 && document.getElementById('__zoomWrapper')) {
            maybeInitialFit();
        } else {
            setTimeout(maybeInitialFit, 0);
        }
    }
    if (document.readyState === 'loading') {
        window.addEventListener('DOMContentLoaded', runInitialFit);
    } else {
        runInitialFit();
    }

    // Zoom handling (Ctrl+Scroll, or Cmd+Scroll on a Mac, where Cmd arrives as metaKey)
    document.addEventListener('wheel', function(e) {
        if (e.ctrlKey || e.metaKey) {
            e.preventDefault();
            var wrapper = document.getElementById('__zoomWrapper');
            var currentScale = parseFloat(wrapper.dataset.scale || '1');
            var delta = e.deltaY < 0 ? 0.1 : -0.1;
            updateZoom(currentScale + delta, e.clientX, e.clientY);
        }
    }, { passive: false });

    // Pan handling (Right-click drag) with rubber band effect
    var isPanning = false;
    var startX = 0, startY = 0, startScrollX = 0, startScrollY = 0;
    var overscrollX = 0, overscrollY = 0;
    var resistance = 0.3, maxOverscroll = 80;

    document.addEventListener('mousedown', function(e) {
        if (e.button === 2) {
            isPanning = true;
            startX = e.clientX;
            startY = e.clientY;
            startScrollX = window.scrollX || 0;
            startScrollY = window.scrollY || 0;
            overscrollX = 0;
            overscrollY = 0;
            var zw = document.getElementById('__zoomWrapper');
            if (zw) zw.style.transition = 'none';
            document.body.style.cursor = 'grabbing';
            document.body.style.userSelect = 'none';
            e.preventDefault();
        }
    });

    document.addEventListener('mousemove', function(e) {
        if (!isPanning) return;
        var deltaX = e.clientX - startX;
        var deltaY = e.clientY - startY;
        var maxScrollX = Math.max(0, document.documentElement.scrollWidth - window.innerWidth);
        var maxScrollY = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
        var targetScrollX = startScrollX - deltaX;
        var targetScrollY = startScrollY - deltaY;
        var clampedX = Math.max(0, Math.min(maxScrollX, targetScrollX));
        var clampedY = Math.max(0, Math.min(maxScrollY, targetScrollY));

        if (targetScrollX < 0) overscrollX = Math.min(maxOverscroll, -targetScrollX * resistance);
        else if (targetScrollX > maxScrollX) overscrollX = Math.max(-maxOverscroll, -(targetScrollX - maxScrollX) * resistance);
        else overscrollX = 0;

        if (targetScrollY < 0) overscrollY = Math.min(maxOverscroll, -targetScrollY * resistance);
        else if (targetScrollY > maxScrollY) overscrollY = Math.max(-maxOverscroll, -(targetScrollY - maxScrollY) * resistance);
        else overscrollY = 0;

        window.scrollTo(clampedX, clampedY);
        var zw = document.getElementById('__zoomWrapper');
        if (zw) {
            var scale = parseFloat(zw.dataset.scale || '1');
            applyTransform(scale, overscrollX, overscrollY);
        }
    });

    document.addEventListener('mouseup', function(e) {
        if (e.button === 2 && isPanning) {
            isPanning = false;
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
            var zw = document.getElementById('__zoomWrapper');
            if (zw) {
                var scale = parseFloat(zw.dataset.scale || '1');
                if (overscrollX !== 0 || overscrollY !== 0) {
                    zw.style.transition = 'transform 0.25s cubic-bezier(0.25, 0.46, 0.45, 0.94)';
                    applyTransform(scale);
                }
            }
            overscrollX = 0;
            overscrollY = 0;
        }
    });

    document.addEventListener('contextmenu', function(e) { e.preventDefault(); });
})();
</script>";

        var restoreScale = _userZoomed
            ? _currentZoom.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "null";
        var restoreScript = $"<script>window.__restoreScale = {restoreScale};</script>";

        // Editing mode adds contenteditable + the edit->postMessage bridge on top of the interaction script.
        // Ahead of everything else: both scripts below post through window.__argoPost.
        var injected = "<script>" + Services.WebViewOutbox.Script + "</script>"
                       + restoreScript
                       + (IsEditable ? interactionScript + BuildEditingScript() : interactionScript);

        // Insert script before closing body tag, or at end if no body tag
        var html = Html!;
        if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace("</body>", injected + "</body>", StringComparison.OrdinalIgnoreCase);
        }
        else if (html.Contains("</html>", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace("</html>", injected + "</html>", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            html += injected;
        }

        try
        {
            _webView!.NavigateToString(html, new Uri("https://localhost/"));
        }
        catch (Exception ex)
        {
            // NavigateToString never delivered the navigation, so OnNavigationCompleted
            // won't fire to reset _hasPendingScroll. Clear it here so the next
            // UpdateWebViewContent can recapture a fresh scroll position instead of
            // being blocked indefinitely by a stale pending capture.
            _hasPendingScroll = false;
            System.Diagnostics.Debug.WriteLine($"InvoicePreview error: {ex.Message}");
        }
    }

    /// <summary>
    /// Captures the current WebView content as a PNG screenshot and returns it as base64.
    /// Returns null on platforms where the WebView is not active.
    /// </summary>
    public async Task<string?> CaptureScreenshotBase64Async()
    {
        if (!_webViewReady || _webView == null)
            return null;

        try
        {
            // Use html2canvas via JavaScript to capture the rendered content
            var captureScript = @"
(function() {
    return new Promise(function(resolve) {
        var wrapper = document.getElementById('__zoomWrapper') || document.body;
        // Reset transform temporarily for clean capture
        var origTransform = wrapper.style.transform;
        var origTransformOrigin = wrapper.style.transformOrigin;
        wrapper.style.transform = 'none';
        wrapper.style.transformOrigin = '';

        // Use canvas to capture
        var canvas = document.createElement('canvas');
        var rect = wrapper.getBoundingClientRect();
        canvas.width = Math.min(rect.width, 1200);
        canvas.height = Math.min(rect.height, 1600);

        // Simple capture: render visible area to canvas via foreignObject
        var svg = '<svg xmlns=""http://www.w3.org/2000/svg"" width=""' + canvas.width + '"" height=""' + canvas.height + '"">' +
            '<foreignObject width=""100%"" height=""100%"">' +
            '<div xmlns=""http://www.w3.org/1999/xhtml"">' + wrapper.innerHTML + '</div>' +
            '</foreignObject></svg>';

        var img = new Image();
        img.onload = function() {
            var ctx = canvas.getContext('2d');
            ctx.drawImage(img, 0, 0);
            wrapper.style.transform = origTransform;
            wrapper.style.transformOrigin = origTransformOrigin;
            resolve(canvas.toDataURL('image/png'));
        };
        img.onerror = function() {
            wrapper.style.transform = origTransform;
            wrapper.style.transformOrigin = origTransformOrigin;
            resolve('');
        };
        img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
    });
})()";

            var result = await _webView.InvokeScript(captureScript);
            if (!string.IsNullOrEmpty(result) && result.StartsWith("data:image/png;base64,"))
            {
                return result.Substring("data:image/png;base64,".Length);
            }

            // Strip surrounding quotes if the result is a JSON string
            if (result != null && result.StartsWith('"') && result.EndsWith('"'))
            {
                result = result[1..^1].Replace("\\\"", "\"");
                if (result.StartsWith("data:image/png;base64,"))
                {
                    return result.Substring("data:image/png;base64,".Length);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseScrollResult(string? result, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (string.IsNullOrEmpty(result))
            return false;

        // InvokeScript may return the JSON string wrapped in quotes with escaped inner quotes.
        var trimmed = result.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            trimmed = trimmed[1..^1].Replace("\\\"", "\"");

        trimmed = trimmed.Trim('[', ']');
        var parts = trimmed.Split(',');
        return parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
    }

    /// <summary>
    /// Reads the current text of every editable field straight from the DOM and applies it to the
    /// model via <see cref="InvoiceEdited"/>. Call this before previewing or saving so a value the
    /// user just typed (still only in the live DOM, not yet posted by the debounced input handler)
    /// isn't lost when the paper re-renders.
    /// </summary>
    public async Task CommitPendingEditsAsync()
    {
        if (_webView == null || !_webViewReady || !IsEditable)
            return;

        try
        {
            const string js =
                "JSON.stringify(Array.prototype.map.call(document.querySelectorAll('[data-field],[data-total-input]'),function(el){" +
                "return {f:(el.dataset.field||el.dataset.totalInput),i:(el.dataset.lineIndex!=null?parseInt(el.dataset.lineIndex,10):null),v:el.textContent};}))";

            var result = await _webView.InvokeScript(js);
            if (string.IsNullOrEmpty(result))
                return;

            // InvokeScript hands back the JS return value as a JSON-encoded string; unwrap one level.
            var json = result.Trim();
            if (json.StartsWith('"') && json.EndsWith('"'))
                json = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? json;

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var field = item.TryGetProperty("f", out var fe) ? fe.GetString() ?? string.Empty : string.Empty;
                // "description" is intentionally excluded: it's a strict product picker, so its DOM text is
                // only a search query and must never be committed to the model as a free-text description.
                if (field is not ("quantity" or "rate" or "notes"
                    or "taxValue" or "shippingValue" or "discountValue" or "feeValue"))
                    continue;

                int? index = item.TryGetProperty("i", out var ie) && ie.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? ie.GetInt32()
                    : null;
                var value = item.TryGetProperty("v", out var ve) ? ve.GetString() ?? string.Empty : string.Empty;
                InvoiceEdited?.Invoke(this, new InvoiceEditEventArgs(field, index, value));
            }
        }
        catch
        {
            // If capture fails, fall back to whatever the debounced handlers already posted.
        }
    }

    private void OpenInBrowserButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenInBrowserCommand?.Execute(null);
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_webView == null || !_webViewReady)
            return;

        var newZoom = Math.Min(_currentZoom + ZoomStep, MaxZoom);
        _ = _webView.InvokeScript($"window.__setZoom({newZoom.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    }

    private void ZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        if (_webView == null || !_webViewReady)
            return;

        var newZoom = Math.Max(_currentZoom - ZoomStep, MinZoom);
        _ = _webView.InvokeScript($"window.__setZoom({newZoom.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    }

    private void FitToWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (_webView == null || !_webViewReady)
            return;

        _ = _webView.InvokeScript("window.__fitToWindow()");
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        // Paired with the subscribe in OnLoaded. WebViewEnvironment.Failed is static, so an
        // unloaded preview left subscribed would be held alive for the life of the process.
        WebViewEnvironment.Failed -= OnWebViewFailed;
        DeactivateWebView();
    }
}

/// <summary>
/// A single edit made directly on the invoice paper. <see cref="Index"/> is the line-item index for
/// per-row fields (description/quantity/rate), or null for document-level fields (e.g. notes).
/// </summary>
public sealed class InvoiceEditEventArgs(string field, int? index, string value) : EventArgs
{
    public string Field { get; } = field;
    public int? Index { get; } = index;
    public string Value { get; } = value;
}

/// <summary>A product chosen from a line item's dropdown on the invoice paper.</summary>
public sealed class ProductPickEventArgs(int index, string productId) : EventArgs
{
    public int Index { get; } = index;
    public string ProductId { get; } = productId;
}
