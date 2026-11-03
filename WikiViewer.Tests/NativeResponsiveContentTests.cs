using System.Text;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class NativeResponsiveContentTests
{
    private static readonly Uri Page = new("https://example.org/wiki/Main_Page");

    [Fact]
    public void NavigationGridsKeepGroupLabelsBesideTheirVersionLists()
    {
        var node = NativeArticleDocumentService.Parse("<div class='navbox-inner' style='display:grid'><div class='navbox-list'>All versions</div><div class='navbox-group'>Windows</div><div class='navbox-list hlist'><ul><li><a href='/10'>10</a></li><li><a href='/11'>11</a></li></ul></div></div>", Page).Nodes.Single();
        Assert.Equal(NativeArticleNodeKind.Table, node.Kind);
        Assert.Equal(2, node.Children.Count);
        Assert.Equal(2, node.Children[0].Children.Single().ColumnSpan);
        Assert.Equal(2, node.Children[1].Children.Count);
        var list = node.Children[1].Children[1].Children.Single().Children.Single();
        Assert.Equal(NativeArticleNodeKind.Container, list.Kind);
        Assert.Equal(" · ", list.Children[1].Text);
        Assert.Equal(NativeArticleNodeKind.Link, list.Children[0].Children.Single().Kind);
    }

    [Fact]
    public void NestedHorizontalListsKeepTheirGrouping()
    {
        var box = NativeArticleDocumentService.Parse("<div class='hlist'><ul><li>Version<ul><li>First</li><li>Second</li></ul></li><li>Next</li></ul></div>", Page).Nodes.Single();
        var nested = box.Children.Single().Children[0].Children[1];
        Assert.Equal(" (", nested.Children[0].Text);
        Assert.Equal(")", nested.Children.Last().Text);
    }

    [Fact]
    public void LegacyCenterMarkupRetainsItsAlignment()
    {
        var document = NativeArticleDocumentService.Parse("<center><p>Centered text</p></center>", Page);
        Assert.Equal("center", document.Nodes.Single().Style.Alignment);
        Assert.Equal("center", document.Nodes.Single().Children.Single().Style.Alignment);
        Assert.Empty(document.UnsupportedFeatures);
    }

    [Fact]
    public void LinkedImagesRemainImagesWhenWikiCssMakesThemBlocks()
    {
        var link = NativeArticleDocumentService.Parse("<style>img{display:block}</style><a href='/File:Picture'><img src='/picture.png' width='160'></a>", Page).Nodes.Single();
        Assert.Equal(NativeArticleNodeKind.Link, link.Kind);
        Assert.Equal(NativeArticleNodeKind.Image, link.Children.Single().Kind);
        Assert.Equal("https://example.org/picture.png", link.Children.Single().Uri.AbsoluteUri);
    }

    [Fact]
    public void LinkedMediaKeepsPlayableSourcesAndItsOwnControls()
    {
        var link = NativeArticleDocumentService.Parse("<a href='/File:Clip'><video controls><source src='/clip.mp4' type='video/mp4'></video></a>", Page).Nodes.Single();
        var media = link.Children.Single();
        Assert.Equal(NativeArticleNodeKind.Media, media.Kind);
        Assert.True(media.IsBlock);
        Assert.Single(media.MediaSources);
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("video")]
    public void NestedFileSpansKeepMediaInFiniteWidthBlockLayout(string tag)
    {
        var wrapper = NativeArticleDocumentService.Parse("<span typeof='mw:File'><span><" + tag + " width='350'><source src='/clip.mp4' type='video/mp4'></" + tag + "></span></span>", Page).Nodes.Single();
        Assert.True(wrapper.IsBlock);
        Assert.True(wrapper.Children.Single().IsBlock);
        var media = wrapper.Children.Single().Children.Single();
        Assert.Equal(NativeArticleNodeKind.Media, media.Kind);
        Assert.Equal(350, media.Style.Width.Value);
    }

    [Fact]
    public void UnequalHomepageColumnsKeepTheirFlexWeights()
    {
        var node = NativeArticleDocumentService.Parse("<style>.banner{display:flex;flex-flow:row wrap;gap:20px}.wide{flex:3}.narrow{flex:1}</style><div class='banner'><div class='wide'>Welcome</div><div class='narrow'>Navigation</div></div>", Page).Nodes.Single();
        var rects = NativeResponsiveLayout.Flex(820, node.Children.Select(n => n.Style).ToList(), node.Style, (i, w) => 100);
        Assert.True(node.Style.Wrap);
        Assert.Equal(600, rects[0].Width); Assert.Equal(200, rects[1].Width);
        Assert.Equal(620, rects[1].X);
    }

    [Theory]
    [InlineData(800, 0)]
    [InlineData(450, 120)]
    public void WrappingCardsRespectTheirMinimumWidth(double width, double secondY)
    {
        var node = NativeArticleDocumentService.Parse("<div style='display:flex;flex-flow:row wrap;gap:20px'><div style='flex:1;min-width:280px'>One</div><div style='flex:1;min-width:280px'>Two</div></div>", Page).Nodes.Single();
        var rects = NativeResponsiveLayout.Flex(width, node.Children.Select(n => n.Style).ToList(), node.Style, (i, w) => 100);
        Assert.Equal(secondY, rects[1].Y);
        Assert.All(rects, r => Assert.True(r.X + r.Width <= width + 0.01));
    }

    [Fact]
    public void CompactIconsRetainTheirWidthAndCenterInTheRow()
    {
        var container = new NativeArticleStyle { Display = "flex", Wrap = true, JustifyContent = "center", Gap = ArticleLength.Parse("10px") };
        var styles = Enumerable.Range(0, 3).Select(_ => new NativeArticleStyle { Width = ArticleLength.Parse("90px"), FlexShrink = 0 }).ToList();
        var rects = NativeResponsiveLayout.Flex(400, styles, container, (i, w) => 80);
        Assert.Equal(55, rects[0].X); Assert.Equal(90, rects[0].Width);
    }

    [Fact]
    public void AudioCardLeavesMostOfTheWidthForItsPlayerAndCaption()
    {
        var row = NativeArticleDocumentService.Parse("<div style='display:flex;gap:8px'><div style='flex:none'><img src='/speaker.png' width='50'></div><div style='flex:1'><audio style='width:232px'><source src='/clip.mp3' type='audio/mpeg'></audio><p>Drum cadences performed by the United States Navy Band</p></div></div>", Page).Nodes.Single();
        var styles = row.Children.Select(node => NativeResponsiveLayout.FlexItemStyle(node, 320)).ToList();
        var rects = NativeResponsiveLayout.Flex(320, styles, row.Style, (i, w) => 100);
        Assert.InRange(rects[0].Width, 50, 80);
        Assert.True(rects[1].Width >= 232);
        Assert.Equal(320, rects.Last().X + rects.Last().Width, 6);
        Assert.Null(row.Children[0].Style.FlexBasis);
    }

    [Fact]
    public void StyledListGridKeepsListItemsAsLayoutChildren()
    {
        var node = NativeArticleDocumentService.Parse("<ul style='display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr))'><li><a href='/A'>One</a></li><li>Two</li></ul>", Page).Nodes.Single();
        Assert.Equal(NativeArticleNodeKind.Container, node.Kind);
        Assert.Equal(200, node.Style.GridMinimum.Value);
        Assert.All(node.Children, n => Assert.True(n.IsBlock));
        Assert.Equal(NativeArticleNodeKind.Link, node.Children[0].Children[0].Kind);
    }

    [Fact]
    public void TablesWrapWithinCardsAndOnlyOverflowWhenColumnMinimumsRequireIt()
    {
        var fit = NativeResponsiveLayout.TableColumns(280, new[] { 240d, 360d });
        Assert.Equal(280, fit.Sum(), 6);
        Assert.True(fit[1] > fit[0]);
        var wide = NativeResponsiveLayout.TableColumns(280, Enumerable.Repeat(80d, 12).ToArray());
        Assert.Equal(480, wide.Sum(), 6);
    }

    [Fact]
    public void TableCellHintsKeepPercentWidthsAndShortLabelsCompact()
    {
        var table = NativeArticleDocumentService.Parse("<table><tr><td width='30%'>Label</td><td>Short</td></tr></table>", Page).Nodes.Single();
        var row = table.Children.Single();
        Assert.Equal(150, NativeResponsiveLayout.PreferredCellWidth(row.Children[0], 500));
        Assert.InRange(NativeResponsiveLayout.PreferredCellWidth(row.Children[1], 500), 40, 100);
    }

    [Fact]
    public void ImagesKeepLazyAndResponsiveAlternativesWithoutUnsafePlaceholders()
    {
        var node = NativeArticleDocumentService.Parse("<picture><source srcset='/fallback.png 1x'><img src='data:image/gif;base64,aaaa' data-src='/first.webp?a=1&amp;b=2' srcset='/second.png 1.5x, /third.png 2x'></picture>", Page).Nodes.Single().Children.Single();
        Assert.Equal(new[] { "https://example.org/first.webp?a=1&b=2", "https://example.org/second.png", "https://example.org/third.png", "https://example.org/fallback.png" }, node.ImageSources.Select(u => u.AbsoluteUri));
        Assert.Equal(node.ImageSources[0], node.Uri);
    }

    [Fact]
    public void WikimediaVideoPrefersMpegTranscodeAndPracticalWebmResolution()
    {
        var node = NativeArticleDocumentService.Parse("<video><source src='/1080.webm' type='video/webm; codecs=&quot;vp9, opus&quot;' data-height='1080'><source src='/240.webm' type='video/webm' data-height='240'><source src='/360.mov' type='video/quicktime; codecs=&quot;mp4v&quot;'><source src='/480.webm' type='video/webm' data-height='480'></video>", Page).Nodes.Single();
        var sources = NativeMediaService.PlaybackSources(node.MediaSources);
        Assert.EndsWith("360.mov", sources[0].Uri.AbsolutePath);
        Assert.EndsWith("480.webm", sources[1].Uri.AbsolutePath);
        Assert.Contains("\"vp9, opus\"", node.MediaSources[0].ContentType);
    }

    [Fact]
    public void AudioPrefersMp3AndKeepsLazySources()
    {
        var node = NativeArticleDocumentService.Parse("<audio><source src='/clip.ogg' type='audio/ogg'><source data-src='/clip.mp3' type='audio/mpeg'></audio>", Page).Nodes.Single();
        Assert.EndsWith("clip.mp3", NativeMediaService.PlaybackSources(node.MediaSources)[0].Uri.AbsolutePath);
    }

    [Theory]
    [InlineData("\uFEFF  <!DOCTYPE html><html>challenge</html>", true)]
    [InlineData("<html>rate limited</html>", true)]
    [InlineData("<?xml version='1.0'?><svg/>", false)]
    [InlineData("GIF89a", false)]
    public void ErrorDocumentsAreNotCachedAsMedia(string content, bool error)
        => Assert.Equal(error, NativeMediaService.IsErrorDocument(Encoding.UTF8.GetBytes(content)));
}
