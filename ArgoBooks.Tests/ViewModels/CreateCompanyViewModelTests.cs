using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the CreateCompanyViewModel.
/// </summary>
public class CreateCompanyViewModelTests
{
    private readonly CreateCompanyViewModel _viewModel = new();

    #region Step Computed Properties Tests

    [Fact]
    public void IsStep1_WhenCurrentStepIsOne_ReturnsTrue()
    {
        _viewModel.CurrentStep = 1;

        Assert.True(_viewModel.IsStep1);
    }

    [Fact]
    public void IsStep1_WhenCurrentStepIsTwo_ReturnsFalse()
    {
        _viewModel.CurrentStep = 2;

        Assert.False(_viewModel.IsStep1);
    }

    [Fact]
    public void IsStep2_WhenCurrentStepIsTwo_ReturnsTrue()
    {
        _viewModel.CurrentStep = 2;

        Assert.True(_viewModel.IsStep2);
    }

    [Fact]
    public void IsStep2_WhenCurrentStepIsOne_ReturnsFalse()
    {
        _viewModel.CurrentStep = 1;

        Assert.False(_viewModel.IsStep2);
    }

    #endregion

    #region Navigation Properties Tests

    [Fact]
    public void CanGoBack_WhenOnStep1_ReturnsFalse()
    {
        _viewModel.CurrentStep = 1;

        Assert.False(_viewModel.CanGoBack);
    }

    [Fact]
    public void CanGoBack_WhenOnStep2_ReturnsTrue()
    {
        _viewModel.CurrentStep = 2;

        Assert.True(_viewModel.CanGoBack);
    }

    [Fact]
    public void CanGoNext_WhenOnStep1_ReturnsTrue()
    {
        _viewModel.CurrentStep = 1;

        Assert.True(_viewModel.CanGoNext);
    }

    [Fact]
    public void CanGoNext_WhenOnStep2_ReturnsFalse()
    {
        _viewModel.CurrentStep = 2;

        Assert.False(_viewModel.CanGoNext);
    }

    [Fact]
    public void IsLastStep_WhenOnStep1_ReturnsFalse()
    {
        _viewModel.CurrentStep = 1;

        Assert.False(_viewModel.IsLastStep);
    }

    [Fact]
    public void IsLastStep_WhenOnStep2_ReturnsTrue()
    {
        _viewModel.CurrentStep = 2;

        Assert.True(_viewModel.IsLastStep);
    }

    #endregion

    #region Data Lists Tests

    [Fact]
    public void BusinessTypes_ListIsNotEmpty()
    {
        Assert.NotEmpty(_viewModel.BusinessTypes);
    }

    [Fact]
    public void BusinessTypes_ContainsExpectedTypes()
    {
        Assert.Contains("Sole Proprietorship", _viewModel.BusinessTypes);
        Assert.Contains("Corporation", _viewModel.BusinessTypes);
        Assert.Contains("LLC", _viewModel.BusinessTypes);
    }

    [Fact]
    public void Industries_ListIsNotEmpty()
    {
        Assert.NotEmpty(_viewModel.Industries);
    }

    [Fact]
    public void Industries_ContainsExpectedIndustries()
    {
        Assert.Contains("Retail", _viewModel.Industries);
        Assert.Contains("Technology", _viewModel.Industries);
        Assert.Contains("Healthcare", _viewModel.Industries);
    }

    [Fact]
    public void Currencies_ListIsPopulated()
    {
        Assert.NotNull(_viewModel.Currencies);
        Assert.NotEmpty(_viewModel.Currencies);
    }

    [Fact]
    public void PriorityCurrencies_ListIsPopulated()
    {
        Assert.NotNull(_viewModel.PriorityCurrencies);
        Assert.NotEmpty(_viewModel.PriorityCurrencies);
    }

    #endregion

    #region Navigation Command Tests

    [Fact]
    public void NextStepCommand_WhenOnStep1_AdvancesToStep2()
    {
        _viewModel.CurrentStep = 1;

        _viewModel.NextStepCommand.Execute(null);

        Assert.Equal(2, _viewModel.CurrentStep);
    }

    [Fact]
    public void PreviousStepCommand_WhenOnStep2_GoesBackToStep1()
    {
        _viewModel.CurrentStep = 2;

        _viewModel.PreviousStepCommand.Execute(null);

        Assert.Equal(1, _viewModel.CurrentStep);
    }

