using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class ImportSchemaDefinitionTests
{
    // Pay runs are exported to be read, not imported: an approved run's figures are frozen so a
    // stub reprinted next year still matches the one the employee was handed, and taking them
    // back from a sheet somebody could have edited would defeat that.
    private static bool IsImportable(SpreadsheetSheetType type) =>
        type is not (SpreadsheetSheetType.PayRuns or SpreadsheetSheetType.Unknown);

    [Fact]
    public void GetSchema_ContainsEveryImportableEntityType()
    {
        // Every sheet the importer will read has to say what its columns mean, otherwise the
        // mapper has nothing to map onto and the sheet silently imports as nothing. Export-only
        // sheets are excluded by IsImportable rather than named here.
        var schema = ImportSchemaDefinition.GetSchema();

        foreach (var entityType in Enum.GetValues<SpreadsheetSheetType>())
        {
            if (!IsImportable(entityType)) continue;
            Assert.True(schema.ContainsKey(entityType), $"Schema missing definition for {entityType}");
        }
    }

    [Fact]
    public void GetSchema_HasNoDefinitionForExportOnlyTypes()
    {
        // The other side of the rule. A schema for an export-only sheet would advertise to the
        // mapper that it can be imported, and pay runs deliberately cannot be.
        var schema = ImportSchemaDefinition.GetSchema();

        foreach (var entityType in Enum.GetValues<SpreadsheetSheetType>())
        {
            if (IsImportable(entityType) || entityType == SpreadsheetSheetType.Unknown) continue;
            Assert.False(schema.ContainsKey(entityType), $"{entityType} is export only but has an import schema");
        }
    }

    [Fact]
    public void GetSchema_AllTypesHaveAtLeastOneColumn()
    {
        var schema = ImportSchemaDefinition.GetSchema();

        foreach (var (entityType, columns) in schema)
        {
            Assert.True(columns.Count > 0, $"Schema for {entityType} has no columns");
        }
    }

    [Fact]
    public void GetSchema_AllColumnsHaveNameAndType()
    {
        var schema = ImportSchemaDefinition.GetSchema();

        foreach (var (entityType, columns) in schema)
        {
            foreach (var col in columns)
            {
                Assert.False(string.IsNullOrWhiteSpace(col.Name),
                    $"Schema column in {entityType} has empty name");
                Assert.False(string.IsNullOrWhiteSpace(col.Type),
                    $"Schema column '{col.Name}' in {entityType} has empty type");
            }
        }
    }

    [Fact]
    public void GetSchema_CustomersHasRequiredColumns()
    {
        var schema = ImportSchemaDefinition.GetSchema();
        var customers = schema[SpreadsheetSheetType.Customers];

        var requiredColumns = customers.Where(c => c.Required).Select(c => c.Name).ToList();
        Assert.Contains("ID", requiredColumns);
        Assert.Contains("Name", requiredColumns);
    }

    [Fact]
    public void GetSchema_InvoicesHasRequiredColumns()
    {
        var schema = ImportSchemaDefinition.GetSchema();
        var invoices = schema[SpreadsheetSheetType.Invoices];

        var columnNames = invoices.Select(c => c.Name).ToList();
        Assert.Contains("Invoice #", columnNames);
        Assert.Contains("Customer ID", columnNames);
        Assert.Contains("Total", columnNames);
    }

    [Fact]
    public void FormatSchemaForPrompt_ReturnsNonEmptyString()
    {
        var prompt = ImportSchemaDefinition.FormatSchemaForPrompt();

        Assert.False(string.IsNullOrWhiteSpace(prompt));
        Assert.Contains("Customers", prompt);
        Assert.Contains("Invoices", prompt);
        Assert.Contains("Expenses", prompt);
    }

    [Fact]
    public void FormatSchemaForPrompt_ContainsEveryImportableEntityType()
    {
        var prompt = ImportSchemaDefinition.FormatSchemaForPrompt();
        var allTypes = Enum.GetValues<SpreadsheetSheetType>();

        foreach (var entityType in allTypes)
        {
            if (!IsImportable(entityType)) continue;
            Assert.Contains(entityType.ToString(), prompt);
        }
    }

    [Fact]
    public void GetSchema_NoDuplicateColumnNames()
    {
        var schema = ImportSchemaDefinition.GetSchema();

        foreach (var (_, columns) in schema)
        {
            var names = columns.Select(c => c.Name).ToList();
            var distinctNames = names.Distinct().ToList();
            Assert.Equal(names.Count, distinctNames.Count);
        }
    }

    [Theory]
    [InlineData("United States", "ZIP Code", "State")]
    [InlineData("US", "ZIP Code", "State")]
    [InlineData("USA", "ZIP Code", "State")]
    [InlineData("Canada", "Postal Code", "Province")]
    [InlineData("CA", "Postal Code", "Province")]
    [InlineData("United Kingdom", "Postcode", "County")]
    [InlineData("UK", "Postcode", "County")]
    [InlineData("GB", "Postcode", "County")]
    [InlineData("Australia", "Postcode", "State")]
    [InlineData("India", "PIN Code", "State")]
    [InlineData("Germany", "Postal Code", "State")]
    [InlineData(null, "Postal Code", "State/Province")]
    [InlineData("", "Postal Code", "State/Province")]
    [InlineData("Unknown Country", "Postal Code", "State/Province")]
    public void GetAddressLabels_ReturnsCountrySpecificLabels(string? country, string expectedPostalLabel, string expectedStateLabel)
    {
        var (stateLabel, _, postalLabel, _) = ImportSchemaDefinition.GetAddressLabels(country);

        Assert.Equal(expectedPostalLabel, postalLabel);
        Assert.Equal(expectedStateLabel, stateLabel);
    }

    [Fact]
    public void GetSchema_WithCountry_UsesCountrySpecificLabels()
    {
        var usSchema = ImportSchemaDefinition.GetSchema("United States");
        var customers = usSchema[SpreadsheetSheetType.Customers];
        var columnNames = customers.Select(c => c.Name).ToList();

        Assert.Contains("ZIP Code", columnNames);
        Assert.Contains("State", columnNames);
        Assert.DoesNotContain("Postal Code", columnNames);
    }

    [Fact]
    public void GetSchema_WithUK_UsesPostcodeAndCounty()
    {
        var ukSchema = ImportSchemaDefinition.GetSchema("United Kingdom");
        var customers = ukSchema[SpreadsheetSheetType.Customers];
        var columnNames = customers.Select(c => c.Name).ToList();

        Assert.Contains("Postcode", columnNames);
        Assert.Contains("County", columnNames);
    }

    [Fact]
    public void FormatSchemaForPrompt_WithCountry_UsesCountryLabels()
    {
        var prompt = ImportSchemaDefinition.FormatSchemaForPrompt("United States");

        Assert.Contains("ZIP Code", prompt);
        Assert.Contains("| State |", prompt);
        Assert.DoesNotContain("Postal Code", prompt);
    }
}
