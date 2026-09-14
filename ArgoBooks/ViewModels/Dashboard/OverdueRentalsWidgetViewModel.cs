using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels.Dashboard;

public record OverdueRentalItem(string ItemName, string CustomerName, string DueDate, int DaysOverdue);

public partial class OverdueRentalsWidgetViewModel : WidgetViewModelBase
{
    public override WidgetType WidgetType => WidgetType.OverdueRentals;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRentals))]
    [NotifyPropertyChangedFor(nameof(HasNoRentals))]
    private ObservableCollection<OverdueRentalItem> _rentals = [];

    public bool HasRentals => Rentals.Count > 0;
    public bool HasNoRentals => Rentals.Count == 0;

    public override void LoadData()
    {
        var data = CompanyManager?.CompanyData;
        if (data == null) return;

        LoadOverdueRentals(data);
    }

    private void LoadOverdueRentals(CompanyData data)
    {
        var today = DateTime.Now.Date;

        var items = data.Rentals
            .Where(r => r.Status == RentalStatus.Overdue)
            .OrderByDescending(r => (today - r.DueDate.Date).Days)
            .Select(r =>
            {
                var itemName = Core.Services.RentalBookings.ItemNames(data, r);
                var customer = data.GetCustomer(r.CustomerId);
                var customerName = customer?.Name ?? "Unknown";
                var dueDateStr = DateFormatService.Format(r.DueDate);
                var daysOverdue = (today - r.DueDate.Date).Days;

                return new OverdueRentalItem(itemName, customerName, dueDateStr, daysOverdue);
            })
            .ToList();

        Rentals = new ObservableCollection<OverdueRentalItem>(items);
    }
}
