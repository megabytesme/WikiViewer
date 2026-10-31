using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class ResolverTests
{
    [Theory]
    [InlineData("https://resolver.example", "https://resolver.example/v1/request")]
    [InlineData("https://resolver.example/", "https://resolver.example/v1/request")]
    [InlineData("https://resolver.example/v1/request", "https://resolver.example/v1/request")]
    [InlineData("https://resolver.example/custom/", "https://resolver.example/custom/v1/request")]
    public void NormalizesBaseAndCompleteEndpointUrls(string input, string expected)
        => Assert.Equal(expected, ResolverConfigurationService.NormalizeEndpoint(input).AbsoluteUri);

    [Theory]
    [InlineData("http://resolver.example")]
    [InlineData("https://name:secret@resolver.example")]
    [InlineData("https://resolver.example?password=secret")]
    [InlineData("https://resolver.example/#secret")]
    [InlineData("")]
    public void RejectsInsecureOrCredentialBearingUrls(string input)
        => Assert.Throws<ArgumentException>(() => ResolverConfigurationService.NormalizeEndpoint(input));

    [Fact]
    public void StoresOnlyAnEndpointAndVaultReferenceInSettingsAndRemovesOldCredentials()
    {
        var settings = new Settings(); var vault = new Vault();
        var service = new ResolverConfigurationService(settings, vault);
        service.Save("https://resolver.example", "test-user", "test-secret");
        Assert.DoesNotContain("test-user", settings.Value); Assert.DoesNotContain("test-secret", settings.Value);
        Assert.Equal("test-secret", service.Load().Password);
        var old = vault.Values.Keys.Single();
        service.Save("https://resolver.example", "test-user", "");
        Assert.False(vault.Values.ContainsKey(old)); Assert.Single(vault.Values);
        service.Clear(); Assert.Null(service.Load()); Assert.Empty(vault.Values); Assert.Equal("", settings.Value);
    }

    [Fact]
    public void NeverReusesSavedPasswordForADifferentEndpointOrUser()
    {
        var service = new ResolverConfigurationService(new Settings(), new Vault());
        service.Save("https://resolver.example", "test-user", "test-secret");
        Assert.Throws<ArgumentException>(() => service.Save("https://other.example", "test-user", ""));
        Assert.Throws<ArgumentException>(() => service.Save("https://resolver.example/other", "test-user", ""));
        Assert.Throws<ArgumentException>(() => service.Save("https://resolver.example", "other-user", ""));
        Assert.Equal("resolver.example", service.Load().Endpoint.Host);
    }

    [Fact]
    public void FailedSettingsWriteKeepsPreviousConfigurationAndCleansNewVaultEntry()
    {
        var settings = new Settings(); var vault = new Vault(); var service = new ResolverConfigurationService(settings, vault);
        service.Save("https://resolver.example", "test-user", "test-secret"); settings.Fail = true;
        Assert.Throws<IOException>(() => service.Save("https://other.example", "new-user", "new-secret"));
        Assert.Single(vault.Values); Assert.Equal("resolver.example", service.Load().Endpoint.Host);
        Assert.Equal("test-secret", service.Load().Password);
    }

    [Fact]
    public async Task SendsBasicAuthOnTheResolverRequestWithoutForwardingItInThePayloadOrDefaultHeaders()
    {
        var handler = new Handler(); using var client = new HttpClient(handler);
        var configuration = new ResolverConfiguration { Endpoint = new Uri("https://resolver.example/v1/request"), Username = "test-user", Password = "test-secret" };
        await ResolverRequestService.SendAsync(client, configuration, new { url = "https://wiki.example/api.php", postData = new { action = "query" } });
        Assert.Equal("https://resolver.example/v1/request", handler.Uri.AbsoluteUri);
        Assert.Equal("POST", handler.Method);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-user:test-secret")), handler.Authorization);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Equal("https://wiki.example/api.php", (string)JObject.Parse(handler.Body)["url"]);
        Assert.DoesNotContain("test-secret", handler.Body);
    }

    [Theory]
    [InlineData(401, "authentication")]
    [InlineData(403, "authentication")]
    [InlineData(302, "redirected")]
    [InlineData(502, "HTTP 502")]
    public async Task ErrorsDoNotExposeTheResolverResponseOrCredentials(int status, string expected)
    {
        var handler = new Handler { Response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private response test-secret") } };
        using var client = new HttpClient(handler);
        var configuration = new ResolverConfiguration { Endpoint = new Uri("https://resolver.example"), Username = "test-user", Password = "test-secret" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ResolverRequestService.SendAsync(client, configuration, new { url = "https://wiki.example" }));
        Assert.Contains(expected, ex.Message); Assert.DoesNotContain("private response", ex.Message); Assert.DoesNotContain("test-secret", ex.Message);
    }

    [Fact]
    public async Task PreservesGzipBinaryResponseContract()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(Encoding.UTF8.GetBytes("{\"query\":{}}"));
        var handler = new Handler { Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(output.ToArray()) } };
        using var client = new HttpClient(handler);
        var result = await ResolverRequestService.SendAsync(client, new ResolverConfiguration { Endpoint = new Uri("https://resolver.example"), Username = "test-user", Password = "test-secret" }, new { url = "https://wiki.example" });
        Assert.Equal("{\"query\":{}}", Encoding.UTF8.GetString(result));
    }

    [Fact]
    public async Task UnconfiguredResolverDoesNotSendANetworkRequest()
    {
        var handler = new Handler(); using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ResolverRequestService.SendAsync(client, null, new { url = "https://wiki.example" }));
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task BoundsUnfinishedNavigationAndPreservesPlatformErrors()
    {
        var never = new TaskCompletionSource<string>();
        await Assert.ThrowsAsync<TimeoutException>(() => NetworkOperationTimeout.RunAsync(() => never.Task, TimeSpan.FromMilliseconds(5), "Navigation timed out"));
        var error = new InvalidOperationException("Navigation failed");
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => NetworkOperationTimeout.RunAsync(() => Task.FromException<string>(error), TimeSpan.FromSeconds(1), "Timeout")));
    }

    private sealed class Settings : ISettingsProvider
    {
        public string Value = ""; public bool Fail;
        public T GetValue<T>(string key, T fallback) => (T)(object)Value;
        public void SetValue<T>(string key, T value) { if (Fail) throw new IOException("Storage failed"); Value = (string)(object)value; }
    }
    private sealed class Vault : ICredentialService
    {
        public Dictionary<Guid, UserCredentials> Values = new();
        public void SaveCredentials(Guid id, string username, string password) => Values[id] = new UserCredentials { Username = username, Password = password };
        public UserCredentials LoadCredentials(Guid id) => Values.TryGetValue(id, out var value) ? value : null;
        public void ClearCredentials(Guid id) => Values.Remove(id);
    }
    private sealed class Handler : HttpMessageHandler
    {
        public Uri Uri; public string Method; public string Authorization; public string Body;
        public HttpResponseMessage Response = new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uri = request.RequestUri; Method = request.Method.Method; Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content.ReadAsStringAsync(); return Response;
        }
    }
}
