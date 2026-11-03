using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class ArticleLayoutTests
{
    private static NativeArticleDocument Parse(string html, string source = null) =>
        NativeArticleDocumentService.Parse(new ArticleContent { HtmlContent = html, RawWikitext = source, RevisionId = 123 }, new Uri("https://example.org/wiki/Page"));
    private static IEnumerable<NativeArticleNode> All(IEnumerable<NativeArticleNode> nodes)
    { foreach (var node in nodes) { yield return node; foreach (var child in All(node.Children)) yield return child; } }

    [Fact]
    public void RetainsInfoboxAndInlineLayoutInsteadOfDiscardingIt()
    {
        var table = Assert.Single(Parse("<table class='infobox custom' style='width:25%;float:left;margin:1em 2px;padding:4px;text-align:center;background-color:#f0f0f0;border:2px solid #123456'><tr><td>A</td></tr></table>").Nodes);
        Assert.Equal("infobox custom", table.Classes);
        Assert.True(table.Style.IsInfobox);
        Assert.Equal("left", table.Style.Float);
        Assert.Equal(200, table.Style.Width.Resolve(800));
        Assert.Equal("center", table.Style.Alignment);
        Assert.Equal(18, table.Style.Margin.Top.Resolve(800));
        Assert.Equal(2, table.Style.Margin.Left.Resolve(800));
        Assert.Equal(4, table.Style.Padding.Bottom.Resolve(800));
        Assert.Equal("#123456", table.Style.BorderColor);
    }

    [Fact]
    public void RetainsRawSourceAndUsesParserImageOptionsWhenHtmlHasNoLayout()
    {
        const string raw = "[[File:Example image.png|left|240px|Caption]]";
        var document = Parse("<figure><a href='/wiki/File:Example_image.png'><img src='/uploads/Example_image.png'></a><figcaption>Expanded caption</figcaption></figure>", raw);
        var image = All(document.Nodes).Single(n => n.Kind == NativeArticleNodeKind.Image);
        Assert.Equal(raw, document.RawWikitext);
        Assert.Equal(123, document.RevisionId);
        Assert.Single(document.SourceImages);
        Assert.True(image.HasSourceLayout);
        Assert.Equal(240, image.ImageWidth);
        Assert.Equal("left", image.Style.Float);
    }

    [Fact]
    public void ParagraphImageFloatsBecomeFlowBlocksWithoutLosingSurroundingText()
    {
        var document = Parse("<p>Before <a href='/wiki/File:A.png'><img resource='./File:A.png' src='/A.png'></a> after.</p>", "[[File:A.png|left|200px]]");
        var root = Assert.Single(document.Nodes);
        Assert.Equal(NativeArticleNodeKind.Container, root.Kind);
        var children = root.Children;
        Assert.Equal(3, children.Count);
        Assert.Equal("left", children[1].Style.Float);
        Assert.Equal("Before  after.", string.Concat(All(document.Nodes).Where(n => n.Kind == NativeArticleNodeKind.Text).Select(n => n.Text)));
        Assert.Single(All(document.Nodes).Where(n => n.Kind == NativeArticleNodeKind.Image));
        Assert.Single(All(document.Nodes).Where(n => n.Kind == NativeArticleNodeKind.Link));
    }

    [Fact]
    public void DoesNotGuessConflictingSourceOccurrencesOrOverrideExpandedStyles()
    {
        var ambiguous = Parse("<img resource='./File:A.png' src='/A.png'>", "[[File:A.png|left]]\n[[File:A.png|right]]");
        Assert.False(Assert.Single(ambiguous.Nodes).HasSourceLayout);
        var explicitHtml = Parse("<img resource='./File:A.png' src='/A.png' width='100' style='float:right'>", "[[File:A.png|left|240px]]");
        Assert.Equal("right", Assert.Single(explicitHtml.Nodes).Style.Float);
        Assert.Equal(100, Assert.Single(explicitHtml.Nodes).ImageWidth);
    }

    [Fact]
    public void DoesNotTreatNowikiAsSourceLayoutAndParsesNestedTemplateImages()
    {
        var hints = WikitextLayoutService.ReadImages("<nowiki>[[File:Ignore.png|left]]</nowiki>\n{{Infobox|picture=[[File:Real.png|center|220px]]}}");
        Assert.Equal("Real.png", Assert.Single(hints).FileName);
        Assert.Equal("center", hints[0].Alignment);
    }

    [Fact]
    public void PreservesInheritedTypographyAndCaseInsensitiveDirection()
    {
        var node = Assert.Single(Parse("<div dir='RTL' style='color:#123456;font-size:20px;text-align:right'><span style='font-size:80%'>text</span></div>").Nodes);
        Assert.True(node.RightToLeft);
        var span = Assert.Single(node.Children);
        Assert.Equal("#123456", span.Style.Foreground);
        Assert.Equal(16, span.Style.FontSize.Resolve(800));
        Assert.Equal("right", span.Style.Alignment);
    }

    [Fact]
    public void KeepsUnknownContainerChildrenAndOriginalUnsupportedMedia()
    {
        var document = Parse("<custom-container data-layout='unknown'><a href='/wiki/A'><img src='/A.png'><img src='/B.png'>label</a></custom-container><video src='/movie.mp4'><source src='/movie.webm'>caption</video>");
        var nodes = All(document.Nodes).ToList();
        Assert.Equal(2, nodes.Count(n => n.Kind == NativeArticleNodeKind.Image));
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.Link);
        Assert.Equal("unknown", document.Nodes[0].Attributes["data-layout"]);
        var video = nodes.Single(n => n.TagName == "video");
        Assert.Contains("movie.webm", video.OriginalHtml);
        Assert.Equal("https://example.org/movie.mp4", video.Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("25%", 200)] [InlineData("2em", 36)] [InlineData("12pt", 16)] [InlineData("120px", 120)] [InlineData("3.198ex", 28.782)]
    public void ResolvesLayoutLengths(string value, double expected) => Assert.Equal(expected, ArticleLength.Parse(value).Resolve(800));

    [Theory]
    [InlineData("calc(100% - 1px)")] [InlineData("url(javascript:bad)")] [InlineData("-20px")] [InlineData("999999999999px")]
    public void RejectsUnsupportedLengths(string value) => Assert.Null(ArticleLength.Parse(value));

    [Fact]
    public void KeepsCollapseAndColumnInstructions()
    {
        var document = Parse("<div class='mw-collapsible mw-collapsed' style='column-count:2;clear:both'><p>content</p></div>");
        var style = Assert.Single(document.Nodes).Style;
        Assert.True(style.Collapsible);
        Assert.True(style.InitiallyCollapsed);
        Assert.Equal(2, style.ColumnCount);
        Assert.Equal("both", style.Clear);
    }
}
