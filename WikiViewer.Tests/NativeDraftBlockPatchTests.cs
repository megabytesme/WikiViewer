using System;
using System.Linq;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

public class NativeDraftBlockPatchTests
{
    private static readonly Uri Page = new Uri("https://example.org/wiki/Test");

    [Fact]
    public void FormattingAnEditedParagraphKeepsItsStyleAndOtherExpandedBlocks()
    {
        var document = NativeArticleDocumentService.Parse("<aside>Expanded infobox</aside><p style='color:#123456'>An electric vehicle uses electric motors.</p>", Page);
        document.RawWikitext = "{{Infobox}}\n\nAn electric vehicle uses electric motors.";
        var patch = NativeDraftBlockPatch.TryCreate(document, document.RawWikitext.Replace("electric motors", "'''electric motors'''"), Page);
        Assert.NotNull(patch);
        Assert.Equal(NativeArticleNodeKind.Strong, patch.Replacements.Single().Children.Single(n => n.Kind == NativeArticleNodeKind.Strong).Kind);
        Assert.Equal(patch.Original.Style.Foreground, patch.Replacements.Single().Style.Foreground);
        Assert.Same(document.Nodes, patch.Parent);
        Assert.Equal(2, document.Nodes.Count);
    }

    [Fact]
    public void UnchangedCitationNumbersAndTargetsSurviveLocalFormatting()
    {
        var document = NativeArticleDocumentService.Parse("<p>A vehicle uses electric motors.<sup class='reference' id='cite_ref-a'><a href='#cite_note-a'>[17]</a></sup></p>", Page);
        document.RawWikitext = "A vehicle uses electric motors.<ref name='a'>Reference metadata</ref>";
        var patch = NativeDraftBlockPatch.TryCreate(document, document.RawWikitext.Replace("electric motors", "''electric motors''"), Page);
        Assert.NotNull(patch);
        Assert.Contains(patch.Replacements.Single().Children, n => n.Id == "cite_ref-a");
        Assert.Contains(patch.Replacements.Single().Children.Single(n => n.Id == "cite_ref-a").Children, n => n.Uri?.Fragment == "#cite_note-a");
    }

    [Fact]
    public void ChangedReferenceMetadataAndUnexpandedInlineTemplatesWaitForTheServer()
    {
        var document = NativeArticleDocumentService.Parse("<p>A vehicle uses electric motors.</p>", Page);
        document.RawWikitext = "A vehicle uses electric motors.<ref>Old</ref>";
        Assert.Null(NativeDraftBlockPatch.TryCreate(document, document.RawWikitext.Replace("Old", "New"), Page));
        document.RawWikitext = "A vehicle uses {{Motor}}.";
        Assert.Null(NativeDraftBlockPatch.TryCreate(document, document.RawWikitext.Replace("uses", "'''uses'''"), Page));
    }

    [Fact]
    public void HeadingEditsUpdateOneGroupAndKeepTheExistingAnchorUntilServerReconciliation()
    {
        var document = NativeArticleDocumentService.Parse("<div class='mw-heading'><h2 id='Electric_motors'>Electric motors</h2></div><p>Other content stays here.</p>", Page);
        document.RawWikitext = "== Electric motors ==\n\nOther content stays here.";
        var patch = NativeDraftBlockPatch.TryCreate(document, document.RawWikitext.Replace("Electric motors", "Efficient electric motors"), Page);
        Assert.NotNull(patch);
        Assert.Equal(NativeArticleNodeKind.Heading, patch.Replacements.Single().Kind);
        Assert.Equal("Electric_motors", patch.Replacements.Single().Id);
        Assert.Same(document.Nodes[0].Children, patch.Parent);
    }
}
