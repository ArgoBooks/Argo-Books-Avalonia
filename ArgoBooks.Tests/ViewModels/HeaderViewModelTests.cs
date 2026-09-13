using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the HeaderViewModel.
/// </summary>
public class HeaderViewModelTests
{
    private readonly HeaderViewModel _viewModel = new(null);

    #region SetPageTitle Tests

    [Fact]
    public void SetPageTitle_WithTitle_UpdatesPageTitle()
    {
        _viewModel.SetPageTitle("Expenses");

        Assert.Equal("Expenses", _viewModel.PageTitle);
    }

    [Fact]
    public void SetPageTitle_WithTitleAndSubtitle_UpdatesBoth()
    {
        _viewModel.SetPageTitle("Reports", "Monthly Summary");

        Assert.Equal("Reports", _viewModel.PageTitle);
        Assert.Equal("Monthly Summary", _viewModel.PageSubtitle);
    }

    [Fact]
    public void SetPageTitle_WithNull_SetsPageTitleToNull()
    {
        _viewModel.SetPageTitle("Something");
        _viewModel.SetPageTitle(null);

        Assert.Null(_viewModel.PageTitle);
    }

    [Fact]
    public void SetPageTitle_WithoutSubtitle_SubtitleIsNull()
    {
        _viewModel.SetPageTitle("Analytics");

        Assert.Null(_viewModel.PageSubtitle);
    }

    #endregion

    #region Plan Status Tests

    [Fact]
    public void ShowUpgrade_WhenNotPremium_IsTrue()
    {
        _viewModel.HasPremium = false;

        Assert.True(_viewModel.ShowUpgrade);
    }

    [Fact]
    public void ShowUpgrade_WhenPremium_IsFalse()
    {
        _viewModel.HasPremium = true;

        Assert.False(_viewModel.ShowUpgrade);
    }

    [Fact]
    public void ShowUpgrade_WhenPremiumChanges_Updates()
    {
        Assert.True(_viewModel.ShowUpgrade);

        _viewModel.HasPremium = true;
        Assert.False(_viewModel.ShowUpgrade);

        _viewModel.HasPremium = false;
        Assert.True(_viewModel.ShowUpgrade);
    }

    #endregion

    #region Notification Count Tests

    [Fact]
    public void AddNotification_UnreadNotification_IncrementsCount()
    {
        var notification = new NotificationItem
        {
            Title = "Test",
            Message = "Test message",
            IsRead = false
        };

        _viewModel.AddNotification(notification);

        Assert.Equal(1, _viewModel.UnreadNotificationCount);
        Assert.True(_viewModel.HasUnreadNotifications);
    }

    [Fact]
    public void AddNotification_ReadNotification_DoesNotIncrementCount()
    {
        var notification = new NotificationItem
        {
            Title = "Test",
            Message = "Test message",
            IsRead = true
        };

        _viewModel.AddNotification(notification);

        Assert.Equal(0, _viewModel.UnreadNotificationCount);
    }

    [Fact]
    public void AddNotification_MultipleUnread_CountsCorrectly()
    {
        _viewModel.AddNotification(new NotificationItem { Title = "A", IsRead = false });
        _viewModel.AddNotification(new NotificationItem { Title = "B", IsRead = false });
        _viewModel.AddNotification(new NotificationItem { Title = "C", IsRead = true });

        Assert.Equal(2, _viewModel.UnreadNotificationCount);
    }

    [Fact]
    public void AddNotification_InsertsAtBeginning()
    {
        _viewModel.AddNotification(new NotificationItem { Title = "First" });
        _viewModel.AddNotification(new NotificationItem { Title = "Second" });

        Assert.Equal("Second", _viewModel.Notifications[0].Title);
        Assert.Equal("First", _viewModel.Notifications[1].Title);
    }

    [Fact]
    public void MarkAllNotificationsAsRead_ResetsCountToZero()
    {
        _viewModel.AddNotification(new NotificationItem { Title = "A", IsRead = false });
        _viewModel.AddNotification(new NotificationItem { Title = "B", IsRead = false });

        _viewModel.MarkAllNotificationsAsRead();

        Assert.Equal(0, _viewModel.UnreadNotificationCount);
        Assert.False(_viewModel.HasUnreadNotifications);
    }

