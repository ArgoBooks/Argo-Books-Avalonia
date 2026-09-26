using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the notification panel.
/// </summary>
public partial class NotificationPanelViewModel : ViewModelBase
{
    private readonly HeaderViewModel? _headerViewModel;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// Gets whether there are any notifications.
    /// </summary>
    public bool HasNotifications => Notifications.Count > 0;

    /// <summary>
    /// Gets whether there are unread notifications.
    /// </summary>
    public bool HasUnreadNotifications => _headerViewModel?.HasUnreadNotifications ?? false;

    public ObservableCollection<NotificationItem> Notifications { get; }

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public NotificationPanelViewModel()
    {
        Notifications =
        [
            new NotificationItem
            {
                Title = "Low Stock Alert",
                Message = "Widget A is running low (5 remaining)",
                Type = NotificationType.Warning,
                Timestamp = DateTime.Now.AddMinutes(-15)
            },

            new NotificationItem
            {
                Title = "Payment Received",
                Message = "$1,500.00 from ABC Company",
                Type = NotificationType.Success,
                Timestamp = DateTime.Now.AddHours(-2)
            },

            new NotificationItem
            {
                Title = "Invoice Overdue",
                Message = "INV-2024-001 is 5 days overdue",
                Type = NotificationType.Warning,
                Timestamp = DateTime.Now.AddDays(-1)
            }
        ];
    }

    /// <summary>
    /// Constructor with header view model dependency.
    /// </summary>
    public NotificationPanelViewModel(HeaderViewModel headerViewModel)
    {
        _headerViewModel = headerViewModel;
        Notifications = headerViewModel.Notifications;

        // Subscribe to collection changes to update HasNotifications and IsLast
        Notifications.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNotifications));
            OnPropertyChanged(nameof(HasUnreadNotifications));
            UpdateIsLast();
        };

        // Initialize IsLast for existing items
        UpdateIsLast();
    }

    /// <summary>
    /// Updates the IsLast property on all notification items.
    /// </summary>
    private void UpdateIsLast()
    {
        for (var i = 0; i < Notifications.Count; i++)
        {
            Notifications[i].IsLast = i == Notifications.Count - 1;
        }
    }

    /// <summary>
    /// Opens the notification panel.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
    }

    /// <summary>
    /// Closes the notification panel.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Toggles the notification panel.
    /// </summary>
    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
    }

    /// <summary>
    /// Marks a specific notification as read.
    /// </summary>
    [RelayCommand]
    private void MarkAsRead(NotificationItem? notification)
    {
        if (notification == null || notification.IsRead)
            return;

        notification.IsRead = true;
        if (_headerViewModel != null)
        {
            _headerViewModel.UnreadNotificationCount = Math.Max(0, _headerViewModel.UnreadNotificationCount - 1);
            _headerViewModel.HasUnreadNotifications = _headerViewModel.UnreadNotificationCount > 0;
        }
        OnPropertyChanged(nameof(HasUnreadNotifications));
    }

    /// <summary>
    /// Marks all notifications as read.
    /// </summary>
    [RelayCommand]
    private void MarkAllAsRead()
    {
        _headerViewModel?.MarkAllNotificationsAsRead();
        OnPropertyChanged(nameof(HasUnreadNotifications));
    }

    /// <summary>
    /// Removes a specific notification.
    /// </summary>
    [RelayCommand]
    private void RemoveNotification(NotificationItem? notification)
    {
        if (notification == null)
            return;

        Notifications.Remove(notification);
        if (!notification.IsRead && _headerViewModel != null)
        {
            _headerViewModel.UnreadNotificationCount = Math.Max(0, _headerViewModel.UnreadNotificationCount - 1);
            _headerViewModel.HasUnreadNotifications = _headerViewModel.UnreadNotificationCount > 0;
        }
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(HasUnreadNotifications));
    }

    /// <summary>
    /// Clears all notifications.
    /// </summary>
    [RelayCommand]
    private void ClearAll()
    {
        _headerViewModel?.ClearNotifications();
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(HasUnreadNotifications));
    }

    /// <summary>
    /// Opens notification settings.
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        Close();
        OpenNotificationSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Event raised when notification settings should be opened.
    /// </summary>
    public event EventHandler? OpenNotificationSettingsRequested;

    /// <summary>
    /// Handles clicking on a notification (navigate to related item).
    /// </summary>
    [RelayCommand]
    private void NotificationClicked(NotificationItem? notification)
    {
        if (notification == null)
            return;

        // Mark as read when clicked
        MarkAsRead(notification);

        // Close the panel
        Close();

        // Run the notification's navigation action, if any (e.g. open the related page).
        notification.OnClick?.Invoke();
    }

}
