using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace _1809_UWP
{
    public class ApiHelper
    {
        private readonly CoreWebView2 _coreWebView;

        public ApiHelper(CoreWebView2 coreWebView)
        {
            _coreWebView = coreWebView ?? throw new ArgumentNullException(nameof(coreWebView));
        }

        public Task<string> GetAsync(string url)
        {
            return PerformRequestAsync(url, null);
        }

        public Task<string> PostAsync(string url, Dictionary<string, string> postData)
        {
            return PerformRequestAsync(url, postData);
        }

        private async Task<string> PerformRequestAsync(string url, Dictionary<string, string> postData)
        {
            var tcs = new TaskCompletionSource<string>();

            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = async (sender, args) =>
            {
                sender.NavigationCompleted -= handler;

                if (!args.IsSuccess)
                {
                    tcs.TrySetException(new HttpRequestException($"API navigation failed for {url} with status: {args.WebErrorStatus}"));
                    return;
                }

                try
                {
                    string scriptResult = await sender.ExecuteScriptAsync("document.body.innerText");
                    tcs.TrySetResult(JsonSerializer.Deserialize<string>(scriptResult ?? "null"));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };

            _coreWebView.NavigationCompleted += handler;

            if (postData == null)
            {
                _coreWebView.Navigate(url);
            }
            else
            {
                var urlEncodedContent = new FormUrlEncodedContent(postData);
                var postDataBytes = await urlEncodedContent.ReadAsByteArrayAsync();

                var uwpStream = new InMemoryRandomAccessStream();

                await uwpStream.WriteAsync(postDataBytes.AsBuffer());

                uwpStream.Seek(0);

                var request = _coreWebView.Environment.CreateWebResourceRequest(
                    url,
                    "POST",
                    uwpStream,
                    "Content-Type: application/x-www-form-urlencoded"
                );

                _coreWebView.NavigateWithWebResourceRequest(request);
            }

            return await tcs.Task;
        }
    }
}