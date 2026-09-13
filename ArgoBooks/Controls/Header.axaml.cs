using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Controls;

/// <summary>
/// Header control containing search, notifications, and user menu.
/// </summary>
public partial class Header : UserControl
{
    #region Styled Properties

    public static readonly StyledProperty<string?> PageTitleProperty =
        AvaloniaProperty.Register<Header, string?>(nameof(PageTitle));

    public static readonly StyledProperty<string?> PageSubtitleProperty =
        AvaloniaProperty.Register<Header, string?>(nameof(PageSubtitle));

    public static readonly StyledProperty<bool> ShowPageTitleProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowPageTitle));

    public static readonly StyledProperty<string?> SearchQueryProperty =
        AvaloniaProperty.Register<Header, string?>(nameof(SearchQuery));

    public static readonly StyledProperty<string?> SearchPlaceholderProperty =
        AvaloniaProperty.Register<Header, string?>(nameof(SearchPlaceholder), "Search...");

    public static readonly StyledProperty<bool> ShowSearchProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowSearch), true);

    public static readonly StyledProperty<bool> ShowSearchHintProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowSearchHint), true);

    public static readonly StyledProperty<bool> ShowQuickActionsProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowQuickActions), true);

    public static readonly StyledProperty<bool> ShowHelpProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowHelp), true);

    public static readonly StyledProperty<bool> ShowNotificationsProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowNotifications), true);

    public static readonly StyledProperty<bool> HasUnreadNotificationsProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(HasUnreadNotifications));

    public static readonly StyledProperty<int> UnreadNotificationCountProperty =
        AvaloniaProperty.Register<Header, int>(nameof(UnreadNotificationCount));

    public static readonly StyledProperty<bool> ShowNotificationCountProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowNotificationCount), true);

    public static readonly StyledProperty<bool> ShowSettingsProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowSettings), true);

    public static readonly StyledProperty<bool> ShowUserMenuProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowUserMenu), true);

    public static readonly StyledProperty<string?> UserDisplayNameProperty =
        AvaloniaProperty.Register<Header, string?>(nameof(UserDisplayName));

    public static readonly StyledProperty<string?> UserInitialsProperty =
        AvaloniaProperty.Register<Header, string?>(nameof(UserInitials));

    public static readonly StyledProperty<bool> ShowUserNameProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowUserName));

    public static readonly StyledProperty<bool> ShowUserInitialsProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(ShowUserInitials));

    public static readonly StyledProperty<bool> HasUserAvatarProperty =
        AvaloniaProperty.Register<Header, bool>(nameof(HasUserAvatar));

    public static readonly StyledProperty<Bitmap?> UserAvatarSourceProperty =
        AvaloniaProperty.Register<Header, Bitmap?>(nameof(UserAvatarSource));

    public static readonly StyledProperty<ICommand?> SearchCommandProperty =
        AvaloniaProperty.Register<Header, ICommand?>(nameof(SearchCommand));

    public static readonly StyledProperty<ICommand?> OpenQuickActionsCommandProperty =
        AvaloniaProperty.Register<Header, ICommand?>(nameof(OpenQuickActionsCommand));

    public static readonly StyledProperty<ICommand?> OpenHelpCommandProperty =
        AvaloniaProperty.Register<Header, ICommand?>(nameof(OpenHelpCommand));

    public static readonly StyledProperty<ICommand?> OpenNotificationsCommandProperty =
        AvaloniaProperty.Register<Header, ICommand?>(nameof(OpenNotificationsCommand));

    public static readonly StyledProperty<ICommand?> OpenSettingsCommandProperty =
        AvaloniaProperty.Register<Header, ICommand?>(nameof(OpenSettingsCommand));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the page title displayed in the header.
    /// </summary>
    public string? PageTitle
    {
        get => GetValue(PageTitleProperty);
        set => SetValue(PageTitleProperty, value);
    }

    public string? PageSubtitle
    {
        get => GetValue(PageSubtitleProperty);
        set => SetValue(PageSubtitleProperty, value);
    }

    public bool ShowPageTitle
    {
        get => GetValue(ShowPageTitleProperty);
        set => SetValue(ShowPageTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the search query text.
    /// </summary>
    public string? SearchQuery
    {
        get => GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    /// <summary>
    /// Gets or sets the search placeholder text.
    /// </summary>
    public string? SearchPlaceholder
    {
        get => GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the search box.
    /// </summary>
    public bool ShowSearch
    {
        get => GetValue(ShowSearchProperty);
        set => SetValue(ShowSearchProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the keyboard shortcut hint.
    /// </summary>
    public bool ShowSearchHint
    {
        get => GetValue(ShowSearchHintProperty);
        set => SetValue(ShowSearchHintProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the quick actions button.
    /// </summary>
    public bool ShowQuickActions
    {
        get => GetValue(ShowQuickActionsProperty);
        set => SetValue(ShowQuickActionsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the help button.
    /// </summary>
    public bool ShowHelp
    {
        get => GetValue(ShowHelpProperty);
        set => SetValue(ShowHelpProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the notifications button.
    /// </summary>
    public bool ShowNotifications
    {
        get => GetValue(ShowNotificationsProperty);
        set => SetValue(ShowNotificationsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether there are unread notifications.
    /// </summary>
    public bool HasUnreadNotifications
    {
        get => GetValue(HasUnreadNotificationsProperty);
        set => SetValue(HasUnreadNotificationsProperty, value);
    }

    public int UnreadNotificationCount
    {
        get => GetValue(UnreadNotificationCountProperty);
        set => SetValue(UnreadNotificationCountProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the notification count badge.
    /// </summary>
    public bool ShowNotificationCount
    {
        get => GetValue(ShowNotificationCountProperty);
        set => SetValue(ShowNotificationCountProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the settings button.
    /// </summary>
    public bool ShowSettings
    {
        get => GetValue(ShowSettingsProperty);
        set => SetValue(ShowSettingsProperty, value);
    }

    public bool ShowUserMenu
    {
        get => GetValue(ShowUserMenuProperty);
        set => SetValue(ShowUserMenuProperty, value);
    }

    public string? UserDisplayName
    {
        get => GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    public string? UserInitials
    {
        get => GetValue(UserInitialsProperty);
        set => SetValue(UserInitialsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the user name next to avatar.
    /// </summary>
    public bool ShowUserName
    {
        get => GetValue(ShowUserNameProperty);
        set => SetValue(ShowUserNameProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show user initials in avatar.
    /// </summary>
    public bool ShowUserInitials
    {
        get => GetValue(ShowUserInitialsProperty);
        set => SetValue(ShowUserInitialsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the user has an avatar image.
    /// </summary>
    public bool HasUserAvatar
    {
        get => GetValue(HasUserAvatarProperty);
        set => SetValue(HasUserAvatarProperty, value);
    }

    /// <summary>
    /// Gets or sets the user avatar image source.
    /// </summary>
    public Bitmap? UserAvatarSource
    {
        get => GetValue(UserAvatarSourceProperty);
        set => SetValue(UserAvatarSourceProperty, value);
    }

    public ICommand? SearchCommand
    {
        get => GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public ICommand? OpenQuickActionsCommand
    {
        get => GetValue(OpenQuickActionsCommandProperty);
        set => SetValue(OpenQuickActionsCommandProperty, value);
    }

    public ICommand? OpenHelpCommand
    {
        get => GetValue(OpenHelpCommandProperty);
        set => SetValue(OpenHelpCommandProperty, value);
    }

    public ICommand? OpenNotificationsCommand
    {
        get => GetValue(OpenNotificationsCommandProperty);
        set => SetValue(OpenNotificationsCommandProperty, value);
    }

    public ICommand? OpenSettingsCommand
    {
        get => GetValue(OpenSettingsCommandProperty);
        set => SetValue(OpenSettingsCommandProperty, value);
    }

    #endregion

    private TextBlock? _asterisk;
    private readonly StackPanel? _saveButtonContainer;
    private bool _isInitialized;
    private HeaderViewModel? _subscribedVm;

    public Header()
    {
        InitializeComponent();
        _saveButtonContainer = this.FindControl<StackPanel>("SaveButtonContainer");
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            // Wait for layout and rendering passes to complete
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);

            _isInitialized = true;

            if (DataContext is HeaderViewModel vm)
            {
                _subscribedVm = vm;
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "Header.OnLoaded");
        }
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }

        Loaded -= OnLoaded;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HeaderViewModel.HasUnsavedChanges) && sender is HeaderViewModel vm)
        {
            if (!_isInitialized) return;

            UpdateAsteriskVisibility(vm.HasUnsavedChanges);
        }
    }

    private void UpdateAsteriskVisibility(bool show)
    {
        if (_saveButtonContainer == null || !_isInitialized) return;

        if (show && _asterisk == null)
        {
            // Create asterisk dynamically only when needed
            _asterisk = new TextBlock
            {
                Text = "*",
                FontSize = 16,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(-8, 0, 0, 0)
            };
            // Insert after the save button (index 1)
            _saveButtonContainer.Children.Insert(1, _asterisk);
        }
        else if (!show && _asterisk != null)
        {
            _saveButtonContainer.Children.Remove(_asterisk);
            _asterisk = null;
        }
    }

    /// <summary>
    /// Opens the quick actions panel when the search input receives focus.
    /// </summary>
    private void SearchInput_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (DataContext is HeaderViewModel vm)
        {
            vm.OpenQuickActionsCommand.Execute(null);
        }
    }

    /// <summary>
    /// Focuses the search input when clicking anywhere on the search box.
    /// </summary>
    private void SearchBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var searchInput = this.FindControl<TextBox>("SearchInput");
        searchInput?.Focus();
    }

    /// <summary>
    /// Handles keyboard navigation in the search input for Quick Actions panel.
    /// </summary>
    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is HeaderViewModel vm)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    vm.OnSearchKeyPressed(SearchKeyAction.Escape);
                    // Clear focus from the search box
                    if (sender is TextBox textBox)
                    {
                        textBox.Text = string.Empty;
                        // Move focus away
                        Focus();
                    }
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.OnSearchKeyPressed(SearchKeyAction.Up);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.OnSearchKeyPressed(SearchKeyAction.Down);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.OnSearchKeyPressed(SearchKeyAction.Enter);
                    e.Handled = true;
                    break;
            }
        }
    }
}
