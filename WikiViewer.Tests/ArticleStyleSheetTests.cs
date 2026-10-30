using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class ArticleStyleSheetTests
{
    [Fact]
    public void InheritedVariablesKeepTheirComputedScopeAndShareUnchangedMaps()
    {
        var node = NativeArticleDocumentService.Parse("<div style='--a:#123456;--b:var(--a)'><p style='--a:#abcdef;color:var(--b)'>First</p><p>Second</p></div>", new Uri("https://example.org/Page")).Nodes.Single();
        Assert.Equal("#123456", node.Children[0].Style.Foreground);
        Assert.Same(node.Style.Variables, node.Children[1].Style.Variables);
        Assert.NotSame(node.Style.Variables, node.Children[0].Style.Variables);
    }
    [Theory]
    [InlineData(".box > p[data-kind='lead']", true)]
    [InlineData(".box > div > p", false)]
    [InlineData("h2 + p", true)]
    [InlineData("h2 ~ p", true)]
    [InlineData("p[data-kind^='le'][data-kind$='ad']", true)]
    [InlineData("p:not(.other):nth-child(2)", true)]
    [InlineData("p:first-child", false)]
    [InlineData("p:last-child", true)]
    [InlineData("p:hover", false)]
    public void StaticSelectorsPreserveRelationships(string selector, bool matches)
    {
        var document = NativeArticleDocumentService.Parse("<style>" + selector + "{color:#123456}</style><div class='box'><h2>Title</h2><!-- gap --><p data-kind='lead'>Text</p></div>", new Uri("https://example.org/Page"));
        Assert.Equal(matches, document.Nodes.Single().Children.Last().Style.Foreground == "#123456");
    }

    [Fact]
    public void ImportantCascadeVariablesAndQuotedSemicolonsRemainIntact()
    {
        const string html = "<style>.box{--Color:#abcdef;color:var(--Color);width:220px!important}#target{width:240px}.box p{background:var(--missing,#eeeeee)}</style><div class='box' id='target' style='width:280px'><p title='x'>Text</p></div>";
        var node = NativeArticleDocumentService.Parse(html, new Uri("https://example.org/Page")).Nodes.Single();
        Assert.Equal(220, node.Style.Width.Value);
        Assert.Equal("#abcdef", node.Children[0].Style.Foreground);
        Assert.Equal("#eeeeee", node.Children[0].Style.Background);
    }

    [Fact]
    public void CyclicVariablesDoNotHangOrEraseOtherProperties()
    {
        var node = NativeArticleDocumentService.Parse("<div style='--a:var(--b);--b:var(--a);width:var(--a);color:#123456'>Text</div>", new Uri("https://example.org/Page")).Nodes.Single();
        Assert.Null(node.Style.Width);
        Assert.Equal("#123456", node.Style.Foreground);
    }

    [Theory]
    [InlineData(320, true)]
    [InlineData(800, false)]
    public void WidthMediaRulesRequireMatchingViewport(double width, bool hidden)
    {
        var content = new WikiViewer.Core.Models.ArticleContent { HtmlContent = "<style>@media screen and (max-width:400px){.box{display:none}}@media print{.box{color:red}}</style><div class='box'>Text</div>" };
        var node = NativeArticleDocumentService.Parse(content, new Uri("https://example.org/Page"), width).Nodes.Single();
        Assert.Equal(hidden, node.Style.Hidden);
        Assert.Null(node.Style.Foreground);
    }

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
