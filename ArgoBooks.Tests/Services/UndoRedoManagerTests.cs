using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the UndoRedoManager class.
/// </summary>
public class UndoRedoManagerTests
{
    private readonly UndoRedoManager _manager = new();

    #region Saved State Tests

    /// <summary>
    /// A save takes a while, and the user can keep editing during it. Only what existed when the
    /// save started is in the file, so an action recorded after that must still count as unsaved.
    /// </summary>
    [Fact]
    public void MarkSaved_ActionRecordedDuringSave_StaysUnsaved()
    {
        _manager.RecordAction(new MockUndoableAction("Before save"));
        var savePoint = _manager.SavePoint;
        _manager.RecordAction(new MockUndoableAction("During save"));

        _manager.MarkSaved(savePoint);

        Assert.False(_manager.IsAtSavedState);
        _manager.Undo();
        Assert.True(_manager.IsAtSavedState);
    }

    [Fact]
    public void MarkSaved_NothingBeforeSave_ActionRecordedDuringSave_StaysUnsaved()
    {
        var savePoint = _manager.SavePoint;
        _manager.RecordAction(new MockUndoableAction("During save"));

        _manager.MarkSaved(savePoint);

        Assert.False(_manager.IsAtSavedState);
    }

    [Fact]
    public void MarkSaved_ClearsHasUnsavedChanges()
    {
        _manager.RecordAction(new MockUndoableAction("Test"));
        Assert.True(_manager.HasUnsavedChanges);

        _manager.MarkSaved();

        Assert.False(_manager.HasUnsavedChanges);
    }

    [Fact]
    public void MarkSaved_NewChangeAfter_SetsUnsaved()
    {
        _manager.RecordAction(new MockUndoableAction("First"));
        _manager.MarkSaved();

        _manager.RecordAction(new MockUndoableAction("Second"));

        Assert.True(_manager.HasUnsavedChanges);
    }

    [Fact]
    public void MarkSaved_UndoAfter_SetsUnsaved()
    {
        _manager.RecordAction(new MockUndoableAction("Test"));
        _manager.MarkSaved();

        _manager.Undo();

        Assert.True(_manager.HasUnsavedChanges);
    }

    [Fact]
    public void MarkSaved_UndoThenRedo_ClearsUnsaved()
    {
        _manager.RecordAction(new MockUndoableAction("Test"));
        _manager.MarkSaved();

        _manager.Undo();
        _manager.Redo();

        Assert.False(_manager.HasUnsavedChanges);
    }

    // The report designer used to track the save by stack depth, and a new action after an undo
    // is back at the saved depth.
    [Fact]
    public void Save_Undo_NewAction_IsUnsaved()
    {
        _manager.RecordAction(new MockUndoableAction("First"));
        _manager.MarkSaved();

        _manager.Undo();
        _manager.RecordAction(new MockUndoableAction("Different"));

        Assert.True(_manager.HasUnsavedChanges);
    }

    // Coalescing changes the saved action itself, so the file no longer matches it.
    [Fact]
    public void Save_ThenAChangeCoalescedIntoTheSavedAction_IsUnsaved()
    {
        var value = 0;
        _manager.RecordAction(new CoalescingPropertyChangeAction<int>("Color", "color", v => value = v, 0, 1));
        _manager.MarkSaved();

        _manager.RecordAction(new CoalescingPropertyChangeAction<int>("Color", "color", v => value = v, 1, 2));

        Assert.Equal(1, _manager.UndoCount);
        Assert.True(_manager.HasUnsavedChanges);
    }

    [Fact]
    public void Clear_ResetsHistoryAndUnsavedChanges()
    {
        _manager.RecordAction(new MockUndoableAction("First"));
        _manager.RecordAction(new MockUndoableAction("Second"));
        _manager.Undo();

        _manager.Clear();

        Assert.False(_manager.CanUndo);
        Assert.False(_manager.CanRedo);
        Assert.False(_manager.HasUnsavedChanges);
    }

    #endregion

    #region Record Tests

    [Fact]
    public void Record_Action_CanUndoBecomeTrue()
    {
        var action = new MockUndoableAction("Test");

        _manager.RecordAction(action);

        Assert.True(_manager.CanUndo);
    }

