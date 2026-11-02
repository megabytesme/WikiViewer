using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Uwp.Services;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;

namespace WikiViewer.Uwp.Services
{
    public class WebView2ApiWorker : IApiWorker
    {
        public WebView2 WebView { get; private set; }
        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }
        private Task _initializationTask;

        private static async Task<bool> WaitForNavigationAsync(CoreWebView2 view, Task<bool> navigation)
        {
            try
            {
                bool success = await NetworkOperationTimeout.RunAsync(() => navigation, TimeSpan.FromSeconds(30),
                    "WebView navigation did not finish within 30 seconds. The site may require verification; configure a resolver in Settings > Connection or try another method.");
                if (!success) throw new InvalidOperationException("WebView could not navigate to the wiki. Try another connection method.");
                return true;
            }
            catch { view.Stop(); throw; }
        }

        private static Task<string> ExecuteScriptAsync(CoreWebView2 view, string script)
            => NetworkOperationTimeout.RunAsync(async () => await view.ExecuteScriptAsync(script), TimeSpan.FromSeconds(10), "WebView stopped responding while reading the page. Try another connection method.");

        public Task InitializeAsync(string baseUrl = null)
        {
            if (IsInitialized)
                return Task.CompletedTask;
            if (_initializationTask != null)
                return _initializationTask;

            _initializationTask = InitializeInternalAsync(baseUrl);
            return _initializationTask;
        }

