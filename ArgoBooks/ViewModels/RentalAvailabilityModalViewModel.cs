using System.Collections.ObjectModel;
using System.Globalization;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the rental availability modal, shows a month calendar of free/booked
/// counts for a single rental item, plus a quantity-aware "next available" answer.
/// </summary>
public partial class RentalAvailabilityModalViewModel : ViewModelBase
{
    #region State

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _itemId = string.Empty;

    [ObservableProperty]
    private string _itemName = string.Empty;

    [ObservableProperty]
    private string _itemSubtitle = string.Empty;

    [ObservableProperty]
    private int _totalQty;

    [ObservableProperty]
    private int _quantityNeeded = 1;

    [ObservableProperty]
    private string _quantityNeededText = "1";

    private bool _syncingQuantity;

    [ObservableProperty]
    private DateTime _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private string _monthLabel = string.Empty;

    [ObservableProperty]
    private int _freeNow;

    [ObservableProperty]
    private int _rentedNow;

    [ObservableProperty]
    private string _nextAvailableDate = string.Empty;

    [ObservableProperty]
    private string _nextAvailableDetail = string.Empty;

    [ObservableProperty]
    private bool _hasNextAvailable;

    [ObservableProperty]
    private bool _quantityExceedsTotal;

    [ObservableProperty]
    private bool _hasActiveRentals;

    [ObservableProperty]
    private string _meetsNeedLegendText = string.Empty;

    public ObservableCollection<AvailabilityDayDisplay> Days { get; } = [];

    public ObservableCollection<ActiveRentalDisplay> ActiveRentals { get; } = [];

    private RentalItem? _currentItem;

    #endregion

    #region Open / Close

    /// <summary>
    /// Opens the modal for the given rental item, computing initial availability.
    /// </summary>
    public void OpenForItem(RentalItemDisplayItem? displayItem)
    {
        if (displayItem == null) return;
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;
        var item = data.RentalInventory.FirstOrDefault(r => r.Id == displayItem.Id);
        if (item == null) return;

        _currentItem = item;
        ItemId = item.Id;
        ItemName = displayItem.Name;
        ItemSubtitle = BuildSubtitle(item, displayItem);
        TotalQty = ComputeTotalFleet(data, item.Id, displayItem.InStock);

        // Reset quantity to 1 each open so the user starts fresh on a new item.
        _syncingQuantity = true;
        try
        {
            QuantityNeeded = 1;
            QuantityNeededText = "1";
        }
        finally
        {
            _syncingQuantity = false;
        }

        // Anchor calendar to today's month.
        CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        Recompute();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    private static string BuildSubtitle(RentalItem item, RentalItemDisplayItem displayItem)
    {
        var parts = new List<string>();
        parts.Add($"{item.Id}");
        if (item.DailyRate > 0) parts.Add($"{CurrencyService.Format(item.DailyRate)}/day");
        if (item.WeeklyRate > 0) parts.Add($"{CurrencyService.Format(item.WeeklyRate)}/wk");
        return string.Join(" · ", parts);
    }

    #endregion

    #region Quantity controls

    [RelayCommand]
    private void IncrementQuantity()
    {
        // Allow exceeding TotalQty, the answer card and calendar will surface "impossible".
        QuantityNeeded++;
    }

    [RelayCommand]
    private void DecrementQuantity()
    {
        if (QuantityNeeded > 1) QuantityNeeded--;
    }

    partial void OnQuantityNeededChanged(int value)
    {
        if (value < 1)
        {
            QuantityNeeded = 1;
            return;
        }
        if (!_syncingQuantity)
        {
            _syncingQuantity = true;
            QuantityNeededText = value.ToString(CultureInfo.InvariantCulture);
            _syncingQuantity = false;
        }
        Recompute();
    }

    partial void OnQuantityNeededTextChanged(string value)
    {
        if (_syncingQuantity) return;

        int parsed;
        if (string.IsNullOrWhiteSpace(value))
        {
            // Treat empty/whitespace as 1 so the calendar doesn't lag behind a cleared
            // textbox. We don't sync the text back here, that would interfere with the
            // user mid-edit; the textbox visually stays blank until they type a digit.
            parsed = 1;
        }
        else
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return;
            if (parsed < 1) parsed = 1;
            // Don't clamp to TotalQty, we want the calendar and answer card to clearly
            // show that the user's request is impossible (all cells unmet, "Need exceeds fleet").
        }

        _syncingQuantity = true;
        try
        {
            QuantityNeeded = parsed;
        }
        finally
        {
            _syncingQuantity = false;
        }
        Recompute();
    }

    #endregion

    #region Month navigation

    [RelayCommand]
    private void PreviousMonth()
    {
        CurrentMonth = CurrentMonth.AddMonths(-1);
        Recompute();
    }

    [RelayCommand]
    private void NextMonth()
    {
        CurrentMonth = CurrentMonth.AddMonths(1);
        Recompute();
    }

    [RelayCommand]
    private void GoToToday()
    {
        CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Recompute();
    }

    #endregion

    #region Compute

