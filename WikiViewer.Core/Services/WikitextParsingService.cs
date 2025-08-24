using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using WikitextParser;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using Windows.UI.Xaml;

namespace WikiViewer.Core.Services
{
    public static class WikitextParsingService
    {
        public static string ParseToFullHtmlDocument(
            string wikitext,
            WikiInstance wikiContext,
            bool isDarkMode
        )
        {
            if (string.IsNullOrEmpty(wikitext) || wikiContext == null)
            {
                return WrapInHtmlShell("", isDarkMode, true);
            }

            string bodyContent;
            bool isShortMessage = true;
            bool isRichContent = Regex.IsMatch(
                wikitext,
                @"(\[\[.*?\]\]|<.*?>|&.*?;|\/index\.php\?)"
            );

            if (!isRichContent)
            {
                bodyContent = $"<p>{WebUtility.HtmlEncode(wikitext)}</p>";
            }
            else if (wikitext.Contains("[[") || wikitext.Contains("{{"))
            {
                var page = Parser.ParsePage(wikitext);
                bodyContent = page.ConvertToHtml();
                isShortMessage = false;
            }
            else
            {
                bodyContent = wikitext;
            }

            string finalBodyContent = FixRelativeUrls(bodyContent, wikiContext);
            return WrapInHtmlShell(finalBodyContent, isDarkMode, isShortMessage);
        }

        private static string FixRelativeUrls(string html, WikiInstance wikiContext)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var baseUri = new Uri(wikiContext.BaseUrl);

            foreach (
                var node in doc.DocumentNode.SelectNodes("//*[@href or @src]")
                    ?? Enumerable.Empty<HtmlNode>()
            )
            {
                string attributeName = node.Attributes["href"] != null ? "href" : "src";
                string url = node.GetAttributeValue(attributeName, "");

                if (string.IsNullOrEmpty(url))
                    continue;

                if (url.StartsWith("/") || url.StartsWith("#"))
                {
                    try
                    {
                        var absoluteUri = new Uri(baseUri, url);
                        node.SetAttributeValue(attributeName, absoluteUri.AbsoluteUri);
                    }
                    catch (UriFormatException) { }
                }
            }
            return doc.DocumentNode.OuterHtml;
        }

        private static string WrapInHtmlShell(
            string bodyContent,
            bool isDarkMode,
            bool isShortMessage
        )
        {
            string textColor = isDarkMode ? "white" : "black";
            string linkColor = isDarkMode ? "#85B9F3" : "#0066CC";

            string bodyStyles = isShortMessage
                ? "display: flex; align-items: center; justify-content: center; text-align: center; min-height: 80vh;"
                : "";

            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='UTF-8'>
                    <style>
                        body {{ 
                            font-family: 'Segoe UI', sans-serif; 
                            color: {textColor}; 
                            background-color: transparent; 
                            margin: 0; 
                            padding: 8px; 
                            font-size: 14px; 
                            {bodyStyles}
                        }} 
                        img {{ max-width: 100%; height: auto; }} 
                        a {{ color: {linkColor}; }}
                    </style>
                </head>
                <body>
                    {bodyContent}
                </body>
                </html>";
        }

        public static async Task<string> ParseWikitextToPreviewHtmlAsync(
            string wikitext,
            string pageTitle,
            WikiInstance wikiContext,
            IApiWorker worker
        )
        {
            if (wikiContext == null || worker == null)
            {
                return WrapInHtmlShell(
                    "Error: Cannot generate preview without a valid wiki context.",
                    Application.Current.RequestedTheme == ApplicationTheme.Dark,
                    true
                );
            }

            var postData = new Dictionary<string, string>
            {
                { "action", "parse" },
                { "format", "json" },
                { "text", wikitext },
                { "title", pageTitle },
                { "prop", "text|modules|jsconfigvars" },
                { "pst", "true" },
                { "disablelimitreport", "true" },
                { "disableeditsection", "true" },
                { "preview", "true" },
            };

            try
            {
                string json = await worker.PostAndGetJsonFromApiAsync(
                    wikiContext.ApiEndpoint,
                    postData
                );
                var response = JObject.Parse(json);

                if (response["error"] != null)
                {
                    throw new Exception(
                        response["error"]["info"]?.ToString()
                            ?? "Unknown API error during preview generation."
                    );
                }

                string previewHtml = response["parse"]?["text"]?["*"]?.ToString();
                if (string.IsNullOrEmpty(previewHtml))
                {
                    return WrapInHtmlShell(
                        "The server returned an empty preview.",
                        Application.Current.RequestedTheme == ApplicationTheme.Dark,
                        true
                    );
                }

                var modules = response["parse"]?["modules"]?.Select(m => m.ToString());
                var moduleStyles = response["parse"]?["modulestyles"]?.Select(m => m.ToString());
                var allModules = (modules ?? Enumerable.Empty<string>())
                    .Concat(moduleStyles ?? Enumerable.Empty<string>())
                    .Distinct();

                string cssUrl = "";
                if (allModules.Any())
                {
                    cssUrl =
                        $"{wikiContext.BaseUrl.TrimEnd('/')}/{wikiContext.ScriptPath.TrimEnd('/')}/load.php?modules={string.Join("|", allModules)}&only=styles";
                }

                string headContent =
                    $@"
            <meta charset='UTF-8'>
            <base href='{wikiContext.BaseUrl}'>
            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            {(string.IsNullOrEmpty(cssUrl) ? "" : $"<link rel='stylesheet' href='{cssUrl}'>")}
            {ArticleProcessingService.GetCssForTheme()}
        ";

                return $"<!DOCTYPE html><html><head>{headContent}</head><body><div class='mw-parser-output'>{previewHtml}</div></body></html>";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Preview Generation] Failed: {ex.Message}");
                return WrapInHtmlShell(
                    $"Error generating preview: {ex.Message}",
                    Application.Current.RequestedTheme == ApplicationTheme.Dark,
                    true
                );
            }
        }
    }
}
