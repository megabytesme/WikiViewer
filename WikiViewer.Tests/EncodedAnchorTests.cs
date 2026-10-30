using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class EncodedAnchorTests
{
    [Fact]
    public void EntityEncodedReferenceIdsMatchDecodedLinkFragmentsAcrossSections()
    {
        var page = new Uri("https://example.org/wiki/John%20Adams");
        var document = NativeArticleDocumentService.Parse("<p><a href='/wiki/John_Adams#cite_note-A-1'>[1]</a></p><h2>References</h2><ol><li id='cite&#95;note-A-1'>Source</li></ol>", page);
        var link = document.Nodes[0].Children.Single();
        var sections = ArticleSectionService.Split(document.Nodes);
        string id = Uri.UnescapeDataString(link.Uri.Fragment.TrimStart('#'));
        Assert.True(NativeArticleDocumentService.IsSamePage(page, link.Uri));
        Assert.Contains(id, sections[1].Anchors);
        Assert.DoesNotContain(id, sections[0].Anchors);
    }

    [Fact]
    public void LegacyNamedAnchorsAndLiteralAmpersandsAreDecodedOnce()
    {
        var nodes = NativeArticleDocumentService.Parse("<a name='Old&#95;anchor'></a><p id='literal&amp;#95;entity'>Text</p>", new Uri("https://example.org/Page")).Nodes;
        Assert.Equal("Old_anchor", nodes[0].Id);
        Assert.Equal("literal&#95;entity", nodes[1].Id);
    }
}
