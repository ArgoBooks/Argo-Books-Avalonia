using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Reports;

/// <summary>
/// Event args for property changing event, including old and new values.
/// </summary>
public class ElementPropertyChangingEventArgs(string propertyName, object? oldValue, object? newValue) : EventArgs
{
    public string PropertyName { get; } = propertyName;
    public object? OldValue { get; } = oldValue;
    public object? NewValue { get; } = newValue;
}

/// <summary>
/// Base class for all report elements.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "elementType")]
[JsonDerivedType(typeof(ChartReportElement), "Chart")]
[JsonDerivedType(typeof(TableReportElement), "Table")]
[JsonDerivedType(typeof(LabelReportElement), "Label")]
[JsonDerivedType(typeof(ImageReportElement), "Image")]
[JsonDerivedType(typeof(DateRangeReportElement), "DateRange")]
[JsonDerivedType(typeof(SummaryReportElement), "Summary")]
[JsonDerivedType(typeof(AccountingTableReportElement), "AccountingTable")]
public abstract class ReportElementBase : INotifyPropertyChanged
{
    /// <summary>
    /// Fired when a property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fired before a property changes, with old and new values for undo/redo tracking.
    /// </summary>
    public event EventHandler<ElementPropertyChangingEventArgs>? PropertyChanging;

    /// <summary>
    /// Unique identifier for the element.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id
    {
        get;
        set => SetField(ref field, value);
    } = Guid.NewGuid().ToString();

    /// <summary>
    /// X position on the page.
    /// </summary>
    [JsonPropertyName("x")]
    public double X
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Y position on the page.
    /// </summary>
    [JsonPropertyName("y")]
    public double Y
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Element width.
    /// </summary>
    [JsonPropertyName("width")]
    public double Width
    {
        get;
        set => SetField(ref field, value);
    } = 200;

    /// <summary>
    /// Element height.
    /// </summary>
    [JsonPropertyName("height")]
    public double Height
    {
        get;
        set => SetField(ref field, value);
    } = 150;

    /// <summary>
    /// Z-order for layering.
    /// </summary>
    [JsonPropertyName("zOrder")]
    public int ZOrder
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Which page this element belongs to (1-based).
    /// </summary>
    [JsonPropertyName("pageNumber")]
    public int PageNumber
    {
        get;
        set => SetField(ref field, value);
    } = 1;

    /// <summary>
    /// Whether the element is visible.
    /// </summary>
    [JsonPropertyName("isVisible")]
    public bool IsVisible
    {
        get;
        set => SetField(ref field, value);
    } = true;

    /// <summary>
    /// Minimum size for the element.
    /// </summary>
    [JsonIgnore]
    public virtual double MinimumSize => 40;

    /// <summary>
    /// Display name for the element type.
    /// </summary>
    [JsonIgnore]
    public abstract string DisplayName { get; }

    public abstract ReportElementType GetElementType();

    /// <summary>
    /// Creates a clone of this element.
    /// </summary>
    public abstract ReportElementBase Clone();

    /// <summary>
    /// Gets the bounds as a rectangle.
    /// </summary>
    [JsonIgnore]
    public (double X, double Y, double Width, double Height) Bounds
    {
        get => (X, Y, Width, Height);
        set => (X, Y, Width, Height) = value;
    }

    /// <summary>
    /// Gets or sets bounds including page number, for undo/redo of cross-page moves.
    /// </summary>
    [JsonIgnore]
    public (double X, double Y, double Width, double Height, int PageNumber) BoundsWithPage
    {
        get => (X, Y, Width, Height, PageNumber);
        set { (X, Y, Width, Height) = (value.X, value.Y, value.Width, value.Height); PageNumber = value.PageNumber; }
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Raises the PropertyChanging event before a property changes.
    /// </summary>
    protected virtual void OnPropertyChanging(string? propertyName, object? oldValue, object? newValue)
    {
        PropertyChanging?.Invoke(this, new ElementPropertyChangingEventArgs(propertyName ?? "", oldValue, newValue));
    }

    /// <summary>
    /// Sets a field value and raises PropertyChanged if the value changed.
    /// Also raises PropertyChanging before the change for undo/redo tracking.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        OnPropertyChanging(propertyName, field, value);
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// Chart element for displaying graphs and charts.
/// </summary>
public class ChartReportElement : ReportElementBase
{
    [JsonPropertyName("chartType")]
    public ChartDataType ChartType
    {
        get;
        set => SetField(ref field, value);
    } = ChartDataType.TotalRevenue;

    [JsonPropertyName("chartStyle")]
    public ReportChartStyle ChartStyle
    {
        get;
        set => SetField(ref field, value);
    } = ReportChartStyle.Area;

    [JsonPropertyName("showLegend")]
    public bool ShowLegend
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showTitle")]
    public bool ShowTitle
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("fontFamily")]
    public string FontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("titleFontSize")]
    public double TitleFontSize
    {
        get;
        set => SetField(ref field, value);
    } = 12;

