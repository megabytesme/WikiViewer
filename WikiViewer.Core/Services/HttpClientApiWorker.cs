using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core.Services
{
    public class HttpClientApiWorker : IApiWorker
    {
        private static readonly HttpClient _gatekeeperClient = new HttpClient();
        private const string GatekeeperEndpoint = "https://wikiflareresolverr.ddns.net";

        public bool IsInitialized { get; private set; }

        public Task InitializeAsync(string baseUrl = null)
        {
            IsInitialized = true;
            return Task.CompletedTask;
        }

        private string ExtractJsonFromHtml(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return responseText;
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
                var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
                if (bodyNode != null)
                {
                    string potentialJson = bodyNode.InnerText.Trim();
                    if (potentialJson.StartsWith("{") || potentialJson.StartsWith("["))
                        return potentialJson;
                }
            }
            catch { }
            return responseText;
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
                try
                {
                    var errorObj = JsonConvert.DeserializeObject<dynamic>(errorContent);
                    string errorMessage = errorObj.error ?? "Unknown proxy error";
                    string errorDetails = errorObj.details ?? "";
                    throw new Exception(
                        $"Proxy returned an error: {response.StatusCode} - {errorMessage} {errorDetails}"
                    );
                }
                catch
                {
                    throw new Exception(
                        $"Proxy returned a non-success status code: {response.StatusCode} - {errorContent}"
                    );
                }
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                using (var compressedStream = new System.IO.MemoryStream(bytes))
                using (
                    var gzipStream = new System.IO.Compression.GZipStream(
                        compressedStream,
                        System.IO.Compression.CompressionMode.Decompress
                    )
                )
                using (var resultStream = new System.IO.MemoryStream())
                {
                    await gzipStream.CopyToAsync(resultStream);
                    bytes = resultStream.ToArray();
                }
            }
            return bytes;
        }

        public async Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            var payload = new { url = url };
            return await MakeGatekeeperRequest(payload);
        }

        public async Task<string> GetRawHtmlFromUrlAsync(string url)
        {
            var bytes = await GetRawBytesFromUrlAsync(url);
            return Encoding.UTF8.GetString(bytes);
        }

        public async Task<string> GetJsonFromApiAsync(string url)
        {
            string responseText = await GetRawHtmlFromUrlAsync(url);
            return ExtractJsonFromHtml(responseText);
        }

        public async Task<string> PostAndGetJsonFromApiAsync(
            string url,
            Dictionary<string, string> postData
        )
        {
            var payload = new { url, postData };
            var bytes = await MakeGatekeeperRequest(payload);
            string responseText = Encoding.UTF8.GetString(bytes);
            return ExtractJsonFromHtml(responseText);
        }

        public Task CopyApiCookiesFromAsync(IApiWorker source) => Task.CompletedTask;

        public void Dispose() { }
    }
}
