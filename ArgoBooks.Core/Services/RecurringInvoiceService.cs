using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Generates concrete invoices from <see cref="RecurringInvoice"/> schedules. Idempotent:
/// generation is keyed on each schedule's <see cref="RecurringInvoice.NextInvoiceDate"/>, so
/// running it again (for example on the next company open) only produces the occurrences that
/// have actually come due since last time. All bookkeeping stays in the local .argo file; nothing
/// runs while the app is closed.
/// </summary>
public static class RecurringInvoiceService
{
    // Safety cap so a corrupt schedule (e.g. a NextInvoiceDate far in the past with a weekly
    // cadence) cannot spin generating thousands of invoices in a single open.
    private const int MaxOccurrencesPerSchedulePerRun = 500;

    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Raised with the generated count after invoices are produced on company open.</summary>
    public static event Action<int>? InvoicesGenerated;

    /// <summary>
    /// Count of invoices generated on the last open that the user has not acknowledged yet. The
    /// Invoices page reads this on construction so the banner survives the gap between generation
    /// (which happens while the user is on the dashboard) and navigating to the Invoices page.
    /// </summary>
    public static int PendingGeneratedCount { get; private set; }

    /// <summary>Records the pending count and fires <see cref="InvoicesGenerated"/>.</summary>
    public static void RaiseGenerated(int count)
    {
        PendingGeneratedCount = count;
        InvoicesGenerated?.Invoke(count);
    }

    /// <summary>Clears the pending banner count once the user has reviewed or dismissed it.</summary>
    public static void ClearPendingGenerated() => PendingGeneratedCount = 0;

    /// <inheritdoc cref="RecurrenceSchedule.AdvanceDate"/>
    public static DateTime AdvanceDate(DateTime date, Frequency frequency, int? anchorDay = null)
        => RecurrenceSchedule.AdvanceDate(date, frequency, anchorDay);

    /// <summary>
    /// Formats payment terms ("Due on receipt" or "Net N") from an invoice's issue and due dates,
    /// so a recurring schedule inherits the terms the user actually set instead of a fixed default.
    /// </summary>
    public static string FormatPaymentTerms(DateTime issueDate, DateTime dueDate)
    {
        var days = (dueDate.Date - issueDate.Date).Days;
        if (days <= 0) return "Due on receipt";
        return $"Net {days}";
    }

    /// <summary>Days added to the issue date to derive the due date, parsed from payment terms.</summary>
    public static int PaymentTermsDays(string? terms)
    {
        var normalized = (terms ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "due on receipt") return 0;
        // Parse any "net N" so arbitrary offsets round-trip, not just the fixed presets.
        if (normalized.StartsWith("net ") && int.TryParse(normalized.AsSpan(4), out var days) && days >= 0)
            return days;
        return 30;
    }

    /// <summary>
    /// Builds a clean template invoice from a just-created invoice: same content, stripped of
    /// identity, payment, status, and back-reference fields so it can be cloned per occurrence.
    /// </summary>
    public static Invoice BuildTemplateFrom(Invoice source)
    {
        var template = Clone(source);
        template.Id = string.Empty;
        template.InvoiceNumber = string.Empty;
        template.Status = InvoiceStatus.Draft;
        template.RecurringInvoiceId = null;
        template.AmountPaid = 0;
        template.AmountRefunded = 0;
        template.History = [];
        template.BankMatched = false;
        template.BankMatchedDate = null;
        template.BankMatchedLineId = null;
        return template;
    }

    /// <summary>
    /// Generates every occurrence that has come due (NextInvoiceDate on or before
    /// <paramref name="today"/>, the local calendar date), appending them to
    /// <see cref="CompanyData.Invoices"/> and advancing each schedule. Schedule dates are calendar
    /// dates, so the UTC date ran a day early or late depending on the time zone. Completes a
    /// schedule once it passes its end date. Idempotent. Returns the invoices generated across all
    /// schedules (possibly empty).
    /// </summary>
    public static IReadOnlyList<Invoice> GenerateDueInvoices(CompanyData data, DateTime today)
    {
        var generated = new List<Invoice>();

        var asOfDate = today.Date;
        var idGenerator = new IdGenerator(data);

        foreach (var schedule in data.RecurringInvoices)
        {
            // Skip legacy schedules without a template, and anything not currently active.
            if (schedule.Template == null) continue;
            if (schedule.Status != RecurringInvoiceStatus.Active) continue;

            var count = 0;
            while (schedule.NextInvoiceDate.Date <= asOfDate && count < MaxOccurrencesPerSchedulePerRun)
            {
                // The next occurrence already falls past the end date: finish without generating.
                if (schedule.EndDate != null && schedule.NextInvoiceDate.Date > schedule.EndDate.Value.Date)
                {
                    schedule.Status = RecurringInvoiceStatus.Completed;
                    break;
                }

                var occurrenceDate = schedule.NextInvoiceDate.Date;
                var invoice = CloneInvoiceFrom(schedule, occurrenceDate, data, idGenerator);
                data.Invoices.Add(invoice);
                generated.Add(invoice);

                schedule.LastGeneratedAt = DateTime.UtcNow;
                // Anchor on the schedule's original billing day-of-month so end-of-month schedules
                // don't drift earlier after passing a shorter month (Jan 31 -> Feb 28 -> Mar 31, ...).
                schedule.NextInvoiceDate = AdvanceDate(
                    schedule.NextInvoiceDate, schedule.Frequency, schedule.StartDate.Day);
                count++;

                // Newly advanced date is past the end date: the schedule is finished.
                if (schedule.EndDate != null && schedule.NextInvoiceDate.Date > schedule.EndDate.Value.Date)
                {
                    schedule.Status = RecurringInvoiceStatus.Completed;
                    break;
                }
            }
        }

        return generated;
    }

    /// <summary>Clones a schedule's template into a concrete draft invoice for one occurrence.</summary>
    public static Invoice CloneInvoiceFrom(RecurringInvoice schedule, DateTime occurrenceDate, CompanyData data, IdGenerator idGenerator)
    {
        var invoice = Clone(schedule.Template!);
        invoice.Id = idGenerator.NextInvoiceId();
        invoice.InvoiceNumber = idGenerator.NextInvoiceNumber();
        invoice.IssueDate = occurrenceDate;
        invoice.DueDate = occurrenceDate.AddDays(PaymentTermsDays(schedule.PaymentTerms));
        invoice.Status = InvoiceStatus.Draft;
        invoice.RecurringInvoiceId = schedule.Id;
        invoice.CreatedAt = occurrenceDate;
        invoice.UpdatedAt = occurrenceDate;
        invoice.AmountPaid = 0;
        invoice.AmountRefunded = 0;
        invoice.History = [];
        invoice.BankMatched = false;
        invoice.BankMatchedDate = null;
        invoice.BankMatchedLineId = null;

        // Totals stay in sync; with no matching payments this keeps Balance == Total and leaves
        // the lifecycle status (Draft) untouched.
        InvoiceTotalsService.Recalculate(invoice, data.Payments);
        return invoice;
    }

    private static Invoice Clone(Invoice source)
    {
        var json = JsonSerializer.Serialize(source, CloneOptions);
        return JsonSerializer.Deserialize<Invoice>(json, CloneOptions)!;
    }
}