    [JsonPropertyName("axisFontSize")]
    public double AxisFontSize
    {
        get;
        set => SetField(ref field, value);
    } = 10;

    [JsonPropertyName("legendFontSize")]
    public double LegendFontSize
    {
        get;
        set => SetField(ref field, value);
    } = 11;

    [JsonPropertyName("borderColor")]
    public string BorderColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportGray;

    [JsonPropertyName("borderThickness")]
    public int BorderThickness
    {
        get;
        set => SetField(ref field, value);
    } = 1;

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.White;

    [JsonPropertyName("titleFontFamily")]
    public string TitleFontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("legendMaxCharacters")]
    public int LegendMaxCharacters
    {
        get;
        set => SetField(ref field, value);
    } = 20;

    public override double MinimumSize => 80;
    public override string DisplayName => "Chart";
    public override ReportElementType GetElementType() => ReportElementType.Chart;

    public override ReportElementBase Clone()
    {
        return new ChartReportElement
        {
            Id = Guid.NewGuid().ToString(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            PageNumber = PageNumber,
            IsVisible = IsVisible,
            ChartType = ChartType,
            ChartStyle = ChartStyle,
            ShowLegend = ShowLegend,
            ShowTitle = ShowTitle,
            FontFamily = FontFamily,
            TitleFontFamily = TitleFontFamily,
            TitleFontSize = TitleFontSize,
            AxisFontSize = AxisFontSize,
            LegendFontSize = LegendFontSize,
            LegendMaxCharacters = LegendMaxCharacters,
            BorderColor = BorderColor,
            BorderThickness = BorderThickness,
            BackgroundColor = BackgroundColor
        };
    }
}

/// <summary>
/// Table element for displaying transaction data.
/// </summary>
public class TableReportElement : ReportElementBase
{
    [JsonPropertyName("transactionType")]
    public TransactionType TransactionType
    {
        get;
        set => SetField(ref field, value);
    } = TransactionType.Revenue;

