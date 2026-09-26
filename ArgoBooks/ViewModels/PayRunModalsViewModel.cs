using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// The three step pay run: pick the period and the people, enter the amounts, review and
/// approve.
///
/// The draft run is built on entering step 2 and recalculated on every amount change, so
/// step 3 is only ever displaying figures that already exist rather than computing them as
/// it renders.
/// </summary>
public partial class PayRunModalsViewModel : ViewModelBase
{
    /// <summary>
    /// One rate service for the whole modal, shared with the payroll service rather than
    /// constructed wherever a table is wanted.
    ///
    /// It matters because an edition can be downloaded mid-session. Each instance parses and
    /// caches the editions it finds, so a second instance would keep serving the editions that
    /// existed when it was built and quietly disagree with the one that just fetched a new one.
    /// </summary>
    private readonly PayrollRateService _rates = new();

    private readonly PayrollService _payroll;
    private PayRun? _draft;

    /// <summary>True while a rate download is in flight, so a rapid date change cannot start several.</summary>
    private bool _fetchingRates;

    public PayRunModalsViewModel() => _payroll = new PayrollService(_rates);

    [ObservableProperty]
    private bool _isRunModalOpen;

    /// <summary>1 = period, 2 = amounts, 3 = review.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(BackButtonText))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    private int _step = 1;

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;

    public string BackButtonText => Step == 1 ? "Cancel" : "Back";
    public string NextButtonText => Step == 3 ? "Approve" : "Next";

    #region Step 1: the period