    [Fact]
    public void MarkAllNotificationsAsRead_MarksAllItemsAsRead()
    {
        _viewModel.AddNotification(new NotificationItem { Title = "A", IsRead = false });
        _viewModel.AddNotification(new NotificationItem { Title = "B", IsRead = false });

        _viewModel.MarkAllNotificationsAsRead();

        Assert.All(_viewModel.Notifications, n => Assert.True(n.IsRead));
    }

    [Fact]
    public void ClearNotifications_RemovesAllNotifications()
    {
        _viewModel.AddNotification(new NotificationItem { Title = "A" });
        _viewModel.AddNotification(new NotificationItem { Title = "B" });

        _viewModel.ClearNotifications();

        Assert.Empty(_viewModel.Notifications);
        Assert.Equal(0, _viewModel.UnreadNotificationCount);
        Assert.False(_viewModel.HasUnreadNotifications);
    }

    #endregion

    #region UserInitials Generation Tests

    [Fact]
    public void SetUserInfo_WithTwoPartName_GeneratesCorrectInitials()
    {
        _viewModel.SetUserInfo("John Doe");

        Assert.Equal("JD", _viewModel.UserInitials);
    }

    [Fact]
    public void SetUserInfo_WithThreePartName_UsesFirstAndLastInitials()
    {
        _viewModel.SetUserInfo("John Michael Doe");

        Assert.Equal("JD", _viewModel.UserInitials);
    }

    [Fact]
    public void SetUserInfo_WithSingleName_UsesFirstTwoChars()
    {
        _viewModel.SetUserInfo("John");

        Assert.Equal("JO", _viewModel.UserInitials);
    }

    [Fact]
    public void SetUserInfo_WithSingleCharName_UsesSingleChar()
    {
        _viewModel.SetUserInfo("J");

        Assert.Equal("J", _viewModel.UserInitials);
    }

    [Fact]
    public void SetUserInfo_WithLowercaseName_ConvertsToUpper()
    {
        _viewModel.SetUserInfo("john doe");

        Assert.Equal("JD", _viewModel.UserInitials);
    }

    [Fact]
    public void SetUserInfo_WithNull_ClearsInitials()
    {
        _viewModel.SetUserInfo("John Doe");
        _viewModel.SetUserInfo(null);

        Assert.Null(_viewModel.UserInitials);
    }

    [Fact]
    public void SetUserInfo_WithEmpty_ClearsInitials()
    {
        _viewModel.SetUserInfo("John Doe");
        _viewModel.SetUserInfo("");

        Assert.Null(_viewModel.UserInitials);
    }

    [Fact]
    public void SetUserInfo_WithWhitespace_ClearsInitials()
    {
        _viewModel.SetUserInfo("John Doe");
        _viewModel.SetUserInfo("   ");

        Assert.Null(_viewModel.UserInitials);
    }

    [Fact]
    public void SetUserInfo_WithDisplayName_SetsUserDisplayName()
    {
        _viewModel.SetUserInfo("Jane Smith");

        Assert.Equal("Jane Smith", _viewModel.UserDisplayName);
    }

    [Fact]
    public void SetUserInfo_WithEmail_SetsUserEmail()
    {
        _viewModel.SetUserInfo("Jane", email: "jane@example.com");

        Assert.Equal("jane@example.com", _viewModel.UserEmail);
    }

    [Fact]
    public void SetUserInfo_WithRole_SetsUserRole()
    {
        _viewModel.SetUserInfo("Jane", role: "Administrator");

        Assert.Equal("Administrator", _viewModel.UserRole);
    }

    [Fact]
    public void SetUserInfo_WithUserId_SetsUserId()
    {
        _viewModel.SetUserInfo("Jane", userId: 42);

        Assert.Equal(42, _viewModel.UserId);
    }

    [Fact]
    public void SetUserInfo_WithNoAvatar_HasUserAvatarIsFalse()
    {
        _viewModel.SetUserInfo("Jane");

        Assert.False(_viewModel.HasUserAvatar);
    }

    [Fact]
    public void SetUserInfo_WithDisplayName_ShowUserInitialsIsTrue()
    {
        _viewModel.SetUserInfo("Jane Smith");

        Assert.True(_viewModel.ShowUserInitials);
    }

    [Fact]
    public void SetUserInfo_WithNull_ShowUserInitialsIsFalse()
    {
        _viewModel.SetUserInfo(null);

        Assert.False(_viewModel.ShowUserInitials);
    }

    #endregion

}
