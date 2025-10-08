using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using System.Linq;

#if UWP_1507 || UWP_1809
using Windows.Web.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Web.Http.Headers;
#else
using System.Net.Http;
#endif

namespace WikiViewer.Core.Services
{
    public class HttpClientApiWorker : IApiWorker
    {
#if UWP_1507 || UWP_1809
        private static readonly HttpClient _sharedClient = new HttpClient();
        private readonly HttpClient _client;
#else
        private static readonly System.Net.Http.HttpClient _sharedClient =
            new System.Net.Http.HttpClient();
        private readonly System.Net.Http.HttpClient _client;
#endif

        private readonly bool _isSharedClient;
        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }

        static HttpClientApiWorker()
        {
#if UWP_1507
            var userAgent =
                "WikiViewer/1.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1507 (Windows.Web.Http.HttpClient)";
#elif UWP_1809
            var userAgent =
                "WikiViewer/2.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1809 (Windows.Web.Http.HttpClient)";
#else
            var userAgent =
                "WikiViewer (https://github.com/megabytesme/WikiViewer) (System.Net.Http.HttpClient)";
#endif

            _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

#if !(UWP_1507 || UWP_1809)
            _sharedClient.Timeout = TimeSpan.FromSeconds(30);
#endif
        }

        public HttpClientApiWorker(bool useDedicatedClient)
        {
            if (useDedicatedClient)
            {
#if UWP_1507
                _client = new HttpClient();
                var userAgent =
                    "WikiViewer/1.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1507 (Windows.Web.Http.HttpClient)";
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
#elif UWP_1809
                _client = new HttpClient();
                var userAgent =
                    "WikiViewer/2.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1809 (Windows.Web.Http.HttpClient)";
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
#else
                _client = new System.Net.Http.HttpClient();
                var userAgent =
                    "WikiViewer (https://github.com/megabytesme/WikiViewer) (System.Net.Http.HttpClient)";
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                _client.Timeout = TimeSpan.FromSeconds(30);
#endif
                _isSharedClient = false;
            }
            else
            {
                _client = _sharedClient;
                _isSharedClient = true;
            }
        }

        public HttpClientApiWorker()
            : this(false) { }

        public Task InitializeAsync(string baseUrl)
        {
            IsInitialized = true;
            return Task.CompletedTask;
        }

        private void CheckInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "Worker must be initialized before use. Call InitializeAsync first."
                );
        }

        public async Task<string> GetRawHtmlFromUrlAsync(string url)
        {
            CheckInitialized();
            Debug.WriteLine($"[HttpClientApiWorker] Attempting to GET URL: {url}");
            try
            {
                return await _client.GetStringAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[HttpClientApiWorker] FAILED to get URL: {url}. Message: {ex.Message}"
                );
                throw;
            }
        }

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            CheckInitialized();
            Debug.WriteLine($"[HttpClientApiWorker] Attempting to GET BYTES for URL: {url}");
            try
            {
#if UWP_1507 || UWP_1809
                var buffer = await _client.GetBufferAsync(new Uri(url));
                return buffer.ToArray();
#else
                return await _client.GetByteArrayAsync(url);
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[HttpClientApiWorker] FAILED to get BYTES for URL: {url}. Message: {ex.Message}"
                );
                throw;
            }
        }

        public async Task<string> GetJsonFromApiAsync(string url)
        {
            CheckInitialized();
            string responseText = await GetRawHtmlFromUrlAsync(url);
            string trimmedText = responseText?.Trim();

            if (
                !string.IsNullOrEmpty(trimmedText)
                && (trimmedText.StartsWith("{") || trimmedText.StartsWith("["))
            )
            {
                return trimmedText;
            }
            return null;
        }

        public async Task<string> PostAndGetJsonFromApiAsync(
            string url,
            Dictionary<string, string> postData
        )
        {
            CheckInitialized();

            var encodedItems = postData.Select(kvp =>
                System.Net.WebUtility.UrlEncode(kvp.Key) + "=" + System.Net.WebUtility.UrlEncode(kvp.Value)
            );
            var encodedContent = string.Join("&", encodedItems);

#if UWP_1507 || UWP_1809
            using (var content = new Windows.Web.Http.HttpStringContent(encodedContent))
            {
                content.Headers.ContentType = new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue("application/x-www-form-urlencoded");
                var response = await _client.PostAsync(new Uri(url), content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
#else
            using (var content = new System.Net.Http.StringContent(encodedContent, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"))
            {
                var response = await _client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
#endif
        }

        public Task CopyApiCookiesFromAsync(IApiWorker source)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_isSharedClient)
            {
                _client?.Dispose();
            }
        }
    }
}
