using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using ArgoBooks.Utilities;
using ArgoBooks.Core.Services.Payroll;
using ArgoBooks.Localization;
using ArgoBooks.Shared.Telemetry;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// The Record of Employment screen, opened from the employee row rather than from year end.
///
/// An ROE is due five calendar days after the end of the pay period in which someone stops being
/// paid, which has nothing to do with December, so putting it behind the year end flow would
/// mean somebody who left in March waited ten months past their deadline.
///
/// It exists as a form at all because of block 16. Everything else here is worked out from the
/// pay runs, but the app knows only that somebody stopped being paid, not whether they quit,
/// were dismissed or went on leave, and those are different legal statements with different
/// consequences for the employee's claim. That is asked, never defaulted.
/// </summary>
public partial class RoeModalViewModel : ViewModelBase
{
    private RoeWorksheet? _sheet;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _employeeName = string.Empty;

    #region What the pay runs already worked out

    [ObservableProperty]
    private string _firstDayWorked = string.Empty;

    [ObservableProperty]
    private string _lastDayPaid = string.Empty;

    [ObservableProperty]
    private string _finalPeriodEnd = string.Empty;

    [ObservableProperty]
    private string _insurableHours = string.Empty;

    [ObservableProperty]
    private string _insurableEarnings = string.Empty;

    [ObservableProperty]
    private string _periodSummary = string.Empty;

    /// <summary>Set when block 15A could not be worked out, and shown in place of the figure.</summary>
    [ObservableProperty]
    private string _hoursUnavailableReason = string.Empty;

    #endregion

    #region What has to be asked

    public ObservableCollection<string> ReasonOptions { get; } = [];

    public ObservableCollection<string> RecallOptions { get; } = [];

    public ObservableCollection<string> LanguageOptions { get; } = [];

    /// <summary>
    /// Deliberately starts unselected. A default here is a legal statement nobody made, and
    /// "shortage of work" is both the most common reason and the most expensive one to be wrong
    /// about, because it is the one that does not require the employee to justify leaving.
    /// </summary>
    [ObservableProperty]
    private string? _selectedReason;

    [ObservableProperty]
    private string? _selectedRecall;

    [ObservableProperty]
    private DateTimeOffset? _recallDate;

    [ObservableProperty]
    private string? _selectedLanguage;

    [ObservableProperty]
    private string _occupation = string.Empty;

    [ObservableProperty]
    private string _payrollReferenceNumber = string.Empty;

    [ObservableProperty]
    private string _contactFirstName = string.Empty;

    [ObservableProperty]
    private string _contactLastName = string.Empty;

    [ObservableProperty]
    private string _contactPhone = string.Empty;

    [ObservableProperty]
    private string _contactPhoneExtension = string.Empty;

    /// <summary>
    /// Block 17A as typed. Starts blank rather than at the final pay run's vacation pay, which is
    /// often the percentage paid with every cheque, and that must never be reported.
    /// </summary>
    [ObservableProperty]
    private string _vacationPayOnLeaving = string.Empty;

    /// <summary>The final pay run's vacation pay, offered as a prompt for block 17A.</summary>
    [ObservableProperty]
    private string _vacationPayHint = string.Empty;

    [ObservableProperty]
    private string _comments = string.Empty;

    #endregion

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Every reason the file cannot be produced yet, shown at once rather than one at a time.
    ///
    /// Replaced wholesale rather than cleared and refilled. This is rebuilt on every keystroke in
    /// the contact fields, and mutating a bound collection item by item while the border hosting
    /// it is being shown or hidden in the same pass is how you get a torn visual tree.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _problems = [];

    [ObservableProperty]
    private bool _canExport;

    /// <summary>True only when a recall date is meaningful, so the picker is not offered otherwise.</summary>
    public bool ShowRecallDate => SelectedRecall == RecallLabel(RoeRecall.ExpectedDate);

    /// <summary>Service Canada sends an ROE carrying a comment to manual review, which delays the claim.</summary>
    public bool ShowCommentWarning => !string.IsNullOrWhiteSpace(Comments);

    public RoeModalViewModel()
    {
        foreach (RoeReason reason in Enum.GetValues<RoeReason>())
        {
            ReasonOptions.Add(ReasonLabel(reason));
        }

        foreach (RoeRecall recall in Enum.GetValues<RoeRecall>())
        {
            RecallOptions.Add(RecallLabel(recall));
        }

        foreach (RoeLanguage language in Enum.GetValues<RoeLanguage>())
        {
            LanguageOptions.Add(LanguageLabel(language));
        }
    }

