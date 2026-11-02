using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using System.Linq;

using System.Net.Http;

namespace WikiViewer.Core.Services
{
    public class HttpClientApiWorker : IApiWorker
    {
        private static readonly System.Net.Http.HttpClient _sharedClient =
            new System.Net.Http.HttpClient();
        private readonly System.Net.Http.HttpClient _client;

        private readonly bool _isSharedClient;
        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }

        static HttpClientApiWorker()
        {
            const string userAgent = "WikiViewer (https://github.com/megabytesme/WikiViewer) (System.Net.Http.HttpClient)";

            _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            _sharedClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public HttpClientApiWorker(bool useDedicatedClient)
        {
            if (useDedicatedClient)
            {
                _client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("WikiViewer (https://github.com/megabytesme/WikiViewer) (System.Net.Http.HttpClient)");
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
                return await _client.GetByteArrayAsync(url);
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

            using (var content = new System.Net.Http.StringContent(encodedContent, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"))
            {
                var response = await _client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
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
