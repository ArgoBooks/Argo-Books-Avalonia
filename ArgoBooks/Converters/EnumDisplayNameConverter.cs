using System.Globalization;
using ArgoBooks.Core.Enums;
using Avalonia.Data.Converters;

namespace ArgoBooks.Converters;

/// <summary>
/// Converter that returns a user-friendly display name for enum values.
/// </summary>
public class EnumDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            TransactionType transactionType => transactionType.GetDisplayName(),
            TableSortOrder sortOrder => sortOrder.GetDisplayName(),
            AccountingReportType accountingReportType => accountingReportType.GetDisplayName(),
            IncomeComparison comparison => comparison.GetDisplayName(),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converter that returns true if the transaction type supports Include Returns/Losses options.
/// </summary>
public class TransactionTypeSupportsReturnsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TransactionType transactionType && transactionType.SupportsReturnsAndLosses();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