    public void Show(RoeWorksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        _sheet = sheet;

        EmployeeName = sheet.EmployeeName;
        FirstDayWorked = Date(sheet.FirstDayWorked);
        LastDayPaid = Date(sheet.LastDayPaid);
        FinalPeriodEnd = Date(sheet.FinalPeriodEnd);
        HoursUnavailableReason = sheet.HoursUnavailableReason ?? string.Empty;

        InsurableHours = sheet.TotalInsurableHours?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
        InsurableEarnings = sheet.TotalInsurableEarnings.ToString("C", CultureInfo.CurrentCulture);
        PeriodSummary = "{0} pay period(s), most recent first".TranslateFormat(sheet.Periods.Count);

        // Seeded from the payroll contact already held for the T4, so it is confirmed rather
        // than retyped every time somebody leaves.
        ContactFirstName = sheet.ContactFirstName ?? string.Empty;
        ContactLastName = sheet.ContactLastName ?? string.Empty;
        ContactPhone = sheet.ContactPhone ?? string.Empty;
        ContactPhoneExtension = string.Empty;

        SelectedReason = null;
        SelectedRecall = RecallLabel(sheet.Recall);
        SelectedLanguage = LanguageLabel(sheet.Language);
        RecallDate = null;
        Occupation = string.Empty;
        PayrollReferenceNumber = string.Empty;
        VacationPayOnLeaving = string.Empty;
        VacationPayHint = sheet.VacationPay > 0
            ? "The final pay run included {0} of vacation pay. Enter it here only if it was paid because they left, not vacation pay added to every cheque."
                .TranslateFormat(sheet.VacationPay.ToString("C", CultureInfo.CurrentCulture))
            : string.Empty;
        Comments = string.Empty;
        StatusMessage = string.Empty;

        Revalidate();
        IsOpen = true;
    }

    partial void OnSelectedReasonChanged(string? value) => Revalidate();

