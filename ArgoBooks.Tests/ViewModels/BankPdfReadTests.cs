using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class BankPdfReadTests
{
    // The import modal closes itself when a read fails. The failure message is shown only while
    // the screen is wanted, so that has to be asked before the close, or the message never shows.
    [Fact]
    public void EndRead_ReportsTheScreenAsWanted_WhenEndingTheFailedReadClosesIt()
    {
        var isOpen = true;
        var progress = new App.BankPdfReadProgress(_ => { }, succeeded => { if (!succeeded) isOpen = false; }, () => isOpen);

        Assert.True(App.EndRead(progress, succeeded: false));
        Assert.False(isOpen);
    }

    [Fact]
    public void EndRead_ReportsTheScreenAsUnwanted_WhenTheUserClosedItDuringTheRead()
    {
        var progress = new App.BankPdfReadProgress(_ => { }, _ => { }, () => false);

        Assert.False(App.EndRead(progress, succeeded: false));
    }
}
