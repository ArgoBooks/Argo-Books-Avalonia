using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ClosedXML.Excel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Recognises a workbook laid out the way Argo Books exports one, so it can be imported straight
/// away instead of asking the AI what its sheets and columns mean.
/// </summary>
public static class ExportLayoutMatcher
{
    private static readonly HashSet<SpreadsheetSheetType> Exported =
    [
        SpreadsheetSheetType.Customers, SpreadsheetSheetType.Invoices, SpreadsheetSheetType.InvoiceLineItems,
        SpreadsheetSheetType.Expenses, SpreadsheetSheetType.Products, SpreadsheetSheetType.Inventory,
        SpreadsheetSheetType.Payments, SpreadsheetSheetType.Suppliers, SpreadsheetSheetType.Revenue,
        SpreadsheetSheetType.RentalInventory, SpreadsheetSheetType.RentalRecords, SpreadsheetSheetType.Categories,
        SpreadsheetSheetType.Locations, SpreadsheetSheetType.RecurringInvoices, SpreadsheetSheetType.StockAdjustments,
        SpreadsheetSheetType.PurchaseOrders, SpreadsheetSheetType.PurchaseOrderLineItems, SpreadsheetSheetType.Employees,
        SpreadsheetSheetType.PayRuns, SpreadsheetSheetType.Assets, SpreadsheetSheetType.SalesTaxFilings,
    ];

    // Columns the export writes for people to read that the import schema doesn't list.
    private static readonly Dictionary<SpreadsheetSheetType, string[]> ExportedBeyondSchema = new()
    {
        [SpreadsheetSheetType.Invoices] = ["Customer Name"],
        [SpreadsheetSheetType.InvoiceLineItems] = ["Amount"],
        [SpreadsheetSheetType.Expenses] = ["Supplier Name"],
        [SpreadsheetSheetType.Inventory] = ["Available"],
        [SpreadsheetSheetType.Payments] = ["Customer Name"],
        [SpreadsheetSheetType.Revenue] = ["Customer Name"],
        [SpreadsheetSheetType.StockAdjustments] = ["Reference Number", "User ID", "Auto Generated"],
        [SpreadsheetSheetType.PurchaseOrders] = ["Supplier Name"],
        [SpreadsheetSheetType.Employees] = ["Ontario Dependants"],
        [SpreadsheetSheetType.Assets] = ["Depreciation to Date", "Book Value"],
    };

    // The export writes these, but the import leaves them out on purpose.
    private static readonly Dictionary<SpreadsheetSheetType, string> ExportOnly = new()
    {
        [SpreadsheetSheetType.PayRuns] = "Pay runs are not imported, since an approved run's figures stay as they were paid.",
        [SpreadsheetSheetType.SalesTaxFilings] = "Sales tax filings are not imported. They are recorded on the Sales Tax page.",
    };

    /// <summary>
    /// The analysis of a workbook whose every sheet has a name and columns the export uses, or
    /// null when any sheet is laid out some other way and needs the AI to read it.
    /// </summary>
    public static SpreadsheetAnalysisResult? Match(string filePath, string? country = null)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);

        var result = new SpreadsheetAnalysisResult { FileName = Path.GetFileName(filePath) };
        foreach (var worksheet in workbook.Worksheets)
        {
            var headers = SpreadsheetRowReader.GetHeaders(worksheet);
            if (headers.All(h => h.Length == 0))
                continue;

            var type = SpreadsheetSheetTypeExtensions.ParseSheetName(worksheet.Name);
            if (!Exported.Contains(type))
                return null;

            var sheet = new SheetAnalysis
            {
                SourceSheetName = worksheet.Name,
                DetectedType = type,
                Confidence = 1,
                Tier = ProcessingTier.Tier1_Mapping,
                RowCount = SpreadsheetRowReader.GetDataRows(worksheet, headers.Count).Count,
            };

            if (ExportOnly.TryGetValue(type, out var reason))
            {
                sheet.IsIncluded = false;
                sheet.UnsupportedReason = reason;
                result.Sheets.Add(sheet);
                continue;
            }

            if (ImportSchemaDefinition.GetSchemaForType(type, country) is not { } schema)
                return null;

            // Ordinal on purpose: the import reads columns by their exact names.
            var known = schema.Select(c => c.Name)
                .Concat(ExportedBeyondSchema.GetValueOrDefault(type) ?? [])
                .ToHashSet(StringComparer.Ordinal);
            if (!headers.All(known.Contains))
                return null;

            sheet.ColumnMappings = headers
                .Select(h => new ColumnMapping { SourceColumn = h, TargetColumn = h, Confidence = 1 })
                .ToList();
            result.Sheets.Add(sheet);
        }

        return result.Sheets.Any(s => s.IsIncluded) ? result : null;
    }
}
