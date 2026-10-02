using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

/// <summary>
/// Modal explaining which QuickBooks reports to export, and taking them in.
/// </summary>
public partial class QuickBooksImportModal : UserControl
{
    public QuickBooksImportModal()
    {
        InitializeComponent();
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = files != null && files.Any(f => QuickBooksImportModalViewModel.IsAccepted(f.TryGetLocalPath()))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not QuickBooksImportModalViewModel vm || e.DataTransfer.TryGetFiles() is not { } files)
            return;

        foreach (var path in files.Select(f => f.TryGetLocalPath()))
        {
            if (path != null) vm.AddFile(path);
        }
    }
}
