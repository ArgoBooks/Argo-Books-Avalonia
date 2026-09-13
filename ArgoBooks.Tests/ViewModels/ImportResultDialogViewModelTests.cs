using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for ImportResultDialogViewModel focusing on the unimported-rows surface.
/// </summary>
public class ImportResultDialogViewModelTests
{
    private static void CallShowAsync(ImportResultDialogViewModel vm,
        List<UnimportedRow>? unimportedRows = null)
    {
        vm.ShowAsync(
            fileName: "test.xlsx",
            sheetResults: [],
            totalNew: 0,
            totalUpdated: 0,
            totalSkipped: 0,
            skipReasons: [],
            warnings: [],
            needsSave: false,
            unimportedRows: unimportedRows);
    }

    #region HasUnimportedRows

    [Fact]
    public void ShowAsync_WithNoUnimportedRows_HasUnimportedRowsIsFalse()
    {
        var vm = new ImportResultDialogViewModel();

        CallShowAsync(vm);

        Assert.False(vm.HasUnimportedRows);
    }

    [Fact]
    public void ShowAsync_WithUnimportedRows_HasUnimportedRowsIsTrue()
    {
        var vm = new ImportResultDialogViewModel();
        var rows = new List<UnimportedRow>
        {
            new() { Sheet = "Expenses", Reason = "Duplicate id", RowNumber = 5 }
        };

        CallShowAsync(vm, rows);

        Assert.True(vm.HasUnimportedRows);
    }

    [Fact]
    public void ShowAsync_WithUnimportedRows_PopulatesUnimportedRowsCollection()
    {
        var vm = new ImportResultDialogViewModel();
        var rows = new List<UnimportedRow>
        {
            new() { Sheet = "Expenses", Reason = "Missing date", RowNumber = 3 },
            new() { Sheet = "Revenue",  Reason = "Invalid amount", RowNumber = 7 }
        };

        CallShowAsync(vm, rows);

        Assert.Equal(2, vm.UnimportedRows.Count);
    }

    [Fact]
    public void ShowAsync_CalledTwice_ClearsUnimportedRowsFromPreviousCall()
    {
        var vm = new ImportResultDialogViewModel();
        var firstRows = new List<UnimportedRow>
        {
            new() { Sheet = "Expenses", Reason = "Bad date", RowNumber = 2 }
        };

        CallShowAsync(vm, firstRows);
        Assert.Single(vm.UnimportedRows);

        // Second call with no unimported rows should clear the collection.
        CallShowAsync(vm);

        Assert.Empty(vm.UnimportedRows);
        Assert.False(vm.HasUnimportedRows);
    }

    #endregion

    #region BuildUnimportedCsv

    [Fact]
    public void BuildUnimportedCsv_WithRows_ContainsHeader()
    {
        var vm = new ImportResultDialogViewModel();
        CallShowAsync(vm, [new() { Sheet = "S", Reason = "R", RowNumber = 1 }]);

        var csv = vm.BuildUnimportedCsv();

        Assert.Contains("Sheet,Row,Reason,Value", csv);
    }

    [Fact]
    public void BuildUnimportedCsv_WithRows_ContainsRowData()
    {
        var vm = new ImportResultDialogViewModel();
        CallShowAsync(vm, [new() { Sheet = "Expenses", Reason = "Missing date", RowNumber = 5, RawValue = "abc" }]);

        var csv = vm.BuildUnimportedCsv();

        Assert.Contains("Expenses", csv);
        Assert.Contains("Missing date", csv);
        Assert.Contains("5", csv);
        Assert.Contains("abc", csv);
    }

    [Fact]
    public void BuildUnimportedCsv_FieldWithComma_IsQuoted()
    {
        var vm = new ImportResultDialogViewModel();
        CallShowAsync(vm, [new() { Sheet = "Sheet, One", Reason = "Bad value", RowNumber = 0 }]);

        var csv = vm.BuildUnimportedCsv();

        // The sheet name containing a comma must be quoted.
        Assert.Contains("\"Sheet, One\"", csv);
    }

    [Fact]
    public void BuildUnimportedCsv_FieldWithDoubleQuote_IsEscaped()
    {
        var vm = new ImportResultDialogViewModel();
        CallShowAsync(vm, [new() { Sheet = "Expenses", Reason = "Value \"bad\"", RowNumber = 0 }]);

        var csv = vm.BuildUnimportedCsv();

        // Double-quotes inside a field must be doubled per RFC-4180.
        Assert.Contains("\"Value \"\"bad\"\"\"", csv);
    }

    [Fact]
    public void BuildUnimportedCsv_ZeroRowNumber_ProducesEmptyRowCell()
    {
        var vm = new ImportResultDialogViewModel();
        CallShowAsync(vm, [new() { Sheet = "Expenses", Reason = "Aggregate error", RowNumber = 0 }]);

        var csv = vm.BuildUnimportedCsv();

        // Row 0 means unknown; the cell should be blank: "Expenses,,Aggregate error,"
        Assert.Contains("Expenses,,Aggregate error", csv);
    }

    [Fact]
    public void BuildUnimportedCsv_WithNoRows_ReturnsOnlyHeader()
    {
        var vm = new ImportResultDialogViewModel();
        // ShowAsync with null unimportedRows leaves the collection empty.
        CallShowAsync(vm);

        var csv = vm.BuildUnimportedCsv();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines);
        Assert.Contains("Sheet,Row,Reason,Value", lines[0]);
    }

    #endregion
}
