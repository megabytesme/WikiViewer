using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public class ProxyHttpClientApiWorker : IApiWorker, IDisposable
    {
        private static readonly HttpClient _sharedGatekeeperClient = CreateClient();

        private readonly HttpClient _gatekeeperClient;

        private readonly bool _isSharedClient;

        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(100) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WikiViewer (https://github.com/megabytesme/WikiViewer)");
            return client;
        }

        public ProxyHttpClientApiWorker()
            : this(false) { }

        public ProxyHttpClientApiWorker(bool useDedicatedClient)
        {
            if (useDedicatedClient)
            {
                _gatekeeperClient = CreateClient();
                _isSharedClient = false;
            }
            else
            {
                _gatekeeperClient = _sharedGatekeeperClient;
                _isSharedClient = true;
            }
        }

        public Task InitializeAsync(string baseUrl)
        {
            if (ResolverConfigurationService.Current?.Load() == null)
                throw new InvalidOperationException("Configure a resolver URL, username and password in Settings > Connection before using Proxy.");
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

        private string ExtractJsonFromHtml(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return null;
            string trimmedText = responseText.Trim();
            if (trimmedText.StartsWith("{") || trimmedText.StartsWith("["))
                return trimmedText;

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(responseText);
                var preNode = doc.DocumentNode.SelectSingleNode("//body/pre");
                if (preNode != null)
                {
                    string potentialJson = preNode.InnerText.Trim();
                    if (potentialJson.StartsWith("{") || potentialJson.StartsWith("["))
                        return potentialJson;
                }
            }
            catch { }
            return null;
        }

        private async Task<byte[]> MakeGatekeeperRequest(object payload)
        {
            return await ResolverRequestService.SendAsync(_gatekeeperClient, ResolverConfigurationService.Current?.Load(), payload);
        }

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            CheckInitialized();
            var payload = new { url = url };
            return await MakeGatekeeperRequest(payload);
        }

        public async Task<string> GetRawHtmlFromUrlAsync(string url)
        {
            CheckInitialized();
            var bytes = await GetRawBytesFromUrlAsync(url);
            return Encoding.UTF8.GetString(bytes);
        }

        public async Task<string> GetJsonFromApiAsync(string url)
        {
            CheckInitialized();
            string responseText = await GetRawHtmlFromUrlAsync(url);
            return ExtractJsonFromHtml(responseText);
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

            var payload = new { url, body = encodedContent, contentType = "application/x-www-form-urlencoded" };
            var bytes = await MakeGatekeeperRequest(payload);
            string responseText = Encoding.UTF8.GetString(bytes);
            return ExtractJsonFromHtml(responseText);
        }

        public Task CopyApiCookiesFromAsync(IApiWorker source) => Task.CompletedTask;

        public void Dispose()
        {
            if (!_isSharedClient)
            {
                _gatekeeperClient?.Dispose();
            }
        }
    }
}
