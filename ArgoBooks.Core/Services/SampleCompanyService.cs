using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Rentals;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for creating and managing sample company data.
/// Creates a temporary company populated with sample data from an Excel file.
/// </summary>
public class SampleCompanyService
{
    private readonly FileService _fileService;
    private readonly SpreadsheetImportService _importService;

    private const string SampleCompanyName = "TechFlow Solutions";

    /// <summary>
    /// How many biweekly pay runs the sample company gets.
    ///
    /// Kept low on purpose. Every run posts its wages to the books as real expenses, and they all
    /// land in the newest month, which is the month the dashboard opens on. Two is enough to show
    /// a history, a year-to-date figure that accumulates, and a remittance due.
    ///
    /// The Employees sheet is sized to match: three part-time staff, because the sample sells
    /// about $166,000 a year and no larger payroll fits inside that.
    /// </summary>
    private const int SamplePayPeriods = 2;

    /// <summary>
    /// Creates a new SampleCompanyService instance.
    /// </summary>
    public SampleCompanyService(FileService fileService, SpreadsheetImportService importService)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
    }

    /// <summary>
    /// Result of validating sample company data before import.
    /// </summary>
    public class SampleCompanyValidationContext
    {
        public ImportValidationResult ValidationResult { get; set; } = new();
        public string TempExcelPath { get; set; } = string.Empty;
        public string TempRoot { get; set; } = string.Empty;
        public CompanyData CompanyData { get; set; } = new();
    }

    /// <summary>
    /// Validates sample company data from the provided Excel stream.
    /// Call FinishSampleCompanyCreationAsync to complete import, or CleanupValidationContext to cancel.
    /// </summary>
    public async Task<SampleCompanyValidationContext> ValidateSampleCompanyAsync(
        Stream excelDataStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(excelDataStream);

        var context = new SampleCompanyValidationContext();

        // Create temporary directories
        context.TempRoot = Path.Combine(Path.GetTempPath(), "ArgoBooks", "Sample", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(context.TempRoot);

        context.TempExcelPath = Path.Combine(context.TempRoot, "SampleData.xlsx");

        await using (var fileStream = new FileStream(context.TempExcelPath, FileMode.Create, FileAccess.Write))
        {
            await excelDataStream.CopyToAsync(fileStream, cancellationToken);
        }

        // Create company data with sample company settings
        context.CompanyData = CreateSampleCompanyData();

        // Validate the import (same as regular imports)
        context.ValidationResult = await _importService.ValidateImportAsync(
            context.TempExcelPath,
            context.CompanyData,
            cancellationToken);

        return context;
    }

    /// <summary>
    /// Completes sample company creation after validation.
    /// </summary>
    public async Task<string> FinishSampleCompanyCreationAsync(
        SampleCompanyValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var companyDir = Path.Combine(context.TempRoot, SampleCompanyName);
        Directory.CreateDirectory(companyDir);

        try
        {
            // Import data from Excel with auto-create missing references
            var importOptions = new ImportOptions
            {
                AutoCreateMissingReferences = true
            };

            await _importService.ImportFromExcelAsync(
                context.TempExcelPath,
                context.CompanyData,
                importOptions,
                cancellationToken);

            // The spreadsheet importer auto-creates Revenue records for paid invoices,
            // but the XLSX Revenue sheet already contains all the financial data we need.
            // Remove these duplicates and properly link invoices to existing revenues instead.
            RemoveAutoCreatedInvoiceRevenues(context.CompanyData);

            // Patch rental records with financial data from rental items
            // (the XLSX may not include rate/deposit columns)
            PatchRentalRecordFinancials(context.CompanyData);

            // Link invoices to existing revenue records by customer + closest date.
            LinkInvoicesToRevenues(context.CompanyData);

            // Add additional active rental records for richer sample data
            // Use the data's max date as the reference point (not DateTime.Today)
            // so that TimeShiftSampleData can shift these dates along with everything else.
            var maxDate = FindMaxDate(context.CompanyData);
            if (maxDate == DateTime.MinValue)
                maxDate = DateTime.Today;
            AddSampleActiveRentals(context.CompanyData, maxDate);

            // After the rentals, and off the same reference date, so the pay dates land inside
            // the window the rest of the sample already occupies.
            AddSamplePayroll(context.CompanyData, maxDate);

            // Save company data to temp directory
            await _fileService.SaveCompanyDataAsync(companyDir, context.CompanyData, cancellationToken);

            // Create receipts subdirectory
            Directory.CreateDirectory(Path.Combine(companyDir, "receipts"));

            var sampleFilePath = GetSampleCompanyPath();

            // Ensure directory exists
            var sampleDir = Path.GetDirectoryName(sampleFilePath);
            if (!string.IsNullOrEmpty(sampleDir))
            {
                Directory.CreateDirectory(sampleDir);
            }

            // Save as unencrypted .argo file
            await _fileService.SaveCompanyAsync(sampleFilePath, context.TempRoot, password: null, cancellationToken);

            return sampleFilePath;
        }
        finally
        {
            CleanupValidationContext(context);
        }
    }

    /// <summary>
    /// Cleans up temporary files from validation (call if user cancels).
    /// </summary>
    public static void CleanupValidationContext(SampleCompanyValidationContext context)
    {
        if (File.Exists(context.TempExcelPath))
        {
            try { File.Delete(context.TempExcelPath); }
            catch { /* Best effort */ }
        }

        if (Directory.Exists(context.TempRoot))
        {
            try { Directory.Delete(context.TempRoot, recursive: true); }
            catch { /* Best effort */ }
        }
    }

    /// <summary>
    /// Creates the sample company data with default settings.
    /// </summary>
    private static CompanyData CreateSampleCompanyData()
    {
        var companyData = new CompanyData();

        // Set company information
        companyData.Settings.Company = new CompanyInfo
        {
            Name = SampleCompanyName,
            BusinessType = "Retail & Services",
            Industry = "Technology",
            Email = "example@samplecompany.com",
            Phone = "+1 5551000000",
            Country = "Canada"
        };

        companyData.Settings.AppVersion = AppInfo.VersionNumber;

        // Set localization defaults
        companyData.Settings.Localization = new LocalizationSettings
        {
            Language = "English",
            Currency = "CAD",
            DateFormat = "MM/DD/YYYY"
        };

        return companyData;
    }

    /// <summary>
    /// Gets the path where the sample company file is stored.
    /// </summary>
    public static string GetSampleCompanyPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "ArgoBooks",
            "SampleCompany.argo");
    }

    /// <summary>
    /// Cleans up any leftover sample company files.
    /// </summary>
    public static void CleanupSampleCompanyFiles()
    {
        var sampleFilePath = GetSampleCompanyPath();

        if (File.Exists(sampleFilePath))
        {
            try { File.Delete(sampleFilePath); }
            catch { /* Best effort */ }
        }
    }

    /// <summary>
    /// Patches rental records that are missing financial data (RateAmount, SecurityDeposit)
    /// by looking up the associated rental item's rates.
    /// </summary>
    private static void PatchRentalRecordFinancials(CompanyData data)
    {
        foreach (var record in data.Rentals)
        {
            // Patch line items with missing rates/deposits
            foreach (var li in record.LineItems)
            {
                if (li.RateAmount == 0 || li.SecurityDeposit == 0)
                {
                    var liItem = data.RentalInventory.FirstOrDefault(i => i.Id == li.RentalItemId);
                    if (liItem == null) continue;

                    if (li.RateAmount == 0)
                    {
                        li.RateAmount = li.RateType switch
                        {
                            RateType.Weekly => liItem.WeeklyRate,
                            RateType.Monthly => liItem.MonthlyRate,
                            _ => liItem.DailyRate
                        };
                    }
                    if (li.SecurityDeposit == 0)
                        li.SecurityDeposit = liItem.SecurityDeposit;
                }
            }

            // Patch top-level fields
            if (record.RateAmount == 0 || record.SecurityDeposit == 0)
            {
                var item = data.RentalInventory.FirstOrDefault(i => i.Id == record.RentalItemId);
                if (item != null)
                {
                    if (record.RateAmount == 0)
                    {
                        record.RateAmount = record.RateType switch
                        {
                            RateType.Weekly => item.WeeklyRate,
                            RateType.Monthly => item.MonthlyRate,
                            _ => item.DailyRate
                        };
                    }

                    if (record.SecurityDeposit == 0)
                    {
                        record.SecurityDeposit = item.SecurityDeposit;
                    }
                }
            }

            // Mark returned rentals as paid and set deposit refunded
            if (record.Status == RentalStatus.Returned)
            {
                record.Paid = true;
                record.DepositRefunded ??= record.SecurityDeposit;
            }
        }
    }

    /// <summary>
    /// Removes Revenue records that were auto-created by SpreadsheetImportService.ImportInvoices().
    /// The XLSX Revenue sheet already contains the authoritative financial data; the auto-created
    /// records are duplicates that inflate chart totals.
    /// </summary>
    private static void RemoveAutoCreatedInvoiceRevenues(CompanyData data)
    {
        data.Revenues.RemoveAll(r =>
            r.Notes.Contains("Auto-created from imported invoice"));
    }

    /// <summary>
    /// Links finalized invoices to existing revenue records by matching customer + closest date.
    /// Does NOT create new revenue records: the Revenue sheet is the single source of truth.
    /// </summary>
    private static void LinkInvoicesToRevenues(CompanyData data)
    {
        var finalizedStatuses = new[]
        {
            InvoiceStatus.Sent, InvoiceStatus.Paid, InvoiceStatus.Partial, InvoiceStatus.Pending
        };

        var matchedRevenueIds = new HashSet<string>();

        foreach (var invoice in data.Invoices.Where(i => finalizedStatuses.Contains(i.Status)))
        {
            // Skip if this invoice already has a linked revenue
            if (data.Revenues.Any(r => r.InvoiceId == invoice.Id))
                continue;

            // Find the closest unlinked revenue for the same customer by date
            var matchingRevenue = data.Revenues
                .Where(r =>
                    r.InvoiceId == null &&
                    !matchedRevenueIds.Contains(r.Id) &&
                    r.CustomerId == invoice.CustomerId)
                .OrderBy(r => Math.Abs((r.Date - invoice.IssueDate).TotalDays))
                .FirstOrDefault();

            if (matchingRevenue != null)
            {
                matchingRevenue.InvoiceId = invoice.Id;
                matchedRevenueIds.Add(matchingRevenue.Id);
            }
            // If no match found, skip, don't create duplicate revenues
        }
    }

    /// <summary>
    /// Adds additional active rental records to the sample company for richer demo data.
    /// </summary>
    private static void AddSampleActiveRentals(CompanyData data, DateTime referenceDate)
    {
        var items = data.RentalInventory;
        var customers = data.Customers;
        if (items.Count == 0 || customers.Count == 0) return;

        var today = referenceDate;
        var maxId = data.Rentals
            .Select(r =>
            {
                var parts = r.Id.Split('-');
                return parts.Length > 0 && int.TryParse(parts[^1], out var n) ? n : 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        var newRentals = new List<RentalRecord>();

        // Pick items and customers for new rentals
        var item0 = items[0];
        var item1 = items.Count > 1 ? items[1] : items[0];
        var item2 = items.Count > 2 ? items[2] : items[0];

        var cust0 = customers[0];
        var cust1 = customers.Count > 1 ? customers[1] : customers[0];
        var cust2 = customers.Count > 2 ? customers[2] : customers[0];
        var cust3 = customers.Count > 3 ? customers[3] : customers[0];

        // Active rental 1 - started 5 days ago, due in 9 days, daily rate (single item)
        newRentals.Add(new RentalRecord
        {
            Id = $"RNT-{++maxId:D3}",
            RentalItemId = item0.Id,
            CustomerId = cust0.Id,
            Quantity = 1,
            RateType = RateType.Daily,
            RateAmount = item0.DailyRate,
            SecurityDeposit = item0.SecurityDeposit,
            LineItems = [new RentalLineItem { RentalItemId = item0.Id, Quantity = 1, RateType = RateType.Daily, RateAmount = item0.DailyRate, SecurityDeposit = item0.SecurityDeposit }],
            StartDate = today.AddDays(-5),
            DueDate = today.AddDays(9),
            Status = RentalStatus.Active,
            Paid = false,
            CreatedAt = referenceDate,
            UpdatedAt = referenceDate
        });

        // Active rental 2 - started 2 days ago, due in 5 days, multi-item (2 different items)
        var multiQty = 1 + 1;
        var multiDeposit = item0.SecurityDeposit + item1.SecurityDeposit;
        newRentals.Add(new RentalRecord
        {
            Id = $"RNT-{++maxId:D3}",
            RentalItemId = item0.Id,
            CustomerId = cust1.Id,
            Quantity = multiQty,
            RateType = RateType.Daily,
            RateAmount = item0.DailyRate,
            SecurityDeposit = multiDeposit,
            LineItems =
            [
                new RentalLineItem { RentalItemId = item0.Id, Quantity = 1, RateType = RateType.Daily, RateAmount = item0.DailyRate, SecurityDeposit = item0.SecurityDeposit },
                new RentalLineItem { RentalItemId = item1.Id, Quantity = 1, RateType = RateType.Daily, RateAmount = item1.DailyRate, SecurityDeposit = item1.SecurityDeposit }
            ],
            StartDate = today.AddDays(-2),
            DueDate = today.AddDays(5),
            Status = RentalStatus.Active,
            Paid = false,
            CreatedAt = referenceDate,
            UpdatedAt = referenceDate
        });

        // Active rental 3 - started 10 days ago, due in 4 days, weekly rate (single item)
        newRentals.Add(new RentalRecord
        {
            Id = $"RNT-{++maxId:D3}",
            RentalItemId = item2.Id,
            CustomerId = cust2.Id,
            Quantity = 1,
            RateType = RateType.Weekly,
            RateAmount = item2.WeeklyRate,
            SecurityDeposit = item2.SecurityDeposit,
            LineItems = [new RentalLineItem { RentalItemId = item2.Id, Quantity = 1, RateType = RateType.Weekly, RateAmount = item2.WeeklyRate, SecurityDeposit = item2.SecurityDeposit }],
            StartDate = today.AddDays(-10),
            DueDate = today.AddDays(4),
            Status = RentalStatus.Active,
            Paid = false,
            CreatedAt = referenceDate,
            UpdatedAt = referenceDate
        });

        // Active rental 4 - overdue, started 20 days ago, due 3 days ago (single item)
        newRentals.Add(new RentalRecord
        {
            Id = $"RNT-{++maxId:D3}",
            RentalItemId = item0.Id,
            CustomerId = cust3.Id,
            Quantity = 1,
            RateType = RateType.Weekly,
            RateAmount = item0.WeeklyRate,
            SecurityDeposit = item0.SecurityDeposit,
            LineItems = [new RentalLineItem { RentalItemId = item0.Id, Quantity = 1, RateType = RateType.Weekly, RateAmount = item0.WeeklyRate, SecurityDeposit = item0.SecurityDeposit }],
            StartDate = today.AddDays(-20),
            DueDate = today.AddDays(-3),
            Status = RentalStatus.Active,
            Paid = false,
            CreatedAt = referenceDate,
            UpdatedAt = referenceDate
        });

        // Reserved - starts in 3 days, due in 6 days, so its stock is still on the shelf
        newRentals.Add(new RentalRecord
        {
            Id = $"RNT-{++maxId:D3}",
            RentalItemId = item1.Id,
            CustomerId = cust2.Id,
            Quantity = 1,
            RateType = RateType.Daily,
            RateAmount = item1.DailyRate,
            SecurityDeposit = item1.SecurityDeposit,
            LineItems = [new RentalLineItem { RentalItemId = item1.Id, Quantity = 1, RateType = RateType.Daily, RateAmount = item1.DailyRate, SecurityDeposit = item1.SecurityDeposit }],
            StartDate = today.AddDays(3),
            DueDate = today.AddDays(6),
            Status = RentalStatus.Reserved,
            Paid = false,
            CreatedAt = referenceDate,
            UpdatedAt = referenceDate
        });

        foreach (var rental in newRentals)
        {
            data.Rentals.Add(rental);
        }

        // Update the ID counter
        data.IdCounters.Rental = maxId;
    }

    /// <summary>
    /// Gives the sample company a payroll history.
    ///
    /// Employees usually come from the workbook's own Employees sheet, which carries names,
    /// salaries and hire dates but nothing payroll-specific, so those are topped up here. Only
    /// when the sheet produced none does this invent its own three.
    ///
    /// The runs are built through <see cref="PayrollService"/> rather than written out by hand,
    /// so every figure is real CRA arithmetic: the year-to-date totals accumulate, the CPP and EI
    /// ceilings apply, and the four cards on the Pay runs page add up to what the pay stubs say.
    /// Hand-written figures would have to be redone every time the rates changed, and would be
    /// wrong in exactly the way a sample is never inspected closely enough to catch.
    ///
    /// A run is skipped when no rate edition covers its pay date. That happens when the app is
    /// run long after these editions shipped, and the sensible answer is a sample company with
    /// employees and no pay runs rather than a crash or invented numbers.
    /// </summary>
    internal static void AddSamplePayroll(CompanyData data, DateTime referenceDate)
    {
        // Keyed on pay runs, not employees. The sample workbook has an Employees sheet, so this
        // used to see eight imported employees, decide payroll was already set up, and add
        // nothing at all.
        if (data.PayRuns.Count > 0)
        {
            return;
        }

        var company = data.Settings.Company;

        // Format-valid and plainly not real, so year end and the T4 export run end to end
        // instead of stopping at the account number validation.
        company.PayrollAccountNumber = "123456789RP0001";
        company.PayrollContactName = "Alex Morgan";
        company.PayrollContactPhone = "5551000000";
        company.PayrollContactEmail = "payroll@samplecompany.com";
        company.RemitterType = RemitterType.Regular;

        DateTime hired = referenceDate.AddYears(-2);

        if (data.Employees.Count == 0)
        {
            data.Employees.AddRange(
            [
                SampleEmployee("EMP-001", "Sarah Chen", "111111118", "ON", hired,
                    "118 Bay Street", "Toronto", "M5J2N8",
                    PayType.Salary, 30000m),

                SampleEmployee("EMP-002", "Marcus Bell", "222222226", "ON", hired.AddMonths(7),
                    "47 King Street East", "Hamilton", "L8N1A9",
                    PayType.Hourly, 24.00m),

                SampleEmployee("EMP-003", "Priya Raman", "333333334", "BC", hired.AddMonths(14),
                    "900 Granville Street", "Vancouver", "V6Z1K3",
                    PayType.Salary, 32000m),
            ]);
        }
        else
        {
            CompletePayrollDetails(data.Employees, hired);
        }

        var payroll = new PayrollService();

        // Oldest first, because each run's deductions depend on the year-to-date the ones before
        // it produced. Building them newest first would put everybody at a zero starting point
        // and quietly under-deduct against the annual ceilings.
        for (int period = SamplePayPeriods - 1; period >= 0; period--)
        {
            DateTime payDate = referenceDate.AddDays(-14 * period);

            PayRun? run = payroll.CreateDraft(data, payDate, payDate.AddDays(-13), payDate);
            if (run == null)
            {
                continue;
            }

            // An hourly employee's pay comes from hours entered on the run, so a draft leaves
            // them at nil until the hours are filled in.
            foreach (PayRunLine line in run.Lines)
            {
                Employee? employee = data.Employees.FirstOrDefault(e => e.Id == line.EmployeeId);

                // Part-time, matching what the Employees sheet says they are.
                if (employee?.PayType == PayType.Hourly)
                {
                    line.HoursWorked = 40m;
                }
            }

            payroll.Recalculate(data, run);

            // In the list before it is approved, so the next run's year-to-date can see it.
            data.PayRuns.Add(run);
            payroll.ApproveAndRecord(data, run);
        }
    }

    /// <summary>
    /// Fills in what a pay run needs and the Employees sheet does not carry.
    ///
    /// The sheet has names, salaries, frequencies and hire dates, but no province, social
    /// insurance number or address. Province decides which tax table applies, so without it a
    /// pay run cannot be calculated at all, and the other two are what a T4 is filed on.
    ///
    /// Only blanks are filled, so anything the spreadsheet did supply wins.
    /// </summary>
    private static void CompletePayrollDetails(List<Employee> employees, DateTime fallbackHireDate)
    {
        // Cycled rather than all one province, so the sample exercises more than a single tax
        // table. Quebec is left out on purpose: it brings QPP, QPIP and a second slip, which is a
        // lot of machinery to switch on in a file people open to look around.
        (string Province, string Street, string City, string PostalCode)[] places =
        [
            ("ON", "118 Bay Street", "Toronto", "M5J2N8"),
            ("BC", "900 Granville Street", "Vancouver", "V6Z1K3"),
            ("AB", "222 5 Avenue SW", "Calgary", "T2P0L4"),
        ];

        for (int i = 0; i < employees.Count; i++)
        {
            Employee employee = employees[i];
            (string province, string street, string city, string postalCode) = places[i % places.Length];

            if (string.IsNullOrWhiteSpace(employee.Province) || employee.Province == "AB")
            {
                employee.Province = province;
            }

            // Patterned so it reads as a placeholder at a glance, and nine digits so year end
            // does not warn about every employee in the sample.
            if (string.IsNullOrWhiteSpace(employee.Sin))
            {
                char digit = (char)('1' + (i % 9));
                employee.Sin = new string(digit, 9);
            }

            if (string.IsNullOrWhiteSpace(employee.Address.City))
            {
                employee.Address = new Models.Common.Address
                {
                    Street = street,
                    City = city,
                    State = province,
                    ZipCode = postalCode,
                    Country = "Canada",
                };
            }

            // Only for the record of employment, and only meaningful for salaried staff, whose
            // pay runs record no hours because none are entered.
            if (employee.PayType == PayType.Salary && employee.StandardHoursPerWeek is null or 0m)
            {
                employee.StandardHoursPerWeek = 37.5m;
            }

            employee.StartDate ??= fallbackHireDate;
        }
    }

    private static Employee SampleEmployee(
        string id, string name, string sin, string province, DateTime start,
        string street, string city, string postalCode,
        PayType payType, decimal payRate) => new()
        {
            Id = id,
            Name = name,
            Sin = sin,
            Province = province,
            PayType = payType,
            PayRate = payRate,
            PayFrequency = PayFrequency.Biweekly,

            // Only meaningful for the salaried two, and only for the record of employment, which
            // wants insurable hours a salaried run never records.
            StandardHoursPerWeek = payType == PayType.Salary ? 37.5m : null,

            StartDate = start,
            DentalBenefit = DentalBenefitCode.PayeeOnly,
            Address = new Models.Common.Address
            {
                Street = street,
                City = city,
                State = province,
                ZipCode = postalCode,
                Country = "Canada",
            },
            CreatedAt = start,
            UpdatedAt = start,
        };

    /// <summary>
    /// Time-shifts all dates in the sample data so that the most recent transaction
    /// appears as if it happened recently (within the last few days).
    /// This ensures the dashboard shows meaningful data regardless of when the sample was created.
    /// Should be called every time the sample company is opened.
    /// </summary>
    /// <param name="data">The company data to time-shift.</param>
    /// <returns>True if data was shifted, false if already current.</returns>
    public static bool TimeShiftSampleData(CompanyData data)
    {
        var maxDate = FindMaxDate(data);
        if (maxDate == DateTime.MinValue)
            return false;

        var targetDate = DateTime.Today.AddDays(-1);
        var offset = targetDate - maxDate.Date;

        if (Math.Abs(offset.TotalDays) <= 1)
            return false;

        ApplyDateOffset(data, offset);
        return true;
    }

    private static DateTime FindMaxDate(CompanyData data)
    {
        var dates = new List<DateTime>();

        // Consider all date-bearing records so the time-shift offset ensures
        // no records end up with future dates (which would be excluded by filters)
        dates.AddRange(data.Revenues.Select(s => s.Date));
        dates.AddRange(data.Expenses.Select(p => p.Date));
        dates.AddRange(data.Returns.Select(r => r.ReturnDate));
        dates.AddRange(data.LostDamaged.Select(ld => ld.DateDiscovered));
        dates.AddRange(data.Invoices.Select(i => i.IssueDate));
        dates.AddRange(data.Payments.Select(p => p.Date));
        dates.AddRange(data.Rentals.Select(r => r.StartDate));
        dates.AddRange(data.PurchaseOrders.Select(po => po.OrderDate));

        return dates.Count > 0 ? dates.Max() : DateTime.MinValue;
    }

    private static void ApplyDateOffset(CompanyData data, TimeSpan offset)
    {
        DateTime Shift(DateTime dt)
        {
            if (dt == DateTime.MinValue || dt.Year < 1900)
                return dt;
            try { return dt.Add(offset); }
            catch (ArgumentOutOfRangeException) { return dt; }
        }

        DateTime? ShiftNullable(DateTime? dt)
        {
            if (!dt.HasValue || dt.Value == DateTime.MinValue || dt.Value.Year < 1900)
                return dt;
            try { return dt.Value.Add(offset); }
            catch (ArgumentOutOfRangeException) { return dt; }
        }

        foreach (var revenue in data.Revenues)
        {
            revenue.Date = Shift(revenue.Date);
            revenue.CreatedAt = Shift(revenue.CreatedAt);
            revenue.UpdatedAt = Shift(revenue.UpdatedAt);
        }

        foreach (var expense in data.Expenses)
        {
            expense.Date = Shift(expense.Date);
            expense.CreatedAt = Shift(expense.CreatedAt);
            expense.UpdatedAt = Shift(expense.UpdatedAt);
        }

        foreach (var invoice in data.Invoices)
        {
            invoice.IssueDate = Shift(invoice.IssueDate);
            invoice.DueDate = Shift(invoice.DueDate);
            invoice.CreatedAt = Shift(invoice.CreatedAt);
            invoice.UpdatedAt = Shift(invoice.UpdatedAt);
        }

        foreach (var payment in data.Payments)
        {
            payment.Date = Shift(payment.Date);
            payment.CreatedAt = Shift(payment.CreatedAt);
        }

        foreach (var rental in data.Rentals)
        {
            rental.StartDate = Shift(rental.StartDate);
            rental.DueDate = Shift(rental.DueDate);
            rental.ReturnDate = ShiftNullable(rental.ReturnDate);
            rental.CreatedAt = Shift(rental.CreatedAt);
            rental.UpdatedAt = Shift(rental.UpdatedAt);
        }

        foreach (var ret in data.Returns)
        {
            ret.ReturnDate = Shift(ret.ReturnDate);
            ret.CreatedAt = Shift(ret.CreatedAt);
        }

        foreach (var receipt in data.Receipts)
        {
            receipt.Date = Shift(receipt.Date);
            receipt.CreatedAt = Shift(receipt.CreatedAt);
        }

        foreach (var ld in data.LostDamaged)
        {
            ld.DateDiscovered = Shift(ld.DateDiscovered);
            ld.CreatedAt = Shift(ld.CreatedAt);
        }

        foreach (var sa in data.StockAdjustments)
            sa.Timestamp = Shift(sa.Timestamp);

        foreach (var st in data.StockTransfers)
        {
            st.TransferDate = Shift(st.TransferDate);
            st.CreatedAt = Shift(st.CreatedAt);
            st.CompletedAt = ShiftNullable(st.CompletedAt);
        }

        foreach (var po in data.PurchaseOrders)
        {
            po.OrderDate = Shift(po.OrderDate);
            po.ExpectedDeliveryDate = Shift(po.ExpectedDeliveryDate);
            po.CreatedAt = Shift(po.CreatedAt);
            po.UpdatedAt = Shift(po.UpdatedAt);
        }

        foreach (var ri in data.RecurringInvoices)
        {
            ri.StartDate = Shift(ri.StartDate);
            ri.EndDate = ShiftNullable(ri.EndDate);
            ri.NextInvoiceDate = Shift(ri.NextInvoiceDate);
            ri.CreatedAt = Shift(ri.CreatedAt);
            ri.LastGeneratedAt = ShiftNullable(ri.LastGeneratedAt);
        }

        foreach (var customer in data.Customers)
            customer.LastTransactionDate = ShiftNullable(customer.LastTransactionDate);

        foreach (var item in data.RentalInventory)
        {
            item.CreatedAt = Shift(item.CreatedAt);
            item.UpdatedAt = Shift(item.UpdatedAt);
        }

        foreach (var product in data.Products)
        {
            product.CreatedAt = Shift(product.CreatedAt);
            product.UpdatedAt = Shift(product.UpdatedAt);
        }

        foreach (var item in data.Inventory)
            item.LastUpdated = Shift(item.LastUpdated);

        foreach (var employee in data.Employees)
        {
            employee.StartDate = ShiftNullable(employee.StartDate);
            employee.EndDate = ShiftNullable(employee.EndDate);
            employee.CreatedAt = Shift(employee.CreatedAt);
            employee.UpdatedAt = Shift(employee.UpdatedAt);
        }

        // The wage expenses these produced are in data.Expenses and were shifted above, so the
        // runs have to move by the same offset or a pay run and the expense it created would end
        // up on different days.
        //
        // Only the dates move. The deductions stay exactly as they were calculated, which means a
        // shift across a year boundary leaves figures worked out under the previous year's rates.
        // That is the same compromise every other record here makes, and re-running payroll on
        // open would be worse: the numbers on a sample pay stub would change under the reader.
        foreach (var run in data.PayRuns)
        {
            run.PayDate = Shift(run.PayDate);
            run.PeriodStart = Shift(run.PeriodStart);
            run.PeriodEnd = Shift(run.PeriodEnd);
            run.CreatedAt = Shift(run.CreatedAt);
            run.ApprovedAt = ShiftNullable(run.ApprovedAt);
        }
    }
}
