using System.Collections.ObjectModel;
using ArgoBooks.Data;
using ArgoBooks.Helpers;
using ArgoBooks.Utilities;
using ArgoBooks.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents a tab in the emoji picker.
/// </summary>
public partial class EmojiTabItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public bool IsSpecial { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Represents an emoji item for display in the picker.
/// </summary>
public partial class EmojiDisplayItem : ObservableObject
{
    public string Emoji { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;
}

/// <summary>
/// ViewModel for the emoji picker modal.
/// </summary>
public partial class EmojiPickerViewModel : ObservableObject
{
    private EmojiPickerSettings? _settings;
    private Action<string>? _onEmojiSelected;

    // Debounce typing in the search box so a fast typist doesn't rebuild the grid on every keystroke.
    private const int SearchDebounceMs = 120;
    private CancellationTokenSource? _searchDebounceCts;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private EmojiTabItem? _selectedTab;

    [ObservableProperty]
    private string? _selectedEmoji;

    public ObservableCollection<EmojiTabItem> Tabs { get; } = [];
    public BatchObservableCollection<EmojiDisplayItem> DisplayedEmojis { get; } = [];

    // Cap search results: the emoji grid is a non-virtualizing WrapPanel where each item is a fairly
    // heavy Button (context menu + tooltip), so a broad query (e.g. a single letter) that matched
    // hundreds of emojis was the main source of lag. Most real searches return far fewer than this.
    private const int MaxSearchResults = 200;

    // Emoji -> item lookup, built once, for resolving names of Recent/Favorite emojis.
    private static readonly Dictionary<string, EmojiData.EmojiItem> EmojiLookup =
        EmojiData.AllEmojis
            .GroupBy(e => e.Emoji)
            .ToDictionary(g => g.Key, g => g.First());

    public bool HasRecentEmojis => _settings?.RecentEmojis.Count > 0;
    public bool HasFavoriteEmojis => _settings?.FavoriteEmojis.Count > 0;

    /// <summary>
    /// Whether to show the empty state message (on Recent/Favorites tab with no items).
    /// </summary>
    public bool ShowEmptyState => DisplayedEmojis.Count == 0 &&
                                   string.IsNullOrWhiteSpace(SearchText) &&
                                   SelectedTab?.IsSpecial == true;

    /// <summary>
    /// Whether to show the clear recent button.
    /// </summary>
    public bool ShowClearRecent => SelectedTab?.Name == "Recent" &&
                                    _settings?.RecentEmojis.Count > 0 &&
                                    string.IsNullOrWhiteSpace(SearchText);

    /// <summary>
    /// Gets the empty state message for the current tab.
    /// </summary>
    public string EmptyStateMessage => SelectedTab?.Name switch
    {
        "Recent" => "No recent emojis yet",
        "Favorites" => "No favorite emojis yet",
        _ => ""
    };

    /// <summary>
    /// Gets the empty state hint for the current tab.
    /// </summary>
    public string EmptyStateHint => SelectedTab?.Name switch
    {
        "Recent" => "Emojis you select will appear here",
        "Favorites" => "Right-click an emoji to add it to favorites",
        _ => ""
    };

    public EmojiPickerViewModel()
    {
        InitializeTabs();
    }

    private void InitializeTabs()
    {
        Tabs.Clear();

        // Special tabs first
        Tabs.Add(new EmojiTabItem { Name = "Recent", Icon = "🕐", IsSpecial = true });
        Tabs.Add(new EmojiTabItem { Name = "Favorites", Icon = "⭐", IsSpecial = true });

        // Category tabs
        foreach (var category in EmojiData.Categories)
        {
            Tabs.Add(new EmojiTabItem { Name = category.Name, Icon = category.Icon });
        }
    }

    /// <summary>
    /// Opens the emoji picker modal.
    /// </summary>
    /// <param name="currentEmoji">Currently selected emoji (if any).</param>
    /// <param name="onSelected">Callback when an emoji is selected.</param>
    public void Open(string? currentEmoji, Action<string> onSelected)
    {
        _onEmojiSelected = onSelected;
        _settings = App.SettingsService?.GlobalSettings.Ui.EmojiPicker ?? new EmojiPickerSettings();
        SelectedEmoji = currentEmoji;
        SearchText = string.Empty;

        // Refresh favorite states
        OnPropertyChanged(nameof(HasRecentEmojis));
        OnPropertyChanged(nameof(HasFavoriteEmojis));

        // Select first tab with content
        if (_settings.RecentEmojis.Count > 0)
        {
            SelectTab(Tabs.First(t => t.Name == "Recent"));
        }
        else if (_settings.FavoriteEmojis.Count > 0)
        {
            SelectTab(Tabs.First(t => t.Name == "Favorites"));
        }
        else
        {
            // Default to Office tab (most relevant for a business app)
            SelectTab(Tabs.FirstOrDefault(t => t.Name == "Office") ??
                      Tabs.FirstOrDefault(t => !t.IsSpecial) ?? Tabs.First());
        }

        IsOpen = true;
    }

