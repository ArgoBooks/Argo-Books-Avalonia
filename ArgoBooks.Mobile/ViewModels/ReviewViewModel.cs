using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Shared.Mobile;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// The editable review screen shown after a successful AI scan (ShellViewModel.OnScanSucceeded).
/// Wraps a <see cref="ReviewModel"/> built by <see cref="ReviewModelMapper.Map"/> - supplier and
/// per-line-item products already auto-suggested from the active company's snapshot - and lets the
/// user edit the Expense/Revenue toggle, supplier/customer, date, total/tax, and each line item's
/// product (via a picker sheet). Line-item rows themselves (description/qty/price) are read-only;
/// only the product chip is editable. "Add to my books" builds the final
/// <see cref="CapturedTransaction"/> and hands it to <see cref="_onConfirm"/>.
/// </summary>
public partial class ReviewViewModel : ViewModelBase
{
    private readonly ReviewModel _reviewModel;
    private readonly byte[] _imageBytes;
    private readonly Func<CapturedTransaction, Task> _onConfirm;
    private readonly Action _onRescan;

    private ReviewLineItemRowViewModel? _activePickerRow;

    public ObservableCollection<ReviewLineItemRowViewModel> LineItems { get; } = new();

    /// <summary>Product names from the active snapshot, offered in the product picker alongside
    /// "create new product".</summary>
    public ObservableCollection<ProductChoiceViewModel> ProductChoices { get; } = new();

    [ObservableProperty]
    private bool _isExpense = true;

    [ObservableProperty]
    private string _supplierOrCustomer = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _taxText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isProductPickerOpen;

    [ObservableProperty]
    private string _newProductName = string.Empty;

    [ObservableProperty]
    private string _productPickerTargetDescription = string.Empty;

    public string SupplierOrCustomerLabel => IsExpense ? "Supplier" : "Customer";

    public ReviewViewModel(
        ReceiptScanResult result,
        byte[] imageBytes,
        MobileSnapshot? snapshot,
        Func<CapturedTransaction, Task> onConfirm,
        Action onRescan)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        _imageBytes = imageBytes ?? [];
        _onConfirm = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));
        _onRescan = onRescan ?? throw new ArgumentNullException(nameof(onRescan));

        _reviewModel = ReviewModelMapper.Map(result, snapshot);

        _isExpense = _reviewModel.Type == CapturedTransactionType.Expense;
        _supplierOrCustomer = _reviewModel.SupplierOrCustomer;
        _dateText = _reviewModel.Date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        _totalText = _reviewModel.Total.ToString("0.00", CultureInfo.InvariantCulture);
        _taxText = _reviewModel.Tax.ToString("0.00", CultureInfo.InvariantCulture);

        foreach (var line in _reviewModel.LineItems)
        {
            LineItems.Add(new ReviewLineItemRowViewModel(line, OpenProductPicker));
        }

        foreach (var product in snapshot?.Products ?? [])
        {
            if (string.IsNullOrWhiteSpace(product.Title))
            {
                continue;
            }

            var title = product.Title;
            ProductChoices.Add(new ProductChoiceViewModel(title, () => SelectProduct(title)));
        }
    }

    partial void OnIsExpenseChanged(bool value) => OnPropertyChanged(nameof(SupplierOrCustomerLabel));

    [RelayCommand]
    private void SetExpense() => IsExpense = true;

    [RelayCommand]
    private void SetRevenue() => IsExpense = false;

    private void OpenProductPicker(ReviewLineItemRowViewModel row)
    {
        _activePickerRow = row;
        ProductPickerTargetDescription = row.Description;
        NewProductName = string.Empty;
        IsProductPickerOpen = true;
    }

    private void SelectProduct(string productName)
    {
        _activePickerRow?.SetProduct(productName, isMatched: true);
        CloseProductPicker();
    }

    [RelayCommand]
    private void CreateNewProduct()
    {
        var name = NewProductName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _activePickerRow?.SetProduct(name, isMatched: false);
        CloseProductPicker();
    }

    [RelayCommand]
    private void CloseProductPicker()
    {
        IsProductPickerOpen = false;
        _activePickerRow = null;
    }

    [RelayCommand]
    private void Rescan() => _onRescan();

    [RelayCommand]
    private async Task AddToBooksAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _reviewModel.Type = IsExpense ? CapturedTransactionType.Expense : CapturedTransactionType.Revenue;
            _reviewModel.SupplierOrCustomer = SupplierOrCustomer?.Trim() ?? string.Empty;

            if (DateTime.TryParse(DateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                _reviewModel.Date = parsedDate;
            }

            if (decimal.TryParse(TotalText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTotal))
            {
                _reviewModel.Total = parsedTotal;
            }

            if (decimal.TryParse(TaxText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTax))
            {
                _reviewModel.Tax = parsedTax;
            }

            var imageBase64 = _imageBytes.Length > 0 ? Convert.ToBase64String(_imageBytes) : null;
            var transaction = ReviewModelMapper.BuildCapturedTransaction(_reviewModel, imageBase64);
            await _onConfirm(transaction);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
