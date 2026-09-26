using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The report designer's undoable actions, run through the designer's UndoRedoManager.
/// </summary>
public class ReportDesignerUndoTests
{
    #region Add/Remove Element Action Integration Tests

    [Fact]
    public void AddThenDelete_UndoBoth_ElementIsRemoved()
    {
        // Regression test: add element, delete it, undo both → element should be gone
        var config = new ReportConfiguration();
        var manager = new UndoRedoManager();

        // Step 1: Add element
        var element = new LabelReportElement { X = 10, Y = 20, Width = 100, Height = 50 };
        config.AddElement(element);
        manager.RecordAction(new AddElementAction(config, element));
        Assert.Single(config.Elements);

        // Step 2: Delete element
        manager.RecordAction(new RemoveElementAction(config, element));
        config.RemoveElement(element.Id);
        Assert.Empty(config.Elements);

        // Step 3: Undo delete → element should reappear
        manager.Undo();
        Assert.Single(config.Elements);

        // Step 4: Undo add → element should be gone
        manager.Undo();
        Assert.Empty(config.Elements);
    }

    [Fact]
    public void AddThenDelete_UndoBoth_RedoBoth_ElementIsRemoved()
    {
        // Full round-trip: add, delete, undo×2, redo×2
        var config = new ReportConfiguration();
        var manager = new UndoRedoManager();

        var element = new LabelReportElement { X = 10, Y = 20, Width = 100, Height = 50 };
        config.AddElement(element);
        manager.RecordAction(new AddElementAction(config, element));

        manager.RecordAction(new RemoveElementAction(config, element));
        config.RemoveElement(element.Id);

        // Undo both
        manager.Undo(); // undo delete
        manager.Undo(); // undo add
        Assert.Empty(config.Elements);

        // Redo both
        manager.Redo(); // redo add
        Assert.Single(config.Elements);
        manager.Redo(); // redo delete
        Assert.Empty(config.Elements);
    }

    #endregion

    #region Page delete and element layer

    /// <summary>Deletes a page the way ReportsPageViewModel.DeletePage does.</summary>
    private static void DeletePage(ReportConfiguration config, UndoRedoManager manager, int page)
    {
        var onPage = config.Elements.Where(e => e.PageNumber == page).ToList();
        manager.RecordAction(new DeletePageAction(config, page, onPage));
        foreach (var element in onPage)
            config.Elements.Remove(element);
        foreach (var element in config.Elements.Where(e => e.PageNumber > page))
            element.PageNumber--;
        config.PageCount--;
    }

    [Fact]
    public void DeletePage_Undo_KeepsElementIds_SoEarlierUndosStillWork()
    {
        var config = new ReportConfiguration { PageCount = 2 };
        var manager = new UndoRedoManager();
        var label = new LabelReportElement { X = 10, Y = 20, Width = 100, Height = 50, PageNumber = 2 };
        config.AddElement(label);

        manager.RecordAction(new MoveResizeElementAction(config, label.Id, (10, 20, 100, 50), (60, 70, 100, 50)));
        label.Bounds = (60, 70, 100, 50);
        DeletePage(config, manager, 2);

        manager.Undo(); // the page comes back
        var restored = Assert.Single(config.Elements);
        Assert.Equal(label.Id, restored.Id);

        manager.Undo(); // then the move is undone
        Assert.Equal(10, restored.X);
    }

    [Fact]
    public void AddElement_ThenDeleteItsPage_UndoAndRedoBoth_LeavesNoCopy()
    {
        var config = new ReportConfiguration { PageCount = 2 };
        var manager = new UndoRedoManager();
        var label = new LabelReportElement { X = 10, Y = 20, Width = 100, Height = 50, PageNumber = 2 };
        config.AddElement(label);
        manager.RecordAction(new AddElementAction(config, label));
        DeletePage(config, manager, 2);

        manager.Undo(); // page back
        manager.Undo(); // add undone
        Assert.Empty(config.Elements);

        manager.Redo(); // add again
        manager.Redo(); // page deleted again
        Assert.Empty(config.Elements);
    }

    [Fact]
    public void RemoveElement_Undo_PutsItBackOnItsOwnLayer()
    {
        var config = new ReportConfiguration();
        var manager = new UndoRedoManager();
        var back = new LabelReportElement();
        var middle = new LabelReportElement();
        var front = new LabelReportElement();
        config.AddElement(back);
        config.AddElement(middle);
        config.AddElement(front);

        manager.RecordAction(new RemoveElementAction(config, middle));
        config.RemoveElement(middle.Id);
        manager.Undo();

        Assert.Equal([back.Id, middle.Id, front.Id], config.GetElementsByZOrder().Select(e => e.Id));
    }

    #endregion

    #region Deleting a multi-element selection

    [Fact]
    public void DeleteSelection_TakesASingleUndo_AndRestoresEveryElement()
    {
        var config = new ReportConfiguration();
        var manager = new UndoRedoManager();
        var selection = new List<ReportElementBase>();
        for (int i = 0; i < 5; i++)
        {
            var label = new LabelReportElement { X = i * 10, Y = i * 20, Width = 100, Height = 50 };
            config.AddElement(label);
            selection.Add(label);
        }

        RemoveElementsAction.RemoveAndRecord(config, selection, manager);

        Assert.Empty(config.Elements);
        Assert.Single(manager.UndoHistory);

        manager.Undo();

        Assert.Equal(5, config.Elements.Count);
        Assert.Equal(selection.Select(e => e.Id).OrderBy(id => id), config.Elements.Select(e => e.Id).OrderBy(id => id));
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void DeleteSelection_Undo_RestoresPageAndLayerOfEachElement()
    {
        var config = new ReportConfiguration { PageCount = 2 };
        var manager = new UndoRedoManager();
        var kept = new LabelReportElement();
        var first = new LabelReportElement { X = 10, Y = 20, Width = 100, Height = 50 };
        var second = new LabelReportElement { X = 30, Y = 40, Width = 100, Height = 50, PageNumber = 2 };
        config.AddElement(kept);
        config.AddElement(first);
        config.AddElement(second);

        RemoveElementsAction.RemoveAndRecord(config, [first, second], manager);
        manager.Undo();

        Assert.Equal([kept.Id, first.Id, second.Id], config.GetElementsByZOrder().Select(e => e.Id));
        Assert.Equal(2, config.GetElementById(second.Id)!.PageNumber);
        Assert.Equal(30, config.GetElementById(second.Id)!.X);
    }

    [Fact]
    public void DeleteSelection_Redo_RemovesTheWholeSelectionAgain()
    {
        var config = new ReportConfiguration();
        var manager = new UndoRedoManager();
        var first = new LabelReportElement();
        var second = new LabelReportElement();
        var third = new LabelReportElement();
        config.AddElement(first);
        config.AddElement(second);
        config.AddElement(third);

        RemoveElementsAction.RemoveAndRecord(config, [first, third], manager);
        manager.Undo();
        manager.Redo();

        Assert.Equal([second.Id], config.Elements.Select(e => e.Id));
    }

    [Fact]
    public void DeleteSelection_WithNothingSelected_RecordsNothing()
    {
        var config = new ReportConfiguration();
        var manager = new UndoRedoManager();

        RemoveElementsAction.RemoveAndRecord(config, [], manager);

        Assert.False(manager.CanUndo);
    }

    #endregion
}
