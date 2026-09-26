using System.Collections.ObjectModel;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services.Payroll;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using ArgoBooks.Utilities;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Year end: produce the T4 slips, the summary, and the XML for filing.
///
/// Used once a year, so it is a modal from the Pay runs page rather than a permanent nav item.
/// Nothing here is stored: the return is rebuilt from approved pay runs every time it is
/// opened, so it always agrees with the books.
/// </summary>
public partial class YearEndModalViewModel : ViewModelBase
{
    private readonly T4Service _t4 = new();
    private readonly Rl1Service _rl1 = new();
    private T4Return? _return;
    private Rl1Return? _quebecReturn;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// Nullable because the ComboBox pushes null back the instant its selected item leaves the
    /// list, and reopening the modal refills that list. Bound to an int, that null had nowhere
    /// to go and surfaced as a conversion error under the control on every reopen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems))]
    [NotifyPropertyChangedFor(nameof(CanFile))]
    private int? _selectedYear = DateTime.Today.Year;

    public ObservableCollection<int> AvailableYears { get; } = [];

    /// <summary>One row per employee, so the figures can be checked before anything is filed.</summary>
    public ObservableCollection<T4RowViewModel> Rows { get; } = [];

    /// <summary>
    /// Everything CRA would reject, or that would make the filing wrong. Shown all at once
    /// rather than one at a time, because fixing them means visiting several screens and it is
    /// better to know the whole list before starting.
    /// </summary>
    public ObservableCollection<string> Problems { get; } = [];

    public bool HasProblems => Problems.Count > 0;

