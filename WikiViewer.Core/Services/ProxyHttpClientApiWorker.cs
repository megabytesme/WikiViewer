using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public class ProxyHttpClientApiWorker : IApiWorker, IDisposable
    {
        private static readonly HttpClient _sharedGatekeeperClient = new HttpClient();

        private readonly HttpClient _gatekeeperClient;
        private const string GatekeeperEndpoint = "https://wikiflareresolverr.ddns.net";

        private readonly bool _isSharedClient;

        public bool IsInitialized { get; private set; }
        public WikiInstance WikiContext { get; set; }

        static ProxyHttpClientApiWorker()
        {
#if UWP_1507
            _sharedGatekeeperClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "WikiViewer/1.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1507 (System.Net.Http.HttpClient)"
            );
#elif UWP_1809
            _sharedGatekeeperClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "WikiViewer/2.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1809 (System.Net.Http.HttpClient)"
            );
#else
            _sharedGatekeeperClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "WikiViewer (https://github.com/megabytesme/WikiViewer) (System.Net.Http.HttpClient)"
            );
#endif
        }

        public ProxyHttpClientApiWorker()
            : this(false) { }

        public ProxyHttpClientApiWorker(bool useDedicatedClient)
        {
            if (useDedicatedClient)
            {
                _gatekeeperClient = new HttpClient();
                _isSharedClient = false;
            }
            else
            {
#if UWP_1507
                _sharedGatekeeperClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "WikiViewer/1.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1507 (System.Net.Http.HttpClient)"
                );
#elif UWP_1809
                _sharedGatekeeperClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "WikiViewer/2.0.1.0 (https://github.com/megabytesme/WikiViewer) UWP/1809 (System.Net.Http.HttpClient)"
                );
#else
                _sharedGatekeeperClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "WikiViewer (https://github.com/megabytesme/WikiViewer) (System.Net.Http.HttpClient)"
                );
#endif
                _gatekeeperClient = _sharedGatekeeperClient;
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
            string jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try
            {
                response = await _gatekeeperClient.PostAsync(
                    GatekeeperEndpoint + "/v1/request",
                    content
                );
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to connect to the gatekeeper proxy service.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception(
                    $"Proxy returned a non-success status code: {response.StatusCode} - {errorContent}"
                );
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                using (var compressedStream = new MemoryStream(bytes))
                using (
                    var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress)
                )
                using (var resultStream = new MemoryStream())
                {
                    await gzipStream.CopyToAsync(resultStream);
                    bytes = resultStream.ToArray();
                }
            }
            return bytes;
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
            var payload = new { url, postData };
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
