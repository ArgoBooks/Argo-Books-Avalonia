using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the NotificationPanelViewModel.
/// </summary>
public class NotificationPanelViewModelTests
{
    #region Default State Tests

    [Fact]
    public void Constructor_Default_HasDesignTimeNotifications()
    {
        var vm = new NotificationPanelViewModel();

        Assert.Equal(3, vm.Notifications.Count);
    }

    #endregion

    #region Constructor with HeaderViewModel Tests

    [Fact]
    public void Constructor_WithHeaderViewModel_SharesNotificationsCollection()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);

        Assert.Same(headerVm.Notifications, vm.Notifications);
    }

    [Fact]
    public void Constructor_WithHeaderViewModel_InitiallyHasNoNotifications()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);

        Assert.False(vm.HasNotifications);
    }

    #endregion

    #region HasNotifications Tests

    [Fact]
    public void HasNotifications_WhenNotificationsExist_IsTrue()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);

        headerVm.AddNotification(new NotificationItem { Title = "Test", Message = "Test" });

        Assert.True(vm.HasNotifications);
    }

    [Fact]
    public void HasNotifications_WhenEmpty_IsFalse()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);

        Assert.False(vm.HasNotifications);
    }

    [Fact]
    public void HasNotifications_AfterClearAll_IsFalse()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        headerVm.AddNotification(new NotificationItem { Title = "Test", Message = "Test" });

        vm.ClearAllCommand.Execute(null);

        Assert.False(vm.HasNotifications);
    }

    #endregion

    #region HasUnreadNotifications Tests

    [Fact]
    public void HasUnreadNotifications_WhenUnreadExist_IsTrue()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);

        headerVm.AddNotification(new NotificationItem { Title = "Test", IsRead = false });

        Assert.True(vm.HasUnreadNotifications);
    }

    [Fact]
    public void HasUnreadNotifications_WhenAllRead_IsFalse()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        headerVm.AddNotification(new NotificationItem { Title = "Test", IsRead = false });

        vm.MarkAllAsReadCommand.Execute(null);

        Assert.False(vm.HasUnreadNotifications);
    }

    [Fact]
    public void HasUnreadNotifications_WhenNoNotifications_IsFalse()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);

        Assert.False(vm.HasUnreadNotifications);
    }

    #endregion

    #region UnreadCount Tests

    [Fact]
    public void UnreadCount_WhenNoNotifications_IsZero()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);

        Assert.Equal(0, vm.UnreadCount);
    }

    [Fact]
    public void UnreadCount_AfterAddingUnread_MatchesHeaderCount()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);

        headerVm.AddNotification(new NotificationItem { Title = "A", IsRead = false });
        headerVm.AddNotification(new NotificationItem { Title = "B", IsRead = false });
        headerVm.AddNotification(new NotificationItem { Title = "C", IsRead = true });

        Assert.Equal(2, vm.UnreadCount);
    }

    [Fact]
    public void UnreadCount_AfterMarkAllAsRead_IsZero()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        headerVm.AddNotification(new NotificationItem { Title = "A", IsRead = false });
        headerVm.AddNotification(new NotificationItem { Title = "B", IsRead = false });

        vm.MarkAllAsReadCommand.Execute(null);

        Assert.Equal(0, vm.UnreadCount);
    }

    #endregion

    #region Open/Close/Toggle Commands Tests

    [Fact]
    public void OpenCommand_WhenExecuted_SetsIsOpenToTrue()
    {
        var vm = new NotificationPanelViewModel();

        vm.OpenCommand.Execute(null);

        Assert.True(vm.IsOpen);
    }

    [Fact]
    public void CloseCommand_WhenOpen_SetsIsOpenToFalse()
    {
        var vm = new NotificationPanelViewModel();
        vm.OpenCommand.Execute(null);

        vm.CloseCommand.Execute(null);

        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void ToggleCommand_WhenClosed_SetsIsOpenToTrue()
    {
        var vm = new NotificationPanelViewModel();

        vm.ToggleCommand.Execute(null);

        Assert.True(vm.IsOpen);
    }

    [Fact]
    public void ToggleCommand_WhenOpen_SetsIsOpenToFalse()
    {
        var vm = new NotificationPanelViewModel
        {
            IsOpen = true
        };

        vm.ToggleCommand.Execute(null);

        Assert.False(vm.IsOpen);
    }

    #endregion

    #region MarkAsRead Tests

    [Fact]
    public void MarkAsReadCommand_WithUnreadNotification_MarksItAsRead()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        var notification = new NotificationItem { Title = "Test", IsRead = false };
        headerVm.AddNotification(notification);

        vm.MarkAsReadCommand.Execute(notification);

        Assert.True(notification.IsRead);
    }

    [Fact]
    public void MarkAsReadCommand_WithUnreadNotification_DecrementsUnreadCount()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        var notification = new NotificationItem { Title = "Test", IsRead = false };
        headerVm.AddNotification(notification);
        Assert.Equal(1, vm.UnreadCount);

        vm.MarkAsReadCommand.Execute(notification);

        Assert.Equal(0, vm.UnreadCount);
    }

    [Fact]
    public void MarkAsReadCommand_WithAlreadyReadNotification_DoesNothing()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        var notification = new NotificationItem { Title = "Test", IsRead = true };
        headerVm.AddNotification(notification);

        vm.MarkAsReadCommand.Execute(notification);

        Assert.Equal(0, vm.UnreadCount);
    }

    #endregion

    #region RemoveNotification Tests

    [Fact]
    public void RemoveNotificationCommand_RemovesFromCollection()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        var notification = new NotificationItem { Title = "Test" };
        headerVm.AddNotification(notification);

        vm.RemoveNotificationCommand.Execute(notification);

        Assert.DoesNotContain(notification, vm.Notifications);
    }

    [Fact]
    public void RemoveNotificationCommand_UnreadNotification_DecrementsCount()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        var notification = new NotificationItem { Title = "Test", IsRead = false };
        headerVm.AddNotification(notification);

        vm.RemoveNotificationCommand.Execute(notification);

        Assert.Equal(0, vm.UnreadCount);
    }

    #endregion

    #region NotificationClicked Tests

    [Fact]
    public void NotificationClickedCommand_MarksNotificationAsRead()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        var notification = new NotificationItem { Title = "Test", IsRead = false };
        headerVm.AddNotification(notification);
        vm.IsOpen = true;

        vm.NotificationClickedCommand.Execute(notification);

        Assert.True(notification.IsRead);
    }

    [Fact]
    public void NotificationClickedCommand_ClosesPanel()
    {
        var headerVm = new HeaderViewModel(null);
        var vm = new NotificationPanelViewModel(headerVm);
        var notification = new NotificationItem { Title = "Test", IsRead = false };
        headerVm.AddNotification(notification);
        vm.IsOpen = true;

        vm.NotificationClickedCommand.Execute(notification);

        Assert.False(vm.IsOpen);
    }

    #endregion
}
