using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class KnownWikiApiWorkerTests
{
    [Fact]
    public async Task AutoUsesTheKnownApiBeforeCreatingABrowserOrDiscoveringPaths()
    {
        var wiki = Wiki(); var factory = new Factory();
        using var worker = await KnownWikiApiWorker.CreateAsync(wiki, factory);
        Assert.Same(factory.Http, worker);
        Assert.Equal(ConnectionMethod.HttpClient, wiki.ResolvedConnectionMethod);
        Assert.Equal(new[] { ConnectionMethod.HttpClient }, factory.Created);
        Assert.StartsWith(wiki.ApiEndpoint + "?action=query", factory.Http.Request);
        Assert.Equal(wiki.Id, worker.WikiContext.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<html>Challenge</html>")]
    [InlineData("{\"query\":{\"other\":{}}}")]
    public async Task InvalidDirectResponsesReleaseTheWorkerAndTryTheBrowser(string response)
    {
        var wiki = Wiki(); var factory = new Factory(); factory.Http.Response = response;
        using var worker = await KnownWikiApiWorker.CreateAsync(wiki, factory);
        Assert.Same(factory.Web, worker);
        Assert.True(factory.Http.Disposed);
        Assert.False(factory.Web.Disposed);
        Assert.Equal(ConnectionMethod.WebView, wiki.ResolvedConnectionMethod);
    }

    [Theory]
    [InlineData(ConnectionMethod.WebView)]
    [InlineData(ConnectionMethod.HttpClient)]
    public async Task ExplicitPreferencesAreRespectedWithoutAdditionalProbes(ConnectionMethod method)
    {
        var wiki = Wiki(); wiki.PreferredConnectionMethod = method;
        var factory = new Factory();
        using var worker = await KnownWikiApiWorker.CreateAsync(wiki, factory);
        Assert.Equal(new[] { method }, factory.Created);
        Assert.Null(((Worker)worker).Request);
        Assert.True(worker.IsInitialized);
    }

    private static WikiInstance Wiki() => new() { BaseUrl = "https://wiki.example", ScriptPath = "mediawiki" };

    private sealed class Factory : IApiWorkerFactory
    {
        public readonly Worker Http = new(), Web = new();
        public readonly List<ConnectionMethod> Created = new();
        public IApiWorker CreateApiWorker(WikiInstance wiki)
        {
            var method = wiki.PreferredConnectionMethod == ConnectionMethod.Auto ? wiki.ResolvedConnectionMethod.Value : wiki.PreferredConnectionMethod;
            Created.Add(method);
            var worker = method == ConnectionMethod.HttpClient ? Http : Web;
            worker.WikiContext = wiki;
            return worker;
        }
        public Task<IApiWorker> CreateApiWorkerAsync(WikiInstance wiki) => throw new InvalidOperationException("Do not rediscover paths for a known API.");
    }

    private sealed class Worker : IApiWorker
    {
        public string Response = "{\"query\":{\"general\":{\"sitename\":\"Wiki\"}}}";
        public string Request;
        public bool Disposed;
        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }
        public Task InitializeAsync(string url) { IsInitialized = true; return Task.CompletedTask; }
        public Task<string> GetJsonFromApiAsync(string url) { Request = url; return Task.FromResult(Response); }
        public Task<string> PostAndGetJsonFromApiAsync(string url, Dictionary<string, string> data) => throw new NotSupportedException();
        public Task<string> GetRawHtmlFromUrlAsync(string url) => throw new NotSupportedException();
        public Task<byte[]> GetRawBytesFromUrlAsync(string url) => throw new NotSupportedException();
        public Task CopyApiCookiesFromAsync(IApiWorker source) => Task.CompletedTask;
        public void Dispose() { Disposed = true; }
    }
}
