using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

[Collection("ArticleCache")]
public class ArticleContentTests
{
    [Theory]
    [InlineData("{\"parse\":{\"title\":\"Resolved\",\"revid\":42,\"text\":{\"*\":\"<p>expanded</p>\"},\"wikitext\":{\"*\":\"{{Template}}\"}}}")]
    [InlineData("{\"parse\":{\"title\":\"Resolved\",\"revid\":42,\"text\":\"<p>expanded</p>\",\"wikitext\":\"{{Template}}\"}}")]
    public void ReadsRevisionMatchedSourcesInBothApiFormats(string json)
    {
        var content = ArticleContentResponse.Parse(json, "Requested");
        Assert.Equal("Resolved", content.Title);
        Assert.Equal(42, content.RevisionId);
        Assert.Equal("<p>expanded</p>", content.HtmlContent);
        Assert.Equal("{{Template}}", content.RawWikitext);
    }

    [Fact]
    public void RequestsBothSourcesInOneParseOperation()
    {
        string url = ArticleContentResponse.BuildUrl("https://example.org/api.php", "A&B#C");
        Assert.Contains("page=A%26B%23C", url);
        Assert.Contains("prop=text%7Cwikitext%7Crevid", url);
    }

    [Fact]
    public void DistinguishesMissingSourceFromAnEmptyPage()
    {
        Assert.True(ArticleContentResponse.Parse("{\"parse\":{\"text\":\"\",\"wikitext\":\"\"}}", "Empty").HasRawSource);
        Assert.False(ArticleContentResponse.Parse("{\"parse\":{\"text\":\"<p>content</p>\"}}", "Legacy").HasRawSource);
        Assert.Throws<InvalidOperationException>(() => ArticleContentResponse.Parse("{\"parse\":{}}", "Missing"));
        Assert.Throws<InvalidOperationException>(() => ArticleContentResponse.Parse("{\"error\":{\"info\":\"Denied\"}}", "Denied"));
    }

    [Fact]
    public async Task CacheKeepsThePairAndRevisionTogether()
    {
        ArticleCacheManager.StorageProvider = new MemoryStorage();
        var id = Guid.NewGuid();
        var source = new ArticleContent { Title = "Page", RevisionId = 99, HtmlContent = "<p>99</p>", RawWikitext = "source 99" };
        await ArticleCacheManager.SaveArticleContentAsync("Page", id, source, DateTime.UtcNow);
        var cached = await ArticleCacheManager.GetCachedArticleContentAsync("Page", id);
        Assert.Equal(99, cached.RevisionId);
        Assert.Equal("source 99", cached.RawWikitext);
        Assert.Equal(source.HtmlContent, await ArticleCacheManager.GetCachedArticleHtmlAsync("Page", id));
        await ArticleCacheManager.SaveArticleHtmlAsync("Page", id, "new legacy HTML", DateTime.UtcNow);
        cached = await ArticleCacheManager.GetCachedArticleContentAsync("Page", id);
        Assert.Null(cached.RawWikitext);
        Assert.Equal(0, cached.RevisionId);
        Assert.Equal("new legacy HTML", cached.HtmlContent);
    }

    [Fact]
    public async Task CacheReadsLegacyAndRecoversFromAnInterruptedPairWrite()
    {
        var storage = new MemoryStorage();
        ArticleCacheManager.StorageProvider = storage;
        var id = Guid.NewGuid();
        await ArticleCacheManager.SaveArticleContentAsync("Page", id, new ArticleContent { HtmlContent = "legacy", RawWikitext = "raw", RevisionId = 1 }, DateTime.UtcNow);
        string pair = storage.Files.Keys.Single(k => k.EndsWith(".article.json"));
        storage.Files[pair] = "{interrupted";
        // Simulate reopening the app: recovery must be checked against disk, without the warm memory copy.
        ArticleCacheManager.StorageProvider = null;
        ArticleCacheManager.StorageProvider = storage;
        var cached = await ArticleCacheManager.GetCachedArticleContentAsync("Page", id);
        Assert.Equal("legacy", cached.HtmlContent);
        Assert.Null(cached.RawWikitext);
        await ArticleCacheManager.ClearCacheForItemAsync("Page", id);
        Assert.Empty(storage.Files);
    }

    private sealed class MemoryStorage : IStorageProvider
    {
        public Dictionary<string, string> Files { get; } = new();
        public Task<string> ReadTextAsync(string path) => Task.FromResult(Files[path]);
        public Task WriteTextAsync(string path, string text) { Files[path] = text; return Task.CompletedTask; }
        public Task<bool> FileExistsAsync(string path) => Task.FromResult(Files.ContainsKey(path));
        public Task DeleteFileAsync(string path) { Files.Remove(path); return Task.CompletedTask; }
        public Task WriteBytesAsync(string path, byte[] bytes) => throw new NotSupportedException();
        public Task RecreateFolderAsync(string path) { Files.Clear(); return Task.CompletedTask; }
        public Task ClearFolderAsync(string path) => RecreateFolderAsync(path);
        public Task<ulong> GetFolderSizeAsync(string path) => Task.FromResult(0UL);
    }
}
