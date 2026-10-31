using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

[Collection("ArticleCache")]
public class ArticleLoadPerformanceTests
{
    [Fact]
    public async Task CacheNotificationsIdentifyTheWikiAsWellAsTheTitle()
    {
        var storage = new Storage(); ArticleCacheManager.StorageProvider = storage; var id = Guid.NewGuid(); ArticleCachedEventArgs notification = null;
        EventHandler<ArticleCachedEventArgs> handler = (sender, args) => notification = args;
        ArticleCacheManager.ArticleCached += handler;
        try
        {
            await ArticleCacheManager.SaveArticleContentAsync("Main Page", id, new ArticleContent { HtmlContent = "text" }, DateTime.UtcNow);
            Assert.Equal(id, notification.WikiId); Assert.Equal("Main Page", notification.PageTitle);
        }
        finally { ArticleCacheManager.ArticleCached -= handler; }
    }
    [Fact]
    public async Task ParsedModelsReuseWorkWithoutSharingMutableLayoutNodes()
    {
        NativeDocumentCache.Clear();
        var content = new ArticleContent { HtmlContent = "<div style='float:right;width:200px'><p>Text</p></div>" };
        var first = await NativeDocumentCache.GetAsync(content, new Uri("https://model.example/Page"), 800);
        first.Nodes[0].Style.Float = null; first.Nodes[0].Children.Clear();
        var second = await NativeDocumentCache.GetAsync(content, new Uri("https://model.example/Page"), 800);
        Assert.Equal("right", second.Nodes[0].Style.Float); Assert.Single(second.Nodes[0].Children);
        content.HtmlContent = "<p>Updated</p>";
        var updated = await NativeDocumentCache.GetAsync(content, new Uri("https://model.example/Page"), 800);
        Assert.Equal(NativeArticleNodeKind.Paragraph, updated.Nodes.Single().Kind);
    }
    [Fact]
    public async Task CacheHitReturnsWithoutStartingOrWaitingForNetwork()
    {
        var coordinator = new ArticleLoadCoordinator(); int requests = 0;
        var never = new TaskCompletionSource<ArticleContent>();
        var task = coordinator.LoadAsync("key", () => Task.FromResult(new ArticleContent { HtmlContent = "saved" }), () => { requests++; return never.Task; });
        Assert.True(task.IsCompletedSuccessfully); Assert.Equal("saved", (await task).HtmlContent); Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ForegroundAndBackgroundLoadsShareOneRequestForEquivalentTitles()
    {
        var coordinator = new ArticleLoadCoordinator(); var id = Guid.NewGuid(); int requests = 0;
        var release = new TaskCompletionSource<ArticleContent>();
        Task<ArticleContent> Fetch() { requests++; return release.Task; }
        var tasks = Enumerable.Range(0, 20).Select(i => coordinator.LoadAsync(CacheKeyService.Article(id, i % 2 == 0 ? "A_B" : "A B"), () => Task.FromResult<ArticleContent>(null), Fetch)).ToArray();
        Assert.Equal(1, requests); release.SetResult(new ArticleContent { HtmlContent = "downloaded" });
        Assert.All(await Task.WhenAll(tasks), content => Assert.Equal("downloaded", content.HtmlContent));
    }

    [Fact]
    public async Task FailedAndCanceledRequestsCanBeRetried()
    {
        var coordinator = new AsyncRequestCoordinator<string>();
        await Assert.ThrowsAsync<IOException>(() => coordinator.RunAsync("key", () => Task.FromException<string>(new IOException())));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync("key", () => Task.FromCanceled<string>(new CancellationToken(true))));
        Assert.Equal("ok", await coordinator.RunAsync("key", () => Task.FromResult("ok")));
    }

    [Fact]
    public async Task ResourceCacheSurvivesRestartAndAvoidsDuplicateFetches()
    {
        var storage = new Storage(); int requests = 0;
        var cache = new WikiResourceCache(storage); var release = new TaskCompletionSource<string>();
        Task<string> Fetch() { requests++; return release.Task; }
        var first = cache.GetAsync("https://wiki.example/styles.css", Fetch);
        var second = cache.GetAsync("https://wiki.example/styles.css", Fetch);
        Assert.Equal(1, requests); release.SetResult(".box{color:red}"); await Task.WhenAll(first, second);
        cache = new WikiResourceCache(storage);
        Assert.Equal(".box{color:red}", await cache.GetAsync("https://wiki.example/styles.css", Fetch)); Assert.Equal(1, requests);
        Assert.Null(await cache.TryReadAsync("https://wiki.example/missing.css"));
    }

