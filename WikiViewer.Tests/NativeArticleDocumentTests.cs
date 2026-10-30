using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class NativeArticleDocumentTests
{
    [Fact]
    public void CitationsMatchCanonicalUnderscoreUrlsWithoutRefetchingTheArticle()
    {
        var page = new Uri("https://en.wikipedia.org/wiki/John%20Adams");
        Assert.True(NativeArticleDocumentService.IsSamePage(page, new Uri(page, "./John_Adams#cite_note-1")));
        Assert.True(NativeArticleDocumentService.IsSamePage(page, new Uri(page, "#cite_note-1")));
        Assert.False(NativeArticleDocumentService.IsSamePage(page, new Uri("https://other.org/wiki/John_Adams#cite_note-1")));
        Assert.False(NativeArticleDocumentService.IsSamePage(page, new Uri("https://en.wikipedia.org/wiki/John_Adams?action=edit#cite_note-1")));
    }
    private static readonly Uri Page = new("https://example.org/wiki/Article");
    private static NativeArticleDocument Parse(string html) => NativeArticleDocumentService.Parse(html, Page);
    private static IEnumerable<NativeArticleNode> All(IEnumerable<NativeArticleNode> nodes)
    { foreach (var node in nodes) { yield return node; foreach (var child in All(node.Children)) yield return child; } }

    [Fact]
    public void UsesExpandedArticleInsteadOfSiteChromeAndScripts()
    {
        var document = Parse("<html><head><style>bad</style></head><body>chrome<div class='mw-parser-output extra'><h2 id='section'>Heading</h2><p>Expanded <strong>template</strong> &amp; module</p><script>bad()</script></div>footer</body></html>");
        var nodes = All(document.Nodes).ToList();
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.Heading && n.Id == "section");
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.Strong);
        string text = string.Concat(nodes.Select(n => n.Text));
        Assert.DoesNotContain("chrome", text);
        Assert.DoesNotContain("bad", text);
        Assert.DoesNotContain("footer", text);
        Assert.Contains(" & module", text);
    }

    [Fact]
    public void ResolvesLinksImagesAndCitationFragmentsAgainstThePage()
    {
        var nodes = All(Parse("<p><a href='#cite_note-1'>[1]</a><a href='/wiki/Other'>Other</a><a href='https://external.org/'>Site</a><img src='//img.example.org/image.png' alt='A &amp; B'></p>").Nodes).ToList();
        Assert.Contains(nodes, n => n.Uri?.AbsoluteUri == Page.AbsoluteUri + "#cite_note-1");
        Assert.Contains(nodes, n => n.Uri?.AbsoluteUri == "https://example.org/wiki/Other");
        Assert.Contains(nodes, n => n.Uri?.AbsoluteUri == "https://external.org/");
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.Image && n.Uri?.AbsoluteUri == "https://img.example.org/image.png" && n.Text == "A & B");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret")]
    [InlineData("ms-appx:///secret")]
    [InlineData("data:text/html,bad")]
    public void DoesNotExposeExecutableOrLocalUris(string uri)
    {
        var nodes = All(Parse("<p><a href='" + uri + "'>label</a><img src='" + uri + "' alt='image'></p>").Nodes);
        Assert.All(nodes.Where(n => n.Kind == NativeArticleNodeKind.Link || n.Kind == NativeArticleNodeKind.Image), n => Assert.Null(n.Uri));
    }

    [Fact]
    public void PreservesNestedListsOrderedValuesAndTableSpans()
    {
        var document = Parse("<ol start='3'><li>First<ul><li>Nested</li></ul></li><li value='7'>Last</li></ol><table><caption>Caption</caption><tr><th colspan='2'>Heading</th></tr><tr><td rowspan='2'>A</td><td>B</td></tr><tr><td>C</td></tr></table>");
        var nodes = All(document.Nodes).ToList();
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.List && n.Ordered && n.ListStart == 3);
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.ListItem && n.ListValue == 7);
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.TableCell && n.ColumnSpan == 2 && n.IsHeader);
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.TableCell && n.RowSpan == 2);
        Assert.Contains(nodes, n => n.Text == "Caption");
    }

    [Fact]
    public void PreservesPreWhitespaceAndLazyImageSource()
    {
        var nodes = All(Parse("<pre> one\n  two &lt;three&gt;</pre><img src='placeholder.png' data-src='/real.png'>").Nodes).ToList();
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.Text && n.PreserveWhitespace && n.Text == " one\n  two <three>");
        Assert.Contains(nodes, n => n.Kind == NativeArticleNodeKind.Image && n.Uri?.AbsoluteUri == "https://example.org/real.png");
    }

    [Fact]
    public void MarksUnsupportedContentAndRetainsReadableFallback()
    {
        var document = Parse("<p>before</p><video>Video caption</video><math>x+y</math>");
        Assert.Contains("video", document.UnsupportedFeatures);
        Assert.Contains(All(document.Nodes), n => n.Kind == NativeArticleNodeKind.Math);
        Assert.Contains(All(document.Nodes), n => n.Kind == NativeArticleNodeKind.Unsupported && n.Text == "Video caption");
    }

    [Fact]
    public void LinkLabelsExcludeUwpUnsupportedInlineStructures()
    {
        var nodes = All(Parse("<p><a href='/wiki/A'>before<br><span>after</span></a></p>").Nodes).ToList();
        var link = nodes.Single(n => n.Kind == NativeArticleNodeKind.Link);
        Assert.DoesNotContain(All(link.Children), n => n.Kind == NativeArticleNodeKind.LineBreak || n.Kind == NativeArticleNodeKind.Link || n.IsBlock);
        Assert.Equal("before after", string.Concat(All(link.Children).Select(n => n.Text)));
    }

    [Fact]
    public void RetainsDeclaredThumbnailWidth()
    {
        var image = Assert.Single(Parse("<img src='/image.png' width='120' height='160'>").Nodes);
        Assert.Equal(120, image.ImageWidth);
    }

    [Fact]
    public void CapsExcessiveNestingAndUntrustedSpans()
    {
        var document = Parse(string.Concat(Enumerable.Repeat("<div>", 300)) + "text" + string.Concat(Enumerable.Repeat("</div>", 300)));
        Assert.Contains("deeply nested content", document.UnsupportedFeatures);
        var cell = All(Parse("<table><tr><td colspan='999999' rowspan='-1'>A</td></tr></table>").Nodes).Single(n => n.Kind == NativeArticleNodeKind.TableCell);
        Assert.Equal(64, cell.ColumnSpan);
        Assert.Equal(1, cell.RowSpan);
    }
}
