using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Transactions;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Undo of a spreadsheet import restores the company from a snapshot taken before it, and redo
/// from one taken after. Anything the snapshot leaves out survives an undo: employees the import
/// added stayed on the payroll, and the conversions it queued stayed behind for rows that were
/// gone, so the conversion service dropped them and a redo brought those rows back pending with
/// nothing left to convert them.
/// </summary>
public class ImportUndoSnapshotTests
{
    [Fact]
    public void UndoingAnImport_TakesItsEmployeesAndQueuedConversions_AndRedoBringsThemBack()
    {
        var data = new CompanyData();
        data.Employees.Add(new Employee { Id = "EMP-001", Name = "Already here" });
        var before = App.CreateCompanyDataSnapshot(data);

        var revenueId = $"REV-{Guid.NewGuid():N}";
        data.Employees.Add(new Employee { Id = "EMP-002", Name = "Imported" });
        data.Revenues.Add(new Revenue { Id = revenueId, Total = 100m, OriginalCurrency = "CAD", IsPendingConversion = true });
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = revenueId, TransactionType = "Revenue", OriginalCurrency = "CAD",
            TransactionDate = new DateTime(2026, 3, 1), Total = 100m
        });
        var after = App.CreateCompanyDataSnapshot(data);

        App.RestoreCompanyDataFromSnapshot(data, before);

        Assert.Equal("EMP-001", Assert.Single(data.Employees).Id);
        Assert.Empty(data.Revenues);
        Assert.Empty(data.PendingConversions);

        App.RestoreCompanyDataFromSnapshot(data, after);

        Assert.Equal(2, data.Employees.Count);
        Assert.True(Assert.Single(data.Revenues).IsPendingConversion);
        Assert.Equal(revenueId, Assert.Single(data.PendingConversions).TransactionId);
    }
}
