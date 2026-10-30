using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class WikiSyntaxTests
{
    [Fact]
    public void SiteInfoSuppliesLocalizedAliasesWithoutTreatingPcreAsDotnet()
    {
        string json = "{\"query\":{\"general\":{\"lang\":\"de\",\"linktrail\":\"/^([a-zäöüß]+)(.*)$/sD\"},\"namespaces\":{\"6\":{\"id\":6,\"*\":\"Datei\",\"canonical\":\"File\"},\"14\":{\"id\":14,\"*\":\"Kategorie\"}},\"namespacealiases\":[{\"id\":6,\"*\":\"Bild\"}],\"magicwords\":[{\"name\":\"img_right\",\"aliases\":[\"rechts\"]},{\"name\":\"redirect\",\"aliases\":[\"#WEITERLEITUNG\"]}]}}";
        var syntax = WikiSyntaxService.Parse(json);
        Assert.Equal("de", syntax.Language);
        Assert.Equal("[a-zäöüß]+", syntax.LinkTrailPattern);
        Assert.False(syntax.UsesFallbackLinkTrail);
        Assert.Contains("Bild", syntax.FileNamespaces);
        Assert.Contains("Kategorie", syntax.CategoryNamespaces);
        Assert.Equal("right", syntax.ImageOptions["rechts"]);
        var hints = WikitextLayoutService.ReadImages("[[Datei:A.svg|rechts|120px]]", syntax);
        Assert.Equal("right", hints.Single().Alignment);
        Assert.Equal(120, hints.Single().Width);
        Assert.Equal("A/B.svg", WikitextLayoutService.NormalizeFileName("./Datei:A/B.svg", syntax));
        Assert.Equal("A/B.svg", WikitextLayoutService.NormalizeFileName("/wiki/Datei:A/B.svg", syntax));
    }

    [Fact]
    public void UnrecognizedPcreUsesAnExplicitFallback()
    {
        var syntax = WikiSyntaxService.Parse("{\"query\":{\"general\":{\"linktrail\":\"PCRE-specific-pattern\"}}}");
        Assert.True(syntax.UsesFallbackLinkTrail);
        Assert.Equal("[a-z]+", syntax.LinkTrailPattern);
    }

    [Fact]
    public void LocalizedSourceHintsMatchExpandedFileResources()
    {
        var syntax = new WikiSyntaxConfiguration { FileNamespaces = new() { "Datei" }, ImageOptions = new() { ["links"] = "left" } };
        var content = new ArticleContent { Syntax = syntax, RawWikitext = "[[Datei:A.svg|links|120px]]", HtmlContent = "<p><img src='/A.svg' resource='./Datei:A.svg'></p>" };
        var document = NativeArticleDocumentService.Parse(content, new Uri("https://example.org/wiki/Page"));
        Assert.Equal("A.svg", document.SourceImages.Single().FileName);
        Assert.Equal("left", document.Nodes[0].Children[0].Style.Float);
    }

    [Fact]
    public void SectionPartitionRetainsEveryNodeAndIndexesNestedAnchors()
    {
        var document = NativeArticleDocumentService.Parse("<p id='lead'>Lead</p><div class='mw-heading'><h2 id='one'>One</h2></div><p><a id='ref-1'>Text</a></p><h2 id='two'>Two</h2><table><tr><td id='cell'>Cell</td></tr></table>", new Uri("https://example.org/wiki/Page"));
        var sections = ArticleSectionService.Split(document.Nodes);
        Assert.Equal(3, sections.Count);
        Assert.Equal(document.Nodes, sections.SelectMany(s => s.Nodes));
        Assert.Contains("ref-1", sections[1].Anchors);
        Assert.Contains("cell", sections[2].Anchors);
        Assert.Equal(new[] { 0, 1, 2 }, sections.Select(s => s.Index));
    }
}
