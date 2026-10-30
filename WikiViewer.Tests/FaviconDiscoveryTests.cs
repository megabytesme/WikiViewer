using System.Text;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class FaviconDiscoveryTests
{
    private static readonly Uri Page = new("https://www.example.org/wiki/Main_Page");
    private static readonly byte[] Png = { 137, 80, 78, 71, 13, 10, 26, 10, 0 };

    [Fact]
    public void HtmlDiscoveryHandlesRelTokensCanonicalRedirectsAndUnsafeUrls()
    {
        const string html = "<link rel='canonical' href='https://example.org/wiki/Main_Page'>" +
            "<link rel='SHORTCUT ICON' href='/old.ico'><link rel='icon' href='javascript:alert(1)'>" +
            "<link rel='apple-touch-icon' href='../icon.png?v=1&amp;x=2'>" +
            "<link rel='icon' href='../icon.png?v=1&amp;x=2'><link rel='icon' href='https://user:pass@example.org/private.png'>";
        var icons = FaviconDiscoveryService.FromHtml(html, Page);
        Assert.Equal(new[] { "https://example.org/icon.png?v=1&x=2", "https://example.org/old.ico" }, icons.Select(u => u.AbsoluteUri));
    }

    [Fact]
    public async Task AdvertisedSiteIconIsTriedBeforeGuessingPaths()
    {
        var requests = new List<string>();
        var result = await FaviconDiscoveryService.FetchAsync(Page, "https://www.example.org/api.php",
            url => Task.FromResult("{query:{general:{favicon:'//cdn.example.org/site.png'}}}"),
            url => throw new InvalidOperationException("No HTML needed"),
            url => { requests.Add(url); return Task.FromResult(Png); });
        Assert.Equal("https://cdn.example.org/site.png", Assert.Single(requests));
        Assert.Equal("png", result.Format);
        Assert.Same(Png, result.Bytes);
    }

    [Fact]
    public async Task RejectsChallengePagesAndFallsBackWithoutRepeatingUrls()
    {
        var requests = new List<string>();
        var result = await FaviconDiscoveryService.FetchAsync(Page, "https://www.example.org/api.php",
            url => Task.FromResult("{query:{general:{favicon:'/favicon.ico',logo:'/logo.png'}}}"),
            url => Task.FromResult("<link rel='icon' href='/favicon.ico'>"),
            url => { requests.Add(url); return Task.FromResult(url.EndsWith("/logo.png") ? Png : Encoding.UTF8.GetBytes("<html>Verification required</html>")); });
        Assert.Equal("https://www.example.org/logo.png", result.Source.AbsoluteUri);
        Assert.Equal(requests.Count, requests.Distinct().Count());
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public async Task FailedMetadataAndImageDownloadsStillTryHtmlIcons()
    {
        var result = await FaviconDiscoveryService.FetchAsync(Page, "https://www.example.org/api.php",
            url => throw new InvalidOperationException(),
            url => Task.FromResult("<base href='https://static.example.org/assets/'><link rel='icon' href='missing.png'><link rel='icon' href='actual.png'>"),
            url => url.EndsWith("actual.png") ? Task.FromResult(Png) : throw new IOException());
        Assert.Equal("https://static.example.org/assets/actual.png", result.Source.AbsoluteUri);
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public void RasterValidationRejectsHtmlVectorsAndOversizedResponses()
    {
        Assert.Null(FaviconDiscoveryService.ImageFormat(Encoding.UTF8.GetBytes("<!doctype html><img src='logo.png'>")));
        Assert.Null(FaviconDiscoveryService.ImageFormat(Encoding.UTF8.GetBytes("<svg width='20'/>")));
        Assert.Null(FaviconDiscoveryService.ImageFormat(new byte[2000001]));
        Assert.Null(FaviconDiscoveryService.ImageFormat(new byte[22]));
        var ico = new byte[22]; ico[2] = 1; ico[4] = 1;
        Assert.Equal("ico", FaviconDiscoveryService.ImageFormat(ico));
    }
}