        private async Task InitializeInternalAsync(string baseUrl)
        {
            var tcs = new TaskCompletionSource<bool>();
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    try
                    {
                        if (WikiViewer.Uwp.App.UIHost == null)
                            throw new InvalidOperationException(
                                "App.UIHost is not available for WebView2 worker."
                            );
                        WebView = new WebView2();
                        WikiViewer.Uwp.App.UIHost.Children.Add(WebView);
                        await NetworkOperationTimeout.RunAsync(async () => await WebView.EnsureCoreWebView2Async(), TimeSpan.FromSeconds(30), "WebView initialization timed out. Try another connection method.");

                        if (WebView.CoreWebView2 == null)
                        {
                            throw new InvalidOperationException(
                                "CoreWebView2 could not be initialized. The environment may be unstable."
                            );
                        }

                        // The operation performs its own navigation. Starting an unawaited base-page
                        // navigation here can race it and report the canceled base navigation as its result.
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        if (WebView != null)
                        {
                            WikiViewer.Uwp.App.UIHost?.Children.Remove(WebView);
                            WebView.Close();
                            WebView = null;
                        }
                        tcs.SetException(ex);
                    }
                }
            );
            await tcs.Task;
            IsInitialized = true;
        }

        private async Task EnsureInitializedAsync()
        {
            if (!IsInitialized)
            {
                await InitializeAsync(this.WikiContext?.BaseUrl);
            }
        }

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            await EnsureInitializedAsync();
            string extension = Path.GetExtension(new Uri(url).LocalPath).ToLowerInvariant();

            if (extension == ".svg")
                return await GetSvgBytesAsync(url);
            if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico" }.Contains(extension))
                return await GetRasterImageBytesAsync(url);
            return await DownloadFileAsBytesAsync(url, extension);
        }

        private Task<byte[]> GetSvgBytesAsync(string url)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            _ = CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    var tempWorker = new WebView2();
                    try
                    {
                        WikiViewer.Uwp.App.UIHost.Children.Add(tempWorker);
                        await NetworkOperationTimeout.RunAsync(async () => await tempWorker.EnsureCoreWebView2Async(), TimeSpan.FromSeconds(30), "WebView initialization timed out.");
                        if (this.WebView?.CoreWebView2 != null)
                            await CopyCookiesInternalAsync(
                                this.WebView.CoreWebView2,
                                tempWorker.CoreWebView2
                            );
                        string svgContent = await GetRawHtmlFromUrlInternalAsync(url, tempWorker);
                        if (string.IsNullOrEmpty(svgContent))
                            throw new Exception("Downloaded SVG content was null or empty.");
                        tcs.SetResult(System.Text.Encoding.UTF8.GetBytes(svgContent));
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                    finally
                    {
                        WikiViewer.Uwp.App.UIHost.Children.Remove(tempWorker);
                        tempWorker.Close();
                    }
                }
            );
            return tcs.Task;
        }

        private Task<byte[]> GetRasterImageBytesAsync(string url)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            _ = CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    var tempWorker = new WebView2();
                    try
                    {
                        WikiViewer.Uwp.App.UIHost.Children.Add(tempWorker);
                        await NetworkOperationTimeout.RunAsync(async () => await tempWorker.EnsureCoreWebView2Async(), TimeSpan.FromSeconds(30), "WebView initialization timed out.");
                        if (this.WebView?.CoreWebView2 != null)
                            await CopyCookiesInternalAsync(
                                this.WebView.CoreWebView2,
                                tempWorker.CoreWebView2
                            );

                        var navTcs = new TaskCompletionSource<bool>();
                        TypedEventHandler<
                            CoreWebView2,
                            CoreWebView2NavigationCompletedEventArgs
                        > navHandler = null;
                        navHandler = (s, e) =>
                        {
                            s.NavigationCompleted -= navHandler;
                            navTcs.TrySetResult(e.IsSuccess);
                        };
                        tempWorker.CoreWebView2.NavigationCompleted += navHandler;
                        tempWorker.CoreWebView2.Navigate(url);
                        if (!await WaitForNavigationAsync(tempWorker.CoreWebView2, navTcs.Task))
                            throw new Exception($"Image navigation failed for {url}");

                        const string script =
                            @"(function() { const img = document.querySelector('img'); const imageDocument = document.contentType.startsWith('image/') || (document.body && document.body.children.length === 1 && document.body.firstElementChild === img); if (imageDocument && img && img.complete && img.naturalWidth > 0) { const canvas = document.createElement('canvas'); canvas.width = img.naturalWidth; canvas.height = img.naturalHeight; const ctx = canvas.getContext('2d'); ctx.drawImage(img, 0, 0); return canvas.toDataURL('image/png').split(',')[1]; } return null; })();";
                        string scriptResult = null;
                        for (int attempt = 0; attempt < 10; attempt++)
                        {
                            scriptResult = await ExecuteScriptAsync(tempWorker.CoreWebView2, script);
                            if (!string.IsNullOrEmpty(scriptResult) && scriptResult != "null") break;
                            await Task.Delay(100);
                        }
                        if (string.IsNullOrEmpty(scriptResult) || scriptResult == "null")
                            throw new Exception(
                                "Failed to get Base64 data from rasterization script."
                            );

                        var base64Data = JsonConvert.DeserializeObject<string>(scriptResult);
                        tcs.SetResult(Convert.FromBase64String(base64Data));
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                    finally
                    {
                        WikiViewer.Uwp.App.UIHost.Children.Remove(tempWorker);
                        tempWorker.Close();
                    }
                }
            );
            return tcs.Task;
        }

        private Task<byte[]> DownloadFileAsBytesAsync(string url, string extension)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            _ = CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    var tempWorker = new WebView2();
                    try
                    {
                        WikiViewer.Uwp.App.UIHost.Children.Add(tempWorker);
                        await NetworkOperationTimeout.RunAsync(async () => await tempWorker.EnsureCoreWebView2Async(), TimeSpan.FromSeconds(30), "WebView initialization timed out.");
                        if (this.WebView?.CoreWebView2 != null)
                            await CopyCookiesInternalAsync(
                                this.WebView.CoreWebView2,
                                tempWorker.CoreWebView2
                            );

                        var cacheFolder =
                            await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                                "cache",
                                CreationCollisionOption.OpenIfExists
                            );
                        var tempFileName = Guid.NewGuid().ToString() + extension;
                        var tempFilePath = Path.Combine(cacheFolder.Path, tempFileName);
                        var downloadTcs = new TaskCompletionSource<bool>();

                        TypedEventHandler<
                            CoreWebView2,
                            CoreWebView2DownloadStartingEventArgs
                        > downloadHandler = null;
                        downloadHandler = (s, e) =>
                        {
                            s.DownloadStarting -= downloadHandler;
                            var deferral = e.GetDeferral();
                            e.ResultFilePath = tempFilePath;
                            e.Handled = true;
                            deferral.Complete();

                            TypedEventHandler<
                                CoreWebView2DownloadOperation,
                                object
                            > stateChangedHandler = null;
                            stateChangedHandler = (op, _) =>
                            {
                                if (op.State != CoreWebView2DownloadState.InProgress)
                                {
                                    op.StateChanged -= stateChangedHandler;
                                    downloadTcs.TrySetResult(
                                        op.State == CoreWebView2DownloadState.Completed
                                    );
                                }
                            };
                            e.DownloadOperation.StateChanged += stateChangedHandler;
                        };
                        tempWorker.CoreWebView2.DownloadStarting += downloadHandler;

                        var navTcs = new TaskCompletionSource<bool>();
                        TypedEventHandler<
                            CoreWebView2,
                            CoreWebView2NavigationCompletedEventArgs
                        > navHandler = null;
                        navHandler = (s, e) =>
                        {
                            s.NavigationCompleted -= navHandler;
                            navTcs.TrySetResult(e.IsSuccess);
                        };
                        tempWorker.CoreWebView2.NavigationCompleted += navHandler;
                        tempWorker.CoreWebView2.Navigate(this.WikiContext.BaseUrl);
                        if (!await WaitForNavigationAsync(tempWorker.CoreWebView2, navTcs.Task))
                            throw new Exception(
                                $"Navigation to base URL '{this.WikiContext.BaseUrl}' failed, preventing download context setup."
                            );

                        string script =
                            $"(async () => {{ try {{ const response = await fetch('{url}'); if (!response.ok) {{ return `Error: ${{response.status}} ${{response.statusText}}`; }} const blob = await response.blob(); const blobUrl = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = blobUrl; a.download = ''; document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(blobUrl); return 'Download initiated'; }} catch (e) {{ return `Error: ${{e.message}}`; }} }})();";
                        await ExecuteScriptAsync(tempWorker.CoreWebView2, script);

                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                        var completedTask = await Task.WhenAny(downloadTcs.Task, timeoutTask);
                        if (completedTask == timeoutTask || !await downloadTcs.Task)
                            throw new Exception($"Download failed or timed out for {url}");

                        var file = await StorageFile.GetFileFromPathAsync(tempFilePath);
                        var buffer = await FileIO.ReadBufferAsync(file);
                        await file.DeleteAsync();
                        tcs.SetResult(buffer.ToArray());
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                    finally
                    {
                        WikiViewer.Uwp.App.UIHost.Children.Remove(tempWorker);
                        tempWorker.Close();
                    }
                }
            );
            return tcs.Task;
        }

        private async Task<string> GetRawHtmlFromUrlInternalAsync(string url, WebView2 worker)
        {
            if (worker?.CoreWebView2 == null)
                throw new InvalidOperationException("CoreWebView2 is not initialized.");
            var navCompleteTcs = new TaskCompletionSource<bool>();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> navHandler =
                null;
            navHandler = (s, e) =>
            {
                s.NavigationCompleted -= navHandler;
                navCompleteTcs.TrySetResult(e.IsSuccess);
            };
            worker.CoreWebView2.NavigationCompleted += navHandler;
            worker.CoreWebView2.Navigate(url);
            await WaitForNavigationAsync(worker.CoreWebView2, navCompleteTcs.Task);
            string scriptResult = await ExecuteScriptAsync(worker.CoreWebView2,
                "document.documentElement.outerHTML"
            );
            return JsonConvert.DeserializeObject<string>(scriptResult);
        }

        public async Task<string> GetRawHtmlFromUrlAsync(string url)
        {
            await EnsureInitializedAsync();
            return await GetContentFromUrlCore(
                url,
                (fullHtml) => !string.IsNullOrEmpty(fullHtml) ? fullHtml : null
            );
        }

        private async Task CopyCookiesInternalAsync(CoreWebView2 source, CoreWebView2 destination)
        {
            if (this.WikiContext == null)
                return;
            var sourceCookies = await source.CookieManager.GetCookiesAsync(this.WikiContext.BaseUrl);
            if (sourceCookies == null)
                return;
            foreach (var cookie in sourceCookies)
            {
                var newCookie = destination.CookieManager.CreateCookie(
                    cookie.Name,
                    cookie.Value,
                    cookie.Domain,
                    cookie.Path
                );
                newCookie.Expires = cookie.Expires;
                newCookie.IsHttpOnly = cookie.IsHttpOnly;
                newCookie.IsSecure = cookie.IsSecure;
                newCookie.SameSite = cookie.SameSite;
                destination.CookieManager.AddOrUpdateCookie(newCookie);
            }
        }

        public async Task CopyApiCookiesFromAsync(IApiWorker source)
        {
            if (
                source is WebView2ApiWorker sourceWvWorker
                && this.WebView?.CoreWebView2 != null
                && sourceWvWorker.WebView?.CoreWebView2 != null
            )
            {
                await CopyCookiesInternalAsync(
                    sourceWvWorker.WebView.CoreWebView2,
                    this.WebView.CoreWebView2
                );
            }
        }

        private async Task<string> GetContentFromUrlCore(
            string url,
            Func<string, string> validationLogic
        )
        {
            return await DispatcherTaskExtensions.RunTaskAsync(
                CoreApplication.MainView.CoreWindow.Dispatcher,
                async () =>
                {
                    if (WebView?.CoreWebView2 == null)
                        throw new InvalidOperationException("CoreWebView2 is not initialized.");

                    var navCompleteTcs = new TaskCompletionSource<bool>();
                    TypedEventHandler<
                        CoreWebView2,
                        CoreWebView2NavigationCompletedEventArgs
                    > navHandler = null;
                    navHandler = (s, e) =>
                    {
                        s.NavigationCompleted -= navHandler;
                        navCompleteTcs.TrySetResult(e.IsSuccess);
                    };
                    WebView.CoreWebView2.NavigationCompleted += navHandler;
                    WebView.CoreWebView2.Navigate(url);
                    await WaitForNavigationAsync(WebView.CoreWebView2, navCompleteTcs.Task);

                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalSeconds < 15)
                    {
                        await Task.Delay(250);
                        string scriptResult = await ExecuteScriptAsync(WebView.CoreWebView2,
                            "document.documentElement.outerHTML"
                        );
                        if (string.IsNullOrEmpty(scriptResult))
                            continue;

                        string fullHtml = JsonConvert.DeserializeObject<string>(scriptResult);
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
        }

        public async Task<string> GetJsonFromApiAsync(string url)
        {
            await EnsureInitializedAsync();
            return await GetContentFromUrlCore(
                url,
                (fullHtml) =>
                {
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(fullHtml);
                    string json = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText;
                    if (string.IsNullOrWhiteSpace(json))
                        json = doc.DocumentNode.SelectSingleNode("//body")?.InnerText;

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        string decodedJson = System.Net.WebUtility.HtmlDecode(json);
                        if (
                            decodedJson.Trim().StartsWith("{") || decodedJson.Trim().StartsWith("[")
                        )
                            return decodedJson.Trim();
                    }
                    return null;
                }
            );
        }

        public async Task<string> PostAndGetJsonFromApiAsync(string url, Dictionary<string, string> postData)
        {
            await EnsureInitializedAsync();
            return await DispatcherTaskExtensions.RunTaskAsync(CoreApplication.MainView.CoreWindow.Dispatcher, async () =>
            {
                var view = WebView?.CoreWebView2 ?? throw new InvalidOperationException("CoreWebView2 is not initialized.");
                string body = string.Join("&", postData.Select(item => System.Net.WebUtility.UrlEncode(item.Key) + "=" + System.Net.WebUtility.UrlEncode(item.Value)));
                var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                TypedEventHandler<CoreWebView2, CoreWebView2WebResourceResponseReceivedEventArgs> handler = async (sender, args) =>
                {
                    if (!string.Equals(args.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(args.Request.Uri, url, StringComparison.OrdinalIgnoreCase) || received.Task.IsCompleted) return;
                    try
                    {
                        using (var content = await args.Response.GetContentAsync())
                        {
                            if (content == null) throw new InvalidOperationException("The wiki API response had no readable content.");
                            // Content may be chunked; read to EOF rather than trusting its initial Size.
                            using (var reader = new StreamReader(content.AsStreamForRead(), Encoding.UTF8))
                            {
                                string text = (await reader.ReadToEndAsync()).Trim();
                                if (CloudflareDetector.IsCloudflareChallenge(text))
                                    throw new NeedsUserVerificationException("The wiki requires a security check.", url);
                                if (args.Response.StatusCode >= 400)
                                    throw new InvalidOperationException("The wiki API returned HTTP " + args.Response.StatusCode + ".");
                                if (!text.StartsWith("{") && !text.StartsWith("["))
                                    throw new InvalidOperationException("The wiki API did not return JSON.");
                                received.TrySetResult(text);
                            }
                        }
                    }
                    catch (Exception error) { received.TrySetException(error); }
                };
                view.WebResourceResponseReceived += handler;
                try
                {
                    // Chromium consumes the request asynchronously, so keep its stream alive
                    // until the response has been read (including large draft POST bodies).
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(body).AsBuffer());
                        stream.Seek(0);
                        var request = view.Environment.CreateWebResourceRequest(url, "POST", stream,
                            "Content-Type: application/x-www-form-urlencoded\r\n");
                        view.NavigateWithWebResourceRequest(request);
                        return await NetworkOperationTimeout.RunAsync(() => received.Task, TimeSpan.FromSeconds(45),
                            "The wiki API did not finish the POST request in time.");
                    }
                }
                finally
                {
                    view.WebResourceResponseReceived -= handler;
                    if (received.Task.Status != TaskStatus.RanToCompletion) view.Stop();
                }
            });
        }

        public void Dispose()
        {
            _ = CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    if (WebView != null)
                    {
                        WikiViewer.Uwp.App.UIHost?.Children.Remove(WebView);
                        WebView.Close();
                        WebView = null;
                    }
                }
            );
        }
    }
}
