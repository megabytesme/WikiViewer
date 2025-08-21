using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public class WikiPaths
    {
        public string ScriptPath { get; set; }
        public string ArticlePath { get; set; }
        public bool WasDetectedSuccessfully => ScriptPath != null && ArticlePath != null;
    }

    public static class WikiPathDetectorService
    {
        public static async Task<WikiPaths> DetectPathsAsync(string baseUrl, IApiWorker worker)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                return new WikiPaths();
            }

            try
            {
                string mainPageHtml = await worker.GetRawHtmlFromUrlAsync(baseUrl);

                if (string.IsNullOrEmpty(mainPageHtml))
                {
                    Debug.WriteLine($"[PathDetector] Fetched empty HTML for {baseUrl}.");
                    return new WikiPaths();
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(mainPageHtml);

                string scriptPath = DetectScriptPath(doc, baseUri);
                string articlePath = DetectArticlePath(doc, baseUri);

                return new WikiPaths { ScriptPath = scriptPath, ArticlePath = articlePath };
            }
            catch (NeedsUserVerificationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[PathDetector] Failed to detect paths for {baseUrl}: {ex.Message}"
                );
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

                    int apiIndex = apiPath.LastIndexOf(
                        "/api.php",
                        StringComparison.OrdinalIgnoreCase
                    );
                    if (apiIndex != -1)
                    {
                        string path = apiPath.Substring(0, apiIndex).TrimStart('/');
                        return string.IsNullOrEmpty(path) ? "" : path + "/";
                    }
                }
            }
            Debug.WriteLine(
                "[PathDetector] Could not find EditURI link. Script path detection failed."
            );
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
                    var canonicalUri = new Uri(href);
                    string canonicalPath = canonicalUri.AbsolutePath;
                    string pageTitle = canonicalUri.Segments.LastOrDefault()?.Trim('/');

                    if (string.IsNullOrEmpty(pageTitle))
                    {
                        Debug.WriteLine(
                            "[PathDetector] Found article path '' (root) via canonical link (root URL)."
                        );
                        return "";
                    }

                    int pageTitleIndex = canonicalPath.LastIndexOf(pageTitle);
                    if (pageTitleIndex > 0)
                    {
                        string articlePath = canonicalPath.Substring(0, pageTitleIndex);
                        Debug.WriteLine(
                            $"[PathDetector] Found article path '{articlePath}' via canonical link."
                        );
                        return articlePath.TrimStart('/');
                    }
                    else if (
                        pageTitleIndex == 0
                        || (pageTitleIndex == 1 && canonicalPath.StartsWith("/"))
                    )
                    {
                        Debug.WriteLine(
                            "[PathDetector] Found article path '' (root) via canonical link (root article)."
                        );
                        return "";
                    }
                }
            }

            Debug.WriteLine(
                "[PathDetector] Canonical link failed or missing. Falling back to analyzing internal links."
            );
            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (linkNodes == null)
            {
                Debug.WriteLine("[PathDetector] Fallback failed: No <a> tags found.");
                return null;
            }

            if (linkNodes.Any(n => n.GetAttributeValue("href", "").StartsWith("/wiki/")))
            {
                Debug.WriteLine("[PathDetector] Found article path 'wiki/' via link analysis.");
                return "wiki/";
            }

            Debug.WriteLine(
                "[PathDetector] No '/wiki/' links found. Assuming empty article path as final fallback."
            );
            return "";
        }
    }
}
