using Avalonia.Controls;

namespace ArgoBooks.Modals;

/// <summary>
/// Window that finishes the move from the old Windows installer to the new one, shown over the app until the update restarts it.
/// </summary>
public partial class FinishingUpdateModal : UserControl
{
    public FinishingUpdateModal()
    {
        InitializeComponent();
    }
}
