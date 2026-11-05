using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Sales Tax modals: filing a period's return with its payment, and taking a filing back.
/// </summary>
public partial class SalesTaxModalsViewModel : ViewModelBase
{
    /// <summary>Raised when a return is filed or taken back, or that is undone or redone.</summary>
    public event EventHandler? FilingChanged;

    private SalesTaxPeriodRow? _filing;

    [ObservableProperty]
    private bool _isFileModalOpen;

    [ObservableProperty]
    private string _fileTitle = string.Empty;

    [ObservableProperty]
    private string _fileSubtitle = string.Empty;

    [ObservableProperty]
    private string _collectedText = string.Empty;

    [ObservableProperty]
    private string _refundedText = string.Empty;

    [ObservableProperty]
    private bool _hasRefunds;

    [ObservableProperty]
    private string _claimedBackText = string.Empty;

    [ObservableProperty]
    private string _netLabel = string.Empty;

    [ObservableProperty]
    private string _netText = string.Empty;

    [ObservableProperty]
    private string _confirmText = string.Empty;

    [ObservableProperty]
    private string _paidOnLabel = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _paidOn;

    [ObservableProperty]
    private string _paidOnError = string.Empty;

    partial void OnPaidOnChanged(DateTimeOffset? value) => PaidOnError = string.Empty;

    public ObservableCollection<string> PaidFromOptions { get; } = ["Bank transfer", "Cheque", "Credit card", "Cash"];

    [ObservableProperty]
    private string _paidFrom = "Bank transfer";

    [ObservableProperty]
    private string _confirmationNumber = string.Empty;

    [ObservableProperty]
    private string _fileNote = string.Empty;

    /// <summary>Opens the filing form for a period, with what it owes worked out.</summary>
    public void OpenFileModal(SalesTaxPeriodRow row)
    {
        _filing = row;
        var amounts = row.Amounts;
        FileTitle = "File {0}".TranslateFormat(row.PeriodName);
        FileSubtitle = "{0}, due {1}".TranslateFormat(row.DatesText, row.DueFormatted);
        CollectedText = CurrencyService.Format(amounts.Collected);
        HasRefunds = amounts.Refunded != 0;
        RefundedText = $"-{CurrencyService.Format(amounts.Refunded)}";
        ClaimedBackText = $"-{CurrencyService.Format(amounts.ClaimedBack)}";

        if (row.IsRefundDue)
        {
            NetLabel = "Refund due to you".Translate();
            ConfirmText = "Record refund".Translate();
            PaidOnLabel = "Received on".Translate();
            FileNote = "Your purchases carried more tax than your sales, so the government owes you the difference.".Translate();
        }
        else
        {
            NetLabel = "Amount to pay".Translate();
            ConfirmText = "Record payment".Translate();
            PaidOnLabel = "Paid on".Translate();
            FileNote = "This takes the amount off what you owe. It isn't counted as a business expense, since the tax was never your money.".Translate();
        }
        NetText = CurrencyService.Format(Math.Abs(amounts.Net));

        PaidOn = DateTimeOffset.Now;
        PaidOnError = string.Empty;
        PaidFrom = PaidFromOptions[0];
        ConfirmationNumber = string.Empty;
        IsFileModalOpen = true;
    }

    [RelayCommand]
    private void CloseFileModal()
    {
        IsFileModalOpen = false;
        _filing = null;
    }

    [RelayCommand]
    private void ConfirmFile()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null || _filing is not { } row) return;

        // A return is filed for a period that has ended, so it can't be paid before then.
        var paidOn = (PaidOn ?? DateTimeOffset.Now).Date;
        if (paidOn <= row.Period.End)
        {
            PaidOnError = "The date has to be after the period ends, on {0}.".TranslateFormat(DateFormatService.Format(row.Period.End));
            return;
        }

        var filing = new SalesTaxFiling
        {
            Id = new IdGenerator(data).NextSalesTaxFilingId(),
            Frequency = row.Frequency,
            PeriodStart = row.Period.Start,
            PeriodEnd = row.Period.End,
            DueDate = row.Period.DueDate,
            PaidDate = paidOn,
            Currency = CurrencyService.CurrentCurrencyCode,
            Collected = Math.Round(row.Amounts.Collected, 2),
            Refunded = Math.Round(row.Amounts.Refunded, 2),
            ClaimedBack = Math.Round(row.Amounts.ClaimedBack, 2),
            PaymentMethod = PaidFrom,
            ConfirmationNumber = ConfirmationNumber.Trim()
        };

        // Filing a period also saves how often the business files, which turns on the dashboard's reminder.
        var frequencyBefore = data.Settings.Company.SalesTaxFrequency;
        void Add()
        {
            data.SalesTaxFilings.RestoreRecord(filing);
            data.Settings.Company.SalesTaxFrequency ??= filing.Frequency;
            Changed(data);
        }
        void Take()
        {
            data.SalesTaxFilings.RemoveRecord(filing);
            // A frequency chosen on the page since is the business's choice, and stays.
            if (data.Settings.Company.SalesTaxFrequency == filing.Frequency)
                data.Settings.Company.SalesTaxFrequency = frequencyBefore;
            Changed(data);
        }

        Add();
        App.UndoRedoManager.RecordAction(new DelegateAction($"File sales tax for {row.PeriodName}", Take, Add));
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.SalesTaxFiled,
            $"{filing.Frequency.ToString().ToLowerInvariant()}:{(filing.Amount < 0 ? "refund" : "paid")}");
        App.CompanyManager?.MarkAsChanged();
        CloseFileModal();
    }

    /// <summary>Takes back a filed return, with undo, so the period is owed again.</summary>
    public void RemoveFiling(SalesTaxFiling filing)
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        // A bank line matched to the payment is unmatched with it, and matched again if it comes back.
        var lines = data.BankImportSessions.SelectMany(s => s.Lines)
            .Where(l => l.MatchedRecordType == BookRecordType.SalesTaxFiling && l.MatchedRecordId == filing.Id)
            .Select(l => (Line: l, l.MatchStatus, l.MatchedDate, l.MatchConfidence))
            .ToList();
        void Take()
        {
            data.SalesTaxFilings.RemoveRecord(filing);
            foreach (var (line, _, _, _) in lines)
                new BankMatchingService().RejectMatch(line);
            Changed(data);
        }
        void Add()
        {
            data.SalesTaxFilings.RestoreRecord(filing);
            foreach (var (line, status, date, confidence) in lines)
            {
                line.MatchStatus = status;
                line.MatchedRecordType = BookRecordType.SalesTaxFiling;
                line.MatchedRecordId = filing.Id;
                line.MatchedDate = date;
                line.MatchConfidence = confidence;
            }
            Changed(data);
        }

        Take();
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Undo sales tax filing for {SalesTaxPeriods.NameOf(new SalesTaxPeriods.Period(filing.PeriodStart, filing.PeriodEnd, filing.DueDate), filing.Frequency)}",
            Add, Take));
        App.CompanyManager?.MarkAsChanged();
    }

    private void Changed(CompanyData data)
    {
        data.MarkAsModified();
        FilingChanged?.Invoke(this, EventArgs.Empty);
    }
}