    [JsonPropertyName("includeReturns")]
    public bool IncludeReturns
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("includeLosses")]
    public bool IncludeLosses
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("dataSelection")]
    public TableDataSelection DataSelection
    {
        get;
        set => SetField(ref field, value);
    } = TableDataSelection.All;

    [JsonPropertyName("sortOrder")]
    public TableSortOrder SortOrder
    {
        get;
        set => SetField(ref field, value);
    } = TableSortOrder.DateDescending;

    [JsonPropertyName("maxRows")]
    public int MaxRows
    {
        get;
        set => SetField(ref field, value);
    } = 10;

    [JsonPropertyName("showHeaders")]
    public bool ShowHeaders
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("alternateRowColors")]
    public bool AlternateRowColors
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showGridLines")]
    public bool ShowGridLines
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showTotalsRow")]
    public bool ShowTotalsRow
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("autoSizeColumns")]
    public bool AutoSizeColumns
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("fontSize")]
    public double FontSize
    {
        get;
        set => SetField(ref field, value);
    } = 10;

    [JsonPropertyName("fontFamily")]
    public string FontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("headerFontSize")]
    public double HeaderFontSize
    {
        get;
        set => SetField(ref field, value);
    } = 10;

    [JsonPropertyName("headerFontFamily")]
    public string HeaderFontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("dataRowHeight")]
    public int DataRowHeight
    {
        get;
        set => SetField(ref field, value);
    } = 20;

    [JsonPropertyName("headerRowHeight")]
    public int HeaderRowHeight
    {
        get;
        set => SetField(ref field, value);
    } = 25;

    [JsonPropertyName("cellPadding")]
    public int CellPadding
    {
        get;
        set => SetField(ref field, value);
    } = 3;

    [JsonPropertyName("headerBackgroundColor")]
    public string HeaderBackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportHighlight;

    [JsonPropertyName("headerTextColor")]
    public string HeaderTextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.White;

    [JsonPropertyName("dataRowTextColor")]
    public string DataRowTextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.Black;

    [JsonPropertyName("gridLineColor")]
    public string GridLineColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportBorder;

    [JsonPropertyName("baseRowColor")]
    public string BaseRowColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.White;

    [JsonPropertyName("alternateRowColor")]
    public string AlternateRowColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportBackgroundAlt;

    [JsonPropertyName("showTitle")]
    public bool ShowTitle
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("titleFontSize")]
    public double TitleFontSize
    {
        get;
        set => SetField(ref field, value);
    } = 10;

    [JsonPropertyName("titleFontFamily")]
    public string TitleFontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("titleBackgroundColor")]
    public string TitleBackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportHeaderBg;

    [JsonPropertyName("titleTextColor")]
    public string TitleTextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.White;

    [JsonPropertyName("textAlignment")]
    public HorizontalTextAlignment TextAlignment
    {
        get;
        set => SetField(ref field, value);
    } = HorizontalTextAlignment.Center;

    // Column visibility
    [JsonPropertyName("showDateColumn")]
    public bool ShowDateColumn
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showTransactionIdColumn")]
    public bool ShowTransactionIdColumn
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showCompanyColumn")]
    public bool ShowCompanyColumn
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showProductColumn")]
    public bool ShowProductColumn
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showQuantityColumn")]
    public bool ShowQuantityColumn
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showTotalColumn")]
    public bool ShowTotalColumn
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showStatusColumn")]
    public bool ShowStatusColumn
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("showAccountantColumn")]
    public bool ShowAccountantColumn
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("showShippingColumn")]
    public bool ShowShippingColumn
    {
        get;
        set => SetField(ref field, value);
    }

    public override double MinimumSize => 100;
    public override string DisplayName => "Table";
    public override ReportElementType GetElementType() => ReportElementType.Table;

    public override ReportElementBase Clone()
    {
        return new TableReportElement
        {
            Id = Guid.NewGuid().ToString(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            PageNumber = PageNumber,
            IsVisible = IsVisible,
            TransactionType = TransactionType,
            IncludeReturns = IncludeReturns,
            IncludeLosses = IncludeLosses,
            DataSelection = DataSelection,
            SortOrder = SortOrder,
            MaxRows = MaxRows,
            ShowHeaders = ShowHeaders,
            AlternateRowColors = AlternateRowColors,
            ShowGridLines = ShowGridLines,
            ShowTotalsRow = ShowTotalsRow,
            AutoSizeColumns = AutoSizeColumns,
            FontSize = FontSize,
            FontFamily = FontFamily,
            HeaderFontSize = HeaderFontSize,
            HeaderFontFamily = HeaderFontFamily,
            ShowTitle = ShowTitle,
            TitleFontSize = TitleFontSize,
            TitleFontFamily = TitleFontFamily,
            TitleBackgroundColor = TitleBackgroundColor,
            TitleTextColor = TitleTextColor,
            TextAlignment = TextAlignment,
            DataRowHeight = DataRowHeight,
            HeaderRowHeight = HeaderRowHeight,
            CellPadding = CellPadding,
            HeaderBackgroundColor = HeaderBackgroundColor,
            HeaderTextColor = HeaderTextColor,
            DataRowTextColor = DataRowTextColor,
            GridLineColor = GridLineColor,
            BaseRowColor = BaseRowColor,
            AlternateRowColor = AlternateRowColor,
            ShowDateColumn = ShowDateColumn,
            ShowTransactionIdColumn = ShowTransactionIdColumn,
            ShowCompanyColumn = ShowCompanyColumn,
            ShowProductColumn = ShowProductColumn,
            ShowQuantityColumn = ShowQuantityColumn,
            ShowTotalColumn = ShowTotalColumn,
            ShowStatusColumn = ShowStatusColumn,
            ShowAccountantColumn = ShowAccountantColumn,
            ShowShippingColumn = ShowShippingColumn
        };
    }
}

