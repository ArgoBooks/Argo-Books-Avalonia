using ArgoBooks.Helpers;
using Xunit;

namespace ArgoBooks.Tests.Utilities;

/// <summary>
/// Tests for the ResponsiveHeaderHelper class.
/// </summary>
public class ResponsiveHeaderHelperTests
{
    #region Compact Mode Tests (< 750px)

    [Fact]
    public void HeaderWidth_Below750_SetsCompactMode()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 600
        };

        Assert.True(helper.IsCompactMode);
        Assert.False(helper.IsMediumMode);
    }

    [Fact]
    public void HeaderWidth_Below750_HidesButtonText()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 600
        };

        Assert.False(helper.ShowButtonText);
    }

    [Fact]
    public void HeaderWidth_Below750_SetsSearchBoxWidth200()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 600
        };

        Assert.Equal(200, helper.SearchBoxWidth);
    }

    [Fact]
    public void HeaderWidth_Below750_SetsHeaderSpacing6()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 600
        };

        Assert.Equal(6, helper.HeaderSpacing);
    }

    [Fact]
    public void HeaderWidth_Below750_SetsSearchBoxMinHeight30()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 600
        };

        Assert.Equal(30, helper.SearchBoxMinHeight);
    }

    #endregion

    #region Medium Mode Tests (750-950px)

    [Fact]
    public void HeaderWidth_Between750And950_SetsMediumMode()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 850
        };

        Assert.False(helper.IsCompactMode);
        Assert.True(helper.IsMediumMode);
    }

    [Fact]
    public void HeaderWidth_Between750And950_ShowsButtonText()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 850
        };

        Assert.True(helper.ShowButtonText);
    }

    [Fact]
    public void HeaderWidth_Between750And950_SetsSearchBoxWidth200()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 850
        };

        Assert.Equal(200, helper.SearchBoxWidth);
    }

    [Fact]
    public void HeaderWidth_Between750And950_SetsHeaderSpacing8()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 850
        };

        Assert.Equal(8, helper.HeaderSpacing);
    }

    #endregion

    #region Full Mode Tests (>= 950px)

    [Fact]
    public void HeaderWidth_Above950_SetsFullMode()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 1200
        };

        Assert.False(helper.IsCompactMode);
        Assert.False(helper.IsMediumMode);
    }

    [Fact]
    public void HeaderWidth_Above950_ShowsButtonText()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 1200
        };

        Assert.True(helper.ShowButtonText);
    }

    [Fact]
    public void HeaderWidth_Above950_SetsSearchBoxWidth250()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 1200
        };

        Assert.Equal(250, helper.SearchBoxWidth);
    }

    [Fact]
    public void HeaderWidth_Above950_SetsHeaderSpacing12()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 1200
        };

        Assert.Equal(12, helper.HeaderSpacing);
    }

    [Fact]
    public void HeaderWidth_Above950_SetsSearchBoxMinHeight36()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 1200
        };

        Assert.Equal(36, helper.SearchBoxMinHeight);
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public void HeaderWidth_Exactly750_IsNotCompact()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 750
        };

        Assert.False(helper.IsCompactMode);
        Assert.True(helper.IsMediumMode);
    }

    [Fact]
    public void HeaderWidth_Exactly950_IsFullMode()
    {
        var helper = new ResponsiveHeaderHelper
        {
            HeaderWidth = 950
        };

        Assert.False(helper.IsCompactMode);
        Assert.False(helper.IsMediumMode);
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void DefaultShowButtonText_IsTrue()
    {
        var helper = new ResponsiveHeaderHelper();

        Assert.True(helper.ShowButtonText);
    }

    [Fact]
    public void DefaultSearchBoxWidth_Is250()
    {
        var helper = new ResponsiveHeaderHelper();

        Assert.Equal(250, helper.SearchBoxWidth);
    }

    [Fact]
    public void DefaultHeaderSpacing_Is12()
    {
        var helper = new ResponsiveHeaderHelper();

        Assert.Equal(12, helper.HeaderSpacing);
    }

    #endregion
}
