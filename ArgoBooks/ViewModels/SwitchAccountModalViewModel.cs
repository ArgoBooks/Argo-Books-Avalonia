using System.Collections.ObjectModel;
using ArgoBooks.Core;
using ArgoBooks.Utilities;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents an account that can be switched to.
/// </summary>
public partial class AccountItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private Color _color = Color.Parse(AppColors.Primary);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>
/// ViewModel for the switch account modal.
/// </summary>
public partial class SwitchAccountModalViewModel : ViewModelBase
{
    private readonly List<AccountItem> _allAccounts = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private AccountItem? _selectedAccount;

    /// <summary>
    /// Filtered accounts to display (excludes current account).
    /// </summary>
    public ObservableCollection<AccountItem> Accounts { get; } = [];

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public SwitchAccountModalViewModel()
    {
        // Add sample accounts for design-time and testing
        _allAccounts.Add(new AccountItem
        {
            Id = "1",
            Name = "Acme Corporation",
            Description = "Main business account",
            Initials = "AC",
            Color = Color.Parse(AppColors.Primary),
            IsCurrent = true
        });
        _allAccounts.Add(new AccountItem
        {
            Id = "2",
            Name = "Smith Consulting LLC",
            Description = "Consulting services",
            Initials = "SC",
            Color = Color.Parse(AppColors.Emerald)
        });
        _allAccounts.Add(new AccountItem
        {
            Id = "3",
            Name = "Personal Finances",
            Description = "Personal accounting",
            Initials = "PF",
            Color = Color.Parse(AppColors.Violet)
        });
        _allAccounts.Add(new AccountItem
        {
            Id = "4",
            Name = "Johnson & Partners",
            Description = "Investment portfolio",
            Initials = "JP",
            Color = Color.Parse(AppColors.Orange)
        });

        RefreshFilteredAccounts();
    }

    partial void OnSearchQueryChanged(string value)
    {
        RefreshFilteredAccounts();
    }

    private void RefreshFilteredAccounts()
    {
        Accounts.Clear();

        var query = SearchQuery.Trim();

        // Filter and score accounts
        var filteredAccounts = _allAccounts
            .Where(a => !a.IsCurrent) // Exclude current account
            .RankBySearch(query, a => [a.Name, a.Description]);

        foreach (var account in filteredAccounts)
        {
            Accounts.Add(account);
        }
    }

    #region Commands

    /// <summary>
    /// Opens the modal.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        SearchQuery = string.Empty;
        SelectedAccount = null;
        RefreshFilteredAccounts();
        IsOpen = true;
    }

    /// <summary>
    /// Closes the modal.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Selects an account and requests login.
    /// </summary>
    [RelayCommand]
    private void SelectAccount(AccountItem? account)
    {
        if (account == null || account.IsCurrent) return;

        SelectedAccount = account;
        Close();
        AccountSelected?.Invoke(this, account);
    }

    /// <summary>
    /// Creates a new account.
    /// </summary>
    [RelayCommand]
    private void CreateAccount()
    {
        Close();
        CreateAccountRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when an account is selected (should open login modal).
    /// </summary>
    public event EventHandler<AccountItem>? AccountSelected;

    /// <summary>
    /// Raised when create new account is requested.
    /// </summary>
    public event EventHandler? CreateAccountRequested;

    #endregion
}
