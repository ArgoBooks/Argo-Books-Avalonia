using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// The method and disposal choices offered wherever an asset is described.
/// </summary>
public static class AssetOptions
{
    public const string StraightLine = "Straight line";
    public const string DecliningBalance = "Declining balance";
    public const string Sold = "Sold";
    public const string Scrapped = "Scrapped or given away";

    public static IReadOnlyList<string> Methods { get; } = [StraightLine, DecliningBalance];

    public static IReadOnlyList<string> DisposalKinds { get; } = [Sold, Scrapped];
}

/// <summary>
/// ViewModel for the asset modals (Add, Edit, Sell or dispose).
/// </summary>
public partial class AssetModalsViewModel : ViewModelBase
{
    /// <summary>Raised whenever an asset is added, changed, sold or removed, including by undo.</summary>
    public event EventHandler? AssetsChanged;

    public IReadOnlyList<string> MethodOptions => AssetOptions.Methods;
    public IReadOnlyList<string> DisposalKindOptions => AssetOptions.DisposalKinds;

    private static CompanyData? Data => App.CompanyManager?.CompanyData;

    private void RaiseAssetsChanged() => AssetsChanged?.Invoke(this, EventArgs.Empty);

    private void Record(string description, Action undo, Action redo)
    {
        var data = Data;
        App.UndoRedoManager.RecordAction(new DelegateAction(description,
            () =>
            {
                undo();
                data?.MarkAsModified();
                RaiseAssetsChanged();
            },
            () =>
            {
                redo();
                data?.MarkAsModified();
                RaiseAssetsChanged();
            }));
        data?.MarkAsModified();
        RaiseAssetsChanged();
    }

    private string Money(decimal amount) => CurrencyInfo.FormatAmount(amount, _currency);

    #region Add / Edit Form

    [ObservableProperty]
    private bool _isFormOpen;

    // Set when the form opens and kept on close, so the title doesn't change while it closes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(FormSaveText))]
    private bool _isEditMode;

    public string FormTitle => IsEditMode ? "Edit Asset".Translate() : "Add Asset".Translate();
    public string FormSaveText => IsEditMode ? "Save Changes".Translate() : "Add Asset".Translate();

    /// <summary>The asset being edited, or null while adding one.</summary>
    private FixedAsset? _editing;

    /// <summary>The expense a new asset is being made from, if any.</summary>
    private string? _sourceExpenseId;

    private string _currency = "USD";

    /// <summary>Shown when the asset was bought with an expense, naming it.</summary>
    [ObservableProperty]
    private string? _sourceExpenseText;

    [ObservableProperty]
    private string _modalName = string.Empty;

    [ObservableProperty]
    private List<AssetTypeOption> _typeOptions = [];

    /// <summary>The asset's type, which for a Canadian company also names its CRA class.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLand), nameof(ShowPriorDepreciation))]
    private AssetTypeOption? _modalType;

