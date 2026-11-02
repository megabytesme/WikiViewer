using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class EditorPreviewTests
{
    [Fact]
    public void NativeKeepsLargeDraftAndExpandedContentBeyondTheWebLimit()
    {
        string source = new string('x', EditorPreviewPolicy.WebByteLimit + 1);
        string html = "<p>" + source + "</p><p>End of large page</p>";
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { parse = new { text = html } });
        var preview = EditorPreviewResponse.Parse(json, source, "Large page");
        Assert.Equal(source, preview.Content.RawWikitext);
        var model = NativeArticleDocumentService.Parse(preview.Content, new Uri("https://example.org/wiki/Large_page"));
        Assert.Equal(2, model.Nodes.Count);
        Assert.Equal("End of large page", model.Nodes[1].Children[0].Text);
        Assert.False(EditorPreviewPolicy.CanDisplayWeb(html));
        Assert.True(EditorPreviewPolicy.CanDisplayWeb(new string('x', EditorPreviewPolicy.WebByteLimit)));
        Assert.False(EditorPreviewPolicy.CanDisplayWeb(new string('界', EditorPreviewPolicy.WebByteLimit / 2)));
    }

    [Fact]
    public void SwitchingModeRejectsPendingExpansionWithoutDiscardingTheDraftSnapshot()
    {
        var session = new EditorPreviewSession();
        var saved = session.Begin("draft", "Page");
        Assert.True(session.Accept(saved, Preview("draft")));
        var pending = session.Begin("new text", "Page");
        session.InvalidateRequests();
        Assert.False(session.Accept(pending, Preview("new text")));
        Assert.Equal("draft", session.Current.Content.RawWikitext);
        Assert.True(session.IsStale);
    }

    [Theory]
    [InlineData("{\"parse\":{\"text\":{\"*\":\"<p>expanded</p>\"},\"wikitext\":{\"*\":\"transformed\"},\"revid\":123,\"headhtml\":\"<html><head></head><body>\",\"modules\":[\"ext.cite\"],\"modulestyles\":[\"ext.cite\"]}}")]
    [InlineData("{\"parse\":{\"text\":\"<p>expanded</p>\",\"headhtml\":\"<html><head></head><body>\",\"modules\":[\"ext.cite\"]}}")]
    public void KeepsExactDraftBesideExpandedOutput(string json)
    {
        var preview = EditorPreviewResponse.Parse(json, "{{Draft|unsaved}}\r\n", "Page");
        Assert.Equal("{{Draft|unsaved}}\r\n", preview.Content.RawWikitext);
        Assert.Equal("<p>expanded</p>", preview.Content.HtmlContent);
        Assert.Equal(0, preview.Content.RevisionId);
        Assert.Single(preview.StyleModules);
        Assert.Contains("<head>", preview.HeadHtml);
    }

    [Fact]
    public void PreviewRequestCannotSaveOrReadThePublishedRevisionInstead()
    {
        var request = EditorPreviewResponse.BuildRequest("unsaved", "Template:Test/Subpage");
        Assert.Equal("parse", request["action"]);
        Assert.Equal("unsaved", request["text"]);
        Assert.Equal("Template:Test/Subpage", request["title"]);
        Assert.Equal("true", request["preview"]);
        Assert.False(request.ContainsKey("page"));
        Assert.False(request.ContainsKey("token"));
        Assert.Throws<InvalidOperationException>(() => EditorPreviewResponse.Parse("{\"error\":{\"info\":\"Cannot parse\"}}", "x", "Page"));
        Assert.Equal("", EditorPreviewResponse.Parse("{\"parse\":{\"text\":\"\"}}", "", "Page").Content.HtmlContent);
    }

    [Fact]
    public void RejectsChangedDraftAndOutOfOrderResponses()
    {
        var session = new EditorPreviewSession();
        var first = session.Begin("one", "Page");
        var second = session.Begin("two", "Page");
        Assert.False(session.Accept(first, Preview("one")));
        session.SourceChanged("three");
        Assert.False(session.Accept(second, Preview("two")));
        Assert.Null(session.Current);
        Assert.True(session.IsLatest(second));
    }

    [Fact]
    public void SwitchingRendererCanReuseTheSnapshotAndNavigationInvalidatesPendingWork()
    {
        var session = new EditorPreviewSession();
        var request = session.Begin("draft", "Page");
        var result = Preview("draft");
        Assert.True(session.Accept(request, result));
        Assert.Same(result, session.Current);
        session.SourceChanged("changed");
        Assert.True(session.IsStale);
        Assert.Equal("draft", session.Current.Content.RawWikitext);
        var pending = session.Begin("changed", "Page");
        session.Clear();
        Assert.False(session.Accept(pending, Preview("changed")));
        Assert.Null(session.Current);
    }

    private static ArticlePreview Preview(string text) => new() { Content = new ArticleContent { RawWikitext = text, HtmlContent = "<p>" + text + "</p>" } };
}
