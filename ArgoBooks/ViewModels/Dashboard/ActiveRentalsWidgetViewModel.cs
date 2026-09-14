using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels.Dashboard;

public partial class ActiveRentalsWidgetViewModel : WidgetViewModelBase
{
    public override WidgetType WidgetType => WidgetType.ActiveRentalsTable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveRentals))]
    [NotifyPropertyChangedFor(nameof(HasNoActiveRentals))]
    private ObservableCollection<ActiveRentalItem> _activeRentalsList = [];

    public bool HasActiveRentals => ActiveRentalsList.Count > 0;
    public bool HasNoActiveRentals => ActiveRentalsList.Count == 0;

    public override bool HasConfig => true;

    [ObservableProperty]
    private int _rowCount = 10;

    [ObservableProperty]
    private bool _overdueOnly;

    public int[] RowCountOptions { get; } = [5, 10, 20];

    partial void OnRowCountChanged(int value) => LoadData();
    partial void OnOverdueOnlyChanged(bool value) => LoadData();

    public override void Initialize(Dictionary<string, string> config)
    {
        ApplyConfig(config);
    }

    public override void ApplyConfig(Dictionary<string, string> config)
    {
        if (config.TryGetValue("RowCount", out var rowCountStr) && int.TryParse(rowCountStr, out var rowCount))
            RowCount = rowCount;
        if (config.TryGetValue("OverdueOnly", out var overdueStr))
            OverdueOnly = overdueStr == "True";
    }

    public override Dictionary<string, string> GetConfig()
    {
        return new Dictionary<string, string>
        {
            ["RowCount"] = RowCount.ToString(),
            ["OverdueOnly"] = OverdueOnly.ToString()
        };
    }

    public override void LoadData()
    {
        var data = CompanyManager?.CompanyData;
        if (data == null) return;

        LoadActiveRentals(data);
    }

    private void LoadActiveRentals(CompanyData data)
    {
        var query = data.Rentals
            .Where(r => r.Status == RentalStatus.Active || r.Status == RentalStatus.Overdue);

        if (OverdueOnly)
            query = query.Where(r => r.Status == RentalStatus.Overdue);

        var activeRentals = query
            .OrderBy(r => r.DueDate)
            .Take(RowCount)
            .Select(r => new ActiveRentalItem
            {
                Id = r.Id,
                ItemName = Core.Services.RentalBookings.ItemNames(data, r),
                CustomerName = GetCustomerName(data, r.CustomerId),
                StartDate = r.StartDate,
                StartDateFormatted = DateFormatService.Format(r.StartDate),
                DueDate = r.DueDate,
                DueDateFormatted = DateFormatService.Format(r.DueDate),
                RateAmount = r.EffectiveLineItems().Count > 1
                    ? "{0} items".TranslateFormat(r.EffectiveLineItems().Count)
                    : CurrencyService.Format(r.RateAmount),
                RateType = r.EffectiveLineItems().Count > 1 ? string.Empty : r.RateType.ToString(),
                Status = r.Status == RentalStatus.Overdue ? "Overdue" : "Active",
                StatusVariant = r.Status == RentalStatus.Overdue ? "error" : "success",
                DaysRemaining = (r.DueDate.Date - DateTime.Now.Date).Days,
                IsOverdue = r.Status == RentalStatus.Overdue
            })
            .ToList();

        ActiveRentalsList = new ObservableCollection<ActiveRentalItem>(activeRentals);
    }

    [RelayCommand]
    private void NavigateToRentals()
    {
        App.NavigationService?.NavigateTo("RentalRecords");
    }

    [RelayCommand]
    private void NavigateToRental(ActiveRentalItem? rental)
    {
        if (rental == null) return;
        App.NavigationService?.NavigateTo("RentalRecords", new TransactionNavigationParameter(rental.Id));
    }

    #region Helper Methods

    private static string GetCustomerName(CompanyData data, string? customerId)
    {
        if (string.IsNullOrEmpty(customerId)) return "Unknown";
        var customer = data.GetCustomer(customerId);
        return customer?.Name ?? "Unknown";
    }

    #endregion
}