    public bool IsLand => ModalType?.Type == AssetType.Land;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPriorDepreciation), nameof(PriorDepreciationLabel))]
    private DateTimeOffset? _modalPurchaseDate = DateTimeOffset.Now;

    [ObservableProperty]
    private decimal? _modalCost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStraightLine))]
    private string _modalMethod = AssetOptions.StraightLine;

    public bool IsStraightLine => ModalMethod == AssetOptions.StraightLine;

    [ObservableProperty]
    private decimal? _modalUsefulLife;

    /// <summary>The yearly percentage for the declining balance method.</summary>
    [ObservableProperty]
    private decimal? _modalRate;

    [ObservableProperty]
    private decimal? _modalEndValue;

    [ObservableProperty]
    private decimal? _modalPriorDepreciation;

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    [ObservableProperty]
    private string? _modalError;

    [ObservableProperty]
    private string _yearlyFigureText = string.Empty;

    [ObservableProperty]
    private bool _hasYearlyFigure;

    [ObservableProperty]
    private bool _isScheduleOpen;

    public ObservableCollection<AssetScheduleRow> ScheduleRows { get; } = [];

    /// <summary>The year the books started, for an asset bought before it: its depreciation until then was claimed in earlier books.</summary>
    private int? PriorDepreciationYear
    {
        get
        {
            if (_sourceExpenseId != null || _editing?.ExpenseId != null || ModalPurchaseDate is not { } purchased)
                return null;
            var startYear = _editing?.TrackedFrom?.Year ?? _editing?.CreatedAt.ToLocalTime().Year ?? DateTime.Today.Year;
            return purchased.Year < startYear ? startYear : null;
        }
    }

    public bool ShowPriorDepreciation => !IsLand && PriorDepreciationYear.HasValue;

    public string PriorDepreciationLabel => "Depreciation already claimed before {0}".TranslateFormat(PriorDepreciationYear ?? DateTime.Today.Year);

    #region CRA Class

    [ObservableProperty]
    private string? _ccaClassHint;

    // Set while the form is being filled from a record, so loading it does not count as the user picking a type.
    private bool _loading;

    private ExpenseAssetLink.CcaChoice? CurrentCcaChoice =>
        !IsLand && ModalPurchaseDate is { } date ? CanadianAssetRates.Choice(ModalType?.CcaClass, date.Date) : null;

    private void PrepareTypes()
    {
        TypeOptions = CanadianAssetRates.TypeOptions(Data);
        if (CanadianAssetRates.AppliesTo(Data))
            CanadianAssetRates.CheckForUpdateOnce();
    }

    /// <summary>Sets a type that carries a CRA class to depreciate at that class's rate.</summary>
    private void UseTypeRate()
    {
        if (CurrentCcaChoice is not { } choice)
            return;
        ModalMethod = AssetOptions.DecliningBalance;
        ModalRate = choice.RatePercent;
    }

    #endregion

    partial void OnModalTypeChanged(AssetTypeOption? value)
    {
        if (!_loading)
            UseTypeRate();
        UpdatePreview();
    }

    partial void OnModalPurchaseDateChanged(DateTimeOffset? value) => UpdatePreview();
    partial void OnModalCostChanged(decimal? value) => UpdatePreview();
    partial void OnModalMethodChanged(string value) => UpdatePreview();
    partial void OnModalUsefulLifeChanged(decimal? value) => UpdatePreview();
    partial void OnModalRateChanged(decimal? value) => UpdatePreview();
    partial void OnModalEndValueChanged(decimal? value) => UpdatePreview();
    partial void OnModalPriorDepreciationChanged(decimal? value) => UpdatePreview();

    private sealed record FormState(
        string Name, string Type, DateTimeOffset? PurchaseDate, decimal? Cost, string Method, decimal? UsefulLife,
        decimal? Rate, decimal? EndValue, decimal? PriorDepreciation, string Notes);

    // The form as it opened, so closing it only asks to discard when something was changed.
    private FormState? _original;

    private FormState Capture() => new(
        ModalName, ModalType?.Label ?? string.Empty, ModalPurchaseDate, ModalCost, ModalMethod, ModalUsefulLife,
        ModalRate, ModalEndValue, ModalPriorDepreciation, ModalNotes);

    public void OpenAddModal()
    {
        ResetForm();
        _currency = Data?.Settings.Localization.Currency ?? "USD";
        UseTypeRate();
        _original = Capture();
        IsEditMode = false;
        IsFormOpen = true;
    }

    public void OpenEditModal(AssetDisplayItem item)
    {
        var asset = Data?.Assets.FirstOrDefault(a => a.Id == item.Id);
        if (asset == null) return;

        ResetForm();
        _loading = true;
        _editing = asset;
        _currency = asset.Currency;
        ModalName = asset.Name;
        var options = TypeOptions.ToList();
        var type = CanadianAssetRates.Find(options, asset.Type, asset.CcaClass);
        TypeOptions = options;
        ModalType = type;
        ModalPurchaseDate = new DateTimeOffset(asset.PurchaseDate);
        ModalCost = asset.Cost;
        ModalMethod = asset.Method == DepreciationMethod.DecliningBalance ? AssetOptions.DecliningBalance : AssetOptions.StraightLine;
        ModalUsefulLife = asset.UsefulLifeYears;
        ModalRate = asset.RatePercent;
        ModalEndValue = asset.EndValue == 0 ? null : asset.EndValue;
        ModalPriorDepreciation = asset.PriorDepreciation == 0 ? null : asset.PriorDepreciation;
        ModalNotes = asset.Notes;
        if (!string.IsNullOrEmpty(asset.ExpenseId))
            SourceExpenseText = "Bought with expense {0}".TranslateFormat(asset.ExpenseId);
        _loading = false;
        UpdatePreview();

        _original = Capture();
        IsEditMode = true;
        IsFormOpen = true;
    }

    /// <summary>Opens the asset bought with this expense, or the add form filled in from it when there is none yet.</summary>
    public void OpenForExpense(string expenseId)
    {
        var data = Data;
        var expense = data?.Expenses.FirstOrDefault(e => e.Id == expenseId);
        if (data == null || expense == null) return;

        if (ExpenseAssetLink.AssetFor(data, expenseId) is { } existing)
        {
            OpenEditModal(new AssetDisplayItem { Id = existing.Id });
            return;
        }

        ResetForm();
        _sourceExpenseId = expense.Id;
        _currency = expense.OriginalCurrency;
        ModalName = expense.Description;
        ModalPurchaseDate = new DateTimeOffset(expense.Date);
        ModalCost = AssetDepreciation.PreTaxCost(expense);
        SourceExpenseText = "From expense {0}. The reports count this cost as depreciation instead of as an expense.".TranslateFormat(expense.Id);
        UseTypeRate();
        UpdatePreview();

        _original = Capture();
        IsEditMode = false;
        IsFormOpen = true;
    }

    [RelayCommand]
    private async Task RequestCloseFormAsync()
    {
        if (Capture() != _original && !(IsEditMode ? await ConfirmDiscardEditsAsync() : await ConfirmDiscardNewAsync()))
            return;
        IsFormOpen = false;
    }

    [RelayCommand]
    private void ToggleSchedule() => IsScheduleOpen = !IsScheduleOpen;

    [RelayCommand]
    private void SaveForm()
    {
        var data = Data;
        if (data == null || !ValidateForm())
            return;

        var formAsset = BuildFromForm();

        if (_editing is { } asset)
        {
            var before = asset.Clone();
            asset.CopyFrom(formAsset);
            var after = asset.Clone();
            Record($"Edit asset '{asset.Name}'", () => asset.CopyFrom(before), () => asset.CopyFrom(after));
        }
        else
        {
            formAsset.Id = new IdGenerator(data).NextAssetId();
            formAsset.ExpenseId = _sourceExpenseId;
            formAsset.CreatedAt = DateTime.UtcNow;
            data.Assets.Add(formAsset);
            Record($"Add asset '{formAsset.Name}'",
                () => data.Assets.RemoveRecord(formAsset),
                () => data.Assets.RestoreRecord(formAsset));
        }

        IsFormOpen = false;
    }

    private bool ValidateForm()
    {
        ModalError = null;
        var cost = ModalCost ?? 0m;
        var endValue = ModalEndValue ?? 0m;

        if (string.IsNullOrWhiteSpace(ModalName))
            ModalError = "Give the asset a name.".Translate();
        else if (ModalPurchaseDate == null)
            ModalError = "Enter the date it was bought.".Translate();
        else if (cost <= 0)
            ModalError = "Enter what it cost.".Translate();
        else if (!IsLand && IsStraightLine && ModalUsefulLife is not > 0)
            ModalError = "Enter how many years it will be used for.".Translate();
        else if (!IsLand && !IsStraightLine && ModalRate is not (> 0 and <= 100))
            ModalError = "Enter a yearly rate above 0 and up to 100 percent.".Translate();
        else if (!IsLand && (endValue < 0 || endValue >= cost))
            ModalError = "The value at the end has to be less than what it cost.".Translate();
        else if (ShowPriorDepreciation && ((ModalPriorDepreciation ?? 0m) < 0 || (ModalPriorDepreciation ?? 0m) > cost - endValue))
            ModalError = "The depreciation already claimed can't be more than it can lose in value.".Translate();

        return ModalError == null;
    }

    /// <summary>The asset as the form describes it, keeping what the form doesn't show from the asset being edited.</summary>
    private FixedAsset BuildFromForm()
    {
        var land = IsLand;
        var showPrior = ShowPriorDepreciation;
        var cca = CurrentCcaChoice;
        return new FixedAsset
        {
            Id = _editing?.Id ?? string.Empty,
            Name = ModalName.Trim(),
            Type = ModalType?.Type ?? AssetType.Other,
            PurchaseDate = ModalPurchaseDate?.Date ?? DateTime.Today,
            Cost = ModalCost ?? 0m,
            Currency = _currency,
            ExpenseId = _editing?.ExpenseId ?? _sourceExpenseId,
            Method = !land && !IsStraightLine ? DepreciationMethod.DecliningBalance : DepreciationMethod.StraightLine,
            UsefulLifeYears = !land && IsStraightLine ? ModalUsefulLife : null,
            RatePercent = !land && !IsStraightLine ? ModalRate : null,
            CcaClass = land ? null : ModalType?.CcaClass,
            FirstYearFactor = !land && !IsStraightLine ? cca?.FirstYearFactor : null,
            EndValue = land ? 0m : ModalEndValue ?? 0m,
            PriorDepreciation = showPrior ? ModalPriorDepreciation ?? 0m : 0m,
            TrackedFrom = showPrior ? new DateTime(PriorDepreciationYear!.Value, 1, 1) : null,
            DisposedDate = _editing?.DisposedDate,
            DisposalKind = _editing?.DisposalKind,
            SalePrice = _editing?.SalePrice ?? 0m,
            Notes = ModalNotes.Trim(),
            CreatedAt = _editing?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private void ResetForm()
    {
        _loading = true;
        PrepareTypes();
        _editing = null;
        _sourceExpenseId = null;
        SourceExpenseText = null;
        ModalName = string.Empty;
        ModalType = TypeOptions.FirstOrDefault(o => o.Type == AssetType.Equipment);
        ModalPurchaseDate = DateTimeOffset.Now;
        ModalCost = null;
        ModalMethod = AssetOptions.StraightLine;
        ModalUsefulLife = null;
        ModalRate = null;
        ModalEndValue = null;
        ModalPriorDepreciation = null;
        ModalNotes = string.Empty;
        ModalError = null;
        IsScheduleOpen = false;
        _loading = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        OnPropertyChanged(nameof(ShowPriorDepreciation));
        OnPropertyChanged(nameof(PriorDepreciationLabel));
        CcaClassHint = CurrentCcaChoice is { } choice
            ? IsStraightLine
                ? "CRA depreciates this class by a yearly rate. Choose Declining balance to follow it.".Translate()
                : CanadianAssetRates.Describe(choice)
            : null;
        ScheduleRows.Clear();
        YearlyFigureText = string.Empty;
        HasYearlyFigure = false;

        if (ModalCost is not > 0 || ModalPurchaseDate == null)
            return;

        var asset = BuildFromForm();
        asset.DisposedDate = null;
        foreach (var year in AssetDepreciation.Schedule(asset))
            ScheduleRows.Add(new AssetScheduleRow(year.Year, Money(year.Amount), Money(year.BookValueAfter)));
        if (ScheduleRows.Count == 0)
            return;

        var firstYear = Money(AssetDepreciation.FirstYear(asset));
        YearlyFigureText = asset.FollowsCca
            ? "{0} in {1}, then {2}% of what is left each year".TranslateFormat(ScheduleRows[0].Amount, ScheduleRows[0].Year, asset.RatePercent!.Value.ToString("0.##"))
            : asset.Method == DepreciationMethod.StraightLine
                ? "{0} a year".TranslateFormat(firstYear)
                : "{0} in the first year, then less each year".TranslateFormat(firstYear);
        HasYearlyFigure = true;
    }

    #endregion

    #region Sell or Dispose

    [ObservableProperty]
    private bool _isDisposeOpen;

    private FixedAsset? _disposing;

    [ObservableProperty]
    private string _disposeAssetName = string.Empty;

    [ObservableProperty]
    private string _disposeBookValueText = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _disposeDate = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSale))]
    private string _disposeKind = AssetOptions.Sold;

    public bool IsSale => DisposeKind == AssetOptions.Sold;

    [ObservableProperty]
    private decimal? _disposeSalePrice;

    [ObservableProperty]
    private string _disposeResultText = string.Empty;

    [ObservableProperty]
    private bool _isDisposeGain;

    [ObservableProperty]
    private string? _disposeError;

    partial void OnDisposeKindChanged(string value) => UpdateDisposeResult();
    partial void OnDisposeSalePriceChanged(decimal? value) => UpdateDisposeResult();
    partial void OnDisposeDateChanged(DateTimeOffset? value) => UpdateDisposeResult();

    public void OpenDisposeModal(AssetDisplayItem item)
    {
        var asset = Data?.Assets.FirstOrDefault(a => a.Id == item.Id);
        if (asset == null || asset.IsDisposed) return;

        _disposing = asset;
        _currency = asset.Currency;
        DisposeAssetName = asset.Name;
        DisposeDate = DateTimeOffset.Now;
        DisposeKind = AssetOptions.Sold;
        DisposeSalePrice = null;
        DisposeError = null;
        UpdateDisposeResult();
        IsDisposeOpen = true;
    }

    [RelayCommand]
    private void RequestCloseDispose()
    {
        IsDisposeOpen = false;
        _disposing = null;
    }

    [RelayCommand]
    private void SaveDispose()
    {
        if (_disposing is not { } asset) return;

        if (DisposeDate is not { } date)
        {
            DisposeError = "Enter the date it left the business.".Translate();
            return;
        }
        if (date.Date < asset.PurchaseDate.Date)
        {
            DisposeError = "It can't leave the business before the date it was bought.".Translate();
            return;
        }
        if (IsSale && (DisposeSalePrice ?? 0m) < 0)
        {
            DisposeError = "The sale price can't be negative.".Translate();
            return;
        }

        var before = asset.Clone();
        asset.DisposedDate = date.Date;
        asset.DisposalKind = IsSale ? AssetDisposalKind.Sold : AssetDisposalKind.Scrapped;
        asset.SalePrice = IsSale ? DisposeSalePrice ?? 0m : 0m;
        asset.UpdatedAt = DateTime.UtcNow;
        var after = asset.Clone();
        Record(IsSale ? $"Edit asset '{asset.Name}' to record its sale" : $"Edit asset '{asset.Name}' to record its disposal",
            () => asset.CopyFrom(before), () => asset.CopyFrom(after));

        IsDisposeOpen = false;
        _disposing = null;
    }

    private void UpdateDisposeResult()
    {
        if (_disposing is not { } asset)
            return;

        var trial = asset.Clone();
        trial.DisposedDate = (DisposeDate ?? DateTimeOffset.Now).Date;
        var bookValue = AssetDepreciation.BookValueAtDisposal(trial);
        DisposeBookValueText = Money(bookValue);

        var difference = (IsSale ? DisposeSalePrice ?? 0m : 0m) - bookValue;
        IsDisposeGain = difference >= 0;
        DisposeResultText = difference switch
        {
            > 0 => "Gain of {0}".TranslateFormat(Money(difference)),
            < 0 => "Loss of {0}".TranslateFormat(Money(-difference)),
            _ => "No gain or loss".Translate()
        };
    }

    /// <summary>Puts a sold or disposed asset back on the books, for a sale recorded by mistake.</summary>
    public void ReverseDisposal(AssetDisplayItem item)
    {
        var asset = Data?.Assets.FirstOrDefault(a => a.Id == item.Id);
        if (asset is not { IsDisposed: true }) return;

        var before = asset.Clone();
        asset.DisposedDate = null;
        asset.DisposalKind = null;
        asset.SalePrice = 0m;
        asset.UpdatedAt = DateTime.UtcNow;
        var after = asset.Clone();
        Record($"Edit asset '{asset.Name}' to put it back on the books", () => asset.CopyFrom(before), () => asset.CopyFrom(after));
    }

    #endregion

    #region Delete

    public async void OpenDeleteConfirm(AssetDisplayItem item)
    {
        try
        {
            var data = Data;
            var asset = data?.Assets.FirstOrDefault(a => a.Id == item.Id);
            if (data == null || asset == null) return;

            var message = string.IsNullOrEmpty(asset.ExpenseId)
                ? "Are you sure you want to delete this asset?\n\n{0}".TranslateFormat(asset.Name)
                : "Are you sure you want to delete this asset?\n\n{0}\n\nExpense {1} will count as an expense again.".TranslateFormat(asset.Name, asset.ExpenseId);
            if (!await ConfirmDeleteAsync("Delete Asset".Translate(), message))
                return;

            data.Assets.RemoveRecord(asset);
            Record($"Delete asset '{asset.Name}'",
                () => data.Assets.RestoreRecord(asset),
                () => data.Assets.RemoveRecord(asset));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Asset.OpenDeleteConfirm");
        }
    }

    #endregion
}

/// <summary>One year of an asset's depreciation schedule.</summary>
public record AssetScheduleRow(int Year, string Amount, string BookValueAfter);
