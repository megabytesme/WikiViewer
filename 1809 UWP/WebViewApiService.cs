using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;

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
            return NavigateInternal(url, null);
        }

        public static async Task NavigateWithPostAsync(string url, Dictionary<string, string> postData)
        {
            if (!IsInitialized) throw new InvalidOperationException("Service not initialized.");
            var urlEncodedContent = new FormUrlEncodedContent(postData);
            var postDataBytes = await urlEncodedContent.ReadAsByteArrayAsync();
            var uwpStream = new InMemoryRandomAccessStream();
            await uwpStream.WriteAsync(postDataBytes.AsBuffer());
            uwpStream.Seek(0);
            var request = _apiWebView.Environment.CreateWebResourceRequest(url, "POST", uwpStream, "Content-Type: application/x-www-form-urlencoded");
            await NavigateInternal(url, request);
        }

        private static Task NavigateInternal(string url, CoreWebView2WebResourceRequest request)
        {
            var tcs = new TaskCompletionSource<bool>();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (sender, args) =>
            {
                sender.NavigationCompleted -= handler;
                tcs.TrySetResult(args.IsSuccess);
            };
            _apiWebView.NavigationCompleted += handler;

            if (request == null)
            {
                _apiWebView.Navigate(url);
            }
            else
            {
                _apiWebView.NavigateWithWebResourceRequest(request);
            }
            return tcs.Task;
        }

        public static CoreWebView2 GetWebView() => _apiWebView;
    }
}