/// <summary>
/// Label element for displaying text content.
/// </summary>
public class LabelReportElement : ReportElementBase
{
    [JsonPropertyName("text")]
    public string Text
    {
        get;
        set => SetField(ref field, value);
    } = "Sample Text";

    [JsonPropertyName("fontFamily")]
    public string FontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("fontSize")]
    public double FontSize
    {
        get;
        set => SetField(ref field, value);
    } = 12;

    [JsonPropertyName("isBold")]
    public bool IsBold
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("isItalic")]
    public bool IsItalic
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("isUnderline")]
    public bool IsUnderline
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("textColor")]
    public string TextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.Black;

    [JsonPropertyName("horizontalAlignment")]
    public HorizontalTextAlignment HorizontalAlignment
    {
        get;
        set => SetField(ref field, value);
    } = HorizontalTextAlignment.Center;

    [JsonPropertyName("verticalAlignment")]
    public VerticalTextAlignment VerticalAlignment
    {
        get;
        set => SetField(ref field, value);
    } = VerticalTextAlignment.Center;

    public override string DisplayName => "Label";
    public override ReportElementType GetElementType() => ReportElementType.Label;

    public override ReportElementBase Clone()
    {
        return new LabelReportElement
        {
            Id = Guid.NewGuid().ToString(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            PageNumber = PageNumber,
            IsVisible = IsVisible,
            Text = Text,
            FontFamily = FontFamily,
            FontSize = FontSize,
            IsBold = IsBold,
            IsItalic = IsItalic,
            IsUnderline = IsUnderline,
            TextColor = TextColor,
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment
        };
    }
}

/// <summary>
/// Image element for displaying images.
/// </summary>
public class ImageReportElement : ReportElementBase
{
    [JsonPropertyName("imagePath")]
    public string ImagePath
    {
        get;
        set => SetField(ref field, value);
    } = string.Empty;

    [JsonPropertyName("scaleMode")]
    public ImageScaleMode ScaleMode
    {
        get;
        set => SetField(ref field, value);
    } = ImageScaleMode.Fit;

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = "#00FFFFFF";

    [JsonPropertyName("borderColor")]
    public string BorderColor
    {
        get;
        set => SetField(ref field, value);
    } = "#00FFFFFF";

    [JsonPropertyName("borderThickness")]
    public int BorderThickness
    {
        get;
        set => SetField(ref field, value);
    } = 1;

    [JsonPropertyName("cornerRadiusPercent")]
    public int CornerRadiusPercent
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("opacity")]
    public int Opacity
    {
        get;
        set => SetField(ref field, Math.Clamp(value, 0, 100));
    } = 100;

    public override string DisplayName => "Image";
    public override ReportElementType GetElementType() => ReportElementType.Image;

    public override ReportElementBase Clone()
    {
        return new ImageReportElement
        {
            Id = Guid.NewGuid().ToString(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            PageNumber = PageNumber,
            IsVisible = IsVisible,
            ImagePath = ImagePath,
            ScaleMode = ScaleMode,
            BackgroundColor = BackgroundColor,
            BorderColor = BorderColor,
            BorderThickness = BorderThickness,
            CornerRadiusPercent = CornerRadiusPercent,
            Opacity = Opacity
        };
    }
}

/// <summary>
/// Date range element for displaying report date filters.
/// </summary>
public class DateRangeReportElement : ReportElementBase
{
    [JsonPropertyName("dateFormat")]
    public string DateFormat
    {
        get;
        set => SetField(ref field, value);
    } = "yyyy-MM-dd";

    [JsonPropertyName("textColor")]
    public string TextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.Black;

    [JsonPropertyName("fontSize")]
    public double FontSize
    {
        get;
        set => SetField(ref field, value);
    } = 13;

