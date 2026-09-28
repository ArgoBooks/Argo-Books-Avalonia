using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the SettingsModalViewModel.
/// </summary>
public class SettingsModalViewModelTests
{
    private readonly SettingsModalViewModel _viewModel;

    public SettingsModalViewModelTests()
    {
        _viewModel = new SettingsModalViewModel();
    }

    #region Time Format Tests

    [Fact]
    public void Is12HourFormat_WhenSelectedTimeFormatIs12h_ReturnsTrue()
    {
        _viewModel.SelectedTimeFormat = "12h";

        Assert.True(_viewModel.Is12HourFormat);
        Assert.False(_viewModel.Is24HourFormat);
    }

    [Fact]
    public void Is24HourFormat_WhenSelectedTimeFormatIs24h_ReturnsTrue()
    {
        _viewModel.SelectedTimeFormat = "24h";

        Assert.True(_viewModel.Is24HourFormat);
        Assert.False(_viewModel.Is12HourFormat);
    }

    #endregion

    #region Data Lists Tests

    [Fact]
    public void DateFormats_IsPopulated()
    {
        Assert.NotEmpty(_viewModel.DateFormats);
    }

    [Fact]
    public void DateFormats_ContainsExpectedFormats()
    {
        Assert.Contains("MM/DD/YYYY", _viewModel.DateFormats);
        Assert.Contains("DD/MM/YYYY", _viewModel.DateFormats);
        Assert.Contains("YYYY-MM-DD", _viewModel.DateFormats);
    }

    [Fact]
    public void Themes_IsPopulated()
    {
        Assert.NotEmpty(_viewModel.Themes);
    }

    [Fact]
    public void Themes_ContainsExpectedThemes()
    {
        Assert.Contains("Light", _viewModel.Themes);
        Assert.Contains("Dark", _viewModel.Themes);
        Assert.Contains("System", _viewModel.Themes);
    }

    [Fact]
    public void MaxPieSlicesOptions_IsPopulated()
    {
        Assert.NotEmpty(_viewModel.MaxPieSlicesOptions);
        Assert.Contains(6, _viewModel.MaxPieSlicesOptions);
    }

    [Fact]
    public void ReminderMinuteOptions_IsPopulated()
    {
        Assert.NotEmpty(_viewModel.ReminderMinuteOptions);
        Assert.Contains(5, _viewModel.ReminderMinuteOptions);
    }

    [Fact]
    public void AutoLockOptions_IsPopulated()
    {
        Assert.NotEmpty(_viewModel.AutoLockOptions);
        Assert.Contains("Never", _viewModel.AutoLockOptions);
        Assert.Contains("5 minutes", _viewModel.AutoLockOptions);
    }

    #endregion

    #region Security Settings Tests

    [Fact]
    public void CanEnableBiometricLogin_WhenHasPremiumAndHasPassword_ReturnsTrue()
    {
        _viewModel.HasPremium = true;
        _viewModel.HasPassword = true;

        Assert.True(_viewModel.CanEnableBiometricLogin);
    }

    [Fact]
    public void CanEnableBiometricLogin_WhenNoPremium_ReturnsFalse()
    {
        _viewModel.HasPremium = false;
        _viewModel.HasPassword = true;

        Assert.False(_viewModel.CanEnableBiometricLogin);
    }

    [Fact]
    public void NeedsPasswordForBiometricLogin_WhenHasPremiumButNoPassword_ReturnsTrue()
    {
        _viewModel.HasPremium = true;
        _viewModel.HasPassword = false;

        Assert.True(_viewModel.NeedsPasswordForBiometricLogin);
    }

    [Fact]
    public void NeedsPasswordForAutoLock_WhenNoPassword_ReturnsTrue()
    {
        _viewModel.HasPassword = false;

        Assert.True(_viewModel.NeedsPasswordForAutoLock);
    }

    #endregion

    #region Mobile Sync Tests