    [ObservableProperty]
    private DateTimeOffset? _payDate = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodError))]
    private DateTimeOffset? _periodStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodError))]
    private DateTimeOffset? _periodEnd;

    /// <summary>
    /// Why the period will not do, or empty when it will.
    ///
    /// A backwards period calculates perfectly happily, which is why it needs catching here. The
    /// deduction arithmetic divides annual figures by the number of pay periods and never looks
    /// at the dates, so nothing downstream objects. It surfaces later, on a pay stub that reads
    /// as nonsense and in the 27 consecutive periods a record of employment is built from, where
    /// it costs somebody part of an EI claim.
    ///
    /// A single day period is allowed. It is a real thing, so the test is "ends before it
    /// starts" rather than "does not end after it starts".
    /// </summary>
    public string PeriodError =>
        PeriodStart is { } start && PeriodEnd is { } end && end.Date < start.Date
            ? "The period ends before it starts."
            : string.Empty;

    /// <summary>
    /// Which CRA edition the pay date falls in. Shown because three provinces carry different
    /// figures in each half of 2026, so a wrong pay date is a silent class of error.
    /// </summary>
    [ObservableProperty]
    private string _rateEditionNote = string.Empty;

    /// <summary>Set when no edition covers the pay date. Blocks the run rather than guessing.</summary>
    [ObservableProperty]
    private string _blockingError = string.Empty;

    public ObservableCollection<PayRunEmployeeSelection> SelectableEmployees { get; } = [];

    /// <summary>
    /// Why there is nobody to pay, or empty when there is. Two different sentences rather than
    /// one, because the fix is different: an employer with no employees has to add one, and an
    /// employer whose staff are all archived has to restore one. An empty list said neither and
    /// read like something had failed to load.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNobodyToPay))]
    private string _noEmployeesMessage = string.Empty;

    public bool HasNobodyToPay => NoEmployeesMessage.Length > 0;

    partial void OnPayDateChanged(DateTimeOffset? value)
    {
        RefreshRateEdition();
        PrefillPeriod();
    }

    #endregion

    #region Step 2: the amounts

    public ObservableCollection<PayRunAmountRow> AmountRows { get; } = [];

    [ObservableProperty]
    private string _totalGross = "$0.00";

    #endregion

    #region Step 3: the review

    public ObservableCollection<PayRunReviewRow> ReviewRows { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private string _totalNetPay = "$0.00";

    [ObservableProperty]
    private string _totalRemittance = "$0.00";

    [ObservableProperty]
    private string _totalCost = "$0.00";

    /// <summary>
    /// When the withheld amounts are due. A regular remitter pays by the 15th of the month
    /// after the one the employees were paid in.
    /// </summary>
    [ObservableProperty]
    private string _remittanceDueNote = string.Empty;

    #endregion

    /// <summary>Raised after approve or void, so the pay runs page reloads.</summary>
    public event EventHandler? PayRunChanged;

    #region Opening

    public void OpenRunModal()
    {
        _draft = null;
        Step = 1;
        PayDate = DateTimeOffset.Now;
        BlockingError = string.Empty;

        // Nothing from the last run may survive into this one.
        ResetRunState();

        RefreshRateEdition();
        PrefillPeriod();
        LoadSelectableEmployees();

        IsRunModalOpen = true;
    }

    [RelayCommand]
    private void CloseRunModal()
    {
        IsRunModalOpen = false;
        _draft = null;
    }

    /// <summary>
    /// True once the run holds work that retyping would cost something.
    ///
    /// Step 1 is deliberately not guarded: the pay date, the period and the employee ticks are
    /// all prefilled defaults, so abandoning there loses nothing anyone chose. From step 2 the
    /// amounts are hand entered, which is the part worth asking about, and the rows survive
    /// stepping back so the check does not depend on where they are now.
    /// </summary>
    private bool HasRunInProgress => Step > 1 || AmountRows.Count > 0;

    [RelayCommand]
    private async Task RequestCloseRunModalAsync()
    {
        if (HasRunInProgress && !await ConfirmDiscardNewAsync())
        {
            return;
        }

        CloseRunModal();
    }

    private void LoadSelectableEmployees()
    {
        SelectableEmployees.Clear();
        NoEmployeesMessage = string.Empty;

        List<Employee> employees = App.CompanyManager?.CompanyData?.Employees ?? [];

        foreach (Employee e in employees.Where(e => !e.IsArchived).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            SelectableEmployees.Add(new PayRunEmployeeSelection
            {
                Id = e.Id,
                Name = e.Name,
                Detail = $"{e.Province} - {e.PayFrequency.DisplayName()}",
                IsSelected = true,
            });
        }

        if (SelectableEmployees.Count > 0)
        {
            return;
        }

        // Archived staff are excluded from a pay run but still exist, so "no employees" would be
        // wrong here and would send the employer off to add someone they already have.
        NoEmployeesMessage = employees.Count > 0
            ? "Every employee is archived, so there is nobody to pay. Restore someone on the Employees page first."
            : "There are no employees yet. Add one on the Employees page, then come back and run payroll.";
    }

    private void RefreshRateEdition()
    {
        DateTime date = PayDate?.DateTime ?? DateTime.Today;
        PayrollRateTable? table = _rates.GetForDate(date);

        if (table == null)
        {
            RateEditionNote = string.Empty;
            BlockingError = $"No CRA payroll tables are loaded for {date:d MMMM yyyy}. " +
                            "Checking for an update.";

            // CRA publishes twice a year on dates nobody chooses, so the edition a pay date
            // needs can exist on the server while this install has never seen it. Ask, rather
            // than telling the user payroll is unavailable until the next app release.
            _ = FetchRatesForAsync(date);
            return;
        }

        BlockingError = string.Empty;
        RateEditionNote = $"Using CRA tables effective {table.EffectiveFrom:d MMMM yyyy}.";
    }

    /// <summary>
    /// Tries to fetch the edition covering this pay date, and refreshes the screen if one
    /// arrives.
    ///
    /// Deliberately silent when it fails. Being offline, or asking before CRA's file has been
    /// uploaded, is the ordinary case rather than an error: the message already on screen says
    /// the tables are missing, which is both true and the only thing the user can act on.
    /// </summary>
    private async Task FetchRatesForAsync(DateTime payDate)
    {
        if (_fetchingRates)
        {
            return;
        }

        _fetchingRates = true;

        try
        {
            bool arrived = await new PayrollRateUpdateService(_rates).TryUpdateForDateAsync(payDate);

            if (!arrived)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    BlockingError = $"No CRA payroll tables are loaded for {payDate:d MMMM yyyy}. " +
                                    "Update the rates before running payroll for this date.");
                return;
            }

            // Back on the UI thread, and through the same path as any other date change so the
            // note and the blocking error are set by one piece of code rather than two.
            await Dispatcher.UIThread.InvokeAsync(RefreshRateEdition);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.FetchRates");
        }
        finally
        {
            _fetchingRates = false;
        }
    }

    /// <summary>
    /// Prefills the period from the most common frequency among the selected employees, which
    /// is right for the overwhelmingly common case of everyone on the same schedule and easy
    /// to correct when it is not.
    /// </summary>
    private void PrefillPeriod()
    {
        DateTime end = PayDate?.DateTime.Date ?? DateTime.Today;

        PayFrequency frequency = App.CompanyManager?.CompanyData?.Employees
            .Where(e => !e.IsArchived)
            .GroupBy(e => e.PayFrequency)
            .OrderByDescending(g => g.Count())
            .Select(g => (PayFrequency?)g.Key)
            .FirstOrDefault() ?? PayFrequency.Biweekly;

        DateTime start = frequency switch
        {
            PayFrequency.Weekly => end.AddDays(-6),
            PayFrequency.Biweekly => end.AddDays(-13),
            PayFrequency.SemiMonthly => end.AddDays(-14),
            PayFrequency.Monthly => end.AddMonths(-1).AddDays(1),
            _ => end.AddDays(-13),
        };

        PeriodStart = new DateTimeOffset(start);
        PeriodEnd = new DateTimeOffset(end);
    }

    #endregion

    #region Navigation

    [RelayCommand]
    private void Back()
    {
        if (Step == 1)
        {
            CloseRunModal();
            return;
        }

        Step--;
    }

    [RelayCommand]
    private void Next()
    {
        switch (Step)
        {
            case 1:
                if (!BuildDraft())
                {
                    return;
                }

                Step = 2;
                break;

            case 2:
                Recalculate();
                BuildReview();
                Step = 3;
                break;

            case 3:
                Approve();
                break;
        }
    }

    #endregion

    #region Step 1 to 2: build the draft

    private bool BuildDraft()
    {
        CompanyData? data = App.CompanyManager?.CompanyData;
        if (data == null)
        {
            return false;
        }

        List<string> chosen = SelectableEmployees.Where(e => e.IsSelected).Select(e => e.Id).ToList();
        if (chosen.Count == 0)
        {
            BlockingError = "Select at least one employee.";
            return false;
        }

        // Refused, but not restated. PeriodError is already on screen beside the date that is
        // wrong, and copying it into BlockingError put the same sentence in two places at once.
        if (PeriodError.Length > 0)
        {
            return false;
        }

        DateTime payDate = PayDate?.DateTime.Date ?? DateTime.Today;

        // Asked before the draft is built, not after. The calculator throws for a province it
        // has no table for. The province dropdown cannot produce a bad code, but the spreadsheet
        // importer takes whatever is in the cell and upper-cases it, so "Ontario" gets stored
        // and every pay run afterwards is unrunnable.
        List<string> unsupported = data.Employees
            .Where(e => chosen.Contains(e.Id) && !_payroll.Supports(payDate, e.Province))
            .Select(e => $"{e.Name} ({e.Province})")
            .ToList();

        if (unsupported.Count > 0)
        {
            BlockingError = $"No rate table covers the province of employment for "
                            + $"{string.Join(", ", unsupported)}. Correct it on the Employees page, "
                            + "or leave them out of this run.";
            return false;
        }

        try
        {
            _draft = _payroll.CreateDraft(
                data,
                payDate,
                PeriodStart?.DateTime.Date ?? payDate,
                PeriodEnd?.DateTime.Date ?? payDate,
                chosen);

            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.PayRunDrafted);
        }
        catch (Exception ex)
        {
            // A backstop for anything the check above did not anticipate. Refusing to start the
            // run and saying so beats taking the window down with the draft half built.
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.BuildDraft");
            _draft = null;
            BlockingError = $"This pay run could not be calculated: {ex.Message}";
            return false;
        }

        if (_draft == null)
        {
            // GetForDate returned null. Never fall back to another edition: the figures would
            // look plausible and be wrong.
            RefreshRateEdition();
            return false;
        }

        BlockingError = string.Empty;
        BuildAmountRows(data);
        return true;
    }

    /// <summary>
    /// Drops everything the previous run left behind.
    ///
    /// Detaching the handlers matters as much as clearing the list: a discarded row that is
    /// still subscribed keeps driving recalculation for a draft it no longer belongs to.
    /// </summary>
    private void ResetRunState()
    {
        ClearAmountRows();
        ReviewRows.Clear();
        Warnings.Clear();
        HasWarnings = false;
        TotalGross = CurrencyService.Format(0m);
        TotalNetPay = CurrencyService.Format(0m);
        TotalRemittance = CurrencyService.Format(0m);
        TotalCost = CurrencyService.Format(0m);
        RemittanceDueNote = string.Empty;
    }

    private void ClearAmountRows()
    {
        foreach (PayRunAmountRow existing in AmountRows)
        {
            existing.Changed -= OnAmountChanged;
        }

        AmountRows.Clear();
    }

    private void BuildAmountRows(CompanyData data)
    {
        // What has already been typed, keyed by employee. Next from step 1 rebuilds these rows
        // every time, so stepping Back to correct a period date and pressing Next again threw
        // away every hour, bonus and vacation figure already entered, with no warning. The class
        // note above promises the rows survive stepping back; this is what keeps that true.
        var typed = AmountRows.ToDictionary(
            r => r.EmployeeId,
            r => (r.Hours, r.Bonus, r.VacationPay),
            StringComparer.Ordinal);

        ClearAmountRows();

        if (_draft == null)
        {
            return;
        }

        foreach (PayRunLine line in _draft.Lines)
        {
            Employee? employee = data.Employees.FirstOrDefault(e => e.Id == line.EmployeeId);
            bool hourly = employee?.PayType == PayType.Hourly;

            var row = new PayRunAmountRow
            {
                EmployeeId = line.EmployeeId,
                EmployeeName = line.EmployeeName,
                IsHourly = hourly,
                RateNote = employee == null
                    ? string.Empty
                    : hourly
                        ? $"{CurrencyService.Format(employee.PayRate)} / hour"
                        : $"{CurrencyService.Format(employee.PayRate)} / year",
                UnitLabel = hourly ? "hrs" : CurrencyService.CurrentSymbol,
                Hours = string.Empty,
                BasePay = hourly ? string.Empty : line.BasePay.ToString("0.00", CultureInfo.CurrentCulture),
                Bonus = string.Empty,
                VacationPay = string.Empty,
            };

            // Restored before the handler is attached, so putting the figures back does not
            // trigger a recalculation per field; the one below covers the whole rebuild.
            if (typed.TryGetValue(line.EmployeeId, out var previous))
            {
                row.Hours = previous.Hours;
                row.Bonus = previous.Bonus;
                row.VacationPay = previous.VacationPay;
            }

            row.Changed += OnAmountChanged;
            AmountRows.Add(row);
        }

        // The same pair the amount-changed handler runs. Without the recalculate the rows show
        // an empty Gross until the first keystroke, even though the figures are already known:
        // a salaried run arrives with its base pay filled in and needs no input at all.
        Recalculate();
        UpdateGrossTotal();
    }

    private void OnAmountChanged(object? sender, EventArgs e)
    {
        Recalculate();
        UpdateGrossTotal();
    }

    /// <summary>Pushes the typed amounts onto the draft and re-runs the deductions.</summary>
    private void Recalculate()
    {
        CompanyData? data = App.CompanyManager?.CompanyData;
        if (_draft == null || data == null)
        {
            return;
        }

        foreach (PayRunLine line in _draft.Lines)
        {
            PayRunAmountRow? row = AmountRows.FirstOrDefault(r => r.EmployeeId == line.EmployeeId);
            if (row == null)
            {
                continue;
            }

            line.HoursWorked = Parse(row.Hours);
            line.Bonus = Parse(row.Bonus);
            line.VacationPay = Parse(row.VacationPay);

            // A salaried person's base pay stays editable, since a mid-period start or an
            // unpaid day is entered by adjusting it.
            if (!row.IsHourly)
            {
                line.BasePay = Parse(row.BasePay);
            }
        }

        _payroll.Recalculate(data, _draft);

        foreach (PayRunAmountRow row in AmountRows)
        {
            PayRunLine? line = _draft.Lines.FirstOrDefault(l => l.EmployeeId == row.EmployeeId);
            row.GrossDisplay = line == null ? CurrencyService.Format(0) : CurrencyService.Format(line.GrossPay);
        }
    }

    private void UpdateGrossTotal() =>
        TotalGross = CurrencyService.Format(_draft?.TotalGross ?? 0m);

    #endregion

    #region Step 3: the review

    private void BuildReview()
    {
        ReviewRows.Clear();
        Warnings.Clear();

        CompanyData? data = App.CompanyManager?.CompanyData;
        if (_draft == null || data == null)
        {
            return;
        }

        PayrollRateTable? rates = _rates.GetForDate(_draft.PayDate);

        AddOverlapWarning(data);

        foreach (PayRunLine line in _draft.Lines)
        {
            bool quebec = string.Equals(line.Province, "QC", StringComparison.OrdinalIgnoreCase);

            ReviewRows.Add(new PayRunReviewRow
            {
                EmployeeName = line.EmployeeName,
                Gross = CurrencyService.Format(line.GrossPay),
                Cpp = CurrencyService.Format(line.CppEmployee + line.Cpp2Employee),

                // Quebec's pension money is QPP. It is stored in the CPP fields because it is
                // the same column in the same run, but naming it CPP on the review is telling
                // the employer they withheld something they did not.
                CppLabel = quebec ? "QPP" : "CPP",
                Ei = CurrencyService.Format(line.EiEmployee),

                // Without this the breakdown does not add up to the net pay beside it for a
                // Quebec employee, because QPIP is withheld and was shown nowhere.
                Qpip = CurrencyService.Format(line.QpipEmployee),
                HasQpip = line.QpipEmployee != 0m,
                FederalTax = CurrencyService.Format(line.FederalTax),
                ProvincialTax = CurrencyService.Format(line.ProvincialTax),
                ProvincialTaxLabel = $"{line.Province} tax",
                NetPay = CurrencyService.Format(line.NetPay),
            });

            AddWarningsFor(data, line, rates);
        }

        HasWarnings = Warnings.Count > 0;

        TotalNetPay = CurrencyService.Format(_draft.TotalNetPay);
        TotalRemittance = CurrencyService.Format(_draft.TotalRemittance);
        TotalCost = CurrencyService.Format(_draft.TotalCost);

        // CRA runs four schedules, so this follows the employer's assigned type rather than
        // assuming the 15th. An accelerated remitter's real deadline can be three weeks earlier.
        RemitterType remitter = data.Settings.Company.RemitterType;

        // The deadline for the period this pay date falls IN. NextRemittance answers a different
        // question, "which deadline has not passed yet", and returns the previous period's.
        DateTime due = PayrollService.RemittanceDueFor(_draft.PayDate, remitter);

        // Quebec income tax, QPP and QPIP are paid to Revenu Quebec, so for a Quebec employee the
        // total above is two payments and the note names both.
        decimal quebecShare = _draft.QuebecRemittance;

        RemittanceDueNote = quebecShare == 0m
            ? $"Due to CRA by {due:d MMMM yyyy}."
            : $"{CurrencyService.Format(_draft.CraRemittance)} due to CRA by {due:d MMMM yyyy}, "
              + $"{CurrencyService.Format(quebecShare)} to Revenu Quebec.";
    }

    /// <summary>
    /// Says so when somebody in this run has already been paid for a period that overlaps this
    /// one.
    ///
    /// A warning and not a refusal, deliberately. Paying the same period twice is usually a
    /// mistake and occasionally exactly right: a correction after an error, or a bonus paid on
    /// its own. Nothing in the data distinguishes the two, so blocking would be wrong the first
    /// time somebody genuinely needs a second run, and there would be no way past it.
    ///
    /// Overlap rather than an exact match, because 3 to 16 August followed by 10 to 23 August
    /// pays a week twice just as surely and is far harder to notice by eye.
    ///
    /// A voided run does not count. Voiding is how a run is undone, so its period has not been
    /// paid, and warning about it would send the employer looking for something that is no
    /// longer there.
    /// </summary>
    private void AddOverlapWarning(CompanyData data)
    {
        if (_draft == null)
        {
            return;
        }

        DateTime start = _draft.PeriodStart.Date;
        DateTime end = _draft.PeriodEnd.Date;

        AddDistantPayDateWarning(start, end);
        var paying = _draft.Lines.Select(l => l.EmployeeId).ToHashSet(StringComparer.Ordinal);

        foreach (PayRun run in data.PayRuns)
        {
            if (run.Id == _draft.Id
                || run.Status != PayRunStatus.Approved
                || run.VoidsPayRunId is { Length: > 0 }
                || run.PeriodStart.Date > end
                || run.PeriodEnd.Date < start
                || !run.Lines.Any(l => paying.Contains(l.EmployeeId)))
            {
                continue;
            }

            Warnings.Add($"{run.Id} already paid {run.PeriodStart:d MMM} to {run.PeriodEnd:d MMM yyyy}, "
                         + "which overlaps this period. Continue only if this run is meant to be on top of it.");
        }
    }

    /// <summary>
    /// Says so when the pay date is nowhere near the period it pays for.
    ///
    /// The two are only loosely related by design: the pay date picks the CRA edition and the
    /// period never enters the arithmetic at all, so a period two years from its pay date
    /// calculates without complaint. It is also precisely what a mistyped year looks like, and
    /// the year is the digit nobody re-reads.
    ///
    /// Ninety days, and only measured forwards from the end of the period, because paying weeks
    /// after a period ends is ordinary and paying BEFORE it ends is a legitimate advance. A
    /// warning rather than a refusal: correcting an old period is a real thing to do, and the
    /// app cannot tell a deliberate one from a typo.
    /// </summary>
    private void AddDistantPayDateWarning(DateTime periodStart, DateTime periodEnd)
    {
        if (_draft == null)
        {
            return;
        }

        DateTime payDate = _draft.PayDate.Date;

        if (payDate >= periodStart.AddDays(-90) && payDate <= periodEnd.AddDays(90))
        {
            return;
        }

        Warnings.Add($"The pay date is {payDate:d MMM yyyy} but the period runs "
                     + $"{periodStart:d MMM yyyy} to {periodEnd:d MMM yyyy}. Check the year if that "
                     + "was not deliberate.");
    }

    /// <summary>
    /// Flags anything that changes the numbers without being visible on the row: a maximum
    /// reached partway through the period, or a missing TD1.
    /// </summary>
    private void AddWarningsFor(CompanyData data, PayRunLine line, PayrollRateTable? rates)
    {
        Employee? employee = data.Employees.FirstOrDefault(e => e.Id == line.EmployeeId);
        if (employee == null || rates == null)
        {
            return;
        }

        PayrollYearToDate ytd = _payroll.YearToDateFor(data, employee.Id, _draft);

        // Quebec runs its own plans at its own maximums, so the figure to compare against is not
        // the federal one. Checking a Quebec employee against CPP's ceiling announces the
        // maximum late, and against EI's higher rest-of-Canada ceiling never announces it at all.
        bool quebec = string.Equals(employee.Province, "QC", StringComparison.OrdinalIgnoreCase);
        QuebecRates? qc = quebec ? rates.Quebec : null;

        decimal pensionMax = qc?.Qpp.MaxContributionEmployee ?? rates.Cpp.MaxContributionEmployee;
        decimal eiMax = qc?.EiMaxPremiumEmployee ?? rates.Ei.MaxPremiumEmployee;

        if (!employee.IsCppExempt && ytd.CppEmployee + line.CppEmployee >= pensionMax)
        {
            Warnings.Add($"{employee.Name} has reached the {(quebec ? "QPP" : "CPP")} maximum for the year.");
        }

        if (!employee.IsEiExempt && ytd.EiEmployee + line.EiEmployee >= eiMax)
        {
            Warnings.Add($"{employee.Name} has reached the EI maximum for the year.");
        }

        if (quebec && ytd.QpipEmployee + line.QpipEmployee >= (qc?.Qpip.MaxPremiumEmployee ?? decimal.MaxValue))
        {
            Warnings.Add($"{employee.Name} has reached the QPIP maximum for the year.");
        }

        if (employee.FederalClaimAmount == 0 && employee.ProvincialClaimAmount == 0
            && !employee.FederalClaimIsZero && !employee.ProvincialClaimIsZero)
        {
            Warnings.Add($"{employee.Name} has no TD1 on file, so the basic personal amount is used.");
        }

        // Quebec is held outside the provinces table, so asking that table alone reported a
        // fully supported jurisdiction as missing on every Quebec run.
        if (!_payroll.Supports(_draft?.PayDate ?? DateTime.Today, employee.Province))
        {
            Warnings.Add($"{employee.Name} works in {employee.Province}, which has no rate table loaded.");
        }
    }

    #endregion

    #region Approve

    private void Approve()
    {
        CompanyData? data = App.CompanyManager?.CompanyData;
        if (_draft == null || data == null)
        {
            return;
        }

        PayRun run = _draft;
        data.PayRuns.Add(run);

        List<Core.Models.Transactions.Expense> expenses = _payroll.ApproveAndRecord(data, run);

        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.PayRunApproved);

        App.CompanyManager?.MarkAsChanged();

        // Undo has to take the expenses with it, otherwise the wages stay in the books after
        // the run they came from is gone.
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Approve pay run {run.Id}",
            () =>
            {
                data.PayRuns.Remove(run);
                PayrollService.RemoveWageExpenses(data, expenses);
                App.CompanyManager?.MarkAsChanged();
                PayRunChanged?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                data.PayRuns.Add(run);
                PayrollService.RestoreWageExpenses(data, expenses);
                App.CompanyManager?.MarkAsChanged();
                PayRunChanged?.Invoke(this, EventArgs.Empty);
            }));

        _draft = null;
        IsRunModalOpen = false;
        PayRunChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    private static decimal Parse(string text) =>
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal d) ? d : 0m;
}