    /// <summary>
    /// Recomputes calendar days, status snapshot, next-available date, and active-rentals list.
    /// </summary>
    private void Recompute()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null || _currentItem == null) return;

        var qtyNeeded = Math.Max(1, QuantityNeeded);
        QuantityExceedsTotal = TotalQty > 0 && qtyNeeded > TotalQty;

        MonthLabel = CurrentMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        var rentals = GetRentalsForItem(data, _currentItem.Id);

        BuildCalendar(rentals, qtyNeeded);
        UpdateNowStats(rentals);
        UpdateNextAvailable(rentals, qtyNeeded);
        UpdateActiveRentalsList(data, rentals);

        MeetsNeedLegendText = Loc.Tr("Meets your need (≥ {0} free)", qtyNeeded);
    }

    private void BuildCalendar(List<RentalSpan> rentals, int qtyNeeded)
    {
        Days.Clear();
        var firstOfMonth = CurrentMonth;

        // Calendar grid starts on Sunday, pad with previous month's tail.
        var leadingPad = (int)firstOfMonth.DayOfWeek; // Sunday=0
        var firstCell = firstOfMonth.AddDays(-leadingPad);

        // Always render 6 rows × 7 = 42 cells for a stable layout.
        const int totalCells = 42;
        var today = DateTime.Today;

        for (int i = 0; i < totalCells; i++)
        {
            var date = firstCell.AddDays(i);
            var rented = SumRentedOn(rentals, date);
            var free = Math.Max(0, TotalQty - rented);
            var meets = TotalQty > 0 && free >= qtyNeeded;
            var hasOverdue = rentals.Any(r => r.IsOverdue && date >= r.Start.Date && date <= r.End.Date);

            string label = string.Empty;
            if (rented > 0)
                label = hasOverdue ? Loc.Tr("⚠ {0} overdue", rented)
                                   : Loc.Tr("{0}× rented", rented);

            var isOutside = date.Month != firstOfMonth.Month;
            var isFull = TotalQty > 0 && free == 0;
            string state;
            if (TotalQty == 0) state = "Empty";
            else if (isFull) state = "Full";
            else if (meets) state = "Met";
            else state = "Partial";

            Days.Add(new AvailabilityDayDisplay
            {
                Date = date,
                DayNumber = date.Day,
                IsOutsideMonth = isOutside,
                IsToday = date.Date == today,
                IsPast = date.Date < today,
                FreeCount = free,
                RentedCount = rented,
                MeetsNeed = meets,
                IsFullyBooked = isFull,
                HasOverdue = hasOverdue,
                State = isOutside ? "Outside" : state,
                EventLabel = label,
                FreeLabel = TotalQty == 0
                    ? "—"
                    : (meets ? $"{free} " + Loc.Tr("free") + " ✓" : $"{free} " + Loc.Tr("free"))
            });
        }
    }

    private void UpdateNowStats(List<RentalSpan> rentals)
    {
        var rented = SumRentedOn(rentals, DateTime.Today);
        RentedNow = rented;
        FreeNow = Math.Max(0, TotalQty - rented);
    }

    private void UpdateNextAvailable(List<RentalSpan> rentals, int qtyNeeded)
    {
        if (TotalQty == 0)
        {
            HasNextAvailable = false;
            NextAvailableDate = "—";
            NextAvailableDetail = Loc.Tr("No items in fleet");
            return;
        }
        if (qtyNeeded > TotalQty)
        {
            HasNextAvailable = false;
            NextAvailableDate = "—";
            NextAvailableDetail = Loc.Tr("Need exceeds total fleet of {0}", TotalQty);
            return;
        }

        // Free today already?
        var today = DateTime.Today;
        if (TotalQty - SumRentedOn(rentals, today) >= qtyNeeded)
        {
            var stretch = MeasureStretch(rentals, today, qtyNeeded);
            HasNextAvailable = true;
            NextAvailableDate = Loc.Tr("Now");
            NextAvailableDetail = stretch >= 365
                ? Loc.Tr("{0} units free indefinitely", qtyNeeded)
                : Loc.Tr("{0} units free for {1} days", qtyNeeded, stretch);
            return;
        }

        // Scan up to 1 year ahead for first day where free >= qtyNeeded.
        for (int offset = 1; offset <= 365; offset++)
        {
            var d = today.AddDays(offset);
            if (TotalQty - SumRentedOn(rentals, d) >= qtyNeeded)
            {
                var stretch = MeasureStretch(rentals, d, qtyNeeded);
                HasNextAvailable = true;
                NextAvailableDate = d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
                NextAvailableDetail = stretch >= 365
                    ? Loc.Tr("{0} units free indefinitely", qtyNeeded)
                    : Loc.Tr("{0} units free for {1} days", qtyNeeded, stretch);
                return;
            }
        }

        HasNextAvailable = false;
        NextAvailableDate = "—";
        NextAvailableDetail = Loc.Tr("Not free in the next 12 months");
    }

    private int MeasureStretch(List<RentalSpan> rentals, DateTime startDay, int qtyNeeded)
    {
        var count = 0;
        for (int offset = 0; offset < 365; offset++)
        {
            var d = startDay.AddDays(offset);
            if (TotalQty - SumRentedOn(rentals, d) >= qtyNeeded)
                count++;
            else
                break;
        }
        return count;
    }

    private void UpdateActiveRentalsList(CompanyData data, List<RentalSpan> rentals)
    {
        ActiveRentals.Clear();
        var customersById = data.Customers.ToDictionary(c => c.Id, c => c);
        foreach (var span in rentals.Where(s => s.IsActive || s.IsReserved).OrderBy(s => s.Start))
        {
            customersById.TryGetValue(span.CustomerId, out var customer);
            var customerName = customer?.Name ?? Loc.Tr("Unknown customer");
            var startLabel = span.Start.ToString("MMM d", CultureInfo.CurrentCulture);
            var endLabel = span.End.ToString("MMM d", CultureInfo.CurrentCulture);
            ActiveRentals.Add(new ActiveRentalDisplay
            {
                Description = span.IsReserved
                    ? Loc.Tr("{0}× reserved {1} to {2}, {3}", span.Quantity, startLabel, endLabel, customerName)
                    : Loc.Tr("{0}× until {1}, {2}", span.Quantity, endLabel, customerName),
                IsOverdue = span.IsOverdue
            });
        }
        HasActiveRentals = ActiveRentals.Count > 0;
    }

    #endregion

    #region Data helpers

    /// <summary>
    /// Total fleet = current InStock (linked InventoryItem) + currently-rented quantity.
    /// InStock decrements on rental creation and increments on return, so we add back
    /// active rentals' quantities to recover the original total.
    /// </summary>
    private static int ComputeTotalFleet(CompanyData data, string rentalItemId, int displayedInStock) =>
        Math.Max(0, displayedInStock) + RentalBookings.UnitsOut(data.Rentals, rentalItemId);

    private static List<RentalSpan> GetRentalsForItem(CompanyData data, string rentalItemId)
    {
        var spans = new List<RentalSpan>();
        var today = DateTime.Today;
        foreach (var rental in data.Rentals)
        {
            // Cancelled rentals occupy nothing.
            if (rental.Status == RentalStatus.Cancelled) continue;

            foreach (var qty in QuantitiesForItem(rental, rentalItemId))
            {
                if (qty <= 0) continue;
                // End of occupancy: ReturnDate if returned, else DueDate; if currently held
                // and overdue, extend through today so the user sees it in past cells.
                DateTime end;
                bool isOverdue = false;
                bool isStillHeld = RentalBookings.HoldsStock(rental);
                if (rental.Status == RentalStatus.Returned && rental.ReturnDate.HasValue)
                {
                    end = rental.ReturnDate.Value;
                }
                else
                {
                    end = rental.DueDate;
                    if (isStillHeld && rental.DueDate.Date < today)
                    {
                        isOverdue = true;
                        if (today > end) end = today;
                    }
                }

                spans.Add(new RentalSpan
                {
                    Start = rental.StartDate,
                    End = end,
                    Quantity = qty,
                    IsActive = isStillHeld,
                    IsReserved = rental.Status == RentalStatus.Reserved,
                    IsOverdue = isOverdue,
                    CustomerId = rental.CustomerId
                });
            }
        }
        return spans;
    }

    /// <summary>
    /// Yields each quantity reserved for the given rental item ID across a rental's line items
    /// (or the legacy top-level fields when no line items exist).
    /// </summary>
    private static IEnumerable<int> QuantitiesForItem(RentalRecord rental, string rentalItemId) =>
        rental.EffectiveLineItems().Where(li => li.RentalItemId == rentalItemId).Select(li => li.Quantity);

    private static int SumRentedOn(List<RentalSpan> spans, DateTime day)
    {
        var d = day.Date;
        var total = 0;
        foreach (var span in spans)
        {
            if (d >= span.Start.Date && d <= span.End.Date)
                total += span.Quantity;
        }
        return total;
    }

    private struct RentalSpan
    {
        public DateTime Start;
        public DateTime End;
        public int Quantity;
        public bool IsActive;
        public bool IsReserved;
        public bool IsOverdue;
        public string CustomerId;
    }

    #endregion
}

/// <summary>
/// Display model for a single calendar day cell. Created fresh on each recompute,
/// so simple POCO without change notification.
/// </summary>
public class AvailabilityDayDisplay
{
    public DateTime Date { get; set; }
    public int DayNumber { get; set; }
    public bool IsOutsideMonth { get; set; }
    public bool IsToday { get; set; }
    public bool IsPast { get; set; }
    public int FreeCount { get; set; }
    public int RentedCount { get; set; }
    public bool MeetsNeed { get; set; }
    public bool IsFullyBooked { get; set; }
    public bool HasOverdue { get; set; }
    public string State { get; set; } = "Empty";
    public string EventLabel { get; set; } = string.Empty;
    public string FreeLabel { get; set; } = string.Empty;

    public bool HasEvent => !string.IsNullOrEmpty(EventLabel);
    public bool ShowFreeLabel => !IsOutsideMonth;
}

/// <summary>
/// Display model for an active rental row in the side panel.
/// </summary>
public class ActiveRentalDisplay
{
    public string Description { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }
}
