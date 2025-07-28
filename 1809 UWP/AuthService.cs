using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace _1809_UWP
{
    public static class AuthService
    {
        public static event EventHandler AuthenticationStateChanged;
        public static bool IsLoggedIn { get; private set; }
        public static string Username { get; private set; }
        private const string ApiUrl = "https://betawiki.net/api.php";

        private static async Task<string> GetStringFromWebView()
        {
            var webView = WebViewApiService.GetWebView();
            if (webView == null) throw new InvalidOperationException("WebView not available.");
            string html = await webView.ExecuteScriptAsync("document.documentElement.outerHTML");
            string fullHtml = JsonSerializer.Deserialize<string>(html ?? "null");
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(fullHtml);
            return doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText;
        }

        public static async Task PerformLoginAsync(string username, string password)
        {
            await WebViewApiService.NavigateAsync($"{ApiUrl}?action=query&meta=tokens&type=login&format=json");
            string tokenJson = await GetStringFromWebView();
            var tokenResponse = JsonSerializer.Deserialize<LoginApiTokenResponse>(tokenJson);
            string loginToken = tokenResponse?.query?.tokens?.logintoken;

            if (string.IsNullOrEmpty(loginToken))
            {
                throw new Exception("Failed to retrieve a login token.");
            }

            var loginPostData = new Dictionary<string, string>
            {
                { "action", "login" }, { "format", "json" }, { "lgname", username },
                { "lgpassword", password }, { "lgtoken", loginToken }
            };

            await WebViewApiService.NavigateWithPostAsync(ApiUrl, loginPostData);
            string resultJson = await GetStringFromWebView();
            var resultResponse = JsonSerializer.Deserialize<LoginResultResponse>(resultJson);

            if (resultResponse?.login?.result == "Success")
            {
                IsLoggedIn = true;
                Username = resultResponse.login.lgusername;
                AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
            }
            else
            {
                throw new Exception($"Login failed: {resultResponse?.login?.result ?? "Unknown error"}");
            }
        }

        public static void Logout()
        {
            IsLoggedIn = false;
            Username = null;
            CredentialService.ClearCredentials();
            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}