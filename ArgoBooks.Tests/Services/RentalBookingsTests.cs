using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// What a rental charges, which dates its units take, and what it records when paid without an invoice.
/// </summary>
public class RentalBookingsTests
{
    private static readonly DateTime Today = DateTime.Today;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    public void ChargeableDays_CountsDatesAndAtLeastOne(int daysLater, int expected) =>
        Assert.Equal(expected, RentalBookings.ChargeableDays(Today, Today.AddDays(daysLater)));

    [Fact]
    public void ChargeableDays_IgnoresTheTimeOfDay() =>
        Assert.Equal(2, RentalBookings.ChargeableDays(Today.AddHours(17), Today.AddDays(2).AddHours(9)));

    [Theory]
    [InlineData(RateType.Daily, 8, 80)]
    [InlineData(RateType.Weekly, 8, 20)]
    [InlineData(RateType.Monthly, 31, 20)]
    public void LineCost_ChargesEveryStartedPeriod(RateType rateType, int days, int expected) =>
        Assert.Equal(expected, RentalBookings.LineCost(new RentalLineItem { RateType = rateType, RateAmount = 10m, Quantity = 1 }, days));

    [Fact]
    public void RentalCost_ChargesEachLineAtItsOwnRate()
    {
        var lines = new List<RentalLineItem>
        {
            new() { RateType = RateType.Daily, RateAmount = 10m, Quantity = 2 },
            new() { RateType = RateType.Daily, RateAmount = 50m, Quantity = 1 }
        };

        Assert.Equal(210m, RentalBookings.RentalCost(lines, Today.AddDays(-3), Today));
    }

    // Rent Out saved the item on the record with the deposit for every unit, not per unit.
    [Fact]
    public void EffectiveLineItems_RecordWithoutLines_KeepsItsTotalDeposit()
    {
        var record = new RentalRecord
        {
            Id = "RNT-001", RentalItemId = "RI-1", Quantity = 3,
            RateType = RateType.Daily, RateAmount = 12.5m, SecurityDeposit = 50m
        };

        var line = Assert.Single(record.EffectiveLineItems());
        Assert.Equal(("RI-1", 3, RateType.Daily, 12.5m), (line.RentalItemId, line.Quantity, line.RateType, line.RateAmount));
        Assert.Equal(50m, RentalBookings.TotalDeposit(record.EffectiveLineItems()));
    }

    [Fact]
    public void EffectiveLineItems_RecordWithLines_UsesTheLines()
    {
        var record = new RentalRecord
        {
            RentalItemId = "RI-OLD", Quantity = 1,
            LineItems = [new RentalLineItem { RentalItemId = "RI-10", Quantity = 2 }, new RentalLineItem { RentalItemId = "RI-11", Quantity = 5 }]
        };

        Assert.Equal(new[] { "RI-10", "RI-11" }, record.EffectiveLineItems().Select(li => li.RentalItemId));
    }

    private static RentalRecord Rental(string id, RentalStatus status, int startIn, int dueIn, int units = 1) => new()
    {
        Id = id,
        Status = status,
        StartDate = Today.AddDays(startIn),
        DueDate = Today.AddDays(dueIn),
        LineItems = [new RentalLineItem { RentalItemId = "RI-1", Quantity = units }]
    };

    [Fact]
    public void MostBookedBetween_CountsReservationsAndRentalsOutOnTheSameDay()
    {
        var rentals = new[]
        {
            Rental("A", RentalStatus.Reserved, 3, 5, units: 2),
            Rental("B", RentalStatus.Active, -2, 4),
            Rental("C", RentalStatus.Returned, -2, 10, units: 5),
            Rental("D", RentalStatus.Cancelled, 0, 10, units: 5)
        };

        Assert.Equal(3, RentalBookings.MostBookedBetween(rentals, "RI-1", Today.AddDays(4), Today.AddDays(6)));
        Assert.Equal(2, RentalBookings.MostBookedBetween(rentals, "RI-1", Today.AddDays(5), Today.AddDays(9)));
        Assert.Equal(0, RentalBookings.MostBookedBetween(rentals, "RI-1", Today.AddDays(6), Today.AddDays(9)));
        Assert.Equal(1, RentalBookings.MostBookedBetween(rentals, "RI-1", Today.AddDays(4), Today.AddDays(6), excludeRentalId: "A"));
    }

    [Fact]
    public void MostBookedBetween_ALateRentalStillTakesToday() =>
        Assert.Equal(1, RentalBookings.MostBookedBetween([Rental("A", RentalStatus.Overdue, -10, -3)], "RI-1", Today, Today));

    private static CompanyData CompanyWithStock(decimal inStock)
    {
        var data = new CompanyData();
        data.Products.Add(new Product { Id = "P1", Name = "Ladder" });
        data.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = inStock });
        data.RentalInventory.Add(new RentalItem { Id = "RI-1", InventoryItemId = "INV-1", DailyRate = 10m });
        return data;
    }

    [Fact]
    public void FindShortfalls_UnitsOutNowCanBeBookedForLaterDates()
    {
        var data = CompanyWithStock(0);
        data.Rentals.Add(Rental("A", RentalStatus.Active, -1, 2, units: 2));
        var line = new RentalLineItem { RentalItemId = "RI-1", Quantity = 2 };

        Assert.Equal(new int?[] { null }, RentalBookings.FindShortfalls(data, [line], Today.AddDays(3), Today.AddDays(5), takesStock: false));
        Assert.Equal(new int?[] { 0 }, RentalBookings.FindShortfalls(data, [line], Today, Today.AddDays(1), takesStock: true));
    }

    [Fact]
    public void FindShortfalls_LinesForTheSameItemShareWhatIsFree()
    {
        var data = CompanyWithStock(5);
        var lines = new List<RentalLineItem>
        {
            new() { RentalItemId = "RI-1", Quantity = 3 },
            new() { RentalItemId = "RI-1", Quantity = 3 }
        };

        Assert.Equal(new int?[] { null, 2 }, RentalBookings.FindShortfalls(data, lines, Today, Today.AddDays(1), takesStock: true));
    }

    [Fact]
    public void PaidRevenue_IsTheChargesPlusTheDepositKept()
    {
        var data = CompanyWithStock(1);
        var rental = Rental("RNT-001", RentalStatus.Returned, -3, 0);
        rental.CustomerId = "CUST-1";
        rental.TotalCost = 100m;
        rental.SecurityDeposit = 30m;
        rental.DepositRefunded = 10m;

        var revenue = RentalBookings.PaidRevenue(data, rental, Today, "USD");

        Assert.NotNull(revenue);
        Assert.Equal((120m, 120m, "CUST-1", "RNT-001", RevenuePaymentStatus.Paid),
            (revenue.Total, revenue.TotalUSD, revenue.CustomerId, revenue.ReferenceNumber, revenue.PaymentStatus));
    }
}
