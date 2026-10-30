using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class NativeStaticContentTests
{
    [Fact]
    public void DirectionInheritsFromTheWikiPageAndCssCanOverrideIt()
    {
        var content = new ArticleContent { HeadHtml = "<html dir='rtl'><head></head><body>", HtmlContent = "<div class='mw-parser-output'><p>Arabic text</p><p style='direction:ltr'>Formula</p></div>" };
        var nodes = NativeArticleDocumentService.Parse(content, new Uri("https://example.org/Page")).Nodes;
        Assert.True(nodes[0].RightToLeft);
        Assert.False(nodes[1].RightToLeft);
    }
    [Fact]
    public void ServerHeadStylesPreserveTheCanvasForInheritedTextColours()
    {
        var content = new ArticleContent { HeadHtml = "<!doctype html><html><head></head><body class='skin-example'>", HtmlContent = "<div class='mw-parser-output'><p>Text</p></div>",
            ResolvedStyleSheets = new() { "body.skin-example{background:#ffffff;color:#202122}" } };
        var document = NativeArticleDocumentService.Parse(content, new Uri("https://example.org/Page"));
        Assert.Equal("#ffffff", document.Background);
        Assert.Equal("#202122", document.Foreground);
        Assert.Equal("#202122", document.Nodes.Single().Style.Foreground);
    }
    [Fact]
    public void PresentationMathKeepsFractionsRootsScriptsAndSkipsSourceAnnotations()
    {
        var math = NativeArticleDocumentService.Parse("<math display='block'><semantics><mfrac><msup><mi>x</mi><mn>2</mn></msup><msqrt><mn>3</mn></msqrt></mfrac><annotation encoding='application/x-tex'>Duplicate TeX</annotation></semantics></math>", new Uri("https://example.org/Page")).Nodes.Single();
        Assert.Equal(NativeArticleNodeKind.Math, math.Kind);
        Assert.True(math.IsBlock);
        var fraction = math.Children.Single().Children.Single();
        Assert.Equal("mfrac", fraction.TagName);
        Assert.Equal(new[] { "msup", "msqrt" }, fraction.Children.Select(n => n.TagName));
    }
    private static readonly Uri Page = new("https://example.org/wiki/Page");

    [Fact]
    public void KeepsMediaSourcesPosterAndReadableFallbackWithoutLoadingThem()
    {
        var node = NativeArticleDocumentService.Parse("<video poster='/poster.jpg'><source src='//example.org/clip.webm' type='video/webm' data-title='WebM'><source src='/clip.mp4' type='video/mp4'><source src='javascript:alert(1)'>Transcript</video>", Page).Nodes.Single();
        Assert.Equal(NativeArticleNodeKind.Media, node.Kind);
        Assert.Equal(2, node.MediaSources.Count);
        Assert.Equal("https://example.org/poster.jpg", node.PosterUri.AbsoluteUri);
        Assert.Equal("video/mp4", node.MediaSources[1].ContentType);
        Assert.Equal("Transcript", node.Children.Single().Text);
    }

    [Fact]
    public void StaticSvgRemovesActiveContentAndExternalReferences()
    {
        var node = NativeArticleDocumentService.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' onload='x()'><title>Diagram</title><script>bad()</script><foreignObject>bad</foreignObject><path id='p' d='M0 0L20 20'/><use href='#p'/><image href='https://other.org/x'/></svg>", Page).Nodes.Single();
        Assert.Equal(NativeArticleNodeKind.Image, node.Kind);
        Assert.Equal("Diagram", node.Text);
        Assert.Contains("#p", node.SvgContent);
        Assert.DoesNotContain("bad", node.SvgContent);
        Assert.DoesNotContain("onload", node.SvgContent);
        Assert.DoesNotContain("other.org", node.SvgContent);
        Assert.Null(NativeSvgService.Prepare("<!DOCTYPE svg [<!ENTITY x 'y'>]><svg/>"));
    }

    [Fact]
    public void GalleriesKeepCaptionsAndUseResponsiveRows()
    {
        var node = NativeArticleDocumentService.Parse("<ul class='gallery'><li class='gallerybox'><img src='/one.png' alt='One'><div class='gallerytext'>Caption</div></li></ul>", Page).Nodes.Single();
        Assert.Equal("gallery", node.Style.Display);
        Assert.Equal(NativeArticleNodeKind.Container, node.Children.Single().Kind);
        Assert.Equal("Caption", node.Children[0].Children[1].Children[0].Text);
        var tiles = NativeFlowLayout.Tiles(640, 5, 3, 8, (i, w) => i == 0 ? 100 : 60);
        Assert.Equal(0, tiles[0].Y);
        Assert.Equal(108, tiles[3].Y);
        Assert.True(tiles.All(r => r.X >= 0 && r.X + r.Width <= 640));
        var narrow = NativeFlowLayout.Tiles(150, 3, 3, 8, (i, w) => 30);
        Assert.All(narrow, r => Assert.Equal(0, r.X));
    }

    [Fact]
    public async Task StylesAreBoundedSameOriginAndRemainCachedWithTheContent()
    {
        var content = new ArticleContent { HeadHtml = "<link rel='stylesheet' href='/w/load.php?modules=site.styles&amp;only=styles'><link rel='stylesheet' href='https://other.org/x.css'><link rel='stylesheet' href='/api.php?action=edit'>", StyleModules = new() { "ext.test" } };
        var urls = ArticleStyleResourceService.GetStyleUrls(content, Page, "w/");
        Assert.Equal(2, urls.Count);
        Assert.Contains("&only=styles", urls[0].Query);
        await ArticleStyleResourceService.ResolveAsync(content, Page, "w/", url => Task.FromResult(".box{color:#112233}"));
        Assert.Equal(2, content.ResolvedStyleSheets.Count);
        content.HtmlContent = "<div class='box'>Text</div>";
        Assert.Equal("#112233", NativeArticleDocumentService.Parse(content, Page).Nodes.Single().Style.Foreground);
    }
}
