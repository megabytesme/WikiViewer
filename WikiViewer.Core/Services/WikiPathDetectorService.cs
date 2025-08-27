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

                if (CloudflareDetector.IsCloudflareChallenge(mainPageHtml))
                {
                    Debug.WriteLine($"[PathDetector] Cloudflare challenge detected for {baseUrl}.");
                    throw new NeedsUserVerificationException(
                        "Cloudflare challenge detected.",
                        baseUrl
                    );
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
            var xpathsToTry = new[]
            {
                "//li[@id='n-randompage']/a",
                "//a[contains(@href, 'Special:RandomRootpage')]",
                "//a[contains(@href, 'Special:Random')]",
            };

            foreach (var xpath in xpathsToTry)
            {
                var linkNode = doc.DocumentNode.SelectSingleNode(xpath);
                if (linkNode != null)
                {
                    var href = linkNode.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href))
                    {
                        try
                        {
                            var fullUri = new Uri(baseUri, href);
                            var absolutePath = fullUri.AbsolutePath;

                            int specialIndex = absolutePath.IndexOf(
                                "Special:",
                                StringComparison.OrdinalIgnoreCase
                            );
                            if (specialIndex > 0)
                            {
                                string articlePath = absolutePath.Substring(0, specialIndex);
                                Debug.WriteLine(
                                    $"[PathDetector] Found article path '{articlePath.TrimEnd('/')}' via XPath '{xpath}'."
                                );
                                return articlePath.TrimEnd('/');
                            }
                        }
                        catch (UriFormatException ex)
                        {
                            Debug.WriteLine(
                                $"[PathDetector] Failed to parse href '{href}' with XPath '{xpath}': {ex.Message}"
                            );
                        }
                    }
                }
            }

            Debug.WriteLine(
                "[PathDetector] All 'Special:Random' link detection methods failed. Falling back to canonical."
            );

            var canonicalNode = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']");
            if (canonicalNode != null)
            {
                var href = canonicalNode.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href))
                {
                    try
                    {
                        var canonicalUri = new Uri(href);
                        string canonicalPath = canonicalUri.AbsolutePath.Trim('/');
                        string pageTitle = canonicalUri.Segments.LastOrDefault()?.Trim('/');

                        if (string.IsNullOrEmpty(pageTitle) || canonicalPath == pageTitle)
                        {
                            Debug.WriteLine(
                                "[PathDetector] Canonical link points to root, inconclusive. Proceeding to final fallback."
                            );
                        }
                        else
                        {
                            int pageTitleIndex = canonicalPath.LastIndexOf(pageTitle);
                            if (pageTitleIndex > 0)
                            {
                                string articlePath = canonicalPath.Substring(0, pageTitleIndex);
                                Debug.WriteLine(
                                    $"[PathDetector] Found article path '{articlePath.TrimEnd('/')}' via canonical link."
                                );
                                return articlePath.TrimEnd('/');
                            }
                        }
                    }
                    catch (UriFormatException ex)
                    {
                        Debug.WriteLine(
                            $"[PathDetector] Failed to parse canonical href '{href}': {ex.Message}"
                        );
                    }
                }
            }

            Debug.WriteLine(
                "[PathDetector] All methods failed. Assuming empty article path as final resort."
            );
            return "";
        }
    }
}
