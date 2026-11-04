using System.Reflection;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A recent company clicked twice asks to open it twice, the second time while the first is still loading.
/// </summary>
public class OpenCompanyTwiceTests : ModalViewModelTestBase
{
    private static readonly FieldInfo MainWindowField =
        typeof(App).GetField("_mainWindowViewModel", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo AppShellField =
        typeof(App).GetField("_appShellViewModel", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo OpenCompanyWithRetry =
        typeof(App).GetMethod("OpenCompanyWithRetryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo IsOpeningField =
        typeof(CompanyManager).GetField("_isOpening", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public async Task ASecondOpenWhileOneIsLoading_LeavesTheFirstOneLoading()
    {
        var mainWindow = new MainWindowViewModel();
        MainWindowField.SetValue(null, mainWindow);
        AppShellField.SetValue(null, new AppShellViewModel());
        try
        {
            IsOpeningField.SetValue(App.CompanyManager, 1);
            mainWindow.ShowLoading("Opening company...");

            await (Task)OpenCompanyWithRetry.Invoke(null, ["Company.argo", "recent", false])!;

            Assert.True(mainWindow.IsLoading);
        }
        finally
        {
            MainWindowField.SetValue(null, null);
            AppShellField.SetValue(null, null);
        }
    }
}
