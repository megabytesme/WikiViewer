using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WikiViewer.Core.Models;
using WikitextParser;

namespace WikiViewer.Core.Services
{
    public static class WikitextParsingService
    {
        public static string ParseToFullHtmlDocument(string wikitext, WikiInstance wikiContext, bool isDarkMode)
        {
            if (string.IsNullOrEmpty(wikitext) || wikiContext == null)

            {
                return WrapInHtmlShell("", isDarkMode, true);
            }

            string bodyContent;
            bool isShortMessage = true;
            bool isRichContent = Regex.IsMatch(wikitext, @"(\[\[.*?\]\]|<.*?>|&.*?;|\/index\.php\?)");

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
            if (string.IsNullOrEmpty(html)) return "";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var baseUri = new Uri(wikiContext.BaseUrl);

            foreach (var node in doc.DocumentNode.SelectNodes("//*[@href or @src]") ?? Enumerable.Empty<HtmlNode>())
            {
                string attributeName = node.Attributes["href"] != null ? "href" : "src";
                string url = node.GetAttributeValue(attributeName, "");

                if (string.IsNullOrEmpty(url)) continue;

                if (url.StartsWith("/") || url.StartsWith("#"))
                {
                    try
                    {
                        var absoluteUri = new Uri(baseUri, url);
                        node.SetAttributeValue(attributeName, absoluteUri.AbsoluteUri);
                    }
                    catch (UriFormatException) {}
                }
            }
            return doc.DocumentNode.OuterHtml;
        }

        private static string WrapInHtmlShell(string bodyContent, bool isDarkMode, bool isShortMessage)
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
    }
}