    [Fact]
    public async Task StylesFetchInParallelButRetainCascadeOrder()
    {
        var content = new ArticleContent { HeadHtml = "<link rel='stylesheet' href='/one.css'><link rel='stylesheet' href='/two.css'><link rel='stylesheet' href='/three.css'>" };
        int started = 0; var release = new TaskCompletionSource<bool>();
        var task = ArticleStyleResourceService.ResolveAsync(content, new Uri("https://parallel.example/Page"), "", async uri => { started++; await release.Task; return "/*" + new Uri(uri).AbsolutePath + "*/ .box{color:red}"; });
        Assert.Equal(3, started); release.SetResult(true); await task;
        Assert.Contains("one.css", content.ResolvedStyleSheets[0]); Assert.Contains("two.css", content.ResolvedStyleSheets[1]); Assert.Contains("three.css", content.ResolvedStyleSheets[2]);
    }

    [Fact]
    public async Task FailedStyleRefreshDoesNotStripWorkingSavedLayouts()
    {
        var content = new ArticleContent { HeadHtml = "<link rel='stylesheet' href='/unavailable.css'>", ResolvedStyleSheets = new() { ".box{display:flex}" } };
        await ArticleStyleResourceService.ResolveAsync(content, new Uri("https://failed-styles.example/Page"), "", url => Task.FromException<string>(new IOException()));
        Assert.Equal(".box{display:flex}", content.ResolvedStyleSheets.Single());
    }

    [Fact]
    public async Task WarmArticlesAvoidDiskReadsAndCallerMutationDoesNotPoisonCache()
    {
        var storage = new Storage(); ArticleCacheManager.StorageProvider = storage; var id = Guid.NewGuid();
        await ArticleCacheManager.SaveArticleContentAsync("A_B", id, new ArticleContent { HtmlContent = "saved", RawWikitext = "source" }, DateTime.UtcNow);
        storage.Reads = 0;
        var first = await ArticleCacheManager.GetCachedArticleContentAsync("A B", id); first.HtmlContent = "changed by reader";
        Assert.Equal("saved", (await ArticleCacheManager.GetCachedArticleContentAsync("A_B", id)).HtmlContent); Assert.Equal(0, storage.Reads);
        await ArticleCacheManager.ClearCacheForItemAsync("A B", id);
        Assert.Null(await ArticleCacheManager.GetCachedArticleContentAsync("A_B", id));
    }

