using System;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

namespace WikiViewer.Core.Services
{
    public static class CloudflareDetector
    {
        private static readonly string[] cloudflareMarkers =
        {
            "window._cf_chl_opt",
            "/cdn-cgi/challenge-platform/",
            "__cf_chl_tk",
            "cf-challenge-running",
            "cf-im-under-attack",
            "challenge-error-text",
            "Enable JavaScript and cookies to continue",
            "Verifying you are human",
            "Checking your browser before accessing",
            "This process is automatic",
            "Just a second...",
            "Just a moment...",
            "Please stand by, while we are checking your browser...",
            "challenge-running",
        };

        public static bool IsCloudflareChallenge(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return false;
            }

            foreach (var marker in cloudflareMarkers)
            {
                if (html.Contains(marker))
                {
                    // Real article/API content can mention these strings or include Cloudflare's
                    // background scripts. A script reference alone is not an interstitial.
                    string trimmed = html.TrimStart();
                    if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                    {
                        try { JToken.Parse(trimmed); return false; } catch (Newtonsoft.Json.JsonException) { }
                    }
                    var document = new HtmlDocument(); document.LoadHtml(html);
                    if (document.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' mw-parser-output ')]") != null &&
                        document.DocumentNode.SelectSingleNode("//link[@rel='EditURI']") != null) return false;
                    return true;
                }
            }

            return false;
        }
    }
}
