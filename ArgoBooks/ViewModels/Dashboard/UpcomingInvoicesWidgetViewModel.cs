using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels.Dashboard;

public record UpcomingInvoiceItem(
    string InvoiceNumber,
    string CustomerName,
    string Amount,
    string DueDate,
    int DaysUntilDue,
    bool IsOverdue,
    bool IsUrgent)
{
    public bool IsNormal => !IsOverdue && !IsUrgent;
}

public partial class UpcomingInvoicesWidgetViewModel : WidgetViewModelBase
{
    public override WidgetType WidgetType => WidgetType.UpcomingInvoiceDueDates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvoices))]
    [NotifyPropertyChangedFor(nameof(HasNoInvoices))]
    private ObservableCollection<UpcomingInvoiceItem> _invoices = [];

    public bool HasInvoices => Invoices.Count > 0;
    public bool HasNoInvoices => Invoices.Count == 0;

    public override bool HasConfig => true;

    [ObservableProperty]
    private int _daysAhead = 14;

    public int[] DaysAheadOptions { get; } = [7, 14, 30];

    partial void OnDaysAheadChanged(int value) => LoadData();

    public override void Initialize(Dictionary<string, string> config)
    {
        ApplyConfig(config);
    }

    public override void ApplyConfig(Dictionary<string, string> config)
    {
        if (config.TryGetValue("DaysAhead", out var daysStr) && int.TryParse(daysStr, out var days))
            DaysAhead = days;
    }

    public override Dictionary<string, string> GetConfig()
    {
        return new Dictionary<string, string>
        {
            ["DaysAhead"] = DaysAhead.ToString()
        };
    }

    public override void LoadData()
    {
        var data = CompanyManager?.CompanyData;
        if (data == null) return;

        LoadUpcomingInvoices(data);
    }

    private void LoadUpcomingInvoices(CompanyData data)
    {
        var today = DateTime.Now.Date;
        var cutoff = today.AddDays(DaysAhead);

        var unpaidStatuses = new[]
        {
            InvoiceStatus.Pending,
            InvoiceStatus.Sent,
            InvoiceStatus.Viewed,
            InvoiceStatus.Partial,
            InvoiceStatus.Overdue
        };

        var items = data.Invoices
            .Where(inv => unpaidStatuses.Contains(inv.Status) && inv.DueDate.Date <= cutoff)
            .OrderBy(inv => inv.DueDate)
            .Select(inv =>
            {
                var daysUntilDue = (inv.DueDate.Date - today).Days;
                var customer = data.GetCustomer(inv.CustomerId);
                var customerName = customer?.Name ?? "Unknown";
                // At the issue date, as invoices convert everywhere else: a due date still to come has no rate.
                var amount = CurrencyService.FormatWithOriginal(
                    inv.Balance, inv.OriginalCurrency, inv.EffectiveBalanceUSD, inv.IssueDate);
                var dueDateStr = DateFormatService.Format(inv.DueDate);

                return new UpcomingInvoiceItem(
                    inv.InvoiceNumber,
                    customerName,
                    amount,
                    dueDateStr,
                    daysUntilDue,
                    IsOverdue: daysUntilDue < 0,
                    IsUrgent: daysUntilDue is >= 0 and <= 3);
            })
            .ToList();

        Invoices = new ObservableCollection<UpcomingInvoiceItem>(items);
    }
}
