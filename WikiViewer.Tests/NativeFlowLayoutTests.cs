using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class NativeFlowLayoutTests
{
    [Fact]
    public void TextFlowsBesideAnInfoboxAndReturnsToFullWidth()
    {
        var items = new[] { new ArticleFlowItem { Float = "right", PreferredWidth = 300 }, new ArticleFlowItem(), new ArticleFlowItem(), new ArticleFlowItem() };
        var rects = NativeFlowLayout.Arrange(900, items, (i, w) => i == 0 ? 250 : 150);
        Assert.Equal(600, rects[0].X);
        Assert.Equal(588, rects[1].Width);
        Assert.Equal(0, rects[1].Y);
        Assert.Equal(588, rects[2].Width);
        Assert.Equal(900, rects[3].Width);
        Assert.Equal(300, rects[3].Y);
    }

    [Fact]
    public void NarrowWindowsStackFloatsInSourceOrder()
    {
        var rects = NativeFlowLayout.Arrange(360, new[] { new ArticleFlowItem { Float = "right", PreferredWidth = 300 }, new ArticleFlowItem() }, (i, w) => 100);
        Assert.Equal(360, rects[0].Width);
        Assert.Equal(100, rects[1].Y);
    }

    [Fact]
    public void ClearMovesFollowingContentBelowBothFloats()
    {
        var items = new[] { new ArticleFlowItem { Float = "left", PreferredWidth = 200 }, new ArticleFlowItem { Float = "right", PreferredWidth = 220 }, new ArticleFlowItem { Clear = "both" } };
        var rects = NativeFlowLayout.Arrange(900, items, (i, w) => i == 0 ? 100 : i == 1 ? 250 : 50);
        Assert.Equal(250, rects[2].Y);
        Assert.Equal(900, rects[2].Width);
    }

    [Fact]
    public void KeepsRequestedFloatWidthsAndAvoidsOverlappingOppositeFloats()
    {
        var items = new[] { new ArticleFlowItem { Float = "left", PreferredWidth = 600 }, new ArticleFlowItem { Float = "right", PreferredWidth = 500 }, new ArticleFlowItem() };
        var rects = NativeFlowLayout.Arrange(900, items, (i, w) => 200);
        Assert.Equal(600, rects[0].Width);
        Assert.Equal(200, rects[1].Y);
        Assert.True(rects[2].Y >= 200);
        var fullWidth = NativeFlowLayout.Arrange(700, new[] { new ArticleFlowItem { Float = "right", PreferredWidth = 700 }, new ArticleFlowItem() }, (i, w) => 100);
        Assert.Equal(700, fullWidth[0].Width);
        Assert.Equal(100, fullWidth[1].Y);
    }

    [Fact]
    public void BalancedColumnsRetainEveryBlockAndCollapseOnNarrowWindows()
    {
        var items = Enumerable.Range(0, 6).Select(_ => new ArticleFlowItem()).ToArray();
        var wide = NativeFlowLayout.Arrange(900, items, (i, w) => 100, 2);
        Assert.Equal(6, wide.Count);
        Assert.Equal(458, wide[3].X);
        Assert.Equal(0, wide[3].Y);
        var narrow = NativeFlowLayout.Arrange(360, items, (i, w) => 100, 2);
        Assert.All(narrow, r => Assert.Equal(0, r.X));
        Assert.Equal(500, narrow[5].Y);
    }
}
