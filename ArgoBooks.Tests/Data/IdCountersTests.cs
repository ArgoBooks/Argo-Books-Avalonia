using System.Reflection;
using ArgoBooks.Core.Data;
using Xunit;

namespace ArgoBooks.Tests.Data;

/// <summary>
/// Undo and redo put the id counters back through <see cref="IdCounters"/> alone. Three copies
/// listed the counters by hand, and each missed some: an import's undo left the recurring
/// transaction and paired device counters where the import had moved them.
/// </summary>
public class IdCountersTests
{
    private static readonly PropertyInfo[] Counters = typeof(IdCounters)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(int))
        .ToArray();

    private static IdCounters Numbered(int start)
    {
        var counters = new IdCounters();
        for (var i = 0; i < Counters.Length; i++)
            Counters[i].SetValue(counters, start + i);
        return counters;
    }

    [Fact]
    public void Clone_CopiesEveryCounter()
    {
        var original = Numbered(10);

        var copy = original.Clone();

        Assert.NotSame(original, copy);
        Assert.All(Counters, p => Assert.Equal(p.GetValue(original), p.GetValue(copy)));
    }

    [Fact]
    public void CopyFrom_OverwritesEveryCounter()
    {
        var target = Numbered(100);

        target.CopyFrom(Numbered(10));

        Assert.All(Counters, p => Assert.Equal(p.GetValue(Numbered(10)), p.GetValue(target)));
    }

    [Fact]
    public void RewindTo_LowersOnlyTheCountersStillWhereTheImportLeftThem()
    {
        var before = Numbered(10);
        var after = Numbered(20);
        var live = after.Clone();
        live.Revenue += 5;

        live.RewindTo(before, after);

        Assert.Equal(after.Revenue + 5, live.Revenue);
        Assert.All(Counters.Where(p => p.Name != nameof(IdCounters.Revenue)),
            p => Assert.Equal(p.GetValue(before), p.GetValue(live)));
    }

    [Fact]
    public void RaiseTo_NeverLowersACounter()
    {
        var live = Numbered(10);
        live.Expense = 500;

        live.RaiseTo(Numbered(20));

        Assert.Equal(500, live.Expense);
        Assert.All(Counters.Where(p => p.Name != nameof(IdCounters.Expense)),
            p => Assert.Equal(p.GetValue(Numbered(20)), p.GetValue(live)));
    }

    [Fact]
    public void RestoringASnapshot_PutsBackEveryCounter()
    {
        var data = new CompanyData();
        data.IdCounters.CopyFrom(Numbered(10));
        var snapshot = App.CreateCompanyDataSnapshot(data);
        data.IdCounters.CopyFrom(Numbered(50));

        App.RestoreCompanyDataFromSnapshot(data, snapshot);

        Assert.All(Counters, p => Assert.Equal(p.GetValue(Numbered(10)), p.GetValue(data.IdCounters)));
    }
}
