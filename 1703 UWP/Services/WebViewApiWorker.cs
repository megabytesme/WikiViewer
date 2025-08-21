using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Shared.Uwp.Services;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace _1703_UWP.Services
{
    public class WebViewApiWorker : IApiWorker
    {
        public WebView WebView { get; private set; }
        private static readonly HttpClient _httpClient = new HttpClient();

        public Task InitializeAsync(string baseUrl = null)
        {
            var tcs = new TaskCompletionSource<bool>();
            _ = CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    try
                    {
                        if (WikiViewer.Shared.Uwp.App.UIHost == null)
                            throw new InvalidOperationException(
                                "App.UIHost is not available for WebView worker."
                            );
                        WebView = new WebView();
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Add(WebView);

                        TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> navHandler =
                            null;
                        navHandler = (s, e) =>
                        {
                            WebView.NavigationCompleted -= navHandler;
                            if (!tcs.Task.IsCompleted)
                                tcs.SetResult(e.IsSuccess);
                        };
                        WebView.NavigationCompleted += navHandler;
                        WebView.Navigate(new Uri(baseUrl ?? AppSettings.BaseUrl));
                    }
                    catch (Exception ex)
                    {
                        if (!tcs.Task.IsCompleted)
                            tcs.SetException(ex);
                    }
                }
            );
            return tcs.Task;
        }

        public async Task<string> GetJsonFromApiAsync(string url) =>
            await GetContentFromUrlCore(
                url,
                (fullHtml) =>
                {
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(fullHtml);
                    string json = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText;
                    if (string.IsNullOrWhiteSpace(json))
                        json = doc.DocumentNode.SelectSingleNode("//body")?.InnerText;
                    if (
                        !string.IsNullOrWhiteSpace(json)
                        && (json.Trim().StartsWith("{") || json.Trim().StartsWith("["))
                    )
                        return json.Trim();
                    return null;
                }
            );

        public async Task<string> PostAndGetJsonFromApiAsync(
            string url,
            Dictionary<string, string> postData
        ) =>
            await DispatcherTaskExtensions.RunTaskAsync(
                CoreApplication.MainView.CoreWindow.Dispatcher,
                async () =>
                {
                    if (WebView == null)
                        throw new InvalidOperationException("WebView not initialized.");
                    var formContent = new HttpFormUrlEncodedContent(postData);
                    var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(url))
                    {
                        Content = formContent,
                    };
                    WebView.NavigateWithHttpRequestMessage(httpRequest);
                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalSeconds < 15)
                    {
                        await Task.Delay(250);
                        string fullHtml = await WebView.InvokeScriptAsync(
                            "eval",
                            new[] { "document.documentElement.outerHTML" }
                        );
                        if (string.IsNullOrEmpty(fullHtml))
                            continue;
                        var doc = new HtmlAgilityPack.HtmlDocument();
                        doc.LoadHtml(fullHtml);
                        string json = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText;
                        if (
                            !string.IsNullOrWhiteSpace(json)
                            && (json.Trim().StartsWith("{") || json.Trim().StartsWith("["))
                        )
                            return json.Trim();
                    }
                    throw new TimeoutException($"Content validation timed out for POST URL: {url}");
                }
            );

        public async Task<string> GetRawHtmlFromUrlAsync(string url) =>
            await GetContentFromUrlCore(
                url,
                (fullHtml) => !string.IsNullOrEmpty(fullHtml) ? fullHtml : null
            );

        private async Task<string> GetContentFromUrlCore(
            string url,
            Func<string, string> validationLogic
        ) =>
            await DispatcherTaskExtensions.RunTaskAsync(
                CoreApplication.MainView.CoreWindow.Dispatcher,
                async () =>
                {
                    if (WebView == null)
                        throw new InvalidOperationException("WebView is not initialized.");
                    var navTcs = new TaskCompletionSource<bool>();
                    TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> navHandler =
                        null;
                    navHandler = (s, e) =>
                    {
                        WebView.NavigationCompleted -= navHandler;
                        navTcs.TrySetResult(e.IsSuccess);
                    };
                    WebView.NavigationCompleted += navHandler;
                    WebView.Navigate(new Uri(url));
                    await navTcs.Task;
                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalSeconds < 15)
                    {
                        await Task.Delay(250);
                        string fullHtml = await WebView.InvokeScriptAsync(
                            "eval",
                            new[] { "document.documentElement.outerHTML" }
                        );
                        if (string.IsNullOrEmpty(fullHtml))
                            continue;
                        if (
                            fullHtml.Contains("g-recaptcha")
                            || fullHtml.Contains("Verifying you are human")
                        )
                            throw new NeedsUserVerificationException(
                                "Interactive user verification required.",
                                url
                            );
                        string extractedContent = validationLogic(fullHtml);
                        if (extractedContent != null)
                            return extractedContent;
                    }
                    throw new TimeoutException($"Content validation timed out for GET URL: {url}");
                }
            );

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(new Uri(url));
                response.EnsureSuccessStatusCode();
                var buffer = await response.Content.ReadAsBufferAsync();
                return buffer.ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[WebViewApiWorker-Bytes] Failed to get bytes from {url}: {ex.Message}"
                );
                return null;
            }
        }

        public Task CopyApiCookiesFromAsync(IApiWorker source)
        {
            if (!(source is WebViewApiWorker sourceWorker))
                return Task.CompletedTask;
            var filter = new HttpBaseProtocolFilter();
            var cookieManager = filter.CookieManager;
            var sourceCookies = cookieManager.GetCookies(new Uri(AppSettings.BaseUrl));
            if (sourceCookies == null || !sourceCookies.Any())
                return Task.CompletedTask;
            foreach (var cookie in sourceCookies)
            {
                cookieManager.SetCookie(
                    new HttpCookie(cookie.Name, cookie.Domain, cookie.Path) { Value = cookie.Value }
                );
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _ = CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    if (WebView != null)
                    {
                        WikiViewer.Shared.Uwp.App.UIHost?.Children.Remove(WebView);
                        WebView.Stop();
                        WebView = null;
                    }
                }
            );
        }
    }
}
