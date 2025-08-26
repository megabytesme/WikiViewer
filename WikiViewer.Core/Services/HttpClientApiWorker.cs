using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core.Services
{
    public class HttpClientApiWorker : IApiWorker
    {
        private static readonly HttpClient _client = new HttpClient();

        public bool IsInitialized { get; private set; }

        public Task InitializeAsync(string baseUrl = null)
        {
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("WikiViewerApp/1.0");
            IsInitialized = true;
            return Task.CompletedTask;
        }

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            return await _client.GetByteArrayAsync(url);
        }

        public async Task<string> GetRawHtmlFromUrlAsync(string url)
        {
            return await _client.GetStringAsync(url);
        }

        public async Task<string> GetJsonFromApiAsync(string url)
        {
            string responseText = await GetRawHtmlFromUrlAsync(url);
            string trimmedText = responseText.Trim();
            if (trimmedText.StartsWith("{") || trimmedText.StartsWith("["))
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