    [Fact]
    public void PreviousStepCommand_WhenOnStep1_StaysOnStep1()
    {
        _viewModel.CurrentStep = 1;

        _viewModel.PreviousStepCommand.Execute(null);

        Assert.Equal(1, _viewModel.CurrentStep);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void IsStep1Valid_WhenCompanyNameAndCountrySet_ReturnsTrue()
    {
        _viewModel.CompanyName = "Test Company";
        _viewModel.Country = "United States";

        Assert.True(_viewModel.IsStep1Valid);
    }

    [Fact]
    public void IsStep1Valid_WhenCompanyNameEmpty_ReturnsFalse()
    {
        _viewModel.CompanyName = "";
        _viewModel.Country = "United States";

        Assert.False(_viewModel.IsStep1Valid);
    }

    [Fact]
    public void IsStep2Valid_WhenPasswordDisabled_ReturnsTrue()
    {
        _viewModel.EnablePassword = false;

        Assert.True(_viewModel.IsStep2Valid);
    }

    [Theory]
    [InlineData("short1", "too short")]
    [InlineData("passwordonly", "no digit")]
    [InlineData("12345678", "no letter")]
    public void IsStep2Valid_WithWeakPassword_ReturnsFalse(string password, string reason)
    {
        // Creating a company must apply the same rules as Settings > Security, otherwise a
        // password could be set here that the app would refuse if changed later.
        _viewModel.EnablePassword = true;
        _viewModel.Password = password;
        _viewModel.ConfirmPassword = password;

        Assert.False(_viewModel.IsStep2Valid, $"Password '{password}' should be rejected: {reason}.");
        Assert.NotNull(_viewModel.PasswordRequirementError);
        Assert.True(_viewModel.ShowPasswordRequirementError);
    }

    [Fact]
    public void IsStep2Valid_WithAcceptablePassword_ReturnsTrue()
    {
        _viewModel.EnablePassword = true;
        _viewModel.Password = "CorrectHorse1";
        _viewModel.ConfirmPassword = "CorrectHorse1";

        Assert.True(_viewModel.IsStep2Valid);
        Assert.Null(_viewModel.PasswordRequirementError);
        Assert.False(_viewModel.ShowPasswordRequirementError);
    }

    [Fact]
    public void PasswordRequirementError_BeforeTyping_StaysHidden()
    {
        // The field should not open already showing an error.
        _viewModel.EnablePassword = true;

        Assert.Null(_viewModel.PasswordRequirementError);
        Assert.False(_viewModel.ShowPasswordRequirementError);
    }

    [Fact]
    public void PasswordRequirementError_WhenPasswordDisabled_StaysHidden()
    {
        _viewModel.EnablePassword = false;
        _viewModel.Password = "a";

        Assert.Null(_viewModel.PasswordRequirementError);
        Assert.True(_viewModel.IsStep2Valid);
    }

    [Fact]
    public void PasswordStrength_IsHiddenUntilSomethingIsTyped()
    {
        Assert.False(_viewModel.ShowPasswordStrength);

        _viewModel.Password = "CorrectHorse1";
        Assert.True(_viewModel.ShowPasswordStrength);

        _viewModel.Password = "";
        Assert.False(_viewModel.ShowPasswordStrength);
        Assert.Equal(0, _viewModel.PasswordStrengthScore);
    }

    [Fact]
    public void PasswordStrength_RatesAStrongPasswordAboveAWeakOne()
    {
        _viewModel.Password = "abc";
        var weakScore = _viewModel.PasswordStrengthScore;
        Assert.True(_viewModel.IsStrengthWeak);

        _viewModel.Password = "T2rq#vLm9!pXw4Zs";
        Assert.True(_viewModel.PasswordStrengthScore > weakScore);
        Assert.True(_viewModel.IsStrengthStrong);
        Assert.False(_viewModel.IsStrengthWeak);
        Assert.NotEmpty(_viewModel.PasswordStrengthText);
    }

    [Fact]
    public void TogglePasswordVisibility_FlipsTheFieldAndTheIcon()
    {
        var hiddenIcon = _viewModel.PasswordVisibilityIcon;
        Assert.False(_viewModel.IsPasswordVisible);

        _viewModel.TogglePasswordVisibilityCommand.Execute(null);

        Assert.True(_viewModel.IsPasswordVisible);
        Assert.NotEqual(hiddenIcon, _viewModel.PasswordVisibilityIcon);

        _viewModel.TogglePasswordVisibilityCommand.Execute(null);
        Assert.False(_viewModel.IsPasswordVisible);
        Assert.Equal(hiddenIcon, _viewModel.PasswordVisibilityIcon);
    }

    [Fact]
    public void ToggleConfirmPasswordVisibility_FlipsIndependentlyOfThePasswordField()
    {
        _viewModel.ToggleConfirmPasswordVisibilityCommand.Execute(null);

        Assert.True(_viewModel.IsConfirmPasswordVisible);
        Assert.False(_viewModel.IsPasswordVisible);
    }

    [Fact]
    public void PasswordsMatch_WhenBothMatch_ReturnsTrue()
    {
        _viewModel.Password = "test123";
        _viewModel.ConfirmPassword = "test123";

        Assert.True(_viewModel.PasswordsMatch);
    }

    [Fact]
    public void PasswordsMatch_WhenDifferent_ReturnsFalse()
    {
        _viewModel.Password = "test123";
        _viewModel.ConfirmPassword = "different";

        Assert.False(_viewModel.PasswordsMatch);
    }

    #endregion

    #region Open/Close Tests

    [Fact]
    public void OpenCommand_WhenExecuted_SetsIsOpenToTrue()
    {
        _viewModel.OpenCommand.Execute(null);

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void OpenCommand_WhenExecuted_ResetsCurrentStepToOne()
    {
        _viewModel.CurrentStep = 2;

        _viewModel.OpenCommand.Execute(null);

        Assert.Equal(1, _viewModel.CurrentStep);
    }

    [Fact]
    public void CloseCommand_WhenExecuted_SetsIsOpenToFalse()
    {
        _viewModel.OpenCommand.Execute(null);
        Assert.True(_viewModel.IsOpen);

        _viewModel.CloseCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    #endregion
}
