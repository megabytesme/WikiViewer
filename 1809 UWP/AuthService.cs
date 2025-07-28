using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace _1809_UWP
{
    public static class AuthService
    {
        public static event EventHandler AuthenticationStateChanged;
        public static bool IsLoggedIn { get; private set; }
        public static string Username { get; private set; }
        private static string _csrfToken;
        private const string ApiUrl = "https://betawiki.net/api.php";

        public static async Task PerformLoginAsync(string username, string password)
        {
            await WebViewApiService.NavigateAsync($"{ApiUrl}?action=query&meta=tokens&type=login&format=json");
            string tokenJson = await WebViewApiService.GetStringContentAsync();
            var tokenResponse = JsonSerializer.Deserialize<LoginApiTokenResponse>(tokenJson);
            string loginToken = tokenResponse?.query?.tokens?.logintoken;
            if (string.IsNullOrEmpty(loginToken)) throw new Exception("Failed to retrieve a login token.");

            var loginPostData = new Dictionary<string, string>
            {
                { "action", "clientlogin" },
                { "format", "json" },
                { "username", username },
                { "password", password },
                { "logintoken", loginToken },
                { "loginreturnurl", "https://betawiki.net" }
            };

            await WebViewApiService.NavigateWithPostAsync(ApiUrl, loginPostData);
            string resultJson = await WebViewApiService.GetStringContentAsync();

            var resultResponse = JsonSerializer.Deserialize<ClientLoginResponse>(resultJson);

            if (resultResponse?.clientlogin?.status != "PASS")
            {
                throw new Exception($"Login failed: {resultResponse?.clientlogin?.status ?? "Unknown API response"}");
            }

            IsLoggedIn = true;
            Username = resultResponse.clientlogin.username;
            await FetchCsrfTokenAsync();
            await SyncAndMergeFavouritesOnLogin();
            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static async Task SyncAndMergeFavouritesOnLogin()
        {
            List<string> localFavourites = FavouritesService.GetFavourites();
            var serverFavourites = await FetchWatchlistAsync();
            var titlesToSync = new List<string>();
            foreach (var localTitle in localFavourites)
            {
                if (serverFavourites.Add(localTitle)) { titlesToSync.Add(localTitle); }
            }
            if (titlesToSync.Any()) { await SyncMultipleFavouritesToServerAsync(titlesToSync, add: true); }
            await FavouritesService.OverwriteLocalFavouritesAsync(serverFavourites);
        }

        public static async void Logout()
        {
            if (!IsLoggedIn || string.IsNullOrEmpty(_csrfToken)) return;

            var logoutPostData = new Dictionary<string, string>
            {
                { "action", "logout" },
                { "format", "json" },
                { "token", _csrfToken }
            };

            try
            {
                await WebViewApiService.NavigateWithPostAsync(ApiUrl, logoutPostData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthService] Logout API call failed, but proceeding with local logout: {ex.Message}");
            }
            finally
            {
                IsLoggedIn = false;
                Username = null;
                _csrfToken = null;
                CredentialService.ClearCredentials();
                await FavouritesService.ClearAllLocalFavouritesAsync();
                AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static async Task SyncSingleFavoriteToServerAsync(string pageTitle, bool add)
        {
            if (!IsLoggedIn || string.IsNullOrEmpty(_csrfToken)) return;

            var postBody = $"action=watch&format=json&titles={Uri.EscapeDataString(pageTitle)}&token={Uri.EscapeDataString(_csrfToken)}";
            if (!add)
            {
                postBody += "&unwatch=";
            }

            Debug.WriteLine($"[AuthService] Manually built POST body: {postBody}");

            try
            {
                await WebViewApiService.NavigateWithPostAsync(ApiUrl, postBody);

                string responseJson = await WebViewApiService.GetStringContentAsync();
                var response = JsonSerializer.Deserialize<WatchActionResponse>(responseJson);

                if (add && (response?.Watch == null || !response.Watch.Any()))
                {
                    throw new Exception("Server did not confirm watch action.");
                }
                else if (!add && (response?.Unwatch == null || !response.Unwatch.Any()))
                {
                    throw new Exception("Server did not confirm unwatch action.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync single favorite '{pageTitle}': {ex.Message}");
            }
        }

        private static async Task SyncMultipleFavouritesToServerAsync(List<string> titles, bool add)
        {
            if (!IsLoggedIn || string.IsNullOrEmpty(_csrfToken) || !titles.Any()) return;

            string batchedTitles = string.Join("|", titles);

            var postBody = $"action=watch&format=json&titles={Uri.EscapeDataString(batchedTitles)}&token={Uri.EscapeDataString(_csrfToken)}";
            if (!add)
            {
                postBody += "&unwatch=";
            }

            Debug.WriteLine($"[AuthService] Manually built POST body: {postBody}");

            try
            {
                await WebViewApiService.NavigateWithPostAsync(ApiUrl, postBody);

                string responseJson = await WebViewApiService.GetStringContentAsync();
                var response = JsonSerializer.Deserialize<WatchActionResponse>(responseJson);

                if (add && (response?.Watch == null || !response.Watch.Any()))
                {
                    throw new Exception("Server did not confirm watch action.");
                }
                else if (!add && (response?.Unwatch == null || !response.Unwatch.Any()))
                {
                    throw new Exception("Server did not confirm unwatch action.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync batch Favourites: {ex.Message}");
            }
        }

        private static async Task<HashSet<string>> FetchWatchlistAsync()
        {
            if (!IsLoggedIn) return new HashSet<string>();
            await WebViewApiService.NavigateAsync($"{ApiUrl}?action=query&list=watchlistraw&wrlimit=max&format=json");
            string watchlistJson = await WebViewApiService.GetStringContentAsync();
            var watchlistResponse = JsonSerializer.Deserialize<WatchlistApiResponse>(watchlistJson);
            var serverFavourites = new HashSet<string>();
            if (watchlistResponse?.WatchlistRaw != null)
            {
                foreach (var item in watchlistResponse.WatchlistRaw)
                {
                    serverFavourites.Add(item.Title);
                }
            }

            return serverFavourites;
        }

        private static async Task FetchCsrfTokenAsync()
        {
            string responseJson = null;
            int retries = 0;
            const int maxRetries = 5;

            while (retries < maxRetries)
            {
                await WebViewApiService.NavigateAsync($"{ApiUrl}?action=query&meta=tokens&type=watch&format=json");
                string html = await WebViewApiService.GetWebView().ExecuteScriptAsync("document.documentElement.outerHTML");
                string fullHtml = JsonSerializer.Deserialize<string>(html ?? "null");

                if (!string.IsNullOrEmpty(fullHtml) && (fullHtml.Contains("Verifying you are human") || fullHtml.Contains("checking your browser")))
                {
                    Debug.WriteLine("[AuthService] Token fetch blocked by challenge page. Retrying...");
                    retries++;
                    await Task.Delay(1000);
                    continue;
                }

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(fullHtml);
                string potentialJson = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText ?? doc.DocumentNode.InnerText;

                if (!string.IsNullOrWhiteSpace(potentialJson) && potentialJson.Trim().StartsWith("{"))
                {
                    responseJson = potentialJson.Trim();
                    break;
                }

                Debug.WriteLine("[AuthService] Token fetch did not return valid JSON. Retrying...");
                retries++;
                await Task.Delay(500);
            }

            if (string.IsNullOrEmpty(responseJson))
            {
                throw new Exception("Failed to retrieve token response after multiple retries.");
            }

            var tokenResponse = JsonSerializer.Deserialize<WatchTokenResponse>(responseJson);
            string rawToken = tokenResponse?.Query?.Tokens?.WatchToken;

            if (string.IsNullOrEmpty(rawToken))
            {
                throw new Exception("Failed to parse a watch token from the server response.");
            }

            _csrfToken = rawToken;
            Debug.WriteLine($"[AuthService] Storing complete raw watch token. Value is: [{_csrfToken}]");
        }
    }
}