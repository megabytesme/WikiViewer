using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class EditorBootstrapTests
{
    [Fact]
    public void OnlyUsesExpansionPairedWithTheFreshlyFetchedSource()
    {
        var published = new ArticleContent { Title = "Page", RawWikitext = "Old\nsource", HtmlContent = "<p>Old source</p>", RevisionId = 77 };
        Assert.Null(EditorPreviewBootstrap.Create(published, "New source", "New source\r"));
        var seed = EditorPreviewBootstrap.Create(published, "Old\nsource", "Old\rsource\r");
        Assert.NotNull(seed);
        Assert.Equal("Old\rsource\r", seed.Content.RawWikitext);
        Assert.Equal(0, seed.Content.RevisionId);
        Assert.True(seed.NeedsServerExpansion);
        Assert.Equal(77, published.RevisionId);
        Assert.Equal("Old\nsource", published.RawWikitext);
        Assert.Null(EditorPreviewBootstrap.Create(new ArticleContent { HtmlContent = "legacy HTML" }, "source", "source"));
    }

    [Fact]
    public void HandOffIsSingleUseAndCannotCrossWikis()
    {
        var wiki = Guid.NewGuid();
        var source = new ArticleContent { RawWikitext = "source", HtmlContent = "<p>source</p>" };
        EditorPreviewBootstrap.Store(wiki, "Page", source);
        Assert.Null(EditorPreviewBootstrap.Take(Guid.NewGuid(), "Page"));
        Assert.Null(EditorPreviewBootstrap.Take(wiki, "Page"));
        EditorPreviewBootstrap.Store(wiki, "Page", source);
        var snapshot = EditorPreviewBootstrap.Take(wiki, "Page");
        Assert.NotSame(source, snapshot);
        snapshot.RawWikitext = "changed";
        Assert.Equal("source", source.RawWikitext);
        Assert.Null(EditorPreviewBootstrap.Take(wiki, "Page"));
    }

    [Fact]
    public void SeedKeepsPreviewAvailableWhenServerResultIsLateOrStale()
    {
        var session = new EditorPreviewSession();
        var request = session.Begin("source", "Page");
        var seed = EditorPreviewBootstrap.Create(new ArticleContent { RawWikitext = "source", HtmlContent = "<p>source</p>" }, "source", "source");
        Assert.True(session.Accept(request, seed));
        var remote = session.Begin("source", "Page");
        session.SourceChanged("new unsaved source");
        Assert.False(session.Accept(remote, new ArticlePreview { Content = new ArticleContent { RawWikitext = "source" } }));
        Assert.Same(seed, session.Current);
        Assert.True(session.IsStale);
    }
}
