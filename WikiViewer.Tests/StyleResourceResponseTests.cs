using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class StyleResourceResponseTests
{
    [Fact]
    public async Task WebViewCssWrapperIsDecodedBeforeCachingAndApplying()
    {
        const string wrapped = "<html><head></head><body><pre>.box &gt; p{color:#123456}</pre></body></html>";
        var page = new Uri("https://example.org/wiki/Page");
        var content = new ArticleContent { HeadHtml = "<link rel='stylesheet' href='/styles.css'>", HtmlContent = "<div class='box'><p>Text</p></div>" };
        await ArticleStyleResourceService.ResolveAsync(content, page, "w", url => Task.FromResult(wrapped));
        Assert.Equal(".box > p{color:#123456}", Assert.Single(content.ResolvedStyleSheets));
        Assert.Equal("#123456", NativeArticleDocumentService.Parse(content, page).Nodes.Single().Children.Single().Style.Foreground);
        content.ResolvedStyleSheets = new List<string> { wrapped }; // Previously cached WebView response.
        Assert.Equal("#123456", NativeArticleDocumentService.Parse(content, page).Nodes.Single().Children.Single().Style.Foreground);
    }

    [Fact]
    public async Task ErrorHtmlDoesNotBecomeAStylesheet()
    {
        var content = new ArticleContent { HeadHtml = "<link rel='stylesheet' href='/styles.css'>" };
        await ArticleStyleResourceService.ResolveAsync(content, new Uri("https://example.org/wiki/Page"), "w", url => Task.FromResult("<html><body>Verification required</body></html>"));
        Assert.Empty(content.ResolvedStyleSheets);
        Assert.Equal("p{color:red}", ArticleStyleResourceService.NormalizeResponse("p{color:red}"));
    }
}
