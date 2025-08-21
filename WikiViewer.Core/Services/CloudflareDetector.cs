namespace WikiViewer.Core.Services
{
    public static class CloudflareDetector
    {
        private static readonly string[] cloudflareMarkers =
        {
            "challenge-running",
            "Just a second...",
            "Just a moment...",
            "Verifying you are human",
            "Please stand by, while we are checking your browser...",
            "Checking your browser before accessing",
            "This process is automatic",
            "g-recaptcha",
            "cf-challenge-running",
            "cf-im-under-attack"
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