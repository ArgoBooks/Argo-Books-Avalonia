using System.Collections.ObjectModel;
using System.Globalization;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Add, edit and filter modals for the Employees page. The pay run flow lives in
/// <see cref="PayRunModalsViewModel"/> so this stays a plain entity form.
/// </summary>
public partial class PayrollModalsViewModel : ViewModelBase
{
    private Employee? _editing;

    /// <summary>
    /// The Record of Employment form. Lives here rather than at year end because an ROE is due
    /// five days after the pay period in which someone stops being paid.
    /// </summary>
    public RoeModalViewModel Roe { get; } = new();

    [ObservableProperty]
    private bool _isEmployeeModalOpen;

    [ObservableProperty]
    private string _modalTitle = "Add employee";

    #region Form fields

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _employeeNumber = string.Empty;

    [ObservableProperty]
    private string _province = "AB";

    [ObservableProperty]
    private bool _isSalaried = true;

    /// <summary>
    /// Money fields are strings so an empty box shows its placeholder instead of "0.00".
    /// Matches how every other modal in the app takes an amount.
    /// </summary>
    [ObservableProperty]
    private string _payRate = string.Empty;

    [ObservableProperty]
    private PayFrequency _payFrequency = PayFrequency.Biweekly;

    /// <summary>
    /// Contract hours in a normal week. Only asked of salaried staff, and only used by the
    /// Record of Employment: block 15A wants insurable hours and a salaried pay run records
    /// none. Blank is allowed, and the ROE worksheet then says the hours are unknown rather
    /// than printing zero, because zero hours would cost the employee their EI claim.
    /// </summary>
    [ObservableProperty]
    private string _standardHoursPerWeek = string.Empty;

    [ObservableProperty]
    private string _federalClaimAmount = string.Empty;

    [ObservableProperty]
    private string _provincialClaimAmount = string.Empty;

    /// <summary>
    /// Dependants from the employee's TD1ON. Only Ontario's tax reduction reads it, so the box
    /// is hidden everywhere else rather than asking a question with no effect.
    /// </summary>
    [ObservableProperty]
    private string _ontarioDependants = string.Empty;

    [ObservableProperty]
    private bool _isCppExempt;

    [ObservableProperty]
    private bool _isEiExempt;

    [ObservableProperty]
    private DateTimeOffset? _startDate;

    /// <summary>
    /// Their last day, if they have left. Kept separate from archiving: a leaver still needs a
    /// T4 and a record of employment, and both want the actual last day rather than the day
    /// someone got round to tidying the employee list.
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? _endDate;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _nameError = string.Empty;

    [ObservableProperty]
    private string _payRateError = string.Empty;

    [ObservableProperty]
    private string _endDateError = string.Empty;

    /// <summary>
    /// Social insurance number, needed to file a T4. Held as typed and stripped to digits on
    /// save, because people write it with spaces or dashes and rejecting that would be rude.
    /// </summary>
    [ObservableProperty]
    private string _sin = string.Empty;

    [ObservableProperty]
    private string _sinError = string.Empty;

    [ObservableProperty]
    private string _addressStreet = string.Empty;

    [ObservableProperty]
    private string _addressCity = string.Empty;

    [ObservableProperty]
    private string _addressProvince = string.Empty;

    [ObservableProperty]
    private string _addressPostalCode = string.Empty;

    /// <summary>
    /// Where they live, which the T4 wants as an ISO country code and the app stores as a name.
    ///
    /// Asked for rather than assumed, because CRA reads the province box differently depending on
    /// it: a Canadian address carries a province code, a US address carries a state, and anywhere
    /// else must carry ZZ.
    ///
    /// Defaults to Canada, which is where an employee on a Canadian payroll almost always lives.
    /// </summary>
    [ObservableProperty]
    private string _addressCountry = "Canada";

    /// <summary>Which of the three is really the province box depends on the country.</summary>
    public string AddressProvinceLabel =>
        Core.Services.Payroll.CraFormat.IsUnitedStates(AddressCountry) ? "State" : "Prov";

