using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class NativeLinkLayoutTests
{
    [Theory]
    [InlineData("display:block", null)]
    [InlineData("float:right", "right")]
    public void BlockAndFloatedLinksKeepTheirNavigationTarget(string css, string side)
    {
        var page = new Uri("https://example.org/wiki/Page");
        var outer = NativeArticleDocumentService.Parse("<a id='section-link' href='/wiki/Other' style='" + css + "'><b>Open</b> article</a>", page).Nodes.Single();
        Assert.Equal(NativeArticleNodeKind.Container, outer.Kind);
        Assert.True(outer.IsBlock);
        Assert.Equal("section-link", outer.Id);
        Assert.Equal(side, outer.Style.Float);
        var link = Assert.Single(outer.Children);
        Assert.Equal(NativeArticleNodeKind.Link, link.Kind);
        Assert.False(link.IsBlock);
        Assert.Null(link.Style.Float);
        Assert.Equal("https://example.org/wiki/Other", link.Uri.AbsoluteUri);
        Assert.Equal(NativeArticleNodeKind.Strong, link.Children[0].Kind);
        Assert.Null(link.Id);
    }
}
