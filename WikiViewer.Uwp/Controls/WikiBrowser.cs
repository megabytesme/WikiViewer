using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
#if MODERN_WEBVIEW
using Microsoft.Web.WebView2.Core;
using BrowserControl = Microsoft.UI.Xaml.Controls.WebView2;
#else
using BrowserControl = Windows.UI.Xaml.Controls.WebView;
#endif

namespace WikiViewer.Uwp.Controls
{
    public sealed class WikiNavigationStartingEventArgs : EventArgs
    {
        public Uri Uri { get; set; }
        public bool Cancel { get; set; }
    }

    public sealed class WikiNavigationCompletedEventArgs : EventArgs
    {
        public Uri Uri { get; set; }
        public bool IsSuccess { get; set; }
    }

    /// <summary>One browser surface for the reader/editor; the package selects its engine.</summary>
    public sealed class WikiBrowser : UserControl
    {
        private BrowserControl _browser;
        private Task _initializing;
        private int _generation;
        public bool IsReady { get; private set; }
        public event EventHandler<WikiNavigationStartingEventArgs> NavigationStarting;
        public event EventHandler<WikiNavigationCompletedEventArgs> NavigationCompleted;

        public Task EnsureInitializedAsync() => _initializing ?? (_initializing = InitializeAsync());

        private async Task InitializeAsync()
        {
            int generation = _generation;
            var browser = new BrowserControl();
            _browser = browser;
            Content = browser;
#if MODERN_WEBVIEW
            await browser.EnsureCoreWebView2Async();
            if (generation != _generation) { browser.Close(); return; }
            _browser.CoreWebView2.NavigationStarting += (sender, args) =>
            {
                Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri);
                var navigation = new WikiNavigationStartingEventArgs { Uri = uri };
                NavigationStarting?.Invoke(this, navigation);
                args.Cancel = navigation.Cancel;
            };
            _browser.CoreWebView2.NavigationCompleted += (sender, args) =>
            {
                Uri.TryCreate(sender.Source, UriKind.Absolute, out var uri);
                NavigationCompleted?.Invoke(this, new WikiNavigationCompletedEventArgs { Uri = uri, IsSuccess = args.IsSuccess });
            };
#else
            _browser.NavigationStarting += (sender, args) =>
            {
                var navigation = new WikiNavigationStartingEventArgs { Uri = args.Uri };
                NavigationStarting?.Invoke(this, navigation);
                args.Cancel = navigation.Cancel;
            };
            _browser.NavigationCompleted += (sender, args) => NavigationCompleted?.Invoke(this,
                new WikiNavigationCompletedEventArgs { Uri = args.Uri, IsSuccess = args.IsSuccess });
            await Task.CompletedTask;
#endif
            IsReady = true;
        }

        public async Task NavigateAsync(Uri uri)
        {
            await EnsureInitializedAsync();
            if (!IsReady) return;
#if MODERN_WEBVIEW
            _browser.CoreWebView2.Navigate(uri.AbsoluteUri);
#else
            _browser.Navigate(uri);
#endif
        }

        public async Task ShowHtmlAsync(string html)
        {
            await EnsureInitializedAsync();
            if (!IsReady) return;
            _browser.NavigateToString(html);
        }

        public async Task ShowArticleHtmlAsync(string html, string virtualHost)
        {
            await EnsureInitializedAsync();
            if (!IsReady) return;
            int generation = _generation;
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("article.html", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, html);
            if (generation != _generation || !IsReady) return;
#if MODERN_WEBVIEW
            _browser.CoreWebView2.SetVirtualHostNameToFolderMapping(virtualHost, ApplicationData.Current.LocalFolder.Path, CoreWebView2HostResourceAccessKind.Allow);
            _browser.CoreWebView2.Navigate("https://" + virtualHost + "/article.html");
#else
            _browser.Navigate(new Uri("ms-appdata:///local/article.html"));
#endif
        }

        public static string LocalAssetUrl(string virtualHost, string localPath)
        {
#if MODERN_WEBVIEW
            return "https://" + virtualHost + localPath.Replace('\\', '/');
#else
            return "ms-appdata:///local" + localPath.Replace('\\', '/');
#endif
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            if (!IsReady) return null;
#if MODERN_WEBVIEW
            string json = await _browser.ExecuteScriptAsync(script);
            try { return JsonConvert.DeserializeObject<string>(json); } catch (JsonException) { return json; }
#else
            return await _browser.InvokeScriptAsync("eval", new[] { script });
#endif
        }

        public void Stop()
        {
            if (!IsReady) return;
#if MODERN_WEBVIEW
            _browser.CoreWebView2.Stop();
#else
            _browser.Stop();
#endif
        }

        public void Close()
        {
            _generation++;
            Stop();
#if MODERN_WEBVIEW
            _browser?.Close();
#endif
            IsReady = false; _initializing = null; _browser = null; Content = null;
        }
    }
}
