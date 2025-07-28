using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP
{
    public static class WebViewApiService
    {
        private static CoreWebView2 _apiWebView;
        public static bool IsInitialized => _apiWebView != null;

        public static void Initialize(WebView2 webView)
        {
            _apiWebView = webView.CoreWebView2;
        }

        public static Task NavigateAsync(string url)
        {
            if (!IsInitialized) throw new InvalidOperationException("Service not initialized.");
            var tcs = new TaskCompletionSource<bool>();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (s, e) => {
                _apiWebView.NavigationCompleted -= handler;
                tcs.SetResult(e.IsSuccess);
            };
            _apiWebView.NavigationCompleted += handler;
            _apiWebView.Navigate(url);
            return tcs.Task;
        }

        public static async Task NavigateWithPostAsync(string url, Dictionary<string, string> postData)
        {
            var content = new FormUrlEncodedContent(postData);
            var postBytes = await content.ReadAsByteArrayAsync();
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(postBytes.AsBuffer());
            stream.Seek(0);
            var request = _apiWebView.Environment.CreateWebResourceRequest(url, "POST", stream, "Content-Type: application/x-www-form-urlencoded");

            var tcs = new TaskCompletionSource<bool>();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (s, e) => {
                _apiWebView.NavigationCompleted -= handler;
                tcs.SetResult(e.IsSuccess);
            };
            _apiWebView.NavigationCompleted += handler;
            _apiWebView.NavigateWithWebResourceRequest(request);
            await tcs.Task;
        }

        public static async Task NavigateWithPostAsync(string url, string postDataString)
        {
            var postBytes = System.Text.Encoding.UTF8.GetBytes(postDataString);
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(postBytes.AsBuffer());
            stream.Seek(0);
            var request = _apiWebView.Environment.CreateWebResourceRequest(url, "POST", stream, "Content-Type: application/x-www-form-urlencoded");
            var tcs = new TaskCompletionSource<bool>();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (s, e) => {
                _apiWebView.NavigationCompleted -= handler;
                tcs.SetResult(e.IsSuccess);
            };
            _apiWebView.NavigationCompleted += handler;
            _apiWebView.NavigateWithWebResourceRequest(request);
            await tcs.Task;
        }

        public static async Task<string> GetStringContentAsync()
        {
            string html = await _apiWebView.ExecuteScriptAsync("document.documentElement.outerHTML");
            string fullHtml = JsonSerializer.Deserialize<string>(html ?? "null");

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(fullHtml);
            return doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText ?? doc.DocumentNode.InnerText;
        }

        public static CoreWebView2 GetWebView() => _apiWebView;
    }
}