/// <summary>One employee on the step 1 checklist.</summary>
public partial class PayRunEmployeeSelection : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private bool _isSelected = true;
}

/// <summary>One employee's editable amounts on step 2.</summary>
public partial class PayRunAmountRow : ObservableObject
{
    /// <summary>Raised on any amount edit so the owning modal re-runs the deductions.</summary>
    public event EventHandler? Changed;

    [ObservableProperty]
    private string _employeeId = string.Empty;

    [ObservableProperty]
    private string _employeeName = string.Empty;

    [ObservableProperty]
    private bool _isHourly;

    [ObservableProperty]
    private string _rateNote = string.Empty;

    /// <summary>
    /// What the amount box is measured in: hours for an hourly employee, money for a salaried
    /// one. Sits beside the box because the column serves both and a placeholder disappears the
    /// moment anything is typed, which is precisely when the reader is checking what they
    /// entered.
    /// </summary>
    [ObservableProperty]
    private string _unitLabel = string.Empty;

    [ObservableProperty]
    private string _hours = string.Empty;

    [ObservableProperty]
    private string _basePay = string.Empty;

    [ObservableProperty]
    private string _bonus = string.Empty;

    [ObservableProperty]
    private string _vacationPay = string.Empty;

    [ObservableProperty]
    private string _grossDisplay = string.Empty;

    partial void OnHoursChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnBasePayChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnBonusChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnVacationPayChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>One employee's breakdown on step 3.</summary>
public partial class PayRunReviewRow : ObservableObject
{
    [ObservableProperty]
    private string _employeeName = string.Empty;

    [ObservableProperty]
    private string _gross = string.Empty;

    [ObservableProperty]
    private string _cpp = string.Empty;

    /// <summary>CPP, or QPP in Quebec. Carried as text so no new translation key is needed.</summary>
    [ObservableProperty]
    private string _cppLabel = "CPP";

    [ObservableProperty]
    private string _ei = string.Empty;

    [ObservableProperty]
    private string _qpip = string.Empty;

    /// <summary>Quebec only, so the row is absent rather than nil everywhere else.</summary>
    [ObservableProperty]
    private bool _hasQpip;

    [ObservableProperty]
    private string _federalTax = string.Empty;

    [ObservableProperty]
    private string _provincialTax = string.Empty;

    [ObservableProperty]
    private string _provincialTaxLabel = string.Empty;

    [ObservableProperty]
    private string _netPay = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