    /// <summary>
    /// Worth knowing before filing, but not a reason to stop. Separate from Problems because
    /// mixing the two meant a missing social insurance number, which CRA accepts, disabled the
    /// export button with no way to clear it from this screen.
    /// </summary>
    public ObservableCollection<string> Warnings { get; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>
    /// The slips can always be produced. Filing is what a problem blocks, and the distinction
    /// matters: an employer with a missing SIN can still hand out stubs while chasing it.
    /// </summary>
    public bool CanFile => Problems.Count == 0 && Rows.Count > 0
                           && (!IsAmending || Rows.Any(r => r.IsSelected));

    public bool HasRows => Rows.Count > 0;

    [ObservableProperty]
    private string _totalIncome = "$0.00";

    [ObservableProperty]
    private string _totalDeductions = "$0.00";

    [ObservableProperty]
    private string _totalRemitted = "$0.00";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    #region Amendments

    /// <summary>
    /// Original, amendment or cancellation.
    ///
    /// CRA is explicit that an amended return must not include original slips and vice versa,
    /// so the two are separate submissions. That is why choosing anything but Original turns on
    /// per-employee selection below: the app has no record of what was filed last time, so only
    /// the employer knows which slips actually changed, and sending all of them would restate
    /// every employee as amended.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAmending))]
    [NotifyPropertyChangedFor(nameof(CanFile))]
    private T4ReportType _filingType = T4ReportType.Original;

    public bool IsAmending => FilingType != T4ReportType.Original;

    /// <summary>Why it was amended. CRA takes it on the summary, for an amendment only.</summary>
    [ObservableProperty]
    private string _amendmentNote = string.Empty;

    /// <summary>
    /// Three booleans rather than a bound enum, so the radio buttons need no converter. Each
    /// setter ignores being set to false, which is what the deselected button does when its
    /// sibling is picked.
    /// </summary>
    public bool IsOriginalFiling
    {
        get => FilingType == T4ReportType.Original;
        set { if (value) { FilingType = T4ReportType.Original; } }
    }

    public bool IsAmendmentFiling
    {
        get => FilingType == T4ReportType.Amendment;
        set { if (value) { FilingType = T4ReportType.Amendment; } }
    }

    public bool IsCancelFiling
    {
        get => FilingType == T4ReportType.Cancel;
        set { if (value) { FilingType = T4ReportType.Cancel; } }
    }

    partial void OnFilingTypeChanged(T4ReportType value)
    {
        OnPropertyChanged(nameof(IsOriginalFiling));
        OnPropertyChanged(nameof(IsAmendmentFiling));
        OnPropertyChanged(nameof(IsCancelFiling));

        // Selecting every row on the way in matches what the employer usually wants when they
        // switch to Cancel, and is one click away from what they want for an amendment.
        foreach (T4RowViewModel row in Rows)
        {
            row.IsSelected = value == T4ReportType.Cancel;
        }

        OnPropertyChanged(nameof(CanFile));
    }

    /// <summary>The slips that will actually be filed.</summary>
    private List<T4Slip> SlipsToFile() =>
        _return == null
            ? []
            : !IsAmending
                ? _return.Slips
                : _return.Slips
                    .Where(s => Rows.FirstOrDefault(r => r.EmployeeId == s.EmployeeId
                                                         && r.Province == s.ProvinceOfEmployment)?.IsSelected == true)
                    .ToList();

    /// <summary>
    /// Called by each row when its checkbox moves, because CanFile depends on how many are
    /// ticked and an amendment with none ticked would file an empty return.
    /// </summary>
    private void OnRowSelectionChanged() => OnPropertyChanged(nameof(CanFile));

    #endregion

    #region Quebec

    /// <summary>
    /// A Quebec employer files twice: a T4 with CRA and an RL-1 with Revenu Quebec. The RL-1
    /// half of this screen only appears when there is actually a Quebec employee, so the vast
    /// majority of employers never see it.
    /// </summary>
    [ObservableProperty]
    private bool _hasQuebec;

    /// <summary>
    /// Kept apart from <see cref="Problems"/> because the two filings fail for different
    /// reasons and are fixed in different places. A missing Revenu Quebec number must not read
    /// as a reason the T4 cannot go.
    /// </summary>
    public ObservableCollection<string> QuebecProblems { get; } = [];

    public bool HasQuebecProblems => QuebecProblems.Count > 0;

    /// <summary>
    /// What the RL-1 output actually is. Always shown when there are Quebec employees, and not
    /// in <see cref="QuebecProblems"/>, because it is a fact about this app rather than a fault
    /// in the employer's data and there is nothing they can do to clear it.
    /// </summary>
    public static string QuebecFilingNotice => Rl1Service.FilingNotice;

    [ObservableProperty]
    private string _quebecIdentificationNumber = string.Empty;

    [ObservableProperty]
    private string _quebecTotalRemitted = "$0.00";

    partial void OnQuebecIdentificationNumberChanged(string value) => SaveDetails();

    #endregion

    #region Filing details

    /// <summary>
    /// The three things CRA needs that are not derivable from pay runs. They live here rather
    /// than in company settings because this is the only screen that needs them and the only
    /// screen that reports their absence: being told what is missing and being able to fix it
    /// in the same place beats a message pointing at a settings tab.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountNumberError))]
    private string _accountNumber = string.Empty;

    /// <summary>
    /// Why this particular number will not do, shown under the field.
    ///
    /// The problem list at the top of the modal already says filing is blocked and why, but it
    /// scrolls out of sight, and the export button sits at the bottom with nothing on it to
    /// explain why it is dead. Somebody who has typed the wrong thing is looking at the field,
    /// so that is where it has to be said.
    ///
    /// Empty is deliberately not an error. It is different from wrong: the field is already
    /// marked required and the problem list reports it as missing, and colouring it red before
    /// anything has been typed is nagging rather than help.
    /// </summary>
    public string AccountNumberError =>
        string.IsNullOrWhiteSpace(AccountNumber) || T4Service.IsPayrollAccountNumber(AccountNumber)
            ? string.Empty
            : "Nine digits, then RP, then four: 000000000RP0000.";

    [ObservableProperty]
    private string _contactName = string.Empty;

    [ObservableProperty]
    private string _contactPhone = string.Empty;

    /// <summary>
    /// Where CRA writes back about the filing. Required by the T619 transmittal record that
    /// wraps the submission, so without it the upload is rejected outright.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContactEmailError))]
    private string _contactEmail = string.Empty;

    /// <summary>
    /// Shown under the field, on the same reasoning as <see cref="AccountNumberError"/>: the
    /// problem list scrolls away long before the export button, and empty is not the same as
    /// wrong.
    /// </summary>
    public string ContactEmailError =>
        string.IsNullOrWhiteSpace(ContactEmail) || T4Service.IsEmailAddress(ContactEmail)
            ? string.Empty
            : "That does not look like an email address.";

    /// <summary>
    /// How often CRA wants the deductions. Not a filing detail, but this is the only screen that
    /// holds payroll settings, and the deadline it drives is on the page the Year end button sits
    /// on, so it is one click from where the question gets asked.
    /// </summary>
    [ObservableProperty]
    private RemitterType _remitterType = RemitterType.Regular;

    public ObservableCollection<RemitterType> RemitterTypes { get; } =
    [
        RemitterType.Regular,
        RemitterType.Quarterly,
        RemitterType.AcceleratedThreshold1,
        RemitterType.AcceleratedThreshold2,
    ];

    /// <summary>The dates in plain words, so nobody has to know what an AMWA is to choose.</summary>
    public string RemitterTypeDescription => RemitterType.Description();

    /// <summary>Stops the setters writing back while Open is filling them in.</summary>
    private bool _loading;

    partial void OnAccountNumberChanged(string value) => SaveDetails();

    partial void OnContactNameChanged(string value) => SaveDetails();

    partial void OnContactPhoneChanged(string value) => SaveDetails();

    partial void OnContactEmailChanged(string value) => SaveDetails();

    partial void OnRemitterTypeChanged(RemitterType value)
    {
        OnPropertyChanged(nameof(RemitterTypeDescription));
        SaveDetails();
    }

    /// <summary>
    /// Written straight through as they are typed, so the problem list updates live and the
    /// employer can watch the reason they cannot file disappear.
    /// </summary>
    private void SaveDetails()
    {
        if (_loading || App.CompanyManager?.CompanyData is not { } data)
        {
            return;
        }

        data.Settings.Company.PayrollAccountNumber = AccountNumber.Trim();
        data.Settings.Company.PayrollContactName = ContactName.Trim();
        data.Settings.Company.PayrollContactPhone = ContactPhone.Trim();
        data.Settings.Company.PayrollContactEmail = ContactEmail.Trim();
        data.Settings.Company.QuebecIdentificationNumber = QuebecIdentificationNumber.Trim();
        data.Settings.Company.RemitterType = RemitterType;
        App.CompanyManager?.MarkAsChanged();

        Rebuild();
    }

    #endregion

    partial void OnSelectedYearChanged(int? value)
    {
        // Ignore the transient null the ComboBox reports while the year list is being refilled.
        // Rebuilding then would wipe the screen and, worse, leave it wiped if the refill picked
        // the same year and so raised no second change.
        if (value.HasValue && !_refillingYears)
        {
            Rebuild();
        }
    }

    /// <summary>Set while <see cref="AvailableYears"/> is being replaced.</summary>
    private bool _refillingYears;

    public void Open()
    {
        CompanyData? data = App.CompanyManager?.CompanyData;

        // Back to an original filing. This view model is a shell singleton, so an amendment
        // filed earlier in the session left the Amendment radio and its note still set, and
        // Rebuild deliberately re-applies the previous slip selection by employee id. Reopening
        // Year end would then export another amended return, or a cancellation, instead of an
        // original.
        FilingType = T4ReportType.Original;
        AmendmentNote = string.Empty;

        _refillingYears = true;
        AvailableYears.Clear();

        // Only years that actually have pay runs. Offering every year since 2020 would invite
        // filing an empty return.
        var years = data?.PayRuns
            .Where(r => r.Status != PayRunStatus.Draft)
            .Select(r => r.PayDate.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList() ?? [];

        foreach (int year in years)
        {
            AvailableYears.Add(year);
        }

        if (AvailableYears.Count == 0)
        {
            AvailableYears.Add(DateTime.Today.Year);
        }

        _loading = true;
        AccountNumber = data?.Settings.Company.PayrollAccountNumber ?? string.Empty;
        ContactName = data?.Settings.Company.PayrollContactName ?? string.Empty;
        ContactPhone = data?.Settings.Company.PayrollContactPhone ?? data?.Settings.Company.Phone ?? string.Empty;
        ContactEmail = data?.Settings.Company.PayrollContactEmail ?? data?.Settings.Company.Email ?? string.Empty;
        RemitterType = data?.Settings.Company.RemitterType ?? RemitterType.Regular;
        QuebecIdentificationNumber = data?.Settings.Company.QuebecIdentificationNumber ?? string.Empty;
        _loading = false;

        // Reselect before lifting the guard, so the whole refill produces exactly one rebuild
        // here rather than one from the setter and another from this call.
        //
        // Clearing AvailableYears makes the ComboBox drop its selection, and assigning the same
        // year back raises no change, so the control sat blank while this held a good year:
        // filing worked, the box just looked empty. Going through null first makes the
        // assignment a real change every time. OnSelectedYearChanged ignores the null.
        SelectedYear = null;
        SelectedYear = AvailableYears[0];
        _refillingYears = false;

        Rebuild();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    private void Rebuild()
    {
        // Rebuild runs on every keystroke in the filing-detail boxes, so the ticks have to survive it.
        HashSet<(string, string)> selected = Rows.Where(r => r.IsSelected)
            .Select(r => (r.EmployeeId, r.Province))
            .ToHashSet();

        Rows.Clear();
        Problems.Clear();
        Warnings.Clear();
        QuebecProblems.Clear();
        StatusMessage = string.Empty;

        CompanyData? data = App.CompanyManager?.CompanyData;
        if (data == null || SelectedYear is not { } year)
        {
            return;
        }

        _return = _t4.Build(data, year);

        foreach (T4Slip slip in _return.Slips)
        {
            string name = $"{slip.GivenName} {slip.Surname}".Trim();

            Rows.Add(new T4RowViewModel(OnRowSelectionChanged)
            {
                EmployeeId = slip.EmployeeId,
                Province = slip.ProvinceOfEmployment,
                Name = HasSeveralSlips(_return, slip) ? $"{name} ({slip.ProvinceOfEmployment})" : name,
                Income = CurrencyService.Format(slip.EmploymentIncome),
                Cpp = CurrencyService.Format(slip.CppContributions + slip.Cpp2Contributions),
                Ei = CurrencyService.Format(slip.EiPremiums),
                Tax = CurrencyService.Format(slip.IncomeTaxDeducted),
                HasSin = slip.Sin.Count(char.IsAsciiDigit) == 9,
                IsSelected = selected.Contains((slip.EmployeeId, slip.ProvinceOfEmployment)),
            });
        }

        foreach (string problem in T4Service.Validate(data, _return))
        {
            Problems.Add(problem);
        }

        foreach (string warning in T4Service.Warnings(_return))
        {
            Warnings.Add(warning);
        }

        TotalIncome = CurrencyService.Format(_return.TotalEmploymentIncome);
        TotalDeductions = CurrencyService.Format(
            _return.TotalEmployeeCpp + _return.TotalEmployeeCpp2 + _return.TotalEmployeeEi + _return.TotalIncomeTax);
        TotalRemitted = CurrencyService.Format(
            _return.TotalEmployeeCpp + _return.TotalEmployeeCpp2 + _return.TotalEmployerCpp + _return.TotalEmployerCpp2
            + _return.TotalEmployeeEi + _return.TotalEmployerEi + _return.TotalIncomeTax);

        BuildQuebec(data, year);

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(CanFile));
        OnPropertyChanged(nameof(HasRows));
    }

    /// <summary>
    /// The Revenu Quebec half. Built from the same pay runs, but it is a genuinely separate
    /// return: it covers only Quebec employees, and its totals are supposed to disagree with
    /// the T4's.
    /// </summary>
    private void BuildQuebec(CompanyData data, int year)
    {
        HasQuebec = Rl1Service.HasQuebecEmployees(data, year);

        if (!HasQuebec)
        {
            _quebecReturn = null;
            QuebecTotalRemitted = CurrencyService.Format(0m);
            OnPropertyChanged(nameof(HasQuebecProblems));
            return;
        }

        _quebecReturn = _rl1.Build(data, year);

        foreach (string problem in Rl1Service.Validate(data, _quebecReturn))
        {
            QuebecProblems.Add(problem);
        }

        QuebecTotalRemitted = CurrencyService.Format(_quebecReturn.TotalRemittable);

        OnPropertyChanged(nameof(HasQuebecProblems));
    }

    /// <summary>An employee who changed province during the year has a T4 for each.</summary>
    private static bool HasSeveralSlips(T4Return t4, T4Slip slip) =>
        t4.Slips.Count(s => s.EmployeeId == slip.EmployeeId) > 1;

    /// <summary>
    /// Saves a slip per employee and province plus the summary, into a folder of the user's
    /// choosing.
    /// </summary>
    [RelayCommand]
    private async Task DownloadSlipsAsync()
    {
        if (_return == null || _return.Slips.Count == 0)
        {
            return;
        }

        string? directory = await PickFolderAsync("Choose where to save the T4 slips");
        if (directory == null)
        {
            return;
        }

        try
        {
            T4Return t4 = _return;

            directory = ExportFolderHelper.Resolve(
                directory, $"T4 {t4.TaxYear}", t4.Slips.Count + 1);

            foreach (T4Slip slip in t4.Slips)
            {
                byte[] bytes = await Task.Run(() => T4PdfRenderer.RenderSlip(t4, slip));
                string who = HasSeveralSlips(t4, slip)
                    ? $"{slip.GivenName} {slip.Surname} {slip.ProvinceOfEmployment}"
                    : $"{slip.GivenName} {slip.Surname}";
                string name = $"T4-{t4.TaxYear}-{ExportFolderHelper.Sanitize(who)}.pdf";
                await File.WriteAllBytesAsync(Path.Combine(directory, name), bytes);
            }

            byte[] summary = await Task.Run(() => T4PdfRenderer.RenderSummary(t4));
            await File.WriteAllBytesAsync(Path.Combine(directory, $"T4-Summary-{t4.TaxYear}.pdf"), summary);

            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.T4SlipsGenerated);

            StatusMessage = "Saved {0} slips and the summary.".TranslateFormat(t4.Slips.Count);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.T4Slips");
            StatusMessage = "Could not save the slips: {0}".TranslateFormat(ex.Message);
        }
    }

    /// <summary>
    /// Saves an RL-1 per Quebec employee plus the RL-1 Summary.
    ///
    /// There is no XML counterpart to this, unlike the T4. Revenu Quebec does not publish the
    /// RL-1 XML specification, so under six slips these PDFs are the filing, sent by mail, and
    /// above five slips the employer is told to go elsewhere rather than handed paper that will
    /// be sent back.
    /// </summary>
    [RelayCommand]
    private async Task DownloadQuebecSlipsAsync()
    {
        if (_quebecReturn == null || _quebecReturn.Slips.Count == 0)
        {
            return;
        }

        string? directory = await PickFolderAsync("Choose where to save the RL-1 slips");
        if (directory == null)
        {
            return;
        }

        try
        {
            Rl1Return rl1 = _quebecReturn;

            directory = ExportFolderHelper.Resolve(
                directory, $"RL-1 {rl1.TaxYear}", rl1.Slips.Count + 1);

            // The slip code is printed on the PDF so the employer keys the right one in. Unlike
            // the T4 there is no per-slip selection: these are printed and re-keyed by hand, so
            // the employer chooses which ones to actually send.
            rl1.SlipCode = FilingType switch
            {
                T4ReportType.Amendment => Rl1SlipCode.Amended,
                T4ReportType.Cancel => Rl1SlipCode.Cancelled,
                _ => Rl1SlipCode.Original,
            };

            foreach (Rl1Slip slip in rl1.Slips)
            {
                byte[] bytes = await Task.Run(() => Rl1PdfRenderer.RenderSlip(rl1, slip));
                string name = $"RL1-{rl1.TaxYear}-{ExportFolderHelper.Sanitize($"{slip.GivenName} {slip.Surname}")}.pdf";
                await File.WriteAllBytesAsync(Path.Combine(directory, name), bytes);
            }

            byte[] summary = await Task.Run(() => Rl1PdfRenderer.RenderSummary(rl1));
            await File.WriteAllBytesAsync(Path.Combine(directory, $"RL1-Summary-{rl1.TaxYear}.pdf"), summary);

            StatusMessage = "Saved {0} RL-1 slips and the summary.".TranslateFormat(rl1.Slips.Count);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.Rl1Slips");
            StatusMessage = "Could not save the RL-1 slips: {0}".TranslateFormat(ex.Message);
        }
    }

    /// <summary>
    /// Writes the XML CRA accepts. Deliberately separate from the slips: the slips go to
    /// employees and the XML goes to CRA, and producing one does not mean the other happened.
    /// </summary>
    [RelayCommand]
    private async Task ExportXmlAsync()
    {
        if (_return == null || !CanFile)
        {
            return;
        }

        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        T4Return? filing = BuildFilingReturn();

        if (filing == null || filing.Slips.Count == 0)
        {
            return;
        }

        try
        {
            string suffix = FilingType switch
            {
                T4ReportType.Amendment => "-amended",
                T4ReportType.Cancel => "-cancelled",
                _ => string.Empty,
            };

            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the T4 XML for filing".Translate(),
                SuggestedFileName = $"T4-{_return.TaxYear}{suffix}.xml",
                DefaultExtension = "xml",
                FileTypeChoices = [new FilePickerFileType("XML") { Patterns = ["*.xml"] }],
            });

            if (file == null)
            {
                return;
            }

            await File.WriteAllTextAsync(file.Path.LocalPath, T4XmlWriter.BuildString(filing), new UTF8Encoding(false));

            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.T4XmlGenerated);

