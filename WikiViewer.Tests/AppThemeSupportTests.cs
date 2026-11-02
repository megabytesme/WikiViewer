using WikiViewer.Core;
using WikiViewer.Core.Services;
using Xunit;

public class AppThemeSupportTests
{
    [Theory]
    [InlineData(15063, AppAppearance.Windows10)]
    [InlineData(16298, AppAppearance.Windows10)]
    [InlineData(16299, AppAppearance.Windows10Fluent)]
    [InlineData(17763, AppAppearance.Windows10Fluent)]
    [InlineData(21999, AppAppearance.Windows10Fluent)]
    [InlineData(22000, AppAppearance.Windows11SunValley)]
    public void AutomaticUsesTheNewestSupportedTheme(int build, AppAppearance theme)
        => Assert.Equal(theme, AppThemeSupport.Resolve(AppAppearance.Automatic, build));

    [Fact]
    public void UnsupportedPreferencesFallBackAndAreNotSelectable()
    {
        Assert.False(AppThemeSupport.IsSupported(AppAppearance.Windows10Fluent, 15063));
        Assert.False(AppThemeSupport.IsSupported(AppAppearance.Windows11SunValley, 19045));
        Assert.Equal(AppAppearance.Windows10, AppThemeSupport.Resolve(AppAppearance.Windows10Fluent, 15063));
        Assert.Equal(AppAppearance.Windows10Fluent, AppThemeSupport.Resolve(AppAppearance.Windows11SunValley, 19045));
        Assert.Equal(1, (int)AppAppearance.Windows10);
        Assert.Equal(2, (int)AppAppearance.Windows10Fluent);
        Assert.Equal(3, (int)AppAppearance.Windows11SunValley);
    }

    [Fact]
    public void LegacySavedSunValleyValueKeepsItsMeaningAfterEnumReordering()
    {
        Assert.Equal(AppAppearance.Windows11SunValley, AppThemeSupport.FromLegacyValue(2));
        Assert.Equal(AppAppearance.Windows10, AppThemeSupport.FromLegacyValue(1));
        Assert.Equal(AppAppearance.Automatic, AppThemeSupport.FromLegacyValue(0));
    }
}
