using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class ArticleThemeTests
{
    [Theory]
    [InlineData("#f8f9fa")]
    [InlineData("#ccccff")]
    [InlineData("#ffe4e1")]
    [InlineData("#ddffdd")]
    public void PalePanelsBecomeMutedWithReadableTextInDarkMode(string source)
    {
        var theme = new ArticleThemeColors(true);
        uint original = ArticleColor.Parse(source).Value;
        var context = new ArticleBackground(original, null);
        uint background = ArticleThemeService.Resolve(original, ArticleColorRole.Background, null, theme);
        uint text = ArticleThemeService.Resolve(0xff202122, ArticleColorRole.Text, context, theme);
        Assert.True(ArticleThemeService.Luminance(background) < 0.1);
        Assert.True(ArticleThemeService.Contrast(text, background) >= 4.5);
        Assert.NotEqual(original, background);
    }

    [Fact]
    public void PastelTitleBarsKeepTheirHueAndLightModeKeepsSourceBackgrounds()
    {
        const uint blue = 0xffccccff;
        uint muted = ArticleThemeService.Resolve(blue, ArticleColorRole.Background, null, new ArticleThemeColors(true));
        Assert.True((muted & 255) > ((muted >> 16) & 255));
        Assert.Equal(blue, ArticleThemeService.Resolve(blue, ArticleColorRole.Background, null, new ArticleThemeColors(false)));
        Assert.Equal(0xff183020u, ArticleThemeService.Resolve(0xff183020, ArticleColorRole.Background, null, new ArticleThemeColors(true)));
    }

    [Fact]
    public void NestedTransparentBackgroundsCompositeAgainstTheirActualPanel()
    {
        var document = NativeArticleDocumentService.Parse("<div style='background:#ccccff'><div style='background:rgba(255,255,255,0.5)'><p>Text</p></div></div>", new Uri("https://example.org/Page"));
        var outer = document.Nodes.Single(); var inner = outer.Children.Single(); var paragraph = inner.Children.Single();
        Assert.Same(outer.Style.ColorContext, inner.Style.ColorContext.Parent);
        Assert.Same(inner.Style.ColorContext, paragraph.Style.ColorContext);
        var theme = new ArticleThemeColors(true);
        uint actual = ArticleThemeService.EffectiveBackground(paragraph.Style.ColorContext, theme);
        uint expected = ArticleThemeService.Composite(ArticleThemeService.Resolve(0x80ffffff, ArticleColorRole.Background, null, theme),
            ArticleThemeService.Resolve(0xffccccff, ArticleColorRole.Background, null, theme));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, "#0000ff")]
    [InlineData(true, "#202122")]
    [InlineData(false, "#eeeeee")]
    public void TextAndLinksRetainContrastAgainstTheEffectiveBackground(bool dark, string color)
    {
        var theme = new ArticleThemeColors(dark);
        uint result = ArticleThemeService.Resolve(ArticleColor.Parse(color), ArticleColorRole.Link, null, theme);
        Assert.True(ArticleThemeService.Contrast(result, theme.Canvas) >= 4.5);
    }

    [Fact]
    public void HighContrastUsesSystemColorsEvenOnNestedCustomPanels()
    {
        var theme = new ArticleThemeColors(true) { HighContrast = true, Canvas = 0xff000000, Text = 0xffffff00, Link = 0xff00ffff };
        var panel = new ArticleBackground(0xffffaaaa, new ArticleBackground(0xffccccff, null));
        Assert.Equal(theme.Canvas, ArticleThemeService.EffectiveBackground(panel, theme));
        Assert.Equal(theme.Canvas, ArticleThemeService.Resolve(0xffccccff, ArticleColorRole.Background, null, theme));
        Assert.Equal(theme.Text, ArticleThemeService.Resolve(0xff000000, ArticleColorRole.Text, panel, theme));
        Assert.Equal(theme.Link, ArticleThemeService.Resolve(0xff0000ff, ArticleColorRole.Link, panel, theme));
        Assert.Equal(0u, ArticleThemeService.Resolve(0, ArticleColorRole.Background, null, theme));
    }
}