    [Fact]
    public async Task OlderUnderscoreCachesRemainReadableAndInvalidatable()
    {
        var storage = new Storage(); ArticleCacheManager.StorageProvider = storage; var id = Guid.NewGuid();
        string name = CacheKeyService.Hash(id + "_a_b");
        storage.Files["ArticleCache\\" + name + ".article.json"] = JsonConvert.SerializeObject(new ArticleContent { HtmlContent = "older cache", Title = "A B" });
        storage.Files["ArticleCache\\" + name + ".html"] = "older cache";
        Assert.True(await ArticleCacheManager.IsArticleCachedAsync("A B", id));
        Assert.Equal("older cache", (await ArticleCacheManager.GetCachedArticleContentAsync("A B", id)).HtmlContent);
        await ArticleCacheManager.ClearCacheForItemAsync("A B", id); Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task CaseSensitiveTitlesCannotShowAnotherPagesOldLowercaseCache()
    {
        var storage = new Storage(); ArticleCacheManager.StorageProvider = storage; var id = Guid.NewGuid();
        string oldName = CacheKeyService.Hash(id + "_us");
        storage.Files["ArticleCache\\" + oldName + ".article.json"] = JsonConvert.SerializeObject(new ArticleContent { Title = "US", HtmlContent = "US page" });
        Assert.Null(await ArticleCacheManager.GetCachedArticleContentAsync("Us", id));
        Assert.Equal("US page", (await ArticleCacheManager.GetCachedArticleContentAsync("US", id)).HtmlContent);
        Assert.NotEqual(CacheKeyService.Article(id, "US"), CacheKeyService.Article(id, "Us"));
    }

    [Fact]
    public async Task LateStyleCompletionCannotOverwriteANewerRevision()
    {
        var storage = new Storage(); ArticleCacheManager.StorageProvider = storage; var id = Guid.NewGuid();
        var old = new ArticleContent { RevisionId = 1, HtmlContent = "old", RawWikitext = "old" };
        await ArticleCacheManager.SaveArticleContentAsync("Page", id, old, DateTime.UtcNow);
        await ArticleCacheManager.SaveArticleContentAsync("Page", id, new ArticleContent { RevisionId = 2, HtmlContent = "new", RawWikitext = "new" }, DateTime.UtcNow);
        var rich = old.Copy(); rich.ResourcesResolved = true;
        Assert.False(await ArticleCacheManager.UpdateResourcesIfCurrentAsync("Page", id, old, rich));
        Assert.Equal(2, (await ArticleCacheManager.GetCachedArticleContentAsync("Page", id)).RevisionId);
    }

    [Fact]
    public async Task CompletingStylesDoesNotMakeOldExpansionAppearFresh()
    {
        var storage = new Storage(); ArticleCacheManager.StorageProvider = storage; var id = Guid.NewGuid(); var time = DateTime.UtcNow.AddDays(-2);
        var content = new ArticleContent { RevisionId = 1, HtmlContent = "text", RawWikitext = "source" };
        await ArticleCacheManager.SaveArticleContentAsync("Page", id, content, time);
        var rich = content.Copy(); rich.ResourcesResolved = true;
        Assert.True(await ArticleCacheManager.UpdateResourcesIfCurrentAsync("Page", id, content, rich));
        Assert.Equal(time, (await ArticleCacheManager.GetCacheMetadataAsync("Page", id)).LastUpdated);
    }

    [Fact]
    public void RevisionPollingIsThrottledAndTemplateExpansionEventuallyRefreshes()
    {
        var now = DateTime.UtcNow; var meta = new ArticleCacheItem { LastUpdated = now, LastCheckedUtc = now.AddMinutes(-1) };
        Assert.False(ArticleLoadCoordinator.NeedsValidation(meta, now)); meta.LastCheckedUtc = now.AddMinutes(-6);
        Assert.True(ArticleLoadCoordinator.NeedsValidation(meta, now));
        var content = new ArticleContent { NativeFormatVersion = ArticleContent.CurrentNativeFormatVersion, RawWikitext = "source", RevisionId = 42 };
        Assert.False(ArticleLoadCoordinator.NeedsExpansion(meta, content, 42, now));
        Assert.True(ArticleLoadCoordinator.NeedsExpansion(meta, content, 43, now));
        meta.LastUpdated = now.AddDays(-2); Assert.True(ArticleLoadCoordinator.NeedsExpansion(meta, content, 42, now));
    }

    private sealed class Storage : IStorageProvider
    {
        public Dictionary<string, string> Files = new(); public int Reads;
        public Task<string> ReadTextAsync(string path) { Reads++; return Task.FromResult(Files.TryGetValue(path, out var value) ? value : null); }
        public Task WriteTextAsync(string path, string value) { Files[path] = value; return Task.CompletedTask; }
        public Task<bool> FileExistsAsync(string path) => Task.FromResult(Files.ContainsKey(path));
        public Task DeleteFileAsync(string path) { Files.Remove(path); return Task.CompletedTask; }
        public Task RecreateFolderAsync(string path) { foreach (string key in Files.Keys.Where(k => k.StartsWith(path + "\\")).ToArray()) Files.Remove(key); return Task.CompletedTask; }
        public Task ClearFolderAsync(string path) => RecreateFolderAsync(path);
        public Task<ulong> GetFolderSizeAsync(string path) => Task.FromResult(0UL);
        public Task WriteBytesAsync(string path, byte[] content) => throw new NotSupportedException();
    }
}
