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
        private static readonly HttpClient _client;

        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }

        static HttpClientApiWorker()
        {
            _client = new HttpClient();

            _client.DefaultRequestHeaders.UserAgent.ParseAdd("WikiViewerApp/2.0");

            _client.Timeout = TimeSpan.FromSeconds(30);
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

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            CheckInitialized();
            Debug.WriteLine($"[HttpClientApiWorker] Attempting to GET BYTES for URL: {url}"); // <-- ADD THIS
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

        public async Task<string> GetRawHtmlFromUrlAsync(string url)
        {
            CheckInitialized();
            Debug.WriteLine($"[HttpClientApiWorker] Attempting to GET URL: {url}"); // <-- ADD THIS
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

        public void Dispose() { }
    }
}
