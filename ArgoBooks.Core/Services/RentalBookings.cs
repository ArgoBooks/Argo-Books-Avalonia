using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// What a rental charges and which dates its units are taken. See docs/Calculations.md §15.
/// </summary>
public static class RentalBookings
{
    /// <summary>Active and overdue rentals have their units out of stock.</summary>
    public static bool HoldsStock(RentalRecord rental) =>
        rental.Status is RentalStatus.Active or RentalStatus.Overdue;

    /// <summary>
    /// Whether the money for a rental has arrived through an invoice rather than through
    /// <see cref="RentalRecord.Paid"/>. An invoiced rental records no revenue of its own, so
    /// paying its invoice is what settles it, and nothing was writing that back to the flag.
    /// Every linked invoice has to be paid: two invoices with one outstanding is not settled.
    /// </summary>
    public static bool SettledByInvoice(CompanyData data, RentalRecord rental)
    {
        var linked = rental.InvoiceIds
            .Select(id => data.Invoices.FirstOrDefault(i => i.Id == id))
            .Where(i => i != null)
            .ToList();

        return linked.Count > 0 && linked.All(i => i!.Status == InvoiceStatus.Paid);
    }

    /// <summary>Whole days between two dates, and at least one: out Monday and back Wednesday is two.</summary>
    public static int ChargeableDays(DateTime start, DateTime end) => Math.Max(1, (end.Date - start.Date).Days);

    /// <summary>What one unit costs for the days out. Every started week or 30-day month counts in full.</summary>
    public static decimal UnitCost(RentalLineItem line, int days) => line.RateType switch
    {
        RateType.Weekly => line.RateAmount * (decimal)Math.Ceiling(days / 7.0),
        RateType.Monthly => line.RateAmount * (decimal)Math.Ceiling(days / 30.0),
        _ => line.RateAmount * days
    };

    public static decimal LineCost(RentalLineItem line, int days) => UnitCost(line, days) * line.Quantity;

    public static decimal RentalCost(IEnumerable<RentalLineItem> lines, DateTime start, DateTime end)
    {
        var days = ChargeableDays(start, end);
        return lines.Sum(line => LineCost(line, days));
    }

    /// <summary>A line's deposit is per unit.</summary>
    public static decimal TotalDeposit(IEnumerable<RentalLineItem> lines) =>
        Math.Round(lines.Sum(line => line.SecurityDeposit * line.Quantity), 2);

    public static int UnitsOut(IEnumerable<RentalRecord> rentals, string rentalItemId) =>
        rentals.Where(HoldsStock).Sum(r => UnitsOf(r, rentalItemId));

    /// <summary>
    /// The most units of an item that rentals take on any one day from start to end. A reservation
    /// takes its dates. A rental that is out takes its dates and, once late, every day up to today.
    /// </summary>
    public static int MostBookedBetween(IEnumerable<RentalRecord> rentals, string rentalItemId,
        DateTime start, DateTime end, string? excludeRentalId = null)
    {
        var today = DateTime.Today;
        var spans = rentals
            .Where(r => r.Id != excludeRentalId && (HoldsStock(r) || r.Status == RentalStatus.Reserved))
            .Select(r => (Start: r.StartDate.Date,
                End: HoldsStock(r) && r.DueDate.Date < today ? today : r.DueDate.Date,
                Units: UnitsOf(r, rentalItemId)))
            .Where(s => s.Units > 0)
            .ToList();

        var first = start.Date;
        var last = end.Date < first ? first : end.Date;

        // Bookings only go up on the day one starts, so those days and the first are the only ones to check.
        return spans
            .Select(s => s.Start)
            .Where(day => day > first && day <= last)
            .Append(first)
            .Select(day => spans.Where(s => s.Start <= day && day <= s.End).Sum(s => s.Units))
            .Max();
    }

    /// <summary>
    /// For each line, how many units were left for it when it asks for more, or null when it fits.
    /// Lines for the same item share what is free, in order. An item's units are its stock plus those
    /// out on rentals. A rental that takes stock now also can't take more than is on the shelf.
    /// </summary>
    public static int?[] FindShortfalls(CompanyData data, IReadOnlyList<RentalLineItem> lines,
        DateTime start, DateTime due, bool takesStock, RentalRecord? editing = null)
    {
        var result = new int?[lines.Count];
        var asked = new Dictionary<string, int>();
        var last = takesStock && due.Date < DateTime.Today ? DateTime.Today : due;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var stock = StockFor(data, line.RentalItemId);
            if (stock == null)
            {
                result[i] = 0;
                continue;
            }

            var onShelf = (int)Math.Floor(Math.Max(0m, stock.InStock));
            var free = onShelf + UnitsOut(data.Rentals, line.RentalItemId)
                       - MostBookedBetween(data.Rentals, line.RentalItemId, start, last, editing?.Id);
            if (takesStock)
                free = Math.Min(free, onShelf + (editing != null && HoldsStock(editing) ? UnitsOf(editing, line.RentalItemId) : 0));

            var before = asked.GetValueOrDefault(line.RentalItemId);
            asked[line.RentalItemId] = before + line.Quantity;
            if (line.Quantity > free - before)
                result[i] = Math.Max(0, free - before);
        }

        return result;
    }

    public static InventoryItem? StockFor(CompanyData data, string rentalItemId)
    {
        var item = data.RentalInventory.FirstOrDefault(i => i.Id == rentalItemId);
        return item == null ? null : data.Inventory.FirstOrDefault(i => i.Id == item.InventoryItemId);
    }

    public static string ItemName(CompanyData? data, string rentalItemId)
    {
        var stock = data == null ? null : StockFor(data, rentalItemId);
        return (stock == null ? null : data!.GetProduct(stock.ProductId)?.Name) ?? "Unknown Item";
    }

    public static string ItemNames(CompanyData? data, RentalRecord rental) =>
        string.Join(", ", rental.EffectiveLineItems().Select(li => ItemName(data, li.RentalItemId)));

    /// <summary>
    /// The revenue for a rental paid without an invoice: its charges, plus any deposit kept once it is
    /// back. Null when there is nothing to record.
    /// </summary>
    public static Revenue? PaidRevenue(CompanyData data, RentalRecord rental, DateTime date, string currency)
    {
        var kept = rental.Status == RentalStatus.Returned
            ? Math.Max(0m, rental.SecurityDeposit - (rental.DepositRefunded ?? rental.SecurityDeposit))
            : 0m;
        var amount = (rental.TotalCost ?? 0m) + kept;
        if (amount <= 0)
            return null;

        var revenue = TransactionFactory.CreateRevenue(data, new TransactionDraft(
            date, $"Rental {rental.Id}: {ItemNames(data, rental)}", amount, rental.CustomerId, null, currency));
        revenue.ReferenceNumber = rental.Id;

        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            revenue.TotalUSD = amount;
            revenue.UnitPriceUSD = amount;
        }
        else if (ExchangeRateService.Instance is { } rates && rates.TryConvertToUsdBase(amount, currency, date, out var usd))
        {
            revenue.TotalUSD = usd;
            revenue.UnitPriceUSD = usd;
        }
        else
        {
            revenue.IsPendingConversion = true;
        }

        return revenue;
    }

    private static int UnitsOf(RentalRecord rental, string rentalItemId) =>
        rental.EffectiveLineItems().Where(li => li.RentalItemId == rentalItemId).Sum(li => li.Quantity);
}