    partial void OnAddressCountryChanged(string value) =>
        OnPropertyChanged(nameof(AddressProvinceLabel));

    [ObservableProperty]
    private string _addressError = string.Empty;

    [ObservableProperty]
    private string _addressProvinceError = string.Empty;

    [ObservableProperty]
    private string _addressPostalCodeError = string.Empty;

    /// <summary>Box 45, mandatory on every T4 since 2023.</summary>
    [ObservableProperty]
    private DentalBenefitCode _dentalBenefit = DentalBenefitCode.NotEligible;

    public ObservableCollection<DentalBenefitCode> DentalOptions { get; } =
    [
        DentalBenefitCode.NotEligible,
        DentalBenefitCode.PayeeOnly,
        DentalBenefitCode.PayeeAndSpouse,
        DentalBenefitCode.PayeeAndChildren,
        DentalBenefitCode.PayeeSpouseAndChildren,
    ];

    /// <summary>Label under the pay rate box, since the same field means two different things.</summary>
    public string PayRateHint => IsSalaried
        ? "Annual salary before deductions."
        : "Rate per hour.";

    partial void OnIsSalariedChanged(bool value) => OnPropertyChanged(nameof(PayRateHint));

    /// <summary>Ontario is the only province whose tax reduction has a dependant component.</summary>
    public bool ShowOntarioDependants => string.Equals(Province, "ON", StringComparison.OrdinalIgnoreCase);

    partial void OnProvinceChanged(string value) => OnPropertyChanged(nameof(ShowOntarioDependants));

    #endregion

    /// <summary>
    /// Provinces the app can actually calculate for. Only those with a rate table are offered,
    /// so an employee cannot be created that no pay run could ever include.
    /// </summary>
    public ObservableCollection<string> SupportedProvinces { get; } = [];

    /// <summary>
    /// Set only when no rate edition covers today, in which case no province can be calculated
    /// for and the employer needs to know before entering an employee.
    /// </summary>
    [ObservableProperty]
    private string _provinceSupportNote = string.Empty;

    public ObservableCollection<PayFrequency> Frequencies { get; } =
        [PayFrequency.Weekly, PayFrequency.Biweekly, PayFrequency.SemiMonthly, PayFrequency.Monthly];

    private readonly Core.Services.PayrollRateService _rates;

    /// <param name="rates">
    /// Optional, and taken the same way <c>PayrollService</c> and <c>Rl1Service</c> take theirs.
    /// It exists so the "no rates loaded" note can be exercised: otherwise the only way to see
    /// that branch is to run the app on a date no CRA edition covers, which is a year away and
    /// is exactly when nobody wants to find out the message is wrong.
    /// </param>
    public PayrollModalsViewModel(Core.Services.PayrollRateService? rates = null)
    {
        _rates = rates ?? new Core.Services.PayrollRateService();
        RefreshSupportedProvinces();
    }

    /// <summary>Event raised after a save, so the page can reload.</summary>
    public event EventHandler? EmployeeSaved;

    /// <summary>
    /// The form as it stood when the modal opened, so closing can tell whether anything was
    /// touched.
    ///
    /// A snapshot rather than one _originalX field per property, which is how the older modals
    /// do it. This form has twenty fields and gained two of them in a single change, and a
    /// per-field list silently stops detecting whatever nobody remembered to add to it.
    /// </summary>
    private string _employeeSnapshot = string.Empty;

    private string EmployeeFormSnapshot() => string.Join('\u001f',
        Name, EmployeeNumber, Province, IsSalaried, Parse(PayRate), PayFrequency,
        Parse(StandardHoursPerWeek), Parse(FederalClaimAmount), IsTypedZero(FederalClaimAmount),
        Parse(ProvincialClaimAmount), IsTypedZero(ProvincialClaimAmount),
        OntarioDependants, IsCppExempt, IsEiExempt, StartDate, EndDate,
        Sin, AddressStreet, AddressCity, AddressProvince, AddressPostalCode, AddressCountry,
        DentalBenefit, Notes);

