using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.UI.Xaml.Controls;

namespace _1809_UWP
{
    public class WikiPaths
    {
        public string ScriptPath { get; set; }
        public string ArticlePath { get; set; }
        public bool WasDetectedSuccessfully => ScriptPath != null && ArticlePath != null;
    }

    public static class WikiPathDetectorService
    {
        public static async Task<WikiPaths> DetectPathsAsync(string baseUrl, WebView2 worker)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                return new WikiPaths();
            }

            try
            {
                string mainPageHtml = await ApiRequestService.GetRawHtmlFromUrlAsync(baseUrl, worker);

                if (string.IsNullOrEmpty(mainPageHtml))
                {
                    Debug.WriteLine($"[PathDetector] Fetched empty HTML for {baseUrl}.");
                    return new WikiPaths();
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(mainPageHtml);

                string scriptPath = DetectScriptPath(doc, baseUri);
                string articlePath = DetectArticlePath(doc, baseUri);

                return new WikiPaths
                {
                    ScriptPath = scriptPath,
                    ArticlePath = articlePath
                };
            }
            catch (NeedsUserVerificationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PathDetector] Failed to detect paths for {baseUrl}: {ex.Message}");
                return new WikiPaths();
            }
        }

        private static string DetectScriptPath(HtmlDocument doc, Uri baseUri)
        {
            var editUriNode = doc.DocumentNode.SelectSingleNode("//link[@rel='EditURI']");
            if (editUriNode != null)
            {
                var href = editUriNode.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href))
                {
                    var fullApiUri = new Uri(baseUri, href);
                    var apiPath = fullApiUri.AbsolutePath;

                    int apiIndex = apiPath.LastIndexOf("/api.php", StringComparison.OrdinalIgnoreCase);
                    if (apiIndex != -1)
                    {
                        string path = apiPath.Substring(0, apiIndex).TrimStart('/');
                        return string.IsNullOrEmpty(path) ? "" : path + "/";
                    }
                }
            }
            Debug.WriteLine("[PathDetector] Could not find EditURI link. Script path detection failed.");
            return null;
        }

        private static string DetectArticlePath(HtmlDocument doc, Uri baseUri)
        {
            var canonicalNode = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']");
            if (canonicalNode != null)
            {
                var href = canonicalNode.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href))
                {
                    var canonicalUri = new Uri(baseUri, href);
                    var canonicalPath = canonicalUri.AbsolutePath;

                    var pageName = canonicalUri.Segments.LastOrDefault()?.TrimEnd('/');

                    if (string.IsNullOrEmpty(pageName))
                    {
                        Debug.WriteLine("[PathDetector] Could not determine page name from canonical URL. Article path detection failed.");
                        return null;
                    }

                    int pageNameIndex = canonicalPath.LastIndexOf(pageName, StringComparison.OrdinalIgnoreCase);

                    if (pageNameIndex != -1)
                    {
                        string path = canonicalPath.Substring(0, pageNameIndex).TrimStart('/');
                        return path;
                    }
                }
            }
            Debug.WriteLine("[PathDetector] Could not find Canonical link. Article path detection failed.");
            return null;
        }
    }
}