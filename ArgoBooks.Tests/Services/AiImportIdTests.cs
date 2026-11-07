using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Rows the AI returns in an unexpected shape, a number for an id or a reply that is not a
/// record at all, must not stop the rest of the import.
/// </summary>
public class AiImportIdTests
{
    private static LlmProcessedData Customers(params string[] entities)
    {
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Customers };
        foreach (var entity in entities)
            chunk.Entities.Add(JsonDocument.Parse(entity).RootElement.Clone());
        return chunk;
    }

    [Fact]
    public void NumericId_IsImportedAsText()
    {
        var data = new CompanyData();

        var result = new SpreadsheetImportService().ImportProcessedEntities(
            data, [Customers("{\"id\":1001,\"name\":\"Acme\"}")], "Customers");

        Assert.Equal(1, result.Inserted);
        Assert.Equal("1001", Assert.Single(data.Customers).Id);
    }

    [Fact]
    public void ReplyThatIsNotARecord_IsSkippedAndTheRestImported()
    {
        var data = new CompanyData();

        var result = new SpreadsheetImportService().ImportProcessedEntities(
            data, [Customers("\"just some text\"", "{\"id\":\"CUS-001\",\"name\":\"Acme\"}")], "Customers");

        Assert.Equal(1, result.Inserted);
        Assert.Single(result.UnimportedRows);
    }
}
