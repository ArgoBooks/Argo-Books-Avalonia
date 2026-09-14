using System.Globalization;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for exporting company data to spreadsheet formats (xlsx, csv, pdf).
/// </summary>
public class SpreadsheetExportService
{
    private string? _country;

    /// <summary>
    /// Exports selected data to an Excel file.
    /// </summary>
    public async Task ExportToExcelAsync(
        string filePath,
        CompanyData companyData,
        List<string> selectedDataItems,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(companyData);
        ArgumentNullException.ThrowIfNull(selectedDataItems);

        _country = companyData.Settings.Company.Country;

        // Building the workbook runs off the calling thread, not just saving it. Composing a
        // sheet per data type for a whole company is where the seconds go, and doing that on the
        // UI thread meant the caller's loading overlay was set and then never painted: the
        // window simply froze until the file appeared. The PDF export below has always worked
        // this way.
        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();

            foreach (var dataItem in selectedDataItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddWorksheet(workbook, dataItem, companyData, startDate, endDate);
            }

            // Ensure at least one worksheet exists
            if (workbook.Worksheets.Count == 0)
            {
                var ws = workbook.Worksheets.Add("Empty");
                ws.Cell(1, 1).Value = "No data selected for export";
            }

            workbook.SaveAs(filePath);
        }, cancellationToken);
    }

    /// <summary>
    /// Exports selected data to a CSV file.
    /// </summary>
    public async Task ExportToCsvAsync(
        string filePath,
        CompanyData companyData,
        List<string> selectedDataItems,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(companyData);
        ArgumentNullException.ThrowIfNull(selectedDataItems);

        _country = companyData.Settings.Company.Country;

        // Off the calling thread for the same reason as the Excel export above: the text is
        // built a row at a time out of the whole company, and the caller cannot paint a loading
        // overlay while that happens on its own thread.
        string csv = await Task.Run(() =>
        {
            var sb = new StringBuilder();

            foreach (var dataItem in selectedDataItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendCsvSection(sb, dataItem, companyData, startDate, endDate);
            }

            return sb.ToString();
        }, cancellationToken);

        await File.WriteAllTextAsync(filePath, csv, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Exports selected data to a PDF file.
    /// </summary>
    public async Task ExportToPdfAsync(
        string filePath,
        CompanyData companyData,
        List<string> selectedDataItems,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(companyData);
        ArgumentNullException.ThrowIfNull(selectedDataItems);

        _country = companyData.Settings.Company.Country;

        QuestPDF.Settings.License = LicenseType.Community;

        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Text($"{companyData.Settings.Company.Name} - Data Export")
                        .SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);

                    page.Content().Column(column =>
                    {
                        column.Spacing(20);

                        foreach (var dataItem in selectedDataItems)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            AddPdfSection(column, dataItem, companyData, startDate, endDate);
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(filePath);
        }, cancellationToken);
    }

    private void AddWorksheet(XLWorkbook workbook, string dataItem, CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var (headers, rows) = GetExportData(dataItem, data, startDate, endDate);
        if (headers.Length == 0) return;

        var ws = workbook.Worksheets.Add(TruncateSheetName(dataItem));

        // Add headers
        for (int col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Add data rows
        for (int row = 0; row < rows.Count; row++)
        {
            for (int col = 0; col < rows[row].Length; col++)
            {
                var value = rows[row][col];
                var cell = ws.Cell(row + 2, col + 1);

                if (value is decimal d)
                {
                    cell.Value = d;
                    cell.Style.NumberFormat.Format = "#,##0.00";
                }
                else if (value is DateTime dt)
                {
                    // DateTime.MinValue is the "no date" sentinel (e.g. an un-returned rental's Return
                    // Date is exported as `ReturnDate ?? DateTime.MinValue`). Leave the cell blank
                    // instead of writing a bogus 0001/1899 date; mirrors FormatValue (CSV/PDF path).
                    if (dt != DateTime.MinValue)
                    {
                        cell.Value = dt;
                        cell.Style.NumberFormat.Format = "yyyy-MM-dd";
                    }
                }
                else if (value is int i)
                {
                    cell.Value = i;
                }
                else
                {
                    cell.Value = value.ToString() ?? "";
                }
            }
        }

        // Auto-fit columns
        ws.Columns().AdjustToContents();
    }

    private void AppendCsvSection(StringBuilder sb, string dataItem, CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var (headers, rows) = GetExportData(dataItem, data, startDate, endDate);
        if (headers.Length == 0) return;

        // Section header
        sb.AppendLine($"# {dataItem}");

        // Column headers
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        // Data rows
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(v => EscapeCsv(FormatValue(v)))));
        }

        sb.AppendLine();
    }

    private void AddPdfSection(ColumnDescriptor column, string dataItem, CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var (headers, rows) = GetExportData(dataItem, data, startDate, endDate);
        if (headers.Length == 0) return;

        column.Item().Text(dataItem).SemiBold().FontSize(12);

        if (rows.Count == 0)
        {
            column.Item().Text("No records found").Italic().FontSize(9);
            return;
        }

        column.Item().Table(table =>
        {
            // Define columns
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in headers)
                {
                    columns.RelativeColumn();
                }
            });

            // Header row
            table.Header(header =>
            {
                foreach (var h in headers)
                {
                    header.Cell().Background(Colors.Blue.Lighten4)
                        .Padding(4).Text(h).SemiBold().FontSize(10);
                }
            });

            // Data rows (limit to avoid huge PDFs)
            var maxRows = Math.Min(rows.Count, 500);
            for (int i = 0; i < maxRows; i++)
            {
                var row = rows[i];
                var bgColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                foreach (var value in row)
                {
                    table.Cell().Background(bgColor)
                        .Padding(3).Text(FormatValue(value)).FontSize(10);
                }
            }

            if (rows.Count > maxRows)
            {
                for (int col = 0; col < headers.Length; col++)
                {
                    table.Cell().Background(Colors.Yellow.Lighten4)
                        .Padding(3).Text(col == 0 ? $"... and {rows.Count - maxRows} more records" : "").FontSize(10);
                }
            }
        });
    }

    private (string[] Headers, List<object[]> Rows) GetExportData(string dataItem, CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        return dataItem switch
        {
            "Customers" => GetCustomersData(data),
            "Invoices" => GetInvoicesData(data, startDate, endDate),
            "Invoice Line Items" => GetInvoiceLineItemsData(data, startDate, endDate),
            "Expenses" or "Purchases" => GetExpensesData(data, startDate, endDate),
            "Products" => GetProductsData(data),
            "Inventory" => GetInventoryData(data),
            "Payments" => GetPaymentsData(data, startDate, endDate),
            "Suppliers" => GetSuppliersData(data),
            "Revenue" or "Sales" => GetRevenueData(data, startDate, endDate),
            "Rental Inventory" => GetRentalInventoryData(data),
            "Rental Records" => GetRentalRecordsData(data, startDate, endDate),
            "Categories" => GetCategoriesData(data),
            "Locations" => GetLocationsData(data),
            "Recurring Invoices" => GetRecurringInvoicesData(data),
            "Stock Adjustments" => GetStockAdjustmentsData(data, startDate, endDate),
            "Purchase Orders" => GetPurchaseOrdersData(data, startDate, endDate),
            "Purchase Order Line Items" => GetPurchaseOrderLineItemsData(data, startDate, endDate),
            "Employees" => GetEmployeesData(data),
            "Pay Runs" => GetPayRunsData(data, startDate, endDate),
            _ => ([], [])
        };
    }

    /// <summary>
    /// Id to name for the sheets that print both.
    ///
    /// Tolerates a repeated id by keeping the last one rather than throwing, which
    /// <c>ToDictionary</c> would. A name column is a convenience and must never be the reason a
    /// whole export fails: the ids come from imported files as well as from this app.
    /// </summary>
    private static Dictionary<string, string> NameLookup<T>(
        IEnumerable<T> items, Func<T, string> id, Func<T, string> name)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (T item in items)
        {
            lookup[id(item)] = name(item);
        }

        return lookup;
    }

    /// <summary>
    /// The name for an id, or blank. Blank rather than the id repeated, so an empty cell reads
    /// as "nobody was recorded" and the reader looks at the id column beside it.
    /// </summary>
    private static string Named(Dictionary<string, string> lookup, string? id) =>
        id is { Length: > 0 } && lookup.TryGetValue(id, out string? name) ? name : "";

    private (string[] Headers, List<object[]> Rows) GetCustomersData(CompanyData data)
    {
        var (stateLabel, _, postalLabel, _) = ImportSchemaDefinition.GetAddressLabels(_country);
        var headers = new[] { "ID", "Name", "Company", "Email", "Phone", "Street", "City", stateLabel, postalLabel, "Country", "Notes", "Status", "Total Purchases" };
        var rows = data.Customers.Select(c => new object[]
        {
            c.Id,
            c.Name,
            c.CompanyName ?? "",
            c.Email,
            c.Phone,
            c.Address.Street,
            c.Address.City,
            c.Address.State,
            c.Address.ZipCode,
            c.Address.Country,
            c.Notes,
            c.Status.ToString(),
            c.TotalPurchases
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetInvoicesData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        // Who the invoice was for, beside the id that identifies them. Chasing an unpaid
        // invoice off this sheet meant looking CUS-003 up somewhere else first.
        var customerNames = NameLookup(data.Customers, c => c.Id, c => c.Name);

        // ID and Invoice # are two different values: INV-2026-00001 and #INV-2026-00001. Only
        // the first appears anywhere else, so exporting only the second left the payments sheet
        // pointing at invoice ids that were nowhere on the invoices sheet, and re-importing put
        // the display number in the id field and prefixed every invoice with a hash.
        var headers = new[] { "ID", "Invoice #", "Customer ID", "Customer Name", "Issue Date", "Due Date", "Subtotal", "Tax", "Total", "Paid", "Balance", "Status", "Currency" };
        var filtered = data.Invoices.Where(i => IsInDateRange(i.IssueDate, startDate, endDate));
        var rows = filtered.Select(i => new object[]
        {
            i.Id,
            i.InvoiceNumber,
            i.CustomerId,
            Named(customerNames, i.CustomerId),
            i.IssueDate,
            i.DueDate,
            i.Subtotal,
            i.TaxAmount,
            i.Total,
            i.AmountPaid,
            i.Balance,
            i.Status.ToString(),
            i.OriginalCurrency
        }).ToList();
        return (headers, rows);
    }

    /// <summary>
    /// What was actually on each invoice, one row per line.
    ///
    /// Modelled on the Purchase Order Line Items sheet, which had the same shape and the same
    /// reason to exist. Without it an invoice exports as a set of totals with nothing behind
    /// them, and re-importing produces an invoice with no lines at all.
    /// </summary>
    private (string[] Headers, List<object[]> Rows) GetInvoiceLineItemsData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var headers = new[] { "Invoice ID", "Product ID", "Description", "Quantity", "Unit Price", "Tax Rate", "Discount", "Amount" };
        var filtered = data.Invoices.Where(i => IsInDateRange(i.IssueDate, startDate, endDate));
        var rows = filtered
            .SelectMany(i => i.LineItems.Select(li => new object[]
            {
                i.Id,
                li.ProductId ?? "",
                li.Description,
                li.Quantity,
                li.UnitPrice,
                li.TaxRate,
                li.Discount,

                // Derived on the model rather than stored, so it is printed for reading and not
                // read back on import: quantity, price and discount are what rebuild the line.
                li.Amount
            }))
            .ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetExpensesData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        // The name sits beside the id so a sheet of scanned receipts can be read on its own,
        // without holding it next to the Suppliers sheet to find out who SUP-014 is.
        //
        // Presentation only. Supplier ID is still what identifies the supplier, and the Expenses
        // import schema does not carry a name column, so a re-imported export is unchanged.
        var supplierNames = NameLookup(data.Suppliers, s => s.Id, s => s.Name);

        var headers = new[] { "ID", "Date", "Supplier ID", "Supplier Name", "Product", "Quantity", "Unit Price", "Tax", "Shipping", "Total", "Reference", "Payment Method", "Currency" };
        var filtered = data.Expenses.Where(p => IsInDateRange(p.Date, startDate, endDate));
        var rows = filtered.Select(p => new object[]
        {
            p.Id,
            p.Date,
            p.SupplierId ?? "",
            Named(supplierNames, p.SupplierId),
            p.Description,
            p.Quantity,
            p.UnitPrice,
            p.TaxAmount,
            p.ShippingCost,
            p.Total,
            p.ReferenceNumber,
            p.PaymentMethod.ToString(),
            p.OriginalCurrency
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetProductsData(CompanyData data)
    {
        // Build lookup dictionaries for category and supplier names
        var categoryLookup = NameLookup(data.Categories, c => c.Id, c => c.Name);
        var supplierLookup = NameLookup(data.Suppliers, s => s.Id, s => s.Name);

        var headers = new[] { "ID", "Name", "Type", "Item Type", "SKU", "Description", "Category ID", "Category Name", "Supplier ID", "Supplier Name", "Reorder Point", "Overstock Threshold" };
        var rows = data.Products.Select(p => new object[]
        {
            p.Id,
            p.Name,
            p.Type switch
            {
                CategoryType.Revenue => "Revenue",
                CategoryType.Expense => "Expenses",
                _ => p.Type.ToString()
            },
            p.ItemType,
            p.Sku,
            p.Description,
            p.CategoryId ?? "",
            Named(categoryLookup, p.CategoryId),
            p.SupplierId ?? "",
            Named(supplierLookup, p.SupplierId),
            p.ReorderPoint,
            p.OverstockThreshold
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetInventoryData(CompanyData data)
    {
        var headers = new[] { "ID", "Product ID", "Location ID", "In Stock", "Reserved", "Available", "Reorder Point", "Unit Cost", "Last Updated" };
        var rows = data.Inventory.Select(i => new object[]
        {
            i.Id,
            i.ProductId,
            i.LocationId,
            i.InStock,
            i.Reserved,
            i.Available,
            i.ReorderPoint,
            i.UnitCost,
            i.LastUpdated
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetPaymentsData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        // Reconciling a bank statement against this sheet is the main use for it, and a column
        // of customer ids is the wrong thing to be holding a statement next to.
        var customerNames = NameLookup(data.Customers, c => c.Id, c => c.Name);

        var headers = new[] { "ID", "Invoice ID", "Customer ID", "Customer Name", "Date", "Amount", "Payment Method", "Reference", "Notes", "Currency" };
        var filtered = data.Payments.Where(p => IsInDateRange(p.Date, startDate, endDate));
        var rows = filtered.Select(p => new object[]
        {
            p.Id,
            p.InvoiceId,
            p.CustomerId,
            Named(customerNames, p.CustomerId),
            p.Date,
            p.Amount,
            p.PaymentMethod.ToString(),
            p.ReferenceNumber ?? "",
            p.Notes,
            p.OriginalCurrency
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetSuppliersData(CompanyData data)
    {
        var (stateLabel, _, postalLabel, _) = ImportSchemaDefinition.GetAddressLabels(_country);
        var headers = new[] { "ID", "Name", "Email", "Phone", "Website", "Street", "City", stateLabel, postalLabel, "Country", "Notes" };
        var rows = data.Suppliers.Select(s => new object[]
        {
            s.Id,
            s.Name,
            s.Email,
            s.Phone,
            s.Website,
            s.Address.Street,
            s.Address.City,
            s.Address.State,
            s.Address.ZipCode,
            s.Address.Country,
            s.Notes
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetRevenueData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        // The mirror of the supplier name on the expenses sheet, and presentation only for the
        // same reason: Customer ID identifies the customer, this just saves the cross-reference.
        var customerNames = NameLookup(data.Customers, c => c.Id, c => c.Name);

        // The same four columns the expenses sheet was missing, for the same reasons.
        // Invoice ID ties a revenue to the invoice it was collected on. Without it the invoice's
        // revenue came back as a stray sale and the importer made a second one for the invoice.
        var headers = new[] { "ID", "Date", "Customer ID", "Customer Name", "Invoice ID", "Product", "Quantity", "Unit Price", "Tax", "Shipping", "Total", "Reference", "Payment Status", "Kept Deposit", "Currency" };
        var filtered = data.Revenues.Where(s => IsInDateRange(s.Date, startDate, endDate));
        var rows = filtered.Select(s => new object[]
        {
            s.Id,
            s.Date,
            s.CustomerId ?? "",
            Named(customerNames, s.CustomerId),
            s.InvoiceId ?? "",
            s.Description,
            s.Quantity,
            s.UnitPrice,
            s.TaxAmount,
            s.ShippingCost,
            s.Total,
            s.ReferenceNumber,
            s.PaymentStatus.ToString(),
            s.IsKeptDeposit,
            s.OriginalCurrency
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetRentalInventoryData(CompanyData data)
    {
        var headers = new[] { "ID", "Inventory Item ID", "Daily Rate", "Weekly Rate", "Monthly Rate", "Deposit", "Status" };
        var rows = data.RentalInventory.Select(r => new object[]
        {
            r.Id,
            r.InventoryItemId,
            r.DailyRate,
            r.WeeklyRate,
            r.MonthlyRate,
            r.SecurityDeposit,
            r.Status.ToString()
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetRentalRecordsData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var headers = new[] { "ID", "Customer ID", "Rental Item ID", "Quantity", "Rate Type", "Rate Amount", "Security Deposit", "Start Date", "Due Date", "Return Date", "Total Cost", "Status", "Paid" };
        var filtered = data.Rentals.Where(r => IsInDateRange(r.StartDate, startDate, endDate));
        var rows = new List<object[]>();

        foreach (var r in filtered)
        {
            var lines = r.EffectiveLineItems();
            if (lines.Count > 0)
            {
                foreach (var li in lines)
                {
                    rows.Add(
                    [
                        r.Id, r.CustomerId, li.RentalItemId, li.Quantity,
                        li.RateType.ToString(), li.RateAmount, li.SecurityDeposit,
                        r.StartDate, r.DueDate, r.ReturnDate ?? DateTime.MinValue,
                        r.TotalCost ?? 0m, r.Status.ToString(), r.Paid ? "Yes" : "No"
                    ]);
                }
            }
            else
            {
                rows.Add(
                [
                    r.Id, r.CustomerId, r.RentalItemId, r.Quantity,
                    r.RateType.ToString(), r.RateAmount, r.SecurityDeposit,
                    r.StartDate, r.DueDate, r.ReturnDate ?? DateTime.MinValue,
                    r.TotalCost ?? 0m, r.Status.ToString(), r.Paid ? "Yes" : "No"
                ]);
            }
        }

        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetCategoriesData(CompanyData data)
    {
        var headers = new[] { "ID", "Name", "Type", "Parent ID", "Description", "Icon" };
        var rows = data.Categories.Select(c => new object[]
        {
            c.Id,
            c.Name,
            c.Type switch
            {
                CategoryType.Revenue => "Revenue",
                CategoryType.Expense => "Expenses",
                _ => c.Type.ToString()
            },
            c.ParentId ?? "",
            c.Description ?? "",
            c.Icon
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetLocationsData(CompanyData data)
    {
        var (stateLabel, _, postalLabel, _) = ImportSchemaDefinition.GetAddressLabels(_country);
        var headers = new[] { "ID", "Name", "Contact Person", "Phone", "Street", "City", stateLabel, postalLabel, "Country", "Capacity", "Utilization" };
        var rows = data.Locations.Select(l => new object[]
        {
            l.Id,
            l.Name,
            l.ContactPerson,
            l.Phone,
            l.Address.Street,
            l.Address.City,
            l.Address.State,
            l.Address.ZipCode,
            l.Address.Country,
            l.Capacity,
            l.CurrentUtilization
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetRecurringInvoicesData(CompanyData data)
    {
        var headers = new[] { "ID", "Customer ID", "Amount", "Description", "Frequency", "Next Date", "Status" };
        var rows = data.RecurringInvoices.Select(r => new object[]
        {
            r.Id,
            r.CustomerId,
            r.Amount,
            r.Description,
            r.Frequency.ToString(),
            r.NextInvoiceDate,
            r.Status
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetStockAdjustmentsData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var headers = new[] { "ID", "Inventory Item ID", "Type", "Quantity", "Previous Stock", "New Stock", "Reason", "Reference Number", "User ID", "Timestamp", "Auto Generated" };
        var filtered = data.StockAdjustments.Where(s => IsInDateRange(s.Timestamp, startDate, endDate));
        var rows = filtered.Select(s => new object[]
        {
            s.Id,
            s.InventoryItemId,
            s.AdjustmentType.ToString(),
            s.Quantity,
            s.PreviousStock,
            s.NewStock,
            s.Reason,
            s.ReferenceNumber ?? "",
            s.UserId ?? "",
            s.Timestamp,
            s.IsAutoGenerated
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetPurchaseOrdersData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var supplierNames = NameLookup(data.Suppliers, s => s.Id, s => s.Name);

        var headers = new[] { "ID", "Supplier ID", "Supplier Name", "Order Date", "Expected Date", "Total", "Status", "Currency" };
        var filtered = data.PurchaseOrders.Where(p => IsInDateRange(p.OrderDate, startDate, endDate));
        var rows = filtered.Select(p => new object[]
        {
            p.Id,
            p.SupplierId,
            Named(supplierNames, p.SupplierId),
            p.OrderDate,
            p.ExpectedDeliveryDate,
            p.Total,
            p.Status.ToString(),
            p.OriginalCurrency
        }).ToList();
        return (headers, rows);
    }

    private (string[] Headers, List<object[]> Rows) GetPurchaseOrderLineItemsData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var headers = new[] { "PO ID", "Product ID", "Quantity", "Unit Cost", "Quantity Received" };
        var filtered = data.PurchaseOrders.Where(p => IsInDateRange(p.OrderDate, startDate, endDate));
        var rows = filtered
            .SelectMany(p => p.LineItems.Select(li => new object[]
            {
                p.Id,
                li.ProductId,
                li.Quantity,
                li.UnitCost,
                li.QuantityReceived
            }))
            .ToList();
        return (headers, rows);
    }

    /// <summary>
    /// The payroll list, as an entity sheet like Customers and Suppliers.
    ///
    /// Includes the social insurance number, because without it the sheet cannot be used to set
    /// payroll up again and a T4 cannot be filed. It is the employer's own record of their own
    /// staff, which is what the rest of this export is too, but it is the most sensitive column
    /// the app writes and worth knowing is there before mailing a workbook to anyone.
    /// </summary>
    private (string[] Headers, List<object[]> Rows) GetEmployeesData(CompanyData data)
    {
        var (stateLabel, _, postalLabel, _) = ImportSchemaDefinition.GetAddressLabels(_country);

        var headers = new[]
        {
            "ID", "Name", "Employee #", "SIN", "Province of Employment", "Pay Type", "Pay Rate",
            "Pay Frequency", "Standard Hours Per Week", "Federal Claim Amount", "Provincial Claim Amount",
            "Federal Claims Zero", "Provincial Claims Zero", "Ontario Dependants", "CPP Exempt", "EI Exempt", "Dental Benefit", "Start Date", "End Date",
            "Street", "City", stateLabel, postalLabel, "Country", "Status", "Notes"
        };

        var rows = data.Employees.Select(e => new[]
        {
            e.Id,
            e.Name,
            e.EmployeeNumber,
            e.Sin,

            // Where they WORK, which picks the tax table. Their home province is in the address
            // columns and is not necessarily the same one.
            e.Province,
            e.PayType.ToString(),
            e.PayRate,
            e.PayFrequency.ToString(),

            // Blank rather than zero when unknown. Zero hours on a record of employment costs
            // the employee their claim, so the difference has to survive the trip.
            e.StandardHoursPerWeek.HasValue ? (object)e.StandardHoursPerWeek.Value : "",
            e.FederalClaimAmount,
            e.ProvincialClaimAmount,

            // A zero amount has always meant no TD1 was filed, so a form claiming nothing needs
            // its own column to survive the trip.
            e.FederalClaimIsZero,
            e.ProvincialClaimIsZero,

            // Ontario only, and zero everywhere else. Exported so a sheet round trip does not
            // quietly drop it.
            e.OntarioDependants,
            e.IsCppExempt,
            e.IsEiExempt,
            e.DentalBenefit.ToString(),
            e.StartDate ?? DateTime.MinValue,
            e.EndDate ?? DateTime.MinValue,
            e.Address.Street,
            e.Address.City,
            e.Address.State,
            e.Address.ZipCode,
            e.Address.Country,
            e.IsArchived ? "Archived" : "Active",
            e.Notes
        }).ToList();

        return (headers, rows);
    }

    /// <summary>
    /// The payroll register: one row per employee per pay run, with the run's own details
    /// repeated across its rows.
    ///
    /// Flat rather than split into a runs sheet and a lines sheet, because this is the shape an
    /// accountant asks for and the shape a pivot table wants. It is also why it is export only:
    /// see the note on <see cref="Models.Payroll.PayRun"/>. An approved run's figures are frozen
    /// so a stub reprinted next year still matches the one the employee was handed, and an
    /// importer that let those be typed over in a spreadsheet would defeat that.
    /// </summary>
    private (string[] Headers, List<object[]> Rows) GetPayRunsData(CompanyData data, DateTime? startDate, DateTime? endDate)
    {
        var headers = new[]
        {
            "Run ID", "Pay Date", "Period Start", "Period End", "Status", "Rate Edition", "Voids Run ID",
            "Employee ID", "Employee Name", "Province", "Pay Periods Per Year", "Hours Worked",
            "Base Pay", "Bonus", "Vacation Pay", "Gross Pay",
            "CPP", "CPP2", "EI", "QPIP", "Federal Tax", "Provincial Tax", "Net Pay",
            "Employer CPP", "Employer CPP2", "Employer EI", "Employer QPIP",
            "Total Remittance", "Total Cost", "Expense ID"
        };

        var filtered = data.PayRuns.Where(r => IsInDateRange(r.PayDate, startDate, endDate));

        var rows = filtered
            .SelectMany(run => run.Lines.Select(line => new object[]
            {
                run.Id,
                run.PayDate,
                run.PeriodStart,
                run.PeriodEnd,
                run.Status.ToString(),

                // Which CRA edition produced these figures. Two runs in the same year can use
                // different tables, so a register without it cannot be checked against anything.
                run.RateEditionId,
                run.VoidsPayRunId ?? "",
                line.EmployeeId,
                line.EmployeeName,
                line.Province,
                line.PayPeriodsPerYear,
                line.HoursWorked,
                line.BasePay,
                line.Bonus,
                line.VacationPay,
                line.GrossPay,
                line.CppEmployee,
                line.Cpp2Employee,
                line.EiEmployee,
                line.QpipEmployee,
                line.FederalTax,
                line.ProvincialTax,
                line.NetPay,
                line.CppEmployer,
                line.Cpp2Employer,
                line.EiEmployer,
                line.QpipEmployer,
                line.TotalRemittance,
                line.TotalCost,
                line.ExpenseId ?? ""
            }))
            .ToList();

        return (headers, rows);
    }

    private static bool IsInDateRange(DateTime date, DateTime? startDate, DateTime? endDate)
    {
        if (startDate.HasValue && date.Date < startDate.Value.Date)
            return false;
        if (endDate.HasValue && date.Date > endDate.Value.Date)
            return false;
        return true;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            DateTime dt => dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal d => d.ToString("F2", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    private static string EscapeCsv(string value) =>
        string.IsNullOrEmpty(value) ? "" : CsvWriter.QuoteField(value);

    private static string TruncateSheetName(string name)
    {
        // Excel worksheet names are limited to 31 characters
        if (name.Length > 31)
            return name[..31];
        return name;
    }
}
