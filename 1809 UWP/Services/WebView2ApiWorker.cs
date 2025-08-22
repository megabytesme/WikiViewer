using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using WikiViewer.Core;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Services;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;

namespace _1809_UWP.Services
{
    public class WebView2ApiWorker : IApiWorker
    {
        public WebView2 WebView { get; private set; }
        public bool IsInitialized { get; private set; }
        private Task _initializationTask;

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
                        if (WikiViewer.Shared.Uwp.App.UIHost == null)
                            throw new InvalidOperationException(
                                "App.UIHost is not available for WebView2 worker."
                            );
                        WebView = new WebView2();
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Add(WebView);
                        await WebView.EnsureCoreWebView2Async();
                        if (!string.IsNullOrEmpty(baseUrl))
                            WebView.CoreWebView2.Navigate(baseUrl);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
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
                await InitializeAsync(SessionManager.CurrentWiki?.BaseUrl);
            }
        }

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            await EnsureInitializedAsync();
            string extension = Path.GetExtension(new Uri(url).LocalPath).ToLowerInvariant();

            if (extension == ".svg")
                return await GetSvgBytesAsync(url);
            if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" }.Contains(extension))
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
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Add(tempWorker);
                        await tempWorker.EnsureCoreWebView2Async();
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
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Remove(tempWorker);
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
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Add(tempWorker);
                        await tempWorker.EnsureCoreWebView2Async();
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
                        if (!await navTcs.Task)
                            throw new Exception($"Image navigation failed for {url}");

                        const string script =
                            @"(function() { const img = document.querySelector('img'); if (img && img.naturalWidth > 0) { const canvas = document.createElement('canvas'); canvas.width = img.naturalWidth; canvas.height = img.naturalHeight; const ctx = canvas.getContext('2d'); ctx.drawImage(img, 0, 0); return canvas.toDataURL('image/png').split(',')[1]; } return null; })();";
                        string scriptResult = await tempWorker.CoreWebView2.ExecuteScriptAsync(
                            script
                        );
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
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Remove(tempWorker);
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
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Add(tempWorker);
                        await tempWorker.EnsureCoreWebView2Async();
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
                        tempWorker.CoreWebView2.Navigate(SessionManager.CurrentWiki.BaseUrl);
                        if (!await navTcs.Task)
                            throw new Exception(
                                $"Navigation to base URL '{SessionManager.CurrentWiki.BaseUrl}' failed, preventing download context setup."
                            );

                        string script =
                            $"(async () => {{ try {{ const response = await fetch('{url}'); if (!response.ok) {{ return `Error: ${{response.status}} ${{response.statusText}}`; }} const blob = await response.blob(); const blobUrl = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = blobUrl; a.download = ''; document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(blobUrl); return 'Download initiated'; }} catch (e) {{ return `Error: ${{e.message}}`; }} }})();";
                        await tempWorker.CoreWebView2.ExecuteScriptAsync(script);

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
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Remove(tempWorker);
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
            await navCompleteTcs.Task;
            string scriptResult = await worker.CoreWebView2.ExecuteScriptAsync(
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
            var sourceCookies = await source.CookieManager.GetCookiesAsync(AppSettings.BaseUrl);
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
                    await navCompleteTcs.Task;

                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalSeconds < 15)
                    {
                        await Task.Delay(250);
                        string scriptResult = await WebView.CoreWebView2.ExecuteScriptAsync(
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
                    if (
                        !string.IsNullOrWhiteSpace(json)
                        && (json.Trim().StartsWith("{") || json.Trim().StartsWith("["))
                    )
                        return json.Trim();
                    return null;
                }
            );
        }

        public async Task<string> PostAndGetJsonFromApiAsync(
            string url,
            Dictionary<string, string> postData
        )
        {
            await EnsureInitializedAsync();
            return await DispatcherTaskExtensions.RunTaskAsync(
                CoreApplication.MainView.CoreWindow.Dispatcher,
                async () =>
                {
                    if (WebView?.CoreWebView2 == null)
                        throw new InvalidOperationException(
                            "CoreWebView2 could not be initialized for POST."
                        );

                    var content = new FormUrlEncodedContent(postData);
                    string postBody = await content.ReadAsStringAsync();
                    var postBytes = System.Text.Encoding.UTF8.GetBytes(postBody);
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        await stream.WriteAsync(postBytes.AsBuffer());
                        stream.Seek(0);
                        var request = WebView.CoreWebView2.Environment.CreateWebResourceRequest(
                            url,
                            "POST",
                            stream,
                            "Content-Type: application/x-www-form-urlencoded"
                        );
                        WebView.CoreWebView2.NavigateWithWebResourceRequest(request);
                    }

                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalSeconds < 15)
                    {
                        await Task.Delay(250);
                        string scriptResult = await WebView.CoreWebView2.ExecuteScriptAsync(
                            "document.documentElement.outerHTML"
                        );
                        if (string.IsNullOrEmpty(scriptResult))
                            continue;

                        string fullHtml = JsonConvert.DeserializeObject<string>(scriptResult);
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
                        WebView.Close();
                        WebView = null;
                    }
                }
            );
        }
    }
}