    [RelayCommand]
    public void Close()
    {
        _searchDebounceCts?.Cancel();
        IsOpen = false;
        _onEmojiSelected = null;
    }

    [RelayCommand]
    public void SelectTab(EmojiTabItem? tab)
    {
        if (tab == null) return;

        foreach (var t in Tabs)
        {
            t.IsSelected = t == tab;
        }

        SelectedTab = tab;
        SearchText = string.Empty; // Clear search when switching tabs
        UpdateDisplayedEmojis();
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    public void ClearRecent()
    {
        if (_settings == null) return;

        _settings.RecentEmojis.Clear();
        SaveSettings();
        OnPropertyChanged(nameof(HasRecentEmojis));
        UpdateDisplayedEmojis();
    }

    [RelayCommand]
    public void SelectEmoji(EmojiDisplayItem? item)
    {
        if (item == null) return;

        SelectedEmoji = item.Emoji;
        AddToRecent(item.Emoji);
        _onEmojiSelected?.Invoke(item.Emoji);
        Close();
    }

    [RelayCommand]
    public void ToggleFavorite(EmojiDisplayItem? item)
    {
        if (item == null || _settings == null) return;

        if (item.IsFavorite)
        {
            _settings.FavoriteEmojis.Remove(item.Emoji);
            item.IsFavorite = false;
        }
        else
        {
            if (!_settings.FavoriteEmojis.Contains(item.Emoji))
            {
                _settings.FavoriteEmojis.Insert(0, item.Emoji);
            }
            item.IsFavorite = true;
        }

        SaveSettings();
        OnPropertyChanged(nameof(HasFavoriteEmojis));

        // Refresh if we're on the Favorites tab
        if (SelectedTab?.Name == "Favorites")
        {
            UpdateDisplayedEmojis();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        // Cancel any pending debounced search.
        _searchDebounceCts?.Cancel();

        // Clearing the box (e.g. the X button, or switching tabs) should feel instant; only debounce
        // actual typing, where results shrink as the query grows.
        if (string.IsNullOrWhiteSpace(value))
        {
            UpdateDisplayedEmojis();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebounceSearchAsync(cts.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, token);
        }
        catch (TaskCanceledException)
        {
            return; // superseded by a newer keystroke
        }

        // Resumes on the UI thread's synchronization context, so it's safe to update DisplayedEmojis.
        if (!token.IsCancellationRequested)
            UpdateDisplayedEmojis();
    }

    private void UpdateDisplayedEmojis()
    {
        if (_settings == null)
        {
            DisplayedEmojis.ReplaceAll([]);
            return;
        }

        IEnumerable<EmojiData.EmojiItem> emojis;

        // If searching, search all emojis
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            emojis = EmojiData.AllEmojis
                .RankBySearch(search, e => [e.Name, e.Emoji])
                .Take(MaxSearchResults);
        }
        else if (SelectedTab == null)
        {
            DisplayedEmojis.ReplaceAll([]);
            return;
        }
        else if (SelectedTab.Name == "Recent")
        {
            emojis = _settings.RecentEmojis
                .Select(emoji => EmojiLookup.GetValueOrDefault(emoji) ?? new EmojiData.EmojiItem(emoji, ""));
        }
        else if (SelectedTab.Name == "Favorites")
        {
            emojis = _settings.FavoriteEmojis
                .Select(emoji => EmojiLookup.GetValueOrDefault(emoji) ?? new EmojiData.EmojiItem(emoji, ""));
        }
        else
        {
            // Find category
            var category = EmojiData.Categories.FirstOrDefault(c => c.Name == SelectedTab.Name);
            emojis = category?.Emojis ?? [];
        }

        // A HashSet of favorites keeps the per-item IsFavorite check O(1) across all results.
        var favorites = _settings.FavoriteEmojis.ToHashSet();

        // Build the whole list, then swap it in with a single Reset notification (instead of clearing
        // and adding one item at a time, which made the non-virtualized grid re-layout per item).
        DisplayedEmojis.ReplaceAll(emojis.Select(e => new EmojiDisplayItem
        {
            Emoji = e.Emoji,
            Name = e.Name,
            IsFavorite = favorites.Contains(e.Emoji)
        }));

        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(EmptyStateHint));
        OnPropertyChanged(nameof(ShowClearRecent));
    }

    private void AddToRecent(string emoji)
    {
        if (_settings == null) return;

        // Remove if already exists
        _settings.RecentEmojis.Remove(emoji);

        // Add to beginning
        _settings.RecentEmojis.Insert(0, emoji);

        // Trim to max size
        while (_settings.RecentEmojis.Count > _settings.MaxRecentEmojis)
        {
            _settings.RecentEmojis.RemoveAt(_settings.RecentEmojis.Count - 1);
        }

        SaveSettings();
        OnPropertyChanged(nameof(HasRecentEmojis));
    }

    private void SaveSettings()
    {
        if (App.SettingsService != null)
        {
            _ = App.SettingsService.SaveGlobalSettingsAsync();
        }
    }
}
