using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public class HttpClientApiWorker : IApiWorker
    {
        private static readonly HttpClient _sharedClient = new HttpClient();

        private readonly HttpClient _client;
        private readonly bool _isSharedClient;

        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }

        static HttpClientApiWorker()
        {
#if UWP_1507
            _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "WikiViewer/1.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1507 (System.Net.Http.HttpClient)"
            );
#elif UWP_1809
            _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "WikiViewer/2.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1809 (System.Net.Http.HttpClient)"
            );
#else
            _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "WikiViewer (https://github.com/megabytesme/WikiViewer) (System.Net.Http.HttpClient)"
            );
#endif
            _sharedClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public HttpClientApiWorker()
        {
            _client = _sharedClient;
            _isSharedClient = true;
        }

        public HttpClientApiWorker(bool useDedicatedClient)
        {
            if (useDedicatedClient)
            {
                _client = new HttpClient();
#if UWP_1507
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "WikiViewer/1.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1507 (System.Net.Http.HttpClient)"
                );
#elif UWP_1809
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "WikiViewer/2.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1809 (System.Net.Http.HttpClient)"
                );
#else
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "WikiViewer (https://github.com/megabytesme/WikiViewer) (System.Net.Http.HttpClient)"
                );
#endif
                _client.Timeout = TimeSpan.FromSeconds(30);
                _isSharedClient = false;
            }
            else
            {
                _client = _sharedClient;
                _isSharedClient = true;
            }
        }

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
                return await _client.GetStringAsync(url);
            }
            catch (HttpRequestException ex)
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
            catch (HttpRequestException ex)
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
            using (var content = new FormUrlEncodedContent(postData))
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