            StatusMessage = IsAmending
                ? "Saved {0} slip(s). Upload it through CRA's Internet File Transfer. Send it on its own: CRA rejects a return that mixes amended and original slips."
                    .TranslateFormat(filing.Slips.Count)
                : "Saved. Upload it through CRA's Internet File Transfer, which will add the transmittal record."
                    .Translate();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.T4Xml");
            StatusMessage = "Could not save the XML: {0}".TranslateFormat(ex.Message);
        }
    }

    /// <summary>
    /// The return that actually gets written, as opposed to the one on screen.
    ///
    /// A separate object rather than the built one mutated, so the figures on screen keep
    /// showing the whole year while the file carries only what is being filed. CRA totals the
    /// summary from the slips it accompanies, so the totals follow the selection by themselves.
    ///
    /// Every field has to be carried across. A field left off is not a compile error and not
    /// visible anywhere in the app: it is an element CRA rejects the submission over, months
    /// later.
    /// </summary>
    public T4Return? BuildFilingReturn() => _return == null ? null : new T4Return
    {
        TaxYear = _return.TaxYear,
        PayrollAccountNumber = _return.PayrollAccountNumber,
        EmployerName = _return.EmployerName,
        EmployerAddress = _return.EmployerAddress,
        ContactName = _return.ContactName,
        ContactPhone = _return.ContactPhone,
        ContactEmail = _return.ContactEmail,
        LanguageCode = _return.LanguageCode,
        ReportType = FilingType,
        AmendmentNote = AmendmentNote,
        Slips = SlipsToFile(),
    };

    private static async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider == null)
        {
            return null;
        }

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title.Translate(), AllowMultiple = false });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

}

/// <summary>One slip's line on the year end review: an employee, in one province.</summary>
public partial class T4RowViewModel : ObservableObject
{
    private readonly Action? _selectionChanged;

    public T4RowViewModel(Action? selectionChanged = null) => _selectionChanged = selectionChanged;

    public string EmployeeId { get; init; } = string.Empty;

    /// <summary>Together with the employee, which slip this is.</summary>
    public string Province { get; init; } = string.Empty;

    /// <summary>
    /// Whether this slip goes in an amended or cancelled return. Ignored on an original, where
    /// every slip is filed.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _income = string.Empty;

    [ObservableProperty]
    private string _cpp = string.Empty;

    [ObservableProperty]
    private string _ei = string.Empty;

    [ObservableProperty]
    private string _tax = string.Empty;

    /// <summary>Shown per row, so a missing one can be traced to a person at a glance.</summary>
    [ObservableProperty]
    private bool _hasSin;
}
