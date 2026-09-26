using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Entities;
using Xunit;

namespace ArgoBooks.Tests.Data;

public class RecordListsTests
{
    // Files from older versions can hold two records with one id. Deleting one of them takes only
    // that one, and undoing the delete brings it back beside the other.
    [Fact]
    public void DeletingAndRestoringOneOfTwoRecordsWithOneId_LeavesBoth()
    {
        var first = new Customer { Id = "CUS-001", Name = "First" };
        var second = new Customer { Id = "CUS-001", Name = "Second" };
        var list = new List<Customer> { first, second };

        list.RemoveRecord(second);

        Assert.Same(first, Assert.Single(list));

        list.RestoreRecord(second);

        Assert.Equal(2, list.Count);
        Assert.Contains(second, list);
    }

    [Fact]
    public void RestoringARecord_WhoseIdCameBackAsAnotherRecord_AddsNothing()
    {
        var record = new Customer { Id = "CUS-001" };
        var list = new List<Customer> { record };

        list.RemoveRecord(record);
        list.Add(new Customer { Id = "CUS-001" });
        list.RestoreRecord(record);

        Assert.Single(list);
    }

    [Fact]
    public void RemovingARecordNoLongerInTheList_TakesOnlyTheFirstWithItsId()
    {
        var list = new List<Customer> { new() { Id = "CUS-001", Name = "A" }, new() { Id = "CUS-001", Name = "B" } };

        Assert.True(list.RemoveRecord(new Customer { Id = "CUS-001" }));

        Assert.Equal("B", Assert.Single(list).Name);
    }

    [Fact]
    public void AnUpdate_WritesOntoTheRecordAlreadyInTheList()
    {
        var existing = new Customer { Id = "CUS-001", Name = "Old", Email = "old@example.com" };
        var list = new List<Customer> { existing };

        var live = list.AddOrUpdate(existing, new Customer { Id = "CUS-001", Name = "New" });

        Assert.Same(existing, live);
        Assert.Same(existing, Assert.Single(list));
        Assert.Equal("New", existing.Name);
        Assert.Equal(string.Empty, existing.Email);
    }
}
