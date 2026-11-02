using System;
using WikiViewer.Core.Services;
using Xunit;

public class NativeDraftTextPatchTests
{
    private static readonly Uri Page = new Uri("https://example.org/wiki/Test");
    [Fact]
    public void PlainTypingKeepsTheExpandedTemplateAndOriginalFormatting()
    {
        var document = NativeArticleDocumentService.Parse("<aside>Expanded infobox</aside><p>A battery electric vehicle uses <b>electric motors</b> for propulsion.</p>", Page);
        document.RawWikitext = "{{Infobox}}\n\nA battery electric vehicle uses '''electric motors''' for propulsion.";
        var card = document.Nodes[0];
        var patch = NativeDraftTextPatch.TryCreate(document, document.RawWikitext.Replace("vehicle uses", "vehicle typically uses"));
        Assert.NotNull(patch);
        Assert.Equal("A battery electric vehicle typically uses ", patch.Text);
        Assert.Same(card, document.Nodes[0]);
        Assert.Equal("A battery electric vehicle uses ", patch.Node.Text);
    }

    [Theory]
    [InlineData("'''different''' words")]
    [InlineData("[[different]] words")]
    [InlineData("{{different}} words")]
    [InlineData("different\n\nwords")]
    public void StructuralEditsWaitForTheGroupedParser(string replacement)
    {
        var document = NativeArticleDocumentService.Parse("<p>Some original words for this paragraph.</p>", Page);
        document.RawWikitext = "Some original words for this paragraph.";
        Assert.Null(NativeDraftTextPatch.TryCreate(document, document.RawWikitext.Replace("original words", replacement)));
    }

    [Fact]
    public void AmbiguousRepeatedTextIsNeverPatchedByGuessing()
    {
        var document = NativeArticleDocumentService.Parse("<p>The original vehicle is here.</p><p>The original vehicle is here.</p>", Page);
        document.RawWikitext = "The original vehicle is here.";
        Assert.Null(NativeDraftTextPatch.TryCreate(document, "The new vehicle is here."));
    }

    [Theory]
    [InlineData("{{Infobox|name=The original vehicle is here.}}")]
    [InlineData("[[The original vehicle is here.]]")]
    public void TemplateArgumentsAndLinkTargetsRequireSemanticParsing(string source)
    {
        var document = NativeArticleDocumentService.Parse("<p>The original vehicle is here.</p>", Page);
        document.RawWikitext = source;
        Assert.Null(NativeDraftTextPatch.TryCreate(document, source.Replace("original", "new")));
    }
}