    [Fact]
    public void Record_Action_UndoDescriptionSet()
    {
        var action = new MockUndoableAction("Test Action");

        _manager.RecordAction(action);

        Assert.Equal("Test Action", _manager.UndoDescription);
    }

    [Fact]
    public void Record_Action_ClearsRedoStack()
    {
        _manager.RecordAction(new MockUndoableAction("Action 1"));
        _manager.Undo();
        Assert.True(_manager.CanRedo);

        _manager.RecordAction(new MockUndoableAction("Action 2"));

        Assert.False(_manager.CanRedo);
    }

    [Fact]
    public void Record_WhenSuppressed_DoesNotRecord()
    {
        _manager.SuppressRecording = true;

        _manager.RecordAction(new MockUndoableAction("Test"));

        Assert.False(_manager.CanUndo);
    }

    [Fact]
    public void Record_PastTheHistorySize_DropsTheOldest()
    {
        var manager = new UndoRedoManager(3);

        foreach (var name in new[] { "First", "Second", "Third", "Fourth" })
            manager.RecordAction(new MockUndoableAction(name));

        Assert.Equal(["Fourth", "Third", "Second"], manager.GetUndoDescriptions());
    }

    #endregion

    #region Undo Tests

    [Fact]
    public void Undo_ExecutesUndoOnAction()
    {
        var action = new MockUndoableAction("Test");
        _manager.RecordAction(action);

        _manager.Undo();

        Assert.True(action.UndoCalled);
    }

    [Fact]
    public void Undo_EnablesRedo()
    {
        _manager.RecordAction(new MockUndoableAction("Test"));

        _manager.Undo();

        Assert.True(_manager.CanRedo);
    }

    [Fact]
    public void Undo_WhenEmpty_DoesNothing()
    {
        _manager.Undo(); // Should not throw
        Assert.False(_manager.CanRedo);
    }

    #endregion

    #region Redo Tests

    [Fact]
    public void Redo_ExecutesRedoOnAction()
    {
        var action = new MockUndoableAction("Test");
        _manager.RecordAction(action);
        _manager.Undo();

        _manager.Redo();

        Assert.True(action.RedoCalled);
    }

    [Fact]
    public void Redo_WhenEmpty_DoesNothing()
    {
        _manager.Redo(); // Should not throw
        Assert.False(_manager.CanUndo);
    }

    #endregion

    #region History Tests

    [Fact]
    public void GetUndoDescriptions_ReturnsRecordedActions()
    {
        _manager.RecordAction(new MockUndoableAction("Action 1"));
        _manager.RecordAction(new MockUndoableAction("Action 2"));

        var history = _manager.GetUndoDescriptions();

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void GetRedoDescriptions_AfterUndo_ContainsUndoneAction()
    {
        _manager.RecordAction(new MockUndoableAction("Action 1"));
        _manager.Undo();

        var history = _manager.GetRedoDescriptions();

        Assert.Single(history);
    }

    #endregion

    #region StateChanged Event Tests

    [Fact]
    public void Record_RaisesStateChanged()
    {
        var eventRaised = false;
        _manager.StateChanged += (_, _) => eventRaised = true;

        _manager.RecordAction(new MockUndoableAction("Test"));

        Assert.True(eventRaised);
    }

    [Fact]
    public void Undo_RaisesStateChanged()
    {
        _manager.RecordAction(new MockUndoableAction("Test"));
        var eventRaised = false;
        _manager.StateChanged += (_, _) => eventRaised = true;

        _manager.Undo();

        Assert.True(eventRaised);
    }

    [Fact]
    public void Redo_RaisesStateChanged()
    {
        _manager.RecordAction(new MockUndoableAction("Test"));
        _manager.Undo();
        var eventRaised = false;
        _manager.StateChanged += (_, _) => eventRaised = true;

        _manager.Redo();

        Assert.True(eventRaised);
    }

    #endregion

    #region Mock Classes

    private class MockUndoableAction(string description) : IUndoableAction
    {
        public string Description { get; } = description;
        public bool UndoCalled { get; private set; }
        public bool RedoCalled { get; private set; }

        public void Undo() => UndoCalled = true;
        public void Redo() => RedoCalled = true;
    }

    #endregion
}
