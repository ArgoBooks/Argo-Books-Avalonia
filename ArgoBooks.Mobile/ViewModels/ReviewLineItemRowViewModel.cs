using ArgoBooks.Shared.Mobile;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// One read-only line-item row on the review screen: quantity/price/total are display-only (rows
/// can't be added/removed/edited), but the product chip opens a picker via
/// <see cref="OpenPickerCommand"/> (wired to <see cref="ReviewViewModel"/>). Wraps a
/// <see cref="ReviewLineItem"/> so picking a product writes straight back into the model
/// <see cref="ReviewModelMapper.BuildCapturedTransaction"/> reads at confirm time.
/// </summary>
public partial class ReviewLineItemRowViewModel : ViewModelBase
{
    private readonly ReviewLineItem _model;
    private readonly Action<ReviewLineItemRowViewModel> _openPicker;

    public string Description => _model.Description;

    public string QuantityAndPriceText => $"{_model.Quantity:0.##} × {_model.UnitPrice:C}";

    public string TotalText => _model.Total.ToString("C");

    [ObservableProperty]
    private string _productName;

    [ObservableProperty]
    private bool _isMatched;

    public string MatchTag => IsMatched ? "matched" : "suggested";

    /// <summary>The product chip's full label (single TextBlock binding target - avoids relying on
    /// Avalonia's Run.Opacity, which isn't guaranteed to render consistently across targets).</summary>
    public string ChipText => $"\U0001F4E6 {ProductName} · {MatchTag}";

    public ReviewLineItemRowViewModel(ReviewLineItem model, Action<ReviewLineItemRowViewModel> openPicker)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _openPicker = openPicker ?? throw new ArgumentNullException(nameof(openPicker));
        _productName = model.ProductName;
        _isMatched = model.IsMatched;
    }

    partial void OnProductNameChanged(string value) => OnPropertyChanged(nameof(ChipText));

    partial void OnIsMatchedChanged(bool value)
    {
        OnPropertyChanged(nameof(MatchTag));
        OnPropertyChanged(nameof(ChipText));
    }

    /// <summary>Applies a product choice from the picker (an existing snapshot product, or a
    /// freshly typed "create new product" name), writing it back into the underlying model.</summary>
    public void SetProduct(string productName, bool isMatched)
    {
        _model.ProductName = productName;
        _model.IsMatched = isMatched;
        ProductName = productName;
        IsMatched = isMatched;
    }

    [RelayCommand]
    private void OpenPicker() => _openPicker(this);
}
