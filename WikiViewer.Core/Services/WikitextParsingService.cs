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

        public static string FixRelativeUrls(string html, WikiInstance wikiContext)
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

        public static async Task<string> ParseWikitextToPreviewHtmlAsync(string wikitext, string pageTitle, WikiInstance wikiContext, IApiWorker worker)
        {
            try { return (await ParseEditorPreviewAsync(wikitext, pageTitle, wikiContext, worker)).WebHtml; }
            catch (Exception ex)
            {
                return WrapInHtmlShell(WebUtility.HtmlEncode("Error generating preview: " + ex.Message),
                    Application.Current.RequestedTheme == ApplicationTheme.Dark, true);
            }
        }

        public static async Task<ArticlePreview> ParseEditorPreviewAsync(string wikitext, string pageTitle, WikiInstance wikiContext, IApiWorker worker)
        {
            if (wikiContext == null) throw new ArgumentNullException(nameof(wikiContext));
            if (worker == null) throw new ArgumentNullException(nameof(worker));
            string json = await worker.PostAndGetJsonFromApiAsync(wikiContext.ApiEndpoint, EditorPreviewResponse.BuildRequest(wikitext, pageTitle));
            var preview = EditorPreviewResponse.Parse(json, wikitext, pageTitle);
            string scriptPath = (wikiContext.ScriptPath ?? "").Trim('/');
            string cssUrl = preview.StyleModules.Count == 0 ? "" : wikiContext.BaseUrl.TrimEnd('/') + "/" +
                (scriptPath.Length == 0 ? "" : scriptPath + "/") + "load.php?modules=" + Uri.EscapeDataString(string.Join("|", preview.StyleModules)) + "&only=styles";
            string headContent = "<meta name='viewport' content='width=device-width, initial-scale=1.0'>" +
                "<base href='" + WebUtility.HtmlEncode(wikiContext.GetWikiPageUrl(pageTitle)) + "'>" +
                (cssUrl.Length == 0 ? "" : "<link rel='stylesheet' href='" + WebUtility.HtmlEncode(cssUrl) + "'>") +
                "<style>" + await ThemeManager.GetThemeCssAsync() + "</style>";
            string body = "<div class='mw-parser-output'>" + preview.Content.HtmlContent + "</div></body></html>";
            int headEnd = (preview.HeadHtml ?? "").IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            preview.WebHtml = headEnd >= 0 ? preview.HeadHtml.Insert(headEnd, headContent) + body :
                "<!DOCTYPE html><html><head><meta charset='utf-8'>" + headContent + "</head><body>" + body;
            return preview;
        }
    }
}