    partial void OnSelectedRecallChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowRecallDate));
        Revalidate();
    }

    partial void OnRecallDateChanged(DateTimeOffset? value) => Revalidate();

    partial void OnContactFirstNameChanged(string value) => Revalidate();

    partial void OnContactLastNameChanged(string value) => Revalidate();

    partial void OnContactPhoneChanged(string value) => Revalidate();

    partial void OnVacationPayOnLeavingChanged(string value) => Revalidate();

    partial void OnCommentsChanged(string value)
    {
        OnPropertyChanged(nameof(ShowCommentWarning));
        Revalidate();
    }

    /// <summary>Copies the form onto the worksheet, then asks the writer what is still missing.</summary>
    private void Revalidate()
    {
        if (_sheet == null)
        {
            return;
        }

        Apply();

        List<string> found = RoeXmlWriter.Validate(_sheet).Select(p => p.Translate()).ToList();

        // Assigning an equal list still raises a change and rebuilds the items, so it is worth
        // the comparison: most keystrokes do not alter what is missing.
        if (!found.SequenceEqual(Problems))
        {
            Problems = found;
        }

        CanExport = found.Count == 0;
    }

    private void Apply()
    {
        if (_sheet == null)
        {
            return;
        }

        _sheet.Reason = ReasonFromLabel(SelectedReason);
        _sheet.Recall = RecallFromLabel(SelectedRecall);
        _sheet.RecallDate = RecallDate?.DateTime;
        _sheet.Language = LanguageFromLabel(SelectedLanguage);
        _sheet.Occupation = Occupation;
        _sheet.PayrollReferenceNumber = PayrollReferenceNumber;
        _sheet.Comments = Comments;
        _sheet.ContactFirstName = ContactFirstName;
        _sheet.ContactLastName = ContactLastName;
        _sheet.ContactPhone = ContactPhone;
        _sheet.ContactPhoneExtension = ContactPhoneExtension;

        // The same parser the payroll amount boxes use, so a figure typed the way this machine
        // writes numbers reads back as that figure.
        _sheet.VacationPayOnLeaving = Behaviors.CurrencyInputBehavior.TryParse(VacationPayOnLeaving, out decimal vacation)
            ? vacation
            : null;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>
    /// The worksheet, in the receipt viewer. Needs none of the form filled in, because the usual
    /// thing to do with it is read figures off it while ROE Web is open in a browser.
    /// </summary>
    [RelayCommand]
    private async Task ViewWorksheetAsync()
    {
        if (_sheet == null)
        {
            return;
        }

        try
        {
            Apply();
            byte[] bytes = await Task.Run(() => RoePdfRenderer.Render(_sheet));

            App.ReceiptViewerModal?.ShowDocument(
                "Record of Employment: {0}".TranslateFormat(_sheet.EmployeeName),
                bytes,
                $"ROE-worksheet-{ExportFolderHelper.Sanitize(_sheet.EmployeeName)}.pdf");

            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.RoeWorksheetGenerated);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.Roe");
            StatusMessage = "Could not build the worksheet: {0}".TranslateFormat(ex.Message);
        }
    }

    [RelayCommand]
    private Task SaveXmlAsync() => SaveAsync(draft: false);

    /// <summary>
    /// A draft uploads into ROE Web without being submitted, so the figures can be checked in
    /// their own screen before anything is filed. Worth doing once, on the first ROE.
    /// </summary>
    [RelayCommand]
    private Task SaveDraftXmlAsync() => SaveAsync(draft: true);

    private async Task SaveAsync(bool draft)
    {
        if (_sheet == null)
        {
            return;
        }

        Revalidate();

        if (!CanExport)
        {
            StatusMessage = "Fill in what is listed above first.".Translate();
            return;
        }

        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        try
        {
            string name = ExportFolderHelper.Sanitize(_sheet.EmployeeName);

            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the ROE payroll extract".Translate(),
                SuggestedFileName = draft ? $"ROE-{name}-draft.xml" : $"ROE-{name}.xml",
                DefaultExtension = "xml",
                FileTypeChoices = [new FilePickerFileType("XML") { Patterns = ["*.xml"] }],
            });

            if (file == null)
            {
                return;
            }

            string xml = RoeXmlWriter.BuildString(_sheet, AppInfo.AssemblyVersion?.ToString(3) ?? string.Empty, draft);
            await File.WriteAllTextAsync(file.Path.LocalPath, xml, new UTF8Encoding(false));

            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.RoeXmlGenerated);

            RememberContact();

            StatusMessage = draft
                ? "Saved as a draft. Upload it through ROE Web, check the figures there, then submit it."
                    .Translate()
                : "Saved. Upload it through ROE Web. Service Canada issues the ROE; this file is the payroll extract they read it from."
                    .Translate();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.RoeXml");
            StatusMessage = "Could not save the file: {0}".TranslateFormat(ex.Message);
        }
    }

    /// <summary>
    /// Keeps the contact on the company, so the next person who leaves does not mean typing it
    /// again. The same field the T4 year end screen writes, because it is the same person for
    /// the same reason: whoever the tax authority phones about a filing.
    ///
    /// Saved on a successful export rather than on every keystroke, so a half-typed name never
    /// replaces a good stored one, and only what was actually changed on the form is written.
    /// </summary>
    private void RememberContact()
    {
        if (App.CompanyManager?.CompanyData is { } data &&
            RoeService.ApplyContact(data.Settings.Company, ContactFirstName, ContactLastName, ContactPhone))
        {
            App.CompanyManager.MarkAsChanged();
        }
    }

    private static string Date(DateTime? value) =>
        value?.ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;

    #region Labels
    //
    // Kept as display strings rather than bound enums so the combo boxes translate with
    // everything else on the page, matching how the rest of the payroll modals do it.

    private static string ReasonLabel(RoeReason reason) => reason switch
    {
        RoeReason.ShortageOfWork => "A - Shortage of work, end of contract or season",
        RoeReason.StrikeOrLockout => "B - Strike or lockout",
        RoeReason.ReturnToSchool => "C - Return to school",
        RoeReason.IllnessOrInjury => "D - Illness or injury",
        RoeReason.Quit => "E - Quit",
        RoeReason.Maternity => "F - Maternity",
        RoeReason.Retirement => "G - Mandatory retirement",
        RoeReason.WorkSharing => "H - Work sharing",
        RoeReason.ApprenticeTraining => "J - Apprentice training",
        RoeReason.Other => "K - Other",
        RoeReason.DismissalOrSuspension => "M - Dismissal or suspension",
        RoeReason.LeaveOfAbsence => "N - Leave of absence",
        RoeReason.Parental => "P - Parental",
        RoeReason.CompassionateCare => "Z - Compassionate care or family caregiver",
        _ => "K - Other",
    };

    private static RoeReason? ReasonFromLabel(string? label) =>
        Enum.GetValues<RoeReason>().Cast<RoeReason?>().FirstOrDefault(r => ReasonLabel(r!.Value) == label);

    private static string RecallLabel(RoeRecall recall) => recall switch
    {
        RoeRecall.NotReturning => "Not returning",
        RoeRecall.ExpectedDate => "Returning on a known date",
        _ => "Unknown",
    };

    private static RoeRecall RecallFromLabel(string? label) =>
        Enum.GetValues<RoeRecall>().FirstOrDefault(r => RecallLabel(r) == label, RoeRecall.Unknown);

    private static string LanguageLabel(RoeLanguage language) =>
        language == RoeLanguage.French ? "French" : "English";

    private static RoeLanguage LanguageFromLabel(string? label) =>
        label == LanguageLabel(RoeLanguage.French) ? RoeLanguage.French : RoeLanguage.English;

    #endregion
}
