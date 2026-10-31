using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class WikiConnectionDiscoveryTests
{
    [Theory]
    [InlineData("", "https://wiki.example/A%20B")]
    [InlineData("/wiki/", "https://wiki.example/wiki/A%20B")]
    public void BuildsArticleUrlsForRootAndPrefixedInstallations(string path, string expected)
        => Assert.Equal(expected, new WikiInstance { BaseUrl = "https://wiki.example/", ArticlePath = path }.GetWikiPageUrl("A B"));
    [Fact]
    public async Task UsesSiteInfoWithoutFetchingAProtectedHomepage()
    {
        var worker = new Worker { Json = "{\"query\":{\"general\":{\"scriptpath\":\"\",\"articlepath\":\"/wiki/$1\"}}}" };
        var paths = await WikiPathDetectorService.DetectPathsAsync("https://wiki.example/", worker);
        Assert.Equal("", paths.ScriptPath); Assert.Equal("wiki", paths.ArticlePath);
        Assert.Equal(0, worker.HtmlCalls); Assert.Single(worker.Urls);
    }
    [Fact]
    public async Task KeepsHtmlDiscoveryForNonstandardInstallations()
    {
        var worker = new Worker { Html = "<link rel='EditURI' href='/custom/api.php?action=rsd'><a href='/articles/Special:Random'>Random</a>" };
        var paths = await WikiPathDetectorService.DetectPathsAsync("https://wiki.example/", worker);
        Assert.Equal("custom/", paths.ScriptPath); Assert.Equal("/articles", paths.ArticlePath);
        Assert.Equal(1, worker.HtmlCalls);
    }
    [Theory]
    [InlineData("<html><title>Just a moment...</title><script>window._cf_chl_opt={}</script></html>", true)]
    [InlineData("<html><link rel='EditURI' href='/api.php'><div class='mw-parser-output'>Article</div><script src='/cdn-cgi/challenge-platform/script.js'></script></html>", false)]
    [InlineData("{\"query\":{\"text\":\"Just a moment...\"}}", false)]
    [InlineData("<html><title>Checking</title>Enable JavaScript and cookies to continue</html>", true)]
    public void DistinguishesInterstitialsFromRealWikiContent(string html, bool expected)
        => Assert.Equal(expected, CloudflareDetector.IsCloudflareChallenge(html));

    private sealed class Worker : IApiWorker
    {
        public string Json; public string Html; public int HtmlCalls; public List<string> Urls = new();
        public bool IsInitialized => true; public WikiInstance WikiContext { get; set; }
        public Task InitializeAsync(string url) => Task.CompletedTask;
        public Task<string> GetJsonFromApiAsync(string url) { Urls.Add(url); return Task.FromResult(Json); }
        public Task<string> GetRawHtmlFromUrlAsync(string url) { HtmlCalls++; return Task.FromResult(Html); }
        public Task<string> PostAndGetJsonFromApiAsync(string url, Dictionary<string,string> data) => throw new NotSupportedException();
        public Task<byte[]> GetRawBytesFromUrlAsync(string url) => throw new NotSupportedException();
        public Task CopyApiCookiesFromAsync(IApiWorker source) => Task.CompletedTask;
        public void Dispose() { }
    }
}
