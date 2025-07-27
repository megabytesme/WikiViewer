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
        private static bool _isLoggedIn = false;
        private const string ApiUrl = "https://betawiki.net/api.php";

        public static bool IsLoggedIn
        {
            get => _isLoggedIn;
            private set
            {
                if (_isLoggedIn != value)
                {
                    _isLoggedIn = value;
                    AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public static string Username { get; private set; }
        public static string CsrfToken { get; private set; }

        private static void SetLoginState(string username, string csrfToken)
        {
            Username = username;
            CsrfToken = csrfToken;
            IsLoggedIn = true;
        }

        public static void Logout()
        {
            Username = null;
            CsrfToken = null;
            IsLoggedIn = false;
            CredentialService.ClearCredentials();
            Debug.WriteLine("[AuthService] User logged out and credentials cleared.");
        }

        public static async Task<string> PerformLoginAsync(string username, string password, ApiHelper apiHelper)
        {
            if (apiHelper == null) throw new InvalidOperationException("ApiHelper cannot be null.");

            string tokenUrl = $"{ApiUrl}?action=query&meta=tokens&type=login&format=json";
            string tokenJson = await apiHelper.GetAsync(tokenUrl);
            if (string.IsNullOrWhiteSpace(tokenJson)) throw new Exception("API returned empty response for login token.");

            var tokenResponse = JsonSerializer.Deserialize<LoginApiTokenResponse>(tokenJson);
            string loginToken = tokenResponse?.query?.tokens?.logintoken;
            if (string.IsNullOrEmpty(loginToken)) throw new Exception($"Failed to retrieve login token. Response: {tokenJson}");

            var loginPostData = new Dictionary<string, string>
            {
                { "action", "login" }, { "format", "json" }, { "lgname", username },
                { "lgpassword", password }, { "lgtoken", loginToken }
            };
            string loginJson = await apiHelper.PostAsync(ApiUrl, loginPostData);
            if (string.IsNullOrWhiteSpace(loginJson)) throw new Exception("API returned empty response for login POST.");

            var loginResponse = JsonSerializer.Deserialize<LoginResultResponse>(loginJson);
            if (loginResponse?.login?.result != "Success") throw new Exception($"Login failed: {loginResponse?.login?.result ?? "Unknown"}.");

            string csrfUrl = $"{ApiUrl}?action=query&meta=tokens&format=json";
            string csrfJson = await apiHelper.GetAsync(csrfUrl);
            if (string.IsNullOrWhiteSpace(csrfJson)) throw new Exception("API returned empty response for CSRF token.");

            var csrfResponse = JsonSerializer.Deserialize<CsrfTokenResponse>(csrfJson);
            string csrfToken = csrfResponse?.query?.tokens?.csrftoken;
            if (string.IsNullOrEmpty(csrfToken) || csrfToken == "+\\") throw new Exception("Failed to retrieve a valid CSRF token after login.");

            SetLoginState(loginResponse.login.lgusername, csrfToken);

            return loginResponse.login.lgusername;
        }
    }
}