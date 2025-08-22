using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Services;
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
        public bool IsInitialized { get; private set; }
        private Task _initializationTask;

        public Task InitializeAsync(string baseUrl = null)
        {
            if (IsInitialized)
            {
                return Task.CompletedTask;
            }

            if (_initializationTask != null)
            {
                return _initializationTask;
            }

            _initializationTask = InitializeInternalAsync(baseUrl);
            return _initializationTask;
        }

        private async Task InitializeInternalAsync(string baseUrl)
        {
            Debug.WriteLine("[WebViewApiWorker] InitializeInternalAsync called.");
            var tcs = new TaskCompletionSource<bool>();
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    try
                    {
                        if (WikiViewer.Shared.Uwp.App.UIHost == null)
                        {
                            throw new InvalidOperationException(
                                "App.UIHost is not available for WebView worker."
                            );
                        }
                        WebView = new WebView();
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Add(WebView);
                        Debug.WriteLine("[WebViewApiWorker] WebView control created.");

                        TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> navHandler =
                            null;
                        navHandler = (s, e) =>
                        {
                            WebView.NavigationCompleted -= navHandler;
                            if (!tcs.Task.IsCompleted)
                                tcs.SetResult(e.IsSuccess);
                        };
                        WebView.NavigationCompleted += navHandler;
                        var urlToNavigate = new Uri(baseUrl ?? SessionManager.CurrentWiki.BaseUrl);
                        WebView.Navigate(urlToNavigate);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[WebViewApiWorker] CRITICAL FAILURE during InitializeAsync: {ex.Message}"
                        );
                        if (!tcs.Task.IsCompleted)
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
                await InitializeAsync();
            }
        }

        public async Task<string> GetJsonFromApiAsync(string url)
        {
            await EnsureInitializedAsync();
            Debug.WriteLine($"[WebViewApiWorker] GetJsonFromApiAsync called for: {url}");
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
                    var tempWebView = new WebView();

                    WikiViewer.Shared.Uwp.App.UIHost.Children.Add(tempWebView);

                    try
                    {
                        var formContent = new HttpFormUrlEncodedContent(postData);
                        var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(url))
                        {
                            Content = formContent,
                        };

                        var navTcs = new TaskCompletionSource<bool>();
                        TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> navHandler =
                            null;
                        navHandler = (s, e) =>
                        {
                            tempWebView.NavigationCompleted -= navHandler;
                            navTcs.TrySetResult(e.IsSuccess);
                        };
                        tempWebView.NavigationCompleted += navHandler;

                        tempWebView.NavigateWithHttpRequestMessage(httpRequest);

                        await navTcs.Task;

                        await Task.Delay(250);
                        string fullHtml = await tempWebView.InvokeScriptAsync(
                            "eval",
                            new[] { "document.documentElement.outerHTML" }
                        );

                        if (string.IsNullOrEmpty(fullHtml))
                        {
                            throw new Exception("POST operation resulted in a blank page.");
                        }

                        var doc = new HtmlAgilityPack.HtmlDocument();
                        doc.LoadHtml(fullHtml);
                        string json = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText;
                        if (string.IsNullOrWhiteSpace(json))
                            json = doc.DocumentNode.SelectSingleNode("//body")?.InnerText;

                        if (
                            !string.IsNullOrWhiteSpace(json)
                            && (json.Trim().StartsWith("{") || json.Trim().StartsWith("["))
                        )
                        {
                            return json.Trim();
                        }

                        throw new Exception(
                            "Could not extract valid JSON from the POST response page."
                        );
                    }
                    finally
                    {
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Remove(tempWebView);
                        tempWebView = null;
                    }
                }
            );
        }

        public async Task<string> GetRawHtmlFromUrlAsync(string url)
        {
            await EnsureInitializedAsync();
            Debug.WriteLine($"[WebViewApiWorker] GetRawHtmlFromUrlAsync called for: {url}");
            return await GetContentFromUrlCore(
                url,
                (fullHtml) => !string.IsNullOrEmpty(fullHtml) ? fullHtml : null
            );
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
                    if (url == null)
                    {
                        Debug.WriteLine(
                            "[WebViewApiWorker] CRITICAL: GetContentFromUrlCore received a NULL url."
                        );
                        throw new ArgumentNullException(
                            nameof(url),
                            "URL cannot be null for WebView navigation."
                        );
                    }
                    if (WebView == null)
                        throw new InvalidOperationException("WebView is not initialized.");

                    var navTcs = new TaskCompletionSource<bool>();
                    TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> navHandler =
                        null;
                    navHandler = (s, e) =>
                    {
                        Debug.WriteLine(
                            $"[WebViewApiWorker] -> NavigationCompleted for {url}. Success: {e.IsSuccess}, Status: {e.WebErrorStatus}"
                        );
                        WebView.NavigationCompleted -= navHandler;
                        navTcs.TrySetResult(e.IsSuccess);
                    };
                    WebView.NavigationCompleted += navHandler;

                    Debug.WriteLine($"[WebViewApiWorker] -> Navigating WebView to: {url}");
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
                        Debug.WriteLine(
                            $"[WebViewApiWorker] -> Scraping attempt for {url}. HTML length: {fullHtml?.Length ?? 0}"
                        );
                        if (string.IsNullOrEmpty(fullHtml))
                            continue;

                        string extractedContent = validationLogic(fullHtml);
                        if (extractedContent != null)
                        {
                            Debug.WriteLine(
                                $"[WebViewApiWorker] -> SUCCESS: Content validated for {url}."
                            );
                            return extractedContent;
                        }
                    }
                    Debug.WriteLine(
                        $"[WebViewApiWorker] -> FAILED: Content validation timed out for {url}."
                    );
                    throw new TimeoutException($"Content validation timed out for GET URL: {url}");
                }
            );
        }

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            return await DispatcherTaskExtensions.RunTaskAsync(
                CoreApplication.MainView.CoreWindow.Dispatcher,
                async () =>
                {
                    Debug.WriteLine($"[WebViewApiWorker-Bytes] Requesting bytes for: {url}");
                    var tempWebView = new WebView();
                    WikiViewer.Shared.Uwp.App.UIHost.Children.Add(tempWebView);

                    var tcs = new TaskCompletionSource<byte[]>();
                    NotifyEventHandler scriptNotifyHandler = null;

                    try
                    {
                        scriptNotifyHandler = (object sender, NotifyEventArgs args) =>
                        {
                            tempWebView.ScriptNotify -= scriptNotifyHandler;
                            string notification = args.Value;
                            if (notification.StartsWith("DOWNLOAD_SUCCESS:"))
                            {
                                string base64Data = notification.Substring(
                                    "DOWNLOAD_SUCCESS:".Length
                                );
                                try
                                {
                                    var bytes = Convert.FromBase64String(base64Data);
                                    Debug.WriteLine(
                                        $"[WebViewApiWorker-Bytes] -> SUCCESS: Received {bytes.Length} bytes via ScriptNotify for {url}."
                                    );
                                    tcs.TrySetResult(bytes);
                                }
                                catch (FormatException ex)
                                {
                                    Debug.WriteLine(
                                        $"[WebViewApiWorker-Bytes] -> FAILED: Invalid Base64 data received. {ex.Message}"
                                    );
                                    tcs.TrySetResult(null);
                                }
                            }
                            else if (notification.StartsWith("DOWNLOAD_ERROR:"))
                            {
                                string errorMessage = notification.Substring(
                                    "DOWNLOAD_ERROR:".Length
                                );
                                Debug.WriteLine(
                                    $"[WebViewApiWorker-Bytes] -> FAILED: JS reported an error for {url}: {errorMessage}"
                                );
                                tcs.TrySetResult(null);
                            }
                        };
                        tempWebView.ScriptNotify += scriptNotifyHandler;

                        string jsScript =
                            $@"
                        (async function() {{
                            try {{
                                const response = await fetch('{url}');
                                if (!response.ok) {{
                                    window.external.notify('DOWNLOAD_ERROR: ' + response.status + ' ' + response.statusText);
                                    return;
                                }}
                                const blob = await response.blob();
                                const reader = new FileReader();
                                reader.onloadend = function() {{
                                    const base64String = reader.result.split(',')[1];
                                    window.external.notify('DOWNLOAD_SUCCESS:' + base64String);
                                }};
                                reader.onerror = function() {{
                                    window.external.notify('DOWNLOAD_ERROR: FileReader error.');
                                }};
                                reader.readAsDataURL(blob);
                            }} catch (e) {{
                                window.external.notify('DOWNLOAD_ERROR: ' + e.message);
                            }}
                        }})();";

                        var navTcs = new TaskCompletionSource<bool>();
                        TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> navHandler =
                            null;
                        navHandler = (s, e) =>
                        {
                            tempWebView.NavigationCompleted -= navHandler;
                            navTcs.TrySetResult(e.IsSuccess);
                        };
                        tempWebView.NavigationCompleted += navHandler;
                        tempWebView.NavigateToString(
                            "<html><head><title>Downloader</title></head><body></body></html>"
                        );

                        if (!await navTcs.Task)
                            throw new Exception("Navigation to downloader page failed.");

                        await tempWebView.InvokeScriptAsync("eval", new[] { jsScript });

                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            throw new TimeoutException($"Download timed out for {url}");
                        }

                        return await tcs.Task;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[WebViewApiWorker-Bytes] -> FAILED to get bytes from {url}: {ex.Message}"
                        );
                        tcs.TrySetResult(null);
                        return null;
                    }
                    finally
                    {
                        if (scriptNotifyHandler != null)
                            tempWebView.ScriptNotify -= scriptNotifyHandler;
                        WikiViewer.Shared.Uwp.App.UIHost.Children.Remove(tempWebView);
                        tempWebView = null;
                    }
                }
            );
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

            Debug.WriteLine(
                $"[WebViewApiWorker] CopyApiCookiesFromAsync: Found {sourceCookies.Count} cookies to share."
            );

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
