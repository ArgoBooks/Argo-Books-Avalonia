using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// After an import the id counters must stay ahead of every id ever handed out, not just the
/// ones still present. A deleted record's id reissued to a new one makes the new record look
/// like the old one to anything that still points at that id.
/// </summary>
public class SpreadsheetImportIdCounterTests
{
    private static void ImportOneCategory(CompanyData data)
    {
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Categories };
        chunk.Entities.Add(JsonDocument.Parse("""{ "id": "CAT-X", "name": "Misc" }""").RootElement.Clone());
        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Categories");
    }

    [Fact]
    public void Import_DoesNotLowerACounterPastADeletedId()
    {
        var data = new CompanyData();
        for (int i = 1; i <= 9; i++)
            data.Customers.Add(new Customer { Id = $"CUS-{i:D3}", Name = $"Customer {i}" });
        data.Revenues.Add(new Revenue { Id = "REV-2026-00003" });

        // CUS-010 and REV-2026-00004 were issued and then deleted.
        data.IdCounters.Customer = 10;
        data.IdCounters.Revenue = 4;

        ImportOneCategory(data);

        Assert.Equal(10, data.IdCounters.Customer);
        Assert.Equal(4, data.IdCounters.Revenue);
    }

    [Fact]
    public void Import_StillRaisesACounterBehindTheImportedIds()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-042", Name = "Imported" });

        ImportOneCategory(data);

        Assert.Equal(42, data.IdCounters.Customer);
    }
}