    [Fact]
    public void ToggleShortCodeRevealCommand_TogglesIsShortCodeRevealed()
    {
        Assert.False(_viewModel.IsShortCodeRevealed);

        _viewModel.ToggleShortCodeRevealCommand.Execute(null);
        Assert.True(_viewModel.IsShortCodeRevealed);

        _viewModel.ToggleShortCodeRevealCommand.Execute(null);
        Assert.False(_viewModel.IsShortCodeRevealed);
    }

    [Fact]
    public void LoadMobileSync_ResetsPairingState()
    {
        _viewModel.ShortCodeDisplay = "ABCD-1234";
        _viewModel.IsShortCodeRevealed = true;
        _viewModel.IsPhoneJustPaired = true;

        _viewModel.LoadMobileSync();

        Assert.Null(_viewModel.QrImage);
        Assert.Equal(string.Empty, _viewModel.ShortCodeDisplay);
        Assert.False(_viewModel.IsShortCodeRevealed);
        Assert.False(_viewModel.IsPhoneJustPaired);
        Assert.Empty(_viewModel.PairedDevices);
    }

    [Fact]
    public void SelectedTabIndex_ChangedAwayFromMobileAppTab_DoesNotThrow()
    {
        // Regression guard for the pairing-poll cancellation hook: navigating tabs (with or
        // without an in-flight pairing poll) must never throw.
        _viewModel.SelectedTabIndex = SettingsModalViewModel.MobileAppTabIndex; // Mobile app tab
        _viewModel.SelectedTabIndex = 0; // General tab

        Assert.Equal(0, _viewModel.SelectedTabIndex);
    }

    // ShouldContinuePairing is the guard ConnectPhoneAsync/PollPairingAsync check before ever
    // showing a pairing code or delivering the encrypted sync key. These tests pin down the
    // security property directly: once the pairing screen is cancelled or no longer visible,
    // the guard must say "don't proceed" so the key is never handed to a screen the user can't
    // see, regardless of network timing.

    [Fact]
    public void ShouldContinuePairing_WhenCancelled_ReturnsFalse()
    {
        // Even if the modal is (still, momentarily) open on the right tab, a cancelled
        // create/poll request must never proceed - this is the exact async-gap scenario: the
        // modal closed while CreatePairingAsync was in flight, which cancels the token.
        var result = SettingsModalViewModel.ShouldContinuePairing(
            isCancellationRequested: true, isModalOpen: true, selectedTabIndex: SettingsModalViewModel.MobileAppTabIndex);

        Assert.False(result);
    }

    [Fact]
    public void ShouldContinuePairing_WhenModalClosed_ReturnsFalse()
    {
        var result = SettingsModalViewModel.ShouldContinuePairing(
            isCancellationRequested: false, isModalOpen: false, selectedTabIndex: SettingsModalViewModel.MobileAppTabIndex);

        Assert.False(result);
    }

    [Fact]
    public void ShouldContinuePairing_WhenOnDifferentTab_ReturnsFalse()
    {
        var result = SettingsModalViewModel.ShouldContinuePairing(
            isCancellationRequested: false, isModalOpen: true, selectedTabIndex: 0);

        Assert.False(result);
    }

    [Fact]
    public void ShouldContinuePairing_WhenNotCancelledModalOpenOnMobileTab_ReturnsTrue()
    {
        var result = SettingsModalViewModel.ShouldContinuePairing(
            isCancellationRequested: false, isModalOpen: true, selectedTabIndex: SettingsModalViewModel.MobileAppTabIndex);

        Assert.True(result);
    }

    #endregion

    #region SelectTimeFormat Command Tests

    [Fact]
    public void SelectTimeFormatCommand_With24h_ChangesFormat()
    {
        _viewModel.SelectTimeFormatCommand.Execute("24h");

        Assert.Equal("24h", _viewModel.SelectedTimeFormat);
        Assert.True(_viewModel.Is24HourFormat);
    }

    [Fact]
    public void SelectTimeFormatCommand_With12h_ChangesFormat()
    {
        _viewModel.SelectedTimeFormat = "24h";

        _viewModel.SelectTimeFormatCommand.Execute("12h");

        Assert.Equal("12h", _viewModel.SelectedTimeFormat);
        Assert.True(_viewModel.Is12HourFormat);
    }

    #endregion
}