    [JsonPropertyName("isBold")]
    public bool IsBold
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("isItalic")]
    public bool IsItalic
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("isUnderline")]
    public bool IsUnderline
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("fontFamily")]
    public string FontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("horizontalAlignment")]
    public HorizontalTextAlignment HorizontalAlignment
    {
        get;
        set => SetField(ref field, value);
    } = HorizontalTextAlignment.Center;

    [JsonPropertyName("verticalAlignment")]
    public VerticalTextAlignment VerticalAlignment
    {
        get;
        set => SetField(ref field, value);
    } = VerticalTextAlignment.Center;

    /// <summary>
    /// When true, displays "As of [end date]" instead of "Period: X to Y".
    /// Used for point-in-time reports like Balance Sheet.
    /// </summary>
    [JsonPropertyName("isAsOfDate")]
    public bool IsAsOfDate
    {
        get;
        set => SetField(ref field, value);
    }

    public override string DisplayName => "Date Range";
    public override ReportElementType GetElementType() => ReportElementType.DateRange;

    public override ReportElementBase Clone()
    {
        return new DateRangeReportElement
        {
            Id = Guid.NewGuid().ToString(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            PageNumber = PageNumber,
            IsVisible = IsVisible,
            DateFormat = DateFormat,
            TextColor = TextColor,
            FontSize = FontSize,
            IsBold = IsBold,
            IsItalic = IsItalic,
            IsUnderline = IsUnderline,
            FontFamily = FontFamily,
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment,
            IsAsOfDate = IsAsOfDate
        };
    }
}

/// <summary>
/// Summary element for displaying statistics and key metrics.
/// </summary>
public class SummaryReportElement : ReportElementBase
{
    [JsonPropertyName("transactionType")]
    public TransactionType TransactionType
    {
        get;
        set => SetField(ref field, value);
    } = TransactionType.Revenue;

    [JsonPropertyName("includeReturns")]
    public bool IncludeReturns
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("includeLosses")]
    public bool IncludeLosses
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showTotalRevenue")]
    public bool ShowTotalRevenue
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showTotalTransactions")]
    public bool ShowTotalTransactions
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showAverageValue")]
    public bool ShowAverageValue
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("showGrowthRate")]
    public bool ShowGrowthRate
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportBackground;

    [JsonPropertyName("borderThickness")]
    public int BorderThickness
    {
        get;
        set => SetField(ref field, value);
    } = 1;

    [JsonPropertyName("borderColor")]
    public string BorderColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportBorder;

    [JsonPropertyName("fontFamily")]
    public string FontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("fontSize")]
    public double FontSize
    {
        get;
        set => SetField(ref field, value);
    } = 10;

    [JsonPropertyName("horizontalAlignment")]
    public HorizontalTextAlignment HorizontalAlignment
    {
        get;
        set => SetField(ref field, value);
    } = HorizontalTextAlignment.Left;

    [JsonPropertyName("verticalAlignment")]
    public VerticalTextAlignment VerticalAlignment
    {
        get;
        set => SetField(ref field, value);
    } = VerticalTextAlignment.Top;

    public override string DisplayName => "Summary";
    public override ReportElementType GetElementType() => ReportElementType.Summary;

    public override ReportElementBase Clone()
    {
        return new SummaryReportElement
        {
            Id = Guid.NewGuid().ToString(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            PageNumber = PageNumber,
            IsVisible = IsVisible,
            TransactionType = TransactionType,
            IncludeReturns = IncludeReturns,
            IncludeLosses = IncludeLosses,
            ShowTotalRevenue = ShowTotalRevenue,
            ShowTotalTransactions = ShowTotalTransactions,
            ShowAverageValue = ShowAverageValue,
            ShowGrowthRate = ShowGrowthRate,
            BackgroundColor = BackgroundColor,
            BorderThickness = BorderThickness,
            BorderColor = BorderColor,
            FontFamily = FontFamily,
            FontSize = FontSize,
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment
        };
    }
}

/// <summary>
/// Accounting table element for structured financial reports (Balance Sheet, P&amp;L, etc.).
/// </summary>
public class AccountingTableReportElement : ReportElementBase
{
    [JsonPropertyName("reportType")]
    public AccountingReportType ReportType
    {
        get;
        set => SetField(ref field, value);
    } = AccountingReportType.IncomeStatement;

    [JsonPropertyName("fontSize")]
    public double FontSize
    {
        get;
        set => SetField(ref field, value);
    } = 10;

