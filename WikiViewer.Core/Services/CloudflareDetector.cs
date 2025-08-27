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
                    return true;
                }
            }

            return false;
        }
    }
}
