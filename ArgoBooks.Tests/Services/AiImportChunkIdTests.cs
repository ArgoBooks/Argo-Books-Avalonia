using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Each chunk of a sheet is converted by the AI on its own, so the ids it makes up for a sheet
/// without an id column can come back the same from two chunks for two different rows.
/// </summary>
public class AiImportChunkIdTests
{
    private static LlmProcessedData Customers(params string[] entities)
    {
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Customers };
        foreach (var entity in entities)
            chunk.Entities.Add(JsonDocument.Parse(entity).RootElement.Clone());
        return chunk;
    }

    [Fact]
    public void MadeUpIdRepeatedInALaterChunk_IsRenamed_SoBothRecordsImport()
    {
        var data = new CompanyData();
        List<List<List<string>>> rows = [[["Acme Ltd"]], [["Beta Inc"]]];
        LlmProcessedData?[] chunks =
        [
            Customers("{\"id\":\"CUS-001\",\"name\":\"Acme Ltd\"}"),
            Customers("{\"id\":\"CUS-001\",\"name\":\"Beta Inc\"}")
        ];

        SpreadsheetAnalysisService.MakeMadeUpIdsUniqueAcrossChunks(rows, chunks);
        new SpreadsheetImportService().ImportProcessedEntities(data, [chunks[0]!, chunks[1]!], "Customers");

        Assert.Equal(2, data.Customers.Count);
        Assert.Equal("CUS-001-c2", chunks[1]!.Entities[0].GetProperty("id").GetString());
    }

    [Fact]
    public void IdTheAiPrefixedFromACellOfTheSheet_IsKept()
    {
        List<List<List<string>>> rows = [[["1001", "Acme Ltd"]], [["1001", "Acme Ltd"]]];
        LlmProcessedData?[] chunks =
        [
            Customers("{\"id\":\"INV-1001\",\"name\":\"Acme Ltd\"}"),
            Customers("{\"id\":\"INV-1001\",\"name\":\"Acme Ltd\"}")
        ];

        SpreadsheetAnalysisService.MakeMadeUpIdsUniqueAcrossChunks(rows, chunks);

        Assert.Equal("INV-1001", chunks[1]!.Entities[0].GetProperty("id").GetString());
    }

    [Fact]
    public void RenamedId_DoesNotMatchTheNumberedCopiesTheBackupImportMakes()
    {
        List<List<List<string>>> rows = [[["X", "a"], ["X", "b"]], [["Beta Inc"]]];
        LlmProcessedData?[] chunks =
        [
            Customers("{\"id\":\"X\",\"name\":\"a\"}", "{\"id\":\"X\",\"name\":\"b\"}"),
            Customers("{\"id\":\"X\",\"name\":\"Beta Inc\"}")
        ];

        SpreadsheetAnalysisService.MakeMadeUpIdsUniqueAcrossChunks(rows, chunks);
        var idsSeen = new Dictionary<string, int>();
        var ids = chunks.SelectMany(c => c!.Entities)
            .Select(e => SpreadsheetAnalysisService.WithUniqueId(e, idsSeen).GetProperty("id").GetString())
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void IdFromTheSheetItself_IsKept_WhenALaterChunkRepeatsIt()
    {
        List<List<List<string>>> rows = [[["C-7", "Acme Ltd"]], [["C-7", "Acme Ltd"]]];
        LlmProcessedData?[] chunks =
        [
            Customers("{\"id\":\"C-7\",\"name\":\"Acme Ltd\"}"),
            Customers("{\"id\":\"C-7\",\"name\":\"Acme Ltd\"}")
        ];

        SpreadsheetAnalysisService.MakeMadeUpIdsUniqueAcrossChunks(rows, chunks);

        Assert.Equal("C-7", chunks[1]!.Entities[0].GetProperty("id").GetString());
    }
}
