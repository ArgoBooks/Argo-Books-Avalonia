using ArgoBooks.Core.Services.Sync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Mobile.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // A trivial use of ArgoBooks.Shared from the Android head, proving the same sync crypto compiles and runs unchanged on both.
    [ObservableProperty]
    private string _greeting = $"Welcome to Argo Books! (demo sync key: {SyncCrypto.GenerateSyncKey()[..8]}...)";
}
