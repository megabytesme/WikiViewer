using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class ArticleVirtualizationTests
{
    [Fact]
    public void CountsNestedReferencesAndNavigationBoxesAsLayoutWork()
    {
        var html = "<h2>Introduction</h2><p>Article</p><h2>References</h2><ol>" +
            string.Concat(Enumerable.Range(0, 150).Select(i => $"<li id='ref-{i}'><a href='#ref-{i}'>Source {i}</a></li>")) + "</ol>";
        var document = NativeArticleDocumentService.Parse(html, new Uri("https://example.org/wiki/Page"));
        var sections = ArticleSectionService.Split(document.Nodes);
        Assert.True(document.Nodes.Count < 100);
        Assert.True(ArticleSectionService.ShouldVirtualize(document.Nodes, sections));
        Assert.Contains("ref-149", sections[1].Anchors);
        Assert.Equal(150, sections[1].Nodes.Last().Children.Count);
    }

    [Fact]
    public void SmallPagesAndSingleLayoutSectionsStayInTheFlowPanel()
    {
        var doc = NativeArticleDocumentService.Parse("<h2>A</h2><p>Text</p><h2>B</h2><p>Text</p>", new Uri("https://example.org/wiki/Page"));
        Assert.False(ArticleSectionService.ShouldVirtualize(doc.Nodes, ArticleSectionService.Split(doc.Nodes)));
    }
}