    public bool HasEmployeeModalChanges => EmployeeFormSnapshot() != _employeeSnapshot;

    public void OpenAddEmployeeModal()
    {
        _editing = null;
        ModalTitle = "Add employee";
        Clear();
        RefreshSupportedProvinces();
        _employeeSnapshot = EmployeeFormSnapshot();
        IsEmployeeModalOpen = true;
    }

    public void OpenEditEmployeeModal(Employee employee)
    {
        _editing = employee;
        ModalTitle = "Edit employee";
        RefreshSupportedProvinces();

        Name = employee.Name;
        EmployeeNumber = employee.EmployeeNumber;
        Province = employee.Province;
        IsSalaried = employee.PayType == PayType.Salary;
        PayRate = employee.PayRate == 0m ? string.Empty : CurrencyService.Format(employee.PayRate);
        PayFrequency = employee.PayFrequency;
        StandardHoursPerWeek = employee.StandardHoursPerWeek?.ToString("0.##") ?? string.Empty;
        FederalClaimAmount = Claim(employee.FederalClaimAmount, employee.FederalClaimIsZero);
        ProvincialClaimAmount = Claim(employee.ProvincialClaimAmount, employee.ProvincialClaimIsZero);
        OntarioDependants = employee.OntarioDependants == 0 ? string.Empty : employee.OntarioDependants.ToString();
        IsCppExempt = employee.IsCppExempt;
        IsEiExempt = employee.IsEiExempt;
        StartDate = employee.StartDate.HasValue ? new DateTimeOffset(employee.StartDate.Value) : null;
        EndDate = employee.EndDate.HasValue ? new DateTimeOffset(employee.EndDate.Value) : null;
        Sin = employee.Sin;
        AddressStreet = employee.Address.Street;
        AddressCity = employee.Address.City;
        AddressProvince = employee.Address.State;
        AddressPostalCode = employee.Address.ZipCode;
        AddressCountry = string.IsNullOrWhiteSpace(employee.Address.Country)
            ? "Canada"
            : employee.Address.Country;
        DentalBenefit = employee.DentalBenefit;
        Notes = employee.Notes;

        NameError = string.Empty;
        PayRateError = string.Empty;
        EndDateError = string.Empty;
        SinError = string.Empty;
        AddressError = string.Empty;
        AddressProvinceError = string.Empty;
        AddressPostalCodeError = string.Empty;

        _employeeSnapshot = EmployeeFormSnapshot();
        IsEmployeeModalOpen = true;
    }

    /// <summary>
    /// Closing with unsaved work asks first, as every other entity modal does. Clicking the
    /// backdrop is the easiest way to lose a half-filled form.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseEmployeeModalAsync()
    {
        if (HasEmployeeModalChanges)
        {
            // A new employee discards differently from an edited one, so the wording matches
            // which of the two is on screen.
            bool confirmed = _editing == null
                ? await ConfirmDiscardNewAsync()
                : await ConfirmDiscardEditsAsync();

            if (!confirmed)
            {
                return;
            }
        }

        CloseEmployeeModal();
    }

    [RelayCommand]
    private void CloseEmployeeModal() => IsEmployeeModalOpen = false;