    [JsonPropertyName("fontFamily")]
    public string FontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("headerFontSize")]
    public double HeaderFontSize
    {
        get;
        set => SetField(ref field, value);
    } = 10;

    [JsonPropertyName("headerFontFamily")]
    public string HeaderFontFamily
    {
        get;
        set => SetField(ref field, value);
    } = "Segoe UI";

    [JsonPropertyName("titleFontSize")]
    public double TitleFontSize
    {
        get;
        set => SetField(ref field, value);
    } = 12;

    [JsonPropertyName("headerBackgroundColor")]
    public string HeaderBackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportSlate;

    [JsonPropertyName("headerTextColor")]
    public string HeaderTextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.White;

    [JsonPropertyName("sectionHeaderBackgroundColor")]
    public string SectionHeaderBackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportSlateLight;

    [JsonPropertyName("sectionHeaderTextColor")]
    public string SectionHeaderTextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportSlate;

    [JsonPropertyName("subtotalBackgroundColor")]
    public string SubtotalBackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.NearWhite;

    [JsonPropertyName("totalBackgroundColor")]
    public string TotalBackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportSlate;

    [JsonPropertyName("totalTextColor")]
    public string TotalTextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.White;

    [JsonPropertyName("dataRowTextColor")]
    public string DataRowTextColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportSlate;

    [JsonPropertyName("gridLineColor")]
    public string GridLineColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.ReportSilver;

    [JsonPropertyName("baseRowColor")]
    public string BaseRowColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.White;

    [JsonPropertyName("alternateRowColor")]
    public string AlternateRowColor
    {
        get;
        set => SetField(ref field, value);
    } = AppColors.NearWhite;

    [JsonPropertyName("dataRowHeight")]
    public double DataRowHeight
    {
        get;
        set => SetField(ref field, value);
    } = 18;

    [JsonPropertyName("headerRowHeight")]
    public double HeaderRowHeight
    {
        get;
        set => SetField(ref field, value);
    } = 22;

    [JsonPropertyName("cellPadding")]
    public double CellPadding
    {
        get;
        set => SetField(ref field, value);
    } = 4;

    [JsonPropertyName("showGridLines")]
    public bool ShowGridLines
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [JsonPropertyName("alternateRowColors")]
    public bool AlternateRowColors
    {
        get;
        set => SetField(ref field, value);
    }

    [JsonPropertyName("indentWidth")]
    public double IndentWidth
    {
        get;
        set => SetField(ref field, value);
    } = 20;

    /// <summary>
    /// Custom column width ratios set by the user via drag-to-resize in the designer.
    /// When null, the default ratios from the data service are used. Values should sum to 1.0.
    /// </summary>
    [JsonPropertyName("columnWidthRatios")]
    public List<double>? ColumnWidthRatios
    {
        get;
        set => SetField(ref field, value);
    }

    public override double MinimumSize => 200;
    public override string DisplayName => "Accounting Table";
    public override ReportElementType GetElementType() => ReportElementType.AccountingTable;

    public override ReportElementBase Clone()
    {
        return new AccountingTableReportElement
        {
            Id = Guid.NewGuid().ToString(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            PageNumber = PageNumber,
            IsVisible = IsVisible,
            ReportType = ReportType,
            FontSize = FontSize,
            FontFamily = FontFamily,
            HeaderFontSize = HeaderFontSize,
            HeaderFontFamily = HeaderFontFamily,
            TitleFontSize = TitleFontSize,
            HeaderBackgroundColor = HeaderBackgroundColor,
            HeaderTextColor = HeaderTextColor,
            SectionHeaderBackgroundColor = SectionHeaderBackgroundColor,
            SectionHeaderTextColor = SectionHeaderTextColor,
            SubtotalBackgroundColor = SubtotalBackgroundColor,
            TotalBackgroundColor = TotalBackgroundColor,
            TotalTextColor = TotalTextColor,
            DataRowTextColor = DataRowTextColor,
            GridLineColor = GridLineColor,
            BaseRowColor = BaseRowColor,
            AlternateRowColor = AlternateRowColor,
            DataRowHeight = DataRowHeight,
            HeaderRowHeight = HeaderRowHeight,
            CellPadding = CellPadding,
            ShowGridLines = ShowGridLines,
            AlternateRowColors = AlternateRowColors,
            IndentWidth = IndentWidth,
            ColumnWidthRatios = ColumnWidthRatios?.ToList()
        };
    }
}
