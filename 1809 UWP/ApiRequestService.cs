using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Core;

namespace _1809_UWP
{
    public static class ApiRequestService
    {
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

                if (!await navCompleteTcs.Task)
                {
                    throw new Exception($"Navigation failed for URL {url}");
                }

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < 15)
                {
                    await Task.Delay(250);

                    string scriptResult = await worker.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                    if (string.IsNullOrEmpty(scriptResult)) continue;

                    string fullHtml = JsonSerializer.Deserialize<string>(scriptResult);
                    if (string.IsNullOrEmpty(fullHtml)) continue;

                    if (fullHtml.Contains("g-recaptcha"))
                        throw new NeedsUserVerificationException("Interactive user verification required.", url);
                    if (fullHtml.Contains("Verifying you are human")) continue;

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

                    string fullHtml = JsonSerializer.Deserialize<string>(scriptResult);
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