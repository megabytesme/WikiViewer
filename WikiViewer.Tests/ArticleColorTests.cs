using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class ArticleColorTests
{
    [Theory]
    [InlineData("#123", 0xff112233u)] [InlineData("#1234", 0x44112233u)]
    [InlineData("#112233", 0xff112233u)] [InlineData("#11223344", 0x44112233u)]
    [InlineData("rgba(255,0,0,0.5)", 0x80ff0000u)] [InlineData("rgb(0,128,255)", 0xff0080ffu)]
    [InlineData("blue", 0xff0000ffu)] [InlineData("transparent", 0u)]
    public void ParsesSupportedCssColors(string source, uint expected) => Assert.Equal(expected, ArticleColor.Parse(source));

    [Theory]
    [InlineData("var(--theme-color)")] [InlineData("url(example.png)")] [InlineData("#xyz")]
    [InlineData("rgb(999,0,0)")] [InlineData("rgba(0,0,0,2)")]
    public void UnsupportedColorsUseNativeDefaults(string source) => Assert.Null(ArticleColor.Parse(source));
}
