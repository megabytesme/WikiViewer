using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

namespace WikiViewer.Core.Services
{
    public sealed class FaviconResult
    {
        public Uri Source { get; set; }
        public byte[] Bytes { get; set; }
        public string Format { get; set; }
        public List<string> Failures { get; } = new List<string>();
    }

    public static class FaviconDiscoveryService
    {
        public static List<Uri> FromHtml(string html, Uri page)
        {
            var doc = new HtmlDocument(); doc.LoadHtml(html ?? "");
            var baseElement = doc.DocumentNode.SelectSingleNode("//base[@href]");
            var canonical = doc.DocumentNode.SelectNodes("//link[@href]")?.FirstOrDefault(n => Rel(n, "canonical"));
            var origin = Resolve(baseElement?.GetAttributeValue("href", "") ?? canonical?.GetAttributeValue("href", ""), page) ?? page;
            return (doc.DocumentNode.SelectNodes("//link[@href]") ?? Enumerable.Empty<HtmlNode>())
                .Where(n => Rel(n, "icon") || Rel(n, "apple-touch-icon") || Rel(n, "apple-touch-icon-precomposed"))
                .Select(n => Resolve(n.GetAttributeValue("href", ""), origin)).Where(u => u != null)
                .OrderBy(u => u.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? 0 : u.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? 2 : 1)
                .Distinct().Take(6).ToList();
        }
        private static bool Rel(HtmlNode node, string value) => node.GetAttributeValue("rel", "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Contains(value, StringComparer.OrdinalIgnoreCase);
        private static Uri Resolve(string value, Uri page)
        {
            Uri uri;
            return !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(page, WebUtility.HtmlDecode(value), out uri) &&
                (uri.Scheme == "https" || uri.Scheme == "http") && uri.UserInfo.Length == 0 ? uri : null;
        }
        public static string ImageFormat(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > 2000000) return null;
            if (bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "png";
            if (bytes.Length >= 22 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0 && (bytes[4] > 0 || bytes[5] > 0)) return "ico";
            if (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "jpg";
            if (bytes[0] == 71 && bytes[1] == 73 && bytes[2] == 70 && bytes[3] == 56 && (bytes[4] == 55 || bytes[4] == 57) && bytes[5] == 97) return "gif";
            if (bytes[0] == 66 && bytes[1] == 77) return "bmp";
            return null; // Do not cache challenge/error HTML or unsupported vector markup as a bitmap.
        }
        public static async Task<FaviconResult> FetchAsync(Uri page, string endpoint, Func<string, Task<string>> json, Func<string, Task<string>> html, Func<string, Task<byte[]>> download)
        {
            var result = new FaviconResult(); var attempted = new HashSet<string>(StringComparer.Ordinal);
            Func<Uri, Task<bool>> tryIcon = async uri =>
            {
                if (uri == null || !attempted.Add(uri.AbsoluteUri)) return false;
                try
                {
                    var bytes = await download(uri.AbsoluteUri); string format = ImageFormat(bytes);
                    if (format == null) { result.Failures.Add(uri.Host + uri.AbsolutePath + ": not a supported raster image"); return false; }
                    result.Source = uri; result.Bytes = bytes; result.Format = format; return true;
                }
                catch (Exception ex) { result.Failures.Add(uri.Host + uri.AbsolutePath + ": " + ex.GetType().Name); return false; }
            };
            Uri logo = null;
            try
            {
                var general = JObject.Parse(await json(endpoint + "?action=query&meta=siteinfo&siprop=general&format=json"))["query"]?["general"];
                var icon = general?["favicon"]; var logoValue = general?["logo"];
                if (logoValue?.Type == JTokenType.String) logo = Resolve((string)logoValue, page);
                if (icon?.Type == JTokenType.String && await tryIcon(Resolve((string)icon, page))) return result;
            }
            catch (Exception ex) { result.Failures.Add("siteinfo: " + ex.GetType().Name); }
            try { foreach (var icon in FromHtml(await html(page.AbsoluteUri), page)) if (await tryIcon(icon)) return result; }
            catch (Exception ex) { result.Failures.Add("page discovery: " + ex.GetType().Name); }
            if (await tryIcon(new Uri(page, "/favicon.ico"))) return result;
            if (await tryIcon(new Uri(page, "favicon.ico"))) return result;
            await tryIcon(logo);
            return result;
        }
    }
}
