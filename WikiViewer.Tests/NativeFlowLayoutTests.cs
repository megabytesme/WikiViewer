using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class NativeFlowLayoutTests
{
    [Fact]
    public void ParagraphBandsReturnToFullWidthBelowFloatsAndKeepFutureFloatStarts()
    {
        var placements = new[] { Tuple.Create("right", new ArticleFlowRect { X = 600, Y = 0, Width = 200, Height = 100 }),
            Tuple.Create("left", new ArticleFlowRect { X = 0, Y = 40, Width = 180, Height = 100 }) };
        var bands = NativeFlowLayout.FlowRegions(800, 20, placements);
        Assert.Equal(new[] { 0d, 20d, 80d, 120d }, bands.Select(b => b.Y));
        Assert.Equal(588, bands[0].Width);
        Assert.Equal(396, bands[1].Width);
        Assert.Equal(800, bands.Last().Width);
        Assert.True(double.IsPositiveInfinity(bands.Last().Height));
    }
    [Fact]
    public void StaticAnnotationsResolvePercentOffsetsAndCenteredMarkers()
    {
        var style = new WikiViewer.Core.Models.NativeArticleStyle { Left = WikiViewer.Core.Models.ArticleLength.Parse("50%"), Top = WikiViewer.Core.Models.ArticleLength.Parse("25%"),
            TranslateX = WikiViewer.Core.Models.ArticleLength.Parse("-50%", true), TranslateY = WikiViewer.Core.Models.ArticleLength.Parse("-50%", true) };
        var marker = NativeFlowLayout.Position(1000, 500, 20, 10, style);
        Assert.Equal(490, marker.X); Assert.Equal(120, marker.Y);
        style.Left = null; style.Top = null; style.Right = WikiViewer.Core.Models.ArticleLength.Parse("10px"); style.Bottom = WikiViewer.Core.Models.ArticleLength.Parse("20px");
        style.TranslateX = null; style.TranslateY = null;
        marker = NativeFlowLayout.Position(400, 200, 30, 15, style);
        Assert.Equal(360, marker.X); Assert.Equal(165, marker.Y);
    }
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