    [RelayCommand]
    private void SaveEmployee()
    {
        decimal rate = Parse(PayRate);

        // The name goes on the T4 as typed, and CRA accepts a narrow set of characters in it. A
        // comma, which is what someone writing "Smith, John" produces, rejects the whole
        // submission at the February deadline. Caught here, where it costs nothing to fix.
        string badName = Core.Services.Payroll.CraFormat.DisallowedCharacters(Name);

        NameError = string.IsNullOrWhiteSpace(Name)
            ? "Enter a name."
            : badName.Length > 0
                ? $"CRA does not accept {Describe(badName)} in a name. Use letters, digits, "
                  + "an apostrophe, an ampersand, a period or a hyphen."
                : string.Empty;

        PayRateError = rate <= 0 ? "Enter a pay rate." : string.Empty;

        // A last day before the first day would put pay periods outside the employment and
        // make the record of employment nonsense.
        EndDateError = StartDate is { } start && EndDate is { } end && end < start
            ? "The end date cannot be before the start date."
            : string.Empty;

        // Checked but not required. Someone can be hired and paid before their SIN arrives,
        // and blocking the whole employee record over it would stop payroll running at all.
        // Year end is where a missing SIN actually becomes a problem, and it is reported there.
        string sinDigits = new(Sin.Where(char.IsAsciiDigit).ToArray());
        SinError = Sin.Trim().Length > 0 && sinDigits.Length != 9
            ? "A social insurance number is 9 digits."
            : string.Empty;

        ValidateAddress();

        if (NameError.Length > 0 || PayRateError.Length > 0 || EndDateError.Length > 0
            || SinError.Length > 0 || AddressError.Length > 0 || AddressProvinceError.Length > 0
            || AddressPostalCodeError.Length > 0)
        {
            return;
        }

        Core.Data.CompanyData? data = App.CompanyManager?.CompanyData;
        if (data == null)
        {
            return;
        }

        if (_editing == null)
        {
            AddEmployee(data, rate);
        }
        else
        {
            UpdateEmployee(data, _editing, rate);
        }

        App.CompanyManager?.MarkAsChanged();
        IsEmployeeModalOpen = false;
        EmployeeSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Checks the parts of the address CRA is fussy about, and quietly tidies the postal code.
    ///
    /// The whole address is optional on a T4, so an empty field is never a problem here. Each
    /// check only applies once something has been typed into the box it guards.
    /// </summary>
    private void ValidateAddress()
    {
        // Everyone writes "K1A 0B1". CRA's format is six characters with no space, and only a
        // USA or foreign code may carry a dash. Correcting it is kinder than refusing it, and it
        // is the one field where the right answer is never ambiguous.
        AddressPostalCode = Core.Services.Payroll.CraFormat
            .NormalizePostalCode(AddressPostalCode, AddressCountry);

        string badAddress = Core.Services.Payroll.CraFormat
            .DisallowedCharacters(AddressStreet + " " + AddressCity, address: true);

        AddressError = badAddress.Length > 0
            ? $"CRA does not accept {Describe(badAddress)} in an address."
            : string.Empty;

        bool canadian = Core.Services.Payroll.CraFormat.IsCanada(AddressCountry);
        bool american = Core.Services.Payroll.CraFormat.IsUnitedStates(AddressCountry);

        AddressProvinceError = AddressProvince.Trim().Length > 0
                               && canadian
                               && !Core.Services.Payroll.CraFormat.IsProvinceCode(AddressProvince)
            ? "Use a two letter province or territory code, such as ON or QC."
            : string.Empty;

        AddressPostalCodeError = AddressPostalCode.Length > 0
                                 && (canadian || american)
                                 && !Core.Services.Payroll.CraFormat.IsPostalCode(AddressPostalCode, AddressCountry)
            ? canadian
                ? "A Canadian postal code is six characters, such as K1A0B1."
                : "A US ZIP code is five digits, or five and four."
            : string.Empty;
    }

    /// <summary>Names the offending characters, since a curly apostrophe looks like a normal one.</summary>
    private static string Describe(string characters) =>
        string.Join(" or ", characters.Select(c => c == ' ' ? "a space" : $"\"{c}\""));

    private void AddEmployee(Core.Data.CompanyData data, decimal rate)
    {
        var employee = new Employee
        {
            Id = NextId(data),
            CreatedAt = DateTime.UtcNow,
        };

        ApplyFormTo(employee, rate);
        data.Employees.Add(employee);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add employee '{employee.Name}'",
            () =>
            {
                data.Employees.Remove(employee);
                App.CompanyManager?.MarkAsChanged();
                EmployeeSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                data.Employees.Add(employee);
                App.CompanyManager?.MarkAsChanged();
                EmployeeSaved?.Invoke(this, EventArgs.Empty);
            }));
    }

    /// <summary>
    /// Edits in place and records the before state, rather than swapping the instance out.
    /// Pay run lines hold the employee id, not a reference, but the archive command and the
    /// employees list both work off the same object, so replacing it would strand them.
    /// </summary>
    private void UpdateEmployee(Core.Data.CompanyData data, Employee employee, decimal rate)
    {
        Employee before = Snapshot(employee);
        ApplyFormTo(employee, rate);
        Employee after = Snapshot(employee);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit employee '{employee.Name}'",
            () =>
            {
                Restore(employee, before);
                App.CompanyManager?.MarkAsChanged();
                EmployeeSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                Restore(employee, after);
                App.CompanyManager?.MarkAsChanged();
                EmployeeSaved?.Invoke(this, EventArgs.Empty);
            }));
    }

    private void ApplyFormTo(Employee employee, decimal rate)
    {
        employee.Name = Name.Trim();
        employee.EmployeeNumber = EmployeeNumber.Trim();
        employee.Province = Province;
        employee.PayType = IsSalaried ? PayType.Salary : PayType.Hourly;
        employee.PayRate = rate;
        employee.PayFrequency = PayFrequency;

        // Null rather than zero when blank, and cleared outright for an hourly employee whose
        // real hours are entered on every run. Zero would read as "worked no hours" on an ROE.
        decimal hours = Parse(StandardHoursPerWeek);
        employee.StandardHoursPerWeek = IsSalaried && hours > 0 ? hours : null;

        employee.FederalClaimAmount = Parse(FederalClaimAmount);
        employee.ProvincialClaimAmount = Parse(ProvincialClaimAmount);
        employee.FederalClaimIsZero = IsTypedZero(FederalClaimAmount);
        employee.ProvincialClaimIsZero = IsTypedZero(ProvincialClaimAmount);
        employee.OntarioDependants = int.TryParse(OntarioDependants, out int dependants) && dependants > 0 ? dependants : 0;
        employee.IsCppExempt = IsCppExempt;
        employee.IsEiExempt = IsEiExempt;
        employee.StartDate = StartDate?.DateTime;
        employee.EndDate = EndDate?.DateTime;
        employee.Sin = new string(Sin.Where(char.IsAsciiDigit).ToArray());
        employee.Address.Street = AddressStreet.Trim();
        employee.Address.City = AddressCity.Trim();
        employee.Address.State = AddressProvince.Trim().ToUpperInvariant();
        employee.Address.ZipCode = AddressPostalCode.Trim();
        employee.Address.Country = AddressCountry.Trim();
        employee.DentalBenefit = DentalBenefit;
        employee.Notes = Notes.Trim();
        employee.UpdatedAt = DateTime.UtcNow;
    }

    private static Employee Snapshot(Employee e) => new()
    {
        Name = e.Name,
        EmployeeNumber = e.EmployeeNumber,
        Province = e.Province,
        PayType = e.PayType,
        PayRate = e.PayRate,
        PayFrequency = e.PayFrequency,
        StandardHoursPerWeek = e.StandardHoursPerWeek,
        FederalClaimAmount = e.FederalClaimAmount,
        ProvincialClaimAmount = e.ProvincialClaimAmount,
        FederalClaimIsZero = e.FederalClaimIsZero,
        ProvincialClaimIsZero = e.ProvincialClaimIsZero,
        OntarioDependants = e.OntarioDependants,
        IsCppExempt = e.IsCppExempt,
        IsEiExempt = e.IsEiExempt,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        Sin = e.Sin,
        DentalBenefit = e.DentalBenefit,
        Address = new Core.Models.Common.Address
        {
            Street = e.Address.Street,
            City = e.Address.City,
            State = e.Address.State,
            ZipCode = e.Address.ZipCode,
            Country = e.Address.Country,
        },
        Notes = e.Notes,
        IsArchived = e.IsArchived,
        UpdatedAt = e.UpdatedAt,
    };

    private static void Restore(Employee target, Employee from)
    {
        target.Name = from.Name;
        target.EmployeeNumber = from.EmployeeNumber;
        target.Province = from.Province;
        target.PayType = from.PayType;
        target.PayRate = from.PayRate;
        target.PayFrequency = from.PayFrequency;
        target.StandardHoursPerWeek = from.StandardHoursPerWeek;
        target.FederalClaimAmount = from.FederalClaimAmount;
        target.ProvincialClaimAmount = from.ProvincialClaimAmount;
        target.FederalClaimIsZero = from.FederalClaimIsZero;
        target.ProvincialClaimIsZero = from.ProvincialClaimIsZero;
        target.OntarioDependants = from.OntarioDependants;
        target.IsCppExempt = from.IsCppExempt;
        target.IsEiExempt = from.IsEiExempt;
        target.StartDate = from.StartDate;
        target.EndDate = from.EndDate;
        target.Sin = from.Sin;
        target.DentalBenefit = from.DentalBenefit;
        target.Address = from.Address;
        target.Notes = from.Notes;
        target.IsArchived = from.IsArchived;
        target.UpdatedAt = from.UpdatedAt;
    }

    #region Filter modal

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string _filterProvince = "All";

    [ObservableProperty]
    private string _filterPayType = "All";

    [ObservableProperty]
    private string _filterFrequency = "All";

    public ObservableCollection<string> StatusOptions { get; } = ["All", "Active", "Archived"];

    public ObservableCollection<string> ProvinceFilterOptions { get; } = ["All"];

    public ObservableCollection<string> PayTypeOptions { get; } = ["All", "Salary", "Hourly"];

    public ObservableCollection<string> FrequencyOptions { get; } =
        ["All", "Weekly", "Biweekly", "Semi-monthly", "Monthly"];

    /// <summary>Raised when Apply is pressed, so the page re-runs its filter.</summary>
    public event EventHandler? FiltersApplied;

    /// <summary>Raised when Clear is pressed, so the page resets its own copy.</summary>
    public event EventHandler? FiltersCleared;

    [RelayCommand]
    public void OpenFilterModal()
    {
        // Only provinces someone actually works in are offered, so the list stays short
        // rather than listing every province the rate table happens to cover.
        ProvinceFilterOptions.Clear();
        ProvinceFilterOptions.Add("All");

        List<Employee>? employees = App.CompanyManager?.CompanyData?.Employees;
        if (employees != null)
        {
            foreach (string code in employees.Select(e => e.Province)
                                             .Where(p => !string.IsNullOrWhiteSpace(p))
                                             .Distinct()
                                             .OrderBy(p => p, StringComparer.Ordinal))
            {
                ProvinceFilterOptions.Add(code);
            }
        }

        if (!ProvinceFilterOptions.Contains(FilterProvince))
        {
            FilterProvince = "All";
        }

        Filters.Capture();
        IsFilterModalOpen = true;
    }

    private sealed record FilterValues(string Status, string Province, string PayType, string Frequency)
    {
        public static readonly FilterValues Default = new("All", "All", "All", "All");
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterStatus, FilterProvince, FilterPayType, FilterFrequency),
        v =>
        {
            FilterStatus = v.Status;
            FilterProvince = v.Province;
            FilterPayType = v.PayType;
            FilterFrequency = v.Frequency;
        });

    /// <summary>
    /// Filters are live properties, so abandoning the modal has to put them back. Without the
    /// restore the page would quietly keep filtering by a choice the user cancelled.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
        {
            CloseFilterModal();
        }
    }

    private void CloseFilterModal() => IsFilterModalOpen = false;

    /// <summary>How many filters are applied, for the page's Filter button.</summary>
    public int ActiveFilterCount { get; private set; }

    [RelayCommand]
    public void ApplyFilters()
    {
        ActiveFilterCount = Filters.ActiveCount;
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        Filters.Reset();
        ActiveFilterCount = 0;
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    #endregion

    /// <summary>
    /// Reads the provinces out of the rate table rather than hard-coding a list, so the
    /// dropdown grows by itself as editions gain provinces and never offers one that would
    /// fail at pay-run time.
    /// </summary>
    private void RefreshSupportedProvinces()
    {
        SupportedProvinces.Clear();

        PayrollRateTable? table = _rates.GetForDate(DateTime.Today);

        if (table == null)
        {
            // No edition covers today. The employee form still works; a pay run is what will
            // refuse, with a message that explains why.
            SupportedProvinces.Add(Province);
            ProvinceSupportNote = "No CRA payroll tables are loaded for today, so a pay run "
                                  + "cannot be calculated until the rates are updated.";
            return;
        }

        // Quebec is NOT in the provinces table. It administers its own income tax, pension plan
        // and parental insurance, so its figures live in their own block and the calculator
        // dispatches on the code before it ever looks a province up. Reading only the table's
        // keys therefore leaves QC off the list and makes a fully supported jurisdiction
        // unselectable.
        var codes = table.Provinces.Keys.ToList();

        if (table.Quebec != null)
        {
            codes.Add("QC");
        }

        foreach (string code in codes.OrderBy(c => c, StringComparer.Ordinal))
        {
            SupportedProvinces.Add(code);
        }

        ProvinceSupportNote = string.Empty;

        if (!SupportedProvinces.Contains(Province) && SupportedProvinces.Count > 0)
        {
            Province = SupportedProvinces[0];
        }
    }

    private static string NextId(Core.Data.CompanyData data)
    {
        int highest = 0;
        foreach (Employee e in data.Employees)
        {
            if (e.Id.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(e.Id[4..], out int n) && n > highest)
            {
                highest = n;
            }
        }

        return $"EMP-{highest + 1:D3}";
    }

    /// <summary>Blank rather than "0.00", so an unset optional amount shows its placeholder.</summary>
    private static string Money(decimal value) =>
        value == 0 ? string.Empty : value.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// A TD1 total. A claim of nothing shows as zero rather than blank, because blank is what
    /// means no TD1 on file and the basic personal amount.
    /// </summary>
    private static string Claim(decimal amount, bool claimsZero) =>
        claimsZero && amount == 0 ? 0m.ToString("0.00", CultureInfo.CurrentCulture) : Money(amount);

    /// <summary>A zero actually typed into a TD1 box, as opposed to the box being left empty.</summary>
    private static bool IsTypedZero(string text) =>
        !string.IsNullOrWhiteSpace(text)
        && Behaviors.CurrencyInputBehavior.TryParse(text, out decimal d)
        && d == 0m;

    /// <summary>
    /// Reads an amount back from a box that may be showing it formatted. Goes through the same
    /// parser the formatting behavior uses, so a figure it wrote can always be read again: a
    /// company keeping books in a currency other than its machine's would otherwise round-trip
    /// its own salary field to zero.
    /// </summary>
    private static decimal Parse(string text) =>
        Behaviors.CurrencyInputBehavior.TryParse(text, out decimal d) ? d : 0m;

    private void Clear()
    {
        Name = string.Empty;
        EmployeeNumber = string.Empty;
        IsSalaried = true;
        PayRate = string.Empty;
        PayFrequency = PayFrequency.Biweekly;
        StandardHoursPerWeek = string.Empty;
        FederalClaimAmount = string.Empty;
        ProvincialClaimAmount = string.Empty;
        OntarioDependants = string.Empty;
        IsCppExempt = false;
        IsEiExempt = false;
        StartDate = null;
        EndDate = null;
        Sin = string.Empty;
        AddressStreet = string.Empty;
        AddressCity = string.Empty;
        AddressProvince = string.Empty;
        AddressPostalCode = string.Empty;
        AddressCountry = "Canada";
        DentalBenefit = DentalBenefitCode.NotEligible;
        Notes = string.Empty;
        NameError = string.Empty;
        PayRateError = string.Empty;
        EndDateError = string.Empty;
        SinError = string.Empty;
        AddressError = string.Empty;
        AddressProvinceError = string.Empty;
        AddressPostalCodeError = string.Empty;
    }
}
