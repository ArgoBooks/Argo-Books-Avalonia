using System.Text.Json;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class SpreadsheetAnalysisServiceTests
{
    /// <summary>
    /// Mock Gemini service for testing analysis without real API calls.
    /// </summary>
    private class MockGeminiService : IGeminiService
    {
        public bool IsConfigured => true;
        public string? LastSystemPrompt { get; private set; }
        public string? LastUserPrompt { get; private set; }
        public string? ResponseToReturn { get; set; }

        public Task<string?> SendChatAsync(
            string systemPrompt, string userPrompt,
            int maxTokens = 4000, double temperature = 0.1,
            CancellationToken cancellationToken = default,
            OperationKind operation = OperationKind.Completion,
            long? sizeFeature = null)
        {
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;
            return Task.FromResult(ResponseToReturn);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_WithNullResponse_ReturnsNull()
    {
        var mock = new MockGeminiService { ResponseToReturn = null };
        var service = new SpreadsheetAnalysisService(mock);

        // This will return null since there's no real file, but tests the null response path
        var result = await service.AnalyzeAsync("/nonexistent.xlsx");
        Assert.Null(result);
    }

    [Fact]
    public void ParseAnalysisResponse_ValidJson_ReturnsResult()
    {
        var json = JsonSerializer.Serialize(new
        {
            sheets = new[]
            {
                new
                {
                    sourceSheetName = "Sheet1",
                    detectedType = "Customers",
                    confidence = 0.95,
                    tier = "Tier1_Mapping",
                    tierReason = "",
                    columnMappings = new[]
                    {
                        new { sourceColumn = "Client Name", targetColumn = "Name", confidence = 0.9, transformHint = (string?)null }
                    },
                    unmappedSourceColumns = new[] { "Extra" },
                    unmappedTargetColumns = new[] { "Phone" }
                }
            },
            warnings = new[] { "Some columns could not be mapped" }
        });

        // ParseAnalysisResponse is internal static
        var method = typeof(SpreadsheetAnalysisService).GetMethod("ParseAnalysisResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var result = method.Invoke(null, [json]) as SpreadsheetAnalysisResult;

        Assert.NotNull(result);
        Assert.Single(result.Sheets);
        Assert.Equal("Sheet1", result.Sheets[0].SourceSheetName);
        Assert.Equal(SpreadsheetSheetType.Customers, result.Sheets[0].DetectedType);
        Assert.Equal(0.95, result.Sheets[0].Confidence);
        Assert.Single(result.Sheets[0].ColumnMappings);
        Assert.Equal("Client Name", result.Sheets[0].ColumnMappings[0].SourceColumn);
        Assert.Equal("Name", result.Sheets[0].ColumnMappings[0].TargetColumn);
    }

    [Fact]
    public void ParseAnalysisResponse_WithMarkdownCodeBlock_StripsAndParses()
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            sheets = new[]
            {
                new
                {
                    sourceSheetName = "Data",
                    detectedType = "Expenses",
                    confidence = 0.8,
                    tier = "Tier1_Mapping",
                    tierReason = "",
                    columnMappings = Array.Empty<object>(),
                    unmappedSourceColumns = Array.Empty<string>(),
                    unmappedTargetColumns = Array.Empty<string>()
                }
            },
            warnings = Array.Empty<string>()
        });

        var wrappedJson = $"```json\n{innerJson}\n```";

        var method = typeof(SpreadsheetAnalysisService).GetMethod("ParseAnalysisResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method!.Invoke(null, [wrappedJson]) as SpreadsheetAnalysisResult;

        Assert.NotNull(result);
        Assert.Single(result.Sheets);
        Assert.Equal("Data", result.Sheets[0].SourceSheetName);
    }

    [Fact]
    public void ParseAnalysisResponse_MalformedJson_ReturnsNull()
    {
        var method = typeof(SpreadsheetAnalysisService).GetMethod("ParseAnalysisResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method!.Invoke(null, ["not valid json at all"]) as SpreadsheetAnalysisResult;

        Assert.Null(result);
    }

    [Fact]
    public void SpreadsheetAnalysisResult_DefaultValues()
    {
        var result = new SpreadsheetAnalysisResult();
        Assert.Empty(result.Sheets);
        Assert.Equal(string.Empty, result.FileName);
    }

    [Fact]
    public void SheetAnalysis_DefaultValues()
    {
        var sheet = new SheetAnalysis();
        Assert.Equal(string.Empty, sheet.SourceSheetName);
        Assert.Equal(0, sheet.Confidence);
        Assert.Empty(sheet.ColumnMappings);
        Assert.Empty(sheet.UnmappedSourceColumns);
        Assert.Empty(sheet.UnmappedTargetColumns);
        Assert.True(sheet.IsIncluded);
    }

    [Fact]
    public void ColumnMapping_Properties()
    {
        var mapping = new ColumnMapping
        {
            SourceColumn = "Client",
            TargetColumn = "Name",
            Confidence = 0.85,
            TransformHint = "trim whitespace"
        };

        Assert.Equal("Client", mapping.SourceColumn);
        Assert.Equal("Name", mapping.TargetColumn);
        Assert.Equal(0.85, mapping.Confidence);
        Assert.Equal("trim whitespace", mapping.TransformHint);
    }

    [Fact]
    public void LlmProcessedData_DefaultValues()
    {
        var data = new LlmProcessedData();
        Assert.Empty(data.Entities);
        Assert.Empty(data.Warnings);
        Assert.Equal(0, data.SourceRowsProcessed);
    }

    [Fact]
    public void ProcessingTier_HasExpectedValues()
    {
        Assert.Equal(0, (int)ProcessingTier.Tier1_Mapping);
        Assert.Equal(1, (int)ProcessingTier.Tier2_LlmProcessing);
    }

    [Fact]
    public void ParseAnalysisResponse_UnknownType_MarksUnsupported()
    {
        var json = "{\"sheets\":[{\"sourceSheetName\":\"Appointments\",\"detectedType\":\"Unknown\",\"confidence\":0.2,\"tier\":\"Tier1_Mapping\"}]}";
        var method = typeof(SpreadsheetAnalysisService).GetMethod("ParseAnalysisResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (SpreadsheetAnalysisResult)method!.Invoke(null, [json])!;
        var sheet = result.Sheets.Single();
        Assert.False(sheet.IsIncluded);
        Assert.False(string.IsNullOrEmpty(sheet.UnsupportedReason));
    }

    [Fact]
    public void ParseAnalysisResponse_LowConfidence_MarksUnsupported()
    {
        var json = "{\"sheets\":[{\"sourceSheetName\":\"Misc\",\"detectedType\":\"Customers\",\"confidence\":0.3,\"tier\":\"Tier1_Mapping\"}]}";
        var method = typeof(SpreadsheetAnalysisService).GetMethod("ParseAnalysisResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (SpreadsheetAnalysisResult)method!.Invoke(null, [json])!;
        var sheet = result.Sheets.Single();
        Assert.False(sheet.IsIncluded);
        Assert.False(string.IsNullOrEmpty(sheet.UnsupportedReason));
    }

    [Fact]
    public void ParseAnalysisResponse_HighConfidenceKnownType_NotMarkedUnsupported()
    {
        var json = "{\"sheets\":[{\"sourceSheetName\":\"Customers\",\"detectedType\":\"Customers\",\"confidence\":0.98,\"tier\":\"Tier1_Mapping\",\"columnMappings\":[],\"unmappedSourceColumns\":[],\"unmappedTargetColumns\":[]}]}";
        var method = typeof(SpreadsheetAnalysisService).GetMethod("ParseAnalysisResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (SpreadsheetAnalysisResult)method!.Invoke(null, [json])!;
        var sheet = result.Sheets.Single();
        Assert.True(sheet.IsIncluded);
        Assert.Null(sheet.UnsupportedReason);
    }

    [Fact]
    public void ParseTier2Response_UnrecognizedWrapperKey_ReturnsNull()
    {
        // The AI wrapped its array under "records" instead of a bare array or an "entities" key.
        // This unrecognized structure must be treated as a failure (null) so the caller counts it,
        // rather than returning an empty result that silently imports zero rows with no warning.
        var method = typeof(SpreadsheetAnalysisService).GetMethod("ParseTier2Response",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        const string json = """{"records":[{"name":"Acme"}]}""";

        var result = method.Invoke(null, [json, SpreadsheetSheetType.Customers, 1]);

        Assert.Null(result);
    }

    [Fact]
    public void ParseAnalysisResponse_OneMalformedSheetElement_KeepsValidSheets()
    {
        // One element is missing "sourceSheetName". The throwing GetProperty discarded the WHOLE
        // batch (including the valid sheet before it); the valid sheet must survive.
        const string json = """
        {"sheets":[
          {"sourceSheetName":"Good","detectedType":"Revenue","confidence":0.9},
          {"detectedType":"Expenses","confidence":0.5}
        ]}
        """;
        var method = typeof(SpreadsheetAnalysisService).GetMethod("ParseAnalysisResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = (SpreadsheetAnalysisResult?)method.Invoke(null, [json]);

        Assert.NotNull(result);
        Assert.Contains(result.Sheets, s => s.SourceSheetName == "Good");
    }
}
