using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Newtonsoft.Json;

namespace _1809_UWP
{
    public static class ApiRequestService
    {
        public static async Task CopyApiCookiesAsync(CoreWebView2 source, CoreWebView2 destination)
        {
            var sourceCookies = await source.CookieManager.GetCookiesAsync("https://betawiki.net");
            if (sourceCookies == null) return;

            foreach (var cookie in sourceCookies)
            {
                var newCookie = destination.CookieManager.CreateCookie(
                    cookie.Name, cookie.Value, cookie.Domain, cookie.Path);

                newCookie.Expires = cookie.Expires;
                newCookie.IsHttpOnly = cookie.IsHttpOnly;
                newCookie.IsSecure = cookie.IsSecure;
                newCookie.SameSite = cookie.SameSite;

                destination.CookieManager.AddOrUpdateCookie(newCookie);
            }
            Debug.WriteLine($"[API Service] Copied {sourceCookies.Count} cookies to new worker.");
        }

        private static async Task<string> GetContentFromUrlCore(string url, Func<string, string> validationLogic, WebView2 worker)
        {
            return await CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(async () =>
            {
                await worker.EnsureCoreWebView2Async();
                if (worker.CoreWebView2 == null) throw new InvalidOperationException("CoreWebView2 could not be initialized.");

                var navCompleteTcs = new TaskCompletionSource<bool>();
                TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> navHandler = null;
                navHandler = (s, e) =>
                {
                    s.NavigationCompleted -= navHandler;
                    navCompleteTcs.TrySetResult(e.IsSuccess);
                };
                worker.CoreWebView2.NavigationCompleted += navHandler;

                worker.CoreWebView2.Navigate(url);

                await navCompleteTcs.Task;

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < 15)
                {
                    await Task.Delay(250);

                    string scriptResult = await worker.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                    if (string.IsNullOrEmpty(scriptResult)) continue;

                    string fullHtml = JsonConvert.DeserializeObject<string>(scriptResult);
                    if (string.IsNullOrEmpty(fullHtml)) continue;

                    if (fullHtml.Contains("g-recaptcha") || fullHtml.Contains("Verifying you are human"))
                        throw new NeedsUserVerificationException("Interactive user verification required.", url);

                    string extractedContent = validationLogic(fullHtml);
                    if (extractedContent != null)
                    {
                        return extractedContent;
                    }
                }
                throw new TimeoutException($"Content validation timed out for GET URL: {url}");
            });
        }

        public static async Task<string> PostAndGetJsonFromApiAsync(WebView2 worker, string url, Dictionary<string, string> postData)
        {
            return await CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(async () =>
            {
                await worker.EnsureCoreWebView2Async();
                if (worker.CoreWebView2 == null) throw new InvalidOperationException("CoreWebView2 could not be initialized for POST.");

                var content = new FormUrlEncodedContent(postData);
                string postBody = await content.ReadAsStringAsync();
                var postBytes = System.Text.Encoding.UTF8.GetBytes(postBody);

                using (var stream = new InMemoryRandomAccessStream())
                {
                    await stream.WriteAsync(postBytes.AsBuffer());
                    stream.Seek(0);
                    var request = worker.CoreWebView2.Environment.CreateWebResourceRequest(url, "POST", stream, "Content-Type: application/x-www-form-urlencoded");
                    worker.CoreWebView2.NavigateWithWebResourceRequest(request);
                }

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < 15)
                {
                    await Task.Delay(250);

                    string scriptResult = await worker.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                    if (string.IsNullOrEmpty(scriptResult)) continue;

                    string fullHtml = JsonConvert.DeserializeObject<string>(scriptResult);
                    if (string.IsNullOrEmpty(fullHtml)) continue;

                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(fullHtml);
                    string json = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText;

                    if (!string.IsNullOrWhiteSpace(json) && (json.Trim().StartsWith("{") || json.Trim().StartsWith("[")))
                    {
                        return json.Trim();
                    }
                }
                throw new TimeoutException($"Content validation timed out for POST URL: {url}");
            });
        }

        public static Task<string> GetRawHtmlFromUrlAsync(string url, WebView2 worker)
        {
            return GetContentFromUrlCore(
                url,
                (fullHtml) => !string.IsNullOrEmpty(fullHtml) ? fullHtml : null,
                worker
            );
        }

        public static Task<string> GetJsonFromApiAsync(string url, WebView2 worker)
        {
            return GetContentFromUrlCore(
                url,
                (fullHtml) =>
                {
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(fullHtml);
                    string json = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText;
                    if (string.IsNullOrWhiteSpace(json))
                        json = doc.DocumentNode.SelectSingleNode("//body")?.InnerText;
                    if (!string.IsNullOrWhiteSpace(json) && (json.Trim().StartsWith("{") || json.Trim().StartsWith("[")))
                        return json.Trim();
                    return null;
                },
                worker
            );
        }
    }

    public static class DispatcherTaskExtensions
    {
        public static Task<T> RunTaskAsync<T>(this CoreDispatcher dispatcher, Func<Task<T>> func, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal)
        {
            var tcs = new TaskCompletionSource<T>();
            _ = dispatcher.RunAsync(
                priority,
                async () =>
                {
                    try
                    {
                        tcs.SetResult(await func());
                    }
                    catch (Exception e)
                    {
                        tcs.SetException(e);
                    }
                }
            );
            return tcs.Task;
        }
    }
}