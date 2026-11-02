using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using System.Linq;

using Windows.Web.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Web.Http.Headers;

namespace WikiViewer.Uwp.Services
{
    public class UwpHttpClientApiWorker : IApiWorker
    {
        private static readonly HttpClient _sharedClient = new HttpClient();
        private readonly HttpClient _client;

        private readonly bool _isSharedClient;
        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }

        static UwpHttpClientApiWorker()
        {
            var userAgent =
                "WikiViewer/2.7.2.0 (https://github.com/megabytesme/WikiViewer) Windows (Windows.Web.Http.HttpClient)";

            _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        }

        public UwpHttpClientApiWorker(bool useDedicatedClient)
        {
            if (useDedicatedClient)
            {
                _client = new HttpClient();
                var userAgent =
                    "WikiViewer/2.7.2.0 (https://github.com/megabytesme/WikiViewer) Windows (Windows.Web.Http.HttpClient)";
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                _isSharedClient = false;
            }
            else
            {
                _client = _sharedClient;
                _isSharedClient = true;
            }
        }

        public UwpHttpClientApiWorker()
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
            Debug.WriteLine($"[UwpHttpClientApiWorker] Attempting to GET URL: {url}");
            try
            {
                return await _client.GetStringAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[UwpHttpClientApiWorker] FAILED to get URL: {url}. Message: {ex.Message}"
                );
                throw;
            }
        }

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            CheckInitialized();
            Debug.WriteLine($"[UwpHttpClientApiWorker] Attempting to GET BYTES for URL: {url}");
            try
            {
                var buffer = await _client.GetBufferAsync(new Uri(url));
                return buffer.ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[UwpHttpClientApiWorker] FAILED to get BYTES for URL: {url}. Message: {ex.Message}"
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

            using (var content = new Windows.Web.Http.HttpStringContent(encodedContent))
            {
                content.Headers.ContentType = new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue("application/x-www-form-urlencoded");
                var response = await _client.PostAsync(new Uri(url), content);
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
