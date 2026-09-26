using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Undoing an integration import must take its currency-conversion entries with it.
///
/// A row imported on a date with no cached rate is stored unconverted and queued for
/// the background service to finish later. Undo removes the row. Nothing removed the
/// queue entry: the reconcile pass only drops entries whose record exists AND is
/// already converted, so one whose record is gone was kept and retried forever.
///
/// The queue is shared across companies, which is why the fix is "forget these ids"
/// rather than "drop anything missing from the open company" - the latter would throw
/// away another company's entries whenever this one happened to be open.
/// </summary>
public class ImportUndoPendingConversionTests
{
    private static PendingConversion Entry(string id, string type = "Revenue") => new()
    {
        TransactionId = id,
        TransactionType = type,
        OriginalCurrency = "EUR",
        TransactionDate = new DateTime(2026, 6, 20),
        Total = 100m
    };

    [Fact]
    public void UndoingAnArgoApiImport_RemovesItsQueuedConversions()
    {
        var data = new CompanyData();
        var revenue = new Revenue { Id = "REV-2026-00001", Description = "Order", Total = 100m };
        var expense = new Expense { Id = "PUR-2026-00001", Description = "Hosting", Total = 24m };

        data.Revenues.Add(revenue);
        data.Expenses.Add(expense);
        data.PendingConversions.Add(Entry(revenue.Id));
        data.PendingConversions.Add(Entry(expense.Id, "Expense"));

        var creation = new ArgoApiImportCreation();
        creation.Revenues.Add(revenue);
        creation.Expenses.Add(expense);

        creation.Undo(data);

        Assert.Empty(data.Revenues);
        Assert.Empty(data.Expenses);
        Assert.Empty(data.PendingConversions);
    }

    [Fact]
    public void UndoingAStripeImport_RemovesItsQueuedConversions()
    {
        var data = new CompanyData();
        var revenue = new Revenue { Id = "REV-2026-00002", Description = "Stripe sale", Total = 50m };

        data.Revenues.Add(revenue);
        data.PendingConversions.Add(Entry(revenue.Id));

        var creation = new StripeImportCreation();
        creation.Revenues.Add(revenue);

        creation.Undo(data);

        Assert.Empty(data.PendingConversions);
    }

    /// <summary>
    /// The reason the fix names ids instead of scanning for orphans. An entry belonging
    /// to a record this import never created must survive, whether it belongs to a row
    /// still present or to another company entirely.
    /// </summary>
    [Fact]
    public void UndoLeavesConversionsItDidNotQueue()
    {
        var data = new CompanyData();
        var mine = new Revenue { Id = "REV-2026-00003", Description = "Imported", Total = 10m };
        var theirs = new Revenue { Id = "REV-2026-00004", Description = "Typed in by hand", Total = 20m };

        data.Revenues.Add(mine);
        data.Revenues.Add(theirs);
        data.PendingConversions.Add(Entry(mine.Id));
        data.PendingConversions.Add(Entry(theirs.Id));
        // No row for this one in the open company: it belongs to a different company file.
        data.PendingConversions.Add(Entry("REV-2026-09999"));

        var creation = new ArgoApiImportCreation();
        creation.Revenues.Add(mine);

        creation.Undo(data);

        var left = data.PendingConversions.Select(p => p.TransactionId).ToList();
        Assert.DoesNotContain(mine.Id, left);
        Assert.Contains(theirs.Id, left);
        Assert.Contains("REV-2026-09999", left);
    }

    // Redo put the rows back without their queued conversions, so a row still waiting for its rate
    // waited forever. One converted before the undo has its figure and needs no entry.
    [Fact]
    public void RedoingAnImport_QueuesAgainOnlyTheRowsStillWaiting()
    {
        var data = new CompanyData();
        var waiting = new Revenue
        {
            Id = "REV-2026-00006", Date = new DateTime(2026, 6, 20), OriginalCurrency = "EUR", Total = 100m, IsPendingConversion = true
        };
        var converted = new Expense { Id = "PUR-2026-00006", OriginalCurrency = "EUR", Total = 24m, TotalUSD = 30m };
        data.Revenues.Add(waiting);
        data.Expenses.Add(converted);
        data.PendingConversions.Add(Entry(waiting.Id));

        var creation = new ArgoApiImportCreation();
        creation.Revenues.Add(waiting);
        creation.Expenses.Add(converted);

        creation.Undo(data);
        creation.Redo(data);

        var entry = Assert.Single(data.PendingConversions);
        Assert.Equal((waiting.Id, "Revenue", 100m), (entry.TransactionId, entry.TransactionType, entry.Total));
    }

    [Fact]
    public void UndoWithNothingQueuedDoesNotThrow()
    {
        var data = new CompanyData();
        var revenue = new Revenue { Id = "REV-2026-00005", Description = "Order", Total = 1m };
        data.Revenues.Add(revenue);

        var creation = new ArgoApiImportCreation();
        creation.Revenues.Add(revenue);

        creation.Undo(data);

        Assert.Empty(data.PendingConversions);
    }
}
