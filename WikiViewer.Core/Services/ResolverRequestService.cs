using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class ResolverRequestService
    {
        public static async Task<byte[]> SendAsync(HttpClient client, ResolverConfiguration configuration, object payload)
        {
            if (configuration == null) throw new InvalidOperationException("Configure a resolver URL, username and password in Settings > Connection before using Proxy.");
            using (var request = new HttpRequestMessage(HttpMethod.Post, ResolverConfigurationService.NormalizeEndpoint(configuration.Endpoint.AbsoluteUri)))
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
            {
                string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(configuration.Username + ":" + configuration.Password));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try { response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token); }
                catch (OperationCanceledException) { throw new TimeoutException("The resolver did not respond within 90 seconds. Check the resolver service or choose another connection method."); }
                catch (HttpRequestException) { throw new InvalidOperationException("Could not connect to the resolver. Check its URL and your internet connection."); }
                using (response)
                {
                    int status = (int)response.StatusCode;
                    if (status == 401 || status == 403) throw new InvalidOperationException("Resolver authentication was rejected. Check the username and password in Settings > Connection.");
                    if (status >= 300 && status < 400) throw new InvalidOperationException("The resolver redirected the request. Enter its final HTTPS URL in Settings > Connection.");
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The resolver returned HTTP " + status + ". Check the resolver service and try again.");
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
                    {
                        using (var input = new MemoryStream(bytes))
                        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                        using (var output = new MemoryStream()) { await gzip.CopyToAsync(output); bytes = output.ToArray(); }
                    }
                    return bytes;
                }
            }
        }
    }
}
