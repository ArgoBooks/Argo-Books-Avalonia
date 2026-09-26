using System.Collections.ObjectModel;
using System.Globalization;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Utilities;
using ArgoBooks.Core.Validation;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Send to accountant: the year's statements, transactions and receipts, emailed to the business's
/// accountant or saved as a zip. Each pack carries a link to Argo Books, which is how accountants who
/// receive one hear about the app.
/// </summary>
public partial class SendToAccountantModalViewModel : ViewModelBase
{
    private static string CustomRange => "Custom range".Translate();

    private bool _receiptsReady;

    [ObservableProperty]
    private bool _isOpen;

    public ObservableCollection<string> Periods { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomRange))]
    private string? _selectedPeriod;

    public bool IsCustomRange => SelectedPeriod == CustomRange;

    [ObservableProperty]
    private DateTimeOffset? _customStart;

    [ObservableProperty]
    private DateTimeOffset? _customEnd;

    [ObservableProperty]
    private bool _includeReports = true;

    [ObservableProperty]
    private bool _includeTransactions = true;

    [ObservableProperty]
    private bool _includeReceipts = true;

    [ObservableProperty]
    private string _accountantName = string.Empty;

    [ObservableProperty]
    private string _accountantEmail = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string? _emailError;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EmailToAccountantCommand), nameof(SaveAsZipCommand), nameof(CloseCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    partial void OnSelectedPeriodChanged(string? value) => UpdateSize();
    partial void OnCustomStartChanged(DateTimeOffset? value) => UpdateSize();
    partial void OnCustomEndChanged(DateTimeOffset? value) => UpdateSize();
    partial void OnIncludeReceiptsChanged(bool value) => UpdateSize();
    partial void OnAccountantEmailChanged(string value) => EmailError = null;

    public void Open()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null)
            return;

        var thisYear = DateTime.Today.Year;
        var earliest = data.GetEarliestDate().Year;
        var firstYear = earliest > 1900 && earliest <= thisYear ? Math.Max(earliest, thisYear - 15) : thisYear;

        Periods.Clear();
        for (var year = thisYear; year >= firstYear; year--)
            Periods.Add(year.ToString(CultureInfo.InvariantCulture));
        Periods.Add(CustomRange);

        // Books go to the accountant after the year closes, so last year comes first when there is one.
        var lastYear = (thisYear - 1).ToString(CultureInfo.InvariantCulture);
        SelectedPeriod = Periods.Contains(lastYear) ? lastYear : Periods[0];
        CustomStart = new DateTimeOffset(new DateTime(thisYear, 1, 1));
        CustomEnd = new DateTimeOffset(DateTime.Today);

        IncludeReports = true;
        IncludeTransactions = true;
        IncludeReceipts = true;
        AccountantName = data.Settings.Company.AccountantName ?? string.Empty;
        AccountantEmail = data.Settings.Company.AccountantEmail ?? string.Empty;
        Note = string.Empty;
        EmailError = null;
        ErrorMessage = null;
        StatusMessage = null;

        IsOpen = true;
        _ = LoadReceiptsAsync();
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Close() => IsOpen = false;

    private async Task LoadReceiptsAsync()
    {
        _receiptsReady = false;
        UpdateSize();
        try
        {
            if (App.CompanyManager != null)
                await App.CompanyManager.EnsureReceiptsLoadedAsync();
            _receiptsReady = true;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "SendToAccountant.LoadReceipts");
        }
        UpdateSize();
    }

    private (DateTime Start, DateTime End)? Range()
    {
        if (IsCustomRange)
        {
            if (CustomStart is not { } start || CustomEnd is not { } end || end.Date < start.Date)
                return null;
            return (start.Date, end.Date);
        }

        return int.TryParse(SelectedPeriod, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? (new DateTime(year, 1, 1), new DateTime(year, 12, 31))
            : null;
    }

    private void UpdateSize()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null || Range() is not { } range)
        {
            SizeText = string.Empty;
            return;
        }

        if (!AccountantPack.HasDataInRange(data, range.Start, range.End))
        {
            SizeText = "Nothing was recorded in this period.".Translate();
            return;
        }

        if (!IncludeReceipts)
        {
            SizeText = string.Empty;
            return;
        }

        if (!_receiptsReady)
        {
            SizeText = "Counting receipts...".Translate();
            return;
        }

        var receipts = AccountantPack.ReceiptsInRange(data, range.Start, range.End);
        if (receipts.Count == 0)
        {
            SizeText = "No receipts in this period.".Translate();
            return;
        }

        var bytes = receipts.Sum(r => AccountantPack.DecodedSize(r.Receipt.FileData!));
        SizeText = bytes > AccountantPack.MaxEmailBytes
            ? "{0} receipts, {1}. That is over the {2} an email can carry, so they go in the zip only.".TranslateFormat(
                receipts.Count, FormatSize(bytes), FormatSize(AccountantPack.MaxEmailBytes))
            : "{0} receipts, {1}.".TranslateFormat(receipts.Count, FormatSize(bytes));
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " MB"
            : Math.Max(1, bytes / 1024).ToString(CultureInfo.CurrentCulture) + " KB";

    private bool TryGetRange(out (DateTime Start, DateTime End) range)
    {
        ErrorMessage = null;
        StatusMessage = null;
        range = default;

        if (Range() is not { } chosen)
        {
            ErrorMessage = "Choose a start date on or before the end date.".Translate();
            return false;
        }
        if (!IncludeReports && !IncludeTransactions && !IncludeReceipts)
        {
            ErrorMessage = "Choose at least one thing to include.".Translate();
            return false;
        }

        if (App.CompanyManager?.CompanyData is { } data && !AccountantPack.HasDataInRange(data, chosen.Start, chosen.End))
        {
            ErrorMessage = "Nothing was recorded in {0}, so there is nothing to send. Choose another period."
                .TranslateFormat(AccountantPack.PeriodLabel(chosen.Start, chosen.End));
            return false;
        }

        range = chosen;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SaveAsZipAsync()
    {
        if (!TryGetRange(out var range))
            return;

        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (topLevel?.StorageProvider == null)
            return;

        var period = AccountantPack.PeriodLabel(range.Start, range.End);
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save accountant pack".Translate(),
            SuggestedFileName = $"{SafeFileName.Create(App.CompanyManager?.CurrentCompanyName, "Company")} books {period}.zip",
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }]
        });
        if (file == null)
            return;

        await RunAsync(async () =>
        {
            var pack = await BuildAsync(range.Start, range.End);
            if (pack == null)
                return;

            try
            {
                BusyText = "Creating zip...".Translate();
                var path = file.Path.LocalPath;
                var readme = AccountantPack.Readme(pack.CompanyName, pack.Period, pack.HasReports, pack.HasTransactions, pack.Receipts.Count);
                await Task.Run(() =>
                {
                    using var stream = File.Create(path);
                    AccountantPack.WriteZip(stream, pack.Files, pack.Receipts, readme);
                });

                RememberAccountant();
                StatusMessage = "Saved {0}.".TranslateFormat(Path.GetFileName(path));
                _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.AccountantPackSent, "zip");
                ShowInFolder(path);
            }
            finally
            {
                DeleteStaging(pack);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task EmailToAccountantAsync()
    {
        if (!TryGetRange(out var range))
            return;

        var email = AccountantEmail.Trim();
        var storedEmail = App.CompanyManager?.CompanyData?.Settings.Company.AccountantEmail;
        if (email.Length == 0 || !DataValidator.IsValidOrUnchangedEmail(email, storedEmail))
        {
            EmailError = "Enter your accountant's email address.".Translate();
            return;
        }

        await RunAsync(async () =>
        {
            var pack = await BuildAsync(range.Start, range.End);
            if (pack == null)
                return;

            try
            {
                BusyText = "Sending...".Translate();

                var attachments = new List<AccountantPackAttachment>();
                long total = 0;
                foreach (var (entryName, path) in pack.Files)
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    total += bytes.Length;
                    attachments.Add(Attachment(Path.GetFileName(entryName), bytes));
                }

                if (total > AccountantPack.MaxEmailBytes)
                {
                    ErrorMessage = "The reports and spreadsheet come to {0}, over the {1} an email can carry. Use Save as Zip instead and share it another way."
                        .TranslateFormat(FormatSize(total), FormatSize(AccountantPack.MaxEmailBytes));
                    return;
                }

                // Receipts ride along as one zip when they fit, and are left out rather than failing the send when they don't.
                var receiptsOmitted = false;
                if (pack.Receipts.Count > 0)
                {
                    using var receiptsZip = new MemoryStream();
                    await Task.Run(() => AccountantPack.WriteZip(receiptsZip, [], pack.Receipts, null));
                    if (total + receiptsZip.Length <= AccountantPack.MaxEmailBytes)
                        attachments.Add(Attachment("Receipts.zip", receiptsZip.ToArray()));
                    else
                        receiptsOmitted = true;
                }

                var company = App.CompanyManager!.CompanyData!.Settings.Company;
                using var service = new AccountantPackEmailService();
                var response = await service.SendAsync(new AccountantPackEmailRequest
                {
                    To = email,
                    ToName = string.IsNullOrWhiteSpace(AccountantName) ? null : AccountantName.Trim(),
                    ReplyTo = string.IsNullOrWhiteSpace(company.Email) ? null : company.Email.Trim(),
                    CompanyName = pack.CompanyName,
                    Period = pack.Period,
                    Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
                    ReceiptsOmitted = receiptsOmitted,
                    Attachments = attachments
                });

                if (!response.Success)
                {
                    ErrorMessage = response.Message;
                    return;
                }

                RememberAccountant();
                StatusMessage = receiptsOmitted
                    ? "Sent to {0}, without the receipts: they are over the {1} an email can carry. Use Save as Zip to share those."
                        .TranslateFormat(email, FormatSize(AccountantPack.MaxEmailBytes))
                    : "Sent to {0}.".TranslateFormat(email);
                _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.AccountantPackSent, "email");
            }
            finally
            {
                DeleteStaging(pack);
            }
        });
    }

    private sealed record Pack(
        string StagingDirectory,
        string CompanyName,
        string Period,
        List<(string EntryName, string FilePath)> Files,
        List<(string EntryName, Receipt Receipt)> Receipts)
    {
        public bool HasReports => Files.Any(f => f.EntryName.StartsWith("Reports/", StringComparison.Ordinal));
        public bool HasTransactions => Files.Any(f => f.EntryName == AccountantPack.TransactionsFileName);
    }

    private async Task<Pack?> BuildAsync(DateTime start, DateTime end)
    {
        var manager = App.CompanyManager;
        var data = manager?.CompanyData;
        if (manager == null || data == null)
            return null;

        var staging = Path.Combine(Path.GetTempPath(), "ArgoBooks", "AccountantPack", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var files = new List<(string EntryName, string FilePath)>();

        if (IncludeReports)
        {
            BusyText = "Creating reports...".Translate();
            var logo = manager.CurrentCompanyLogoPath;
            var use24Hour = TimeZoneService.Is24HourFormat;

            foreach (var (templateName, fileName) in AccountantPack.Reports)
            {
                var config = ReportTemplateFactory.CreateFromTemplate(templateName);
                config.Filters.DatePresetName = DatePresetNames.Custom;
                config.Filters.StartDate = start;
                config.Filters.EndDate = end;
                config.Use24HourFormat = use24Hour;
                config.CompanyLogoPath = logo;

                var path = Path.Combine(staging, fileName);
                var rendered = await Task.Run(async () =>
                {
                    using var renderer = new ReportRenderer(config, data, PageDimensions.RenderScale,
                        LanguageServiceTranslationProvider.Instance, App.ErrorLogger);
                    return await renderer.ExportToPdfAsync(path);
                });
                if (rendered)
                    files.Add(($"Reports/{fileName}", path));
            }
        }

        if (IncludeTransactions)
        {
            BusyText = "Exporting transactions...".Translate();
            var path = Path.Combine(staging, AccountantPack.TransactionsFileName);
            await new SpreadsheetExportService().ExportToExcelAsync(path, data, [.. AccountantPack.TransactionSheets], start, end);
            files.Add((AccountantPack.TransactionsFileName, path));
        }

        List<(string EntryName, Receipt Receipt)> receipts = [];
        if (IncludeReceipts)
        {
            BusyText = "Gathering receipts...".Translate();
            await manager.EnsureReceiptsLoadedAsync();
            receipts = AccountantPack.ReceiptsInRange(data, start, end);
        }

        return new Pack(staging, data.Settings.Company.Name, AccountantPack.PeriodLabel(start, end), files, receipts);
    }

    private async Task RunAsync(Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "SendToAccountant");
            ErrorMessage = "Could not create the pack: {0}".TranslateFormat(ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    private void RememberAccountant()
    {
        if (App.CompanyManager?.CompanyData is not { } data)
            return;

        var name = AccountantName.Trim() is { Length: > 0 } n ? n : null;
        var email = AccountantEmail.Trim() is { Length: > 0 } e ? e : null;
        var company = data.Settings.Company;
        if (company.AccountantName == name && company.AccountantEmail == email)
            return;

        company.AccountantName = name;
        company.AccountantEmail = email;
        App.CompanyManager.MarkAsChanged();
    }

    private static AccountantPackAttachment Attachment(string fileName, byte[] bytes) => new()
    {
        Filename = fileName,
        ContentType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/zip"
        },
        Data = Convert.ToBase64String(bytes)
    };

    private static void DeleteStaging(Pack pack)
    {
        try
        {
            Directory.Delete(pack.StagingDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogWarning($"Could not delete accountant pack staging folder: {ex.Message}", "SendToAccountant");
        }
    }

    private static void ShowInFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", $"-R \"{path}\"");
            else if (OperatingSystem.IsLinux() && Path.GetDirectoryName(path) is { } directory)
                System.Diagnostics.Process.Start("xdg-open", directory);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogWarning($"Failed to open folder after saving accountant pack: {ex.Message}", "SendToAccountant");
        }
    }
}
