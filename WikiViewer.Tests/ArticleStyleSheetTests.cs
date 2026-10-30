using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class ArticleStyleSheetTests
{
    [Fact]
    public void AppliesTemplateStylesWithSpecificityAndInlineOverrides()
    {
        const string html = "<style>.box{float:right;width:220px;background-color:#abcdef}.mw-parser-output .box{width:240px}#special{width:260px}</style><div class='mw-parser-output'><div id='special' class='box' style='width:280px'>content</div></div>";
        var document = NativeArticleDocumentService.Parse(html, new Uri("https://example.org/wiki/Page"));
        var node = Assert.Single(document.Nodes);
        Assert.Single(document.EmbeddedStyleSheets);
        Assert.Equal(3, document.StyleRules.Count);
        Assert.Equal("right", node.Style.Float);
        Assert.Equal(280, node.Style.Width.Resolve(800));
        Assert.Equal("#abcdef", node.Style.Background);
    }

    [Fact]
    public void NestedRulesKeepFollowingOrdinaryRulesAndLinkLabelsStayNativeSafe()
    {
        const string html = "<style>@media(max-width:200px){.label{display:none}} .label{float:right;font-weight:normal}</style><p><a href='/wiki/A'><span class='label'>label</span></a></p>";
        var document = NativeArticleDocumentService.Parse(html, new Uri("https://example.org/wiki/Page"));
        var label = document.Nodes[0].Children[0].Children[0];
        Assert.False(label.Style.Hidden);
        Assert.Null(label.Style.Float);
        Assert.Equal("normal", label.Style.FontWeight);
    }

    [Fact]
    public void DoesNotApplyConditionalOrDynamicSelectorsUnconditionally()
    {
        string css = "@media(max-width:300px){.box{display:none}}.box:hover{color:red}.box{padding:4px}";
        var rules = ArticleStyleSheet.Parse(css).ToList();
        Assert.Single(rules);
        Assert.Equal(".box", rules[0].Selector);
        Assert.Contains("padding", rules[0].Declarations);
    }
}
