using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shared_Code
{
    public static class AuthService
    {
        public static event EventHandler AuthenticationStateChanged;
        public static bool IsLoggedIn { get; private set; }
        public static string Username { get; private set; }
        private static string _csrfToken;
        private static IApiWorker _authenticatedWorker;

        private static async Task<IApiWorker> CreateAndInitApiWorker()
        {
            IApiWorker worker = AppSettings.ConnectionBackend == ConnectionMethod.HttpClientProxy
                ? (IApiWorker)new HttpClientApiWorker()
                : new WebView2ApiWorker();

            await worker.InitializeAsync();
            return worker;
        }

        private static string ExtractJsonFromHtmlWrapper(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return rawResponse;
            }

            string trimmedResponse = rawResponse.Trim();
            if (trimmedResponse.StartsWith("{") && trimmedResponse.EndsWith("}"))
            {
                return trimmedResponse;
            }

            try
            {
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(rawResponse);

                var preNode = doc.DocumentNode.SelectSingleNode("//pre");
                if (preNode != null && !string.IsNullOrWhiteSpace(preNode.InnerText))
                {
                    string potentialJson = preNode.InnerText.Trim();
                    if (potentialJson.StartsWith("{") && potentialJson.EndsWith("}"))
                    {
                        Debug.WriteLine("[AuthService] Extracted JSON from HTML <pre> tag.");
                        return potentialJson;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthService] HTML parsing failed during JSON extraction: {ex.Message}");
            }

            return rawResponse;
        }

        public static async Task PerformLoginAsync(string username, string password)
        {
            _authenticatedWorker = await CreateAndInitApiWorker();

            Debug.WriteLine("[AuthService] Attempting to fetch login token...");
            string rawTokenResponse = await _authenticatedWorker.GetJsonFromApiAsync(
                $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&type=login&format=json&_={DateTime.Now.Ticks}"
            );

            string tokenJson = ExtractJsonFromHtmlWrapper(rawTokenResponse);

            if (string.IsNullOrEmpty(tokenJson))
            {
                throw new Exception("Could not get a valid response for login token (response was null or empty).");
            }

            Debug.WriteLine($"[AuthService] Cleaned login token response: {tokenJson}");

            LoginApiTokenResponse tokenResponse;
            try
            {
                tokenResponse = JsonConvert.DeserializeObject<LoginApiTokenResponse>(tokenJson);
            }
            catch (JsonReaderException ex)
            {
                Debug.WriteLine($"[AuthService] FATAL: Failed to parse login token JSON. The server likely returned an HTML error page.");
                throw new Exception($"The server's response for the login token was not valid JSON. Raw response: '{tokenJson}'", ex);
            }

            string loginToken = tokenResponse?.query?.tokens?.logintoken;
            if (string.IsNullOrEmpty(loginToken))
            {
                throw new Exception("Failed to retrieve a login token from the JSON response.");
            }
            Debug.WriteLine("[AuthService] Successfully parsed login token.");

            var loginPostData = new Dictionary<string, string>
            {
                { "action", "clientlogin" }, { "format", "json" }, { "username", username }, { "password", password }, { "logintoken", loginToken }, { "loginreturnurl", AppSettings.BaseUrl }
            };

            Debug.WriteLine("[AuthService] Posting credentials to log in...");
            string resultJson = await _authenticatedWorker.PostAndGetJsonFromApiAsync(AppSettings.ApiEndpoint, loginPostData);
            string cleanResultJson = ExtractJsonFromHtmlWrapper(resultJson);

            Debug.WriteLine($"[AuthService] Raw client login response: {resultJson}");
            Debug.WriteLine($"[AuthService] Cleaned client login response: {cleanResultJson}");

            ClientLoginResponse resultResponse;
            try
            {
                resultResponse = JsonConvert.DeserializeObject<ClientLoginResponse>(cleanResultJson);
            }
            catch (JsonReaderException ex)
            {
                Debug.WriteLine($"[AuthService] FATAL: Failed to parse client login response JSON.");
                throw new Exception($"The server's response after posting credentials was not valid JSON. Raw response: '{resultJson}'", ex);
            }

            if (resultResponse?.clientlogin?.status != "PASS")
            {
                _authenticatedWorker?.Dispose();
                _authenticatedWorker = null;
                throw new Exception($"Login failed: {resultResponse?.clientlogin?.status ?? "Unknown API response"}");
            }

            Debug.WriteLine("[AuthService] Login successful!");
            IsLoggedIn = true;
            Username = resultResponse.clientlogin.username;

            await FetchCsrfTokenAsync(_authenticatedWorker);
            await SyncAndMergeFavouritesOnLogin(_authenticatedWorker);

            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        public static async void Logout()
        {
            if (!IsLoggedIn) return;

            if (!string.IsNullOrEmpty(_csrfToken) && _authenticatedWorker != null)
            {
                var logoutPostData = new Dictionary<string, string>
                {
                    { "action", "logout" }, { "format", "json" }, { "token", _csrfToken },
                };
                try
                {
                    await _authenticatedWorker.PostAndGetJsonFromApiAsync(AppSettings.ApiEndpoint, logoutPostData);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AuthService] Logout API call failed, but proceeding with local logout: {ex.Message}");
                }
            }

            _authenticatedWorker?.Dispose();
            _authenticatedWorker = null;

            IsLoggedIn = false;
            Username = null;
            _csrfToken = null;
            CredentialService.ClearCredentials();
            await FavouritesService.ClearAllLocalFavouritesAsync();
            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static async Task SyncAndMergeFavouritesOnLogin(IApiWorker worker)
        {
            Debug.WriteLine("[AuthService] Starting sync process...");
            var pendingDeletes = FavouritesService.GetAndClearPendingDeletes();
            if (pendingDeletes.Any())
            {
                Debug.WriteLine($"[AuthService] Syncing {pendingDeletes.Count} offline deletions to server...");
                await SyncMultipleFavouritesToServerInternalAsync(worker, pendingDeletes, add: false);
            }
            var pendingAdds = FavouritesService.GetAndClearPendingAdds();
            if (pendingAdds.Any())
            {
                Debug.WriteLine($"[AuthService] Syncing {pendingAdds.Count} offline additions to server...");
                await SyncMultipleFavouritesToServerInternalAsync(worker, pendingAdds, add: true);
            }
            Debug.WriteLine("[AuthService] Fetching final list from server...");
            var serverFavourites = await FetchWatchlistAsync(worker);
            Debug.WriteLine("[AuthService] Overwriting local state with server's watchlist.");
            await FavouritesService.OverwriteLocalFavouritesAsync(serverFavourites);

            await BackgroundCacheService.CacheFavouritesAsync(serverFavourites);

            Debug.WriteLine("[AuthService] Sync complete.");
        }
        public static async Task SyncSingleFavoriteToServerAsync(string pageTitle, bool add)
        {
            await SyncMultipleFavouritesToServerAsync(new List<string> { pageTitle }, add);
        }

        public static async Task SyncMultipleFavouritesToServerAsync(List<string> titles, bool add)
        {
            if (!IsLoggedIn || _authenticatedWorker == null)
            {
                Debug.WriteLine("[AuthService] Sync attempt failed: Not logged in or worker is null.");
                return;
            }
            await SyncMultipleFavouritesToServerInternalAsync(_authenticatedWorker, titles, add);
        }

        private static async Task SyncMultipleFavouritesToServerInternalAsync(IApiWorker worker, List<string> titles, bool add)
        {
            if (!IsLoggedIn || string.IsNullOrEmpty(_csrfToken) || !titles.Any() || worker == null) return;
            string batchedTitles = string.Join("|", titles);

            var postData = new Dictionary<string, string>
    {
        {"action", "watch"}, {"format", "json"}, {"titles", batchedTitles}, {"token", _csrfToken}
    };
            if (!add)
            {
                postData.Add("unwatch", "");
            }

            try
            {
                string rawResponseJson = await worker.PostAndGetJsonFromApiAsync(AppSettings.ApiEndpoint, postData);
                string responseJson = ExtractJsonFromHtmlWrapper(rawResponseJson);

                var response = JsonConvert.DeserializeObject<WatchActionResponse>(responseJson);
                if (add && (response?.Watch == null || !response.Watch.Any())) throw new Exception("Server did not confirm watch action.");
                else if (!add && (response?.Unwatch == null || !response.Unwatch.Any())) throw new Exception("Server did not confirm unwatch action.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync batch Favourites: {ex.Message}");
            }
        }

        private static async Task<HashSet<string>> FetchWatchlistAsync(IApiWorker worker)
        {
            string url = $"{AppSettings.ApiEndpoint}?action=query&list=watchlistraw&wrlimit=max&format=json&_={DateTime.Now.Ticks}";
            string watchlistJson = await worker.GetJsonFromApiAsync(url);

            if (string.IsNullOrEmpty(watchlistJson))
            {
                Debug.WriteLine("[AuthService] FetchWatchlistAsync: Received empty or invalid JSON.");
                return new HashSet<string>();
            }

            var watchlistResponse = JsonConvert.DeserializeObject<WatchlistApiResponse>(watchlistJson);

            var serverFavourites = new HashSet<string>();
            if (watchlistResponse?.WatchlistRaw != null)
            {
                foreach (var item in watchlistResponse.WatchlistRaw)
                {
                    serverFavourites.Add(item.Title);
                }
                Debug.WriteLine($"[AuthService] Successfully parsed {serverFavourites.Count} favourites from server.");
            }
            else
            {
                Debug.WriteLine("[AuthService] Failed to parse watchlist. 'watchlistResponse.WatchlistRaw' is null.");
            }
            return serverFavourites;
        }

        private static async Task FetchCsrfTokenAsync(IApiWorker worker)
        {
            Debug.WriteLine("[AuthService] Attempting to fetch CSRF (watch) token...");
            string url = $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&type=watch&format=json&_={DateTime.Now.Ticks}";
            string rawResponseJson = await worker.GetJsonFromApiAsync(url);
            string responseJson = ExtractJsonFromHtmlWrapper(rawResponseJson);

            if (string.IsNullOrEmpty(responseJson))
            {
                throw new Exception("Failed to retrieve valid JSON for CSRF token (response was null or empty).");
            }

            Debug.WriteLine($"[AuthService] Cleaned CSRF token response: {responseJson}");

            WatchTokenResponse tokenResponse;
            try
            {
                tokenResponse = JsonConvert.DeserializeObject<WatchTokenResponse>(responseJson);
            }
            catch (JsonReaderException ex)
            {
                Debug.WriteLine($"[AuthService] FATAL: Failed to parse CSRF token JSON.");
                throw new Exception($"The server's response for the CSRF token was not valid JSON. Raw response: '{responseJson}'", ex);
            }

            string rawToken = tokenResponse?.Query?.Tokens?.WatchToken;
            if (string.IsNullOrEmpty(rawToken))
            {
                throw new Exception("Failed to parse a watch token from the server response.");
            }

            _csrfToken = rawToken;
            Debug.WriteLine($"[AuthService] Successfully parsed and stored CSRF token.");
        }

        public static async Task<List<AuthRequest>> GetCreateAccountFieldsAsync()
        {
            using (var tempWorker = await CreateAndInitApiWorker())
            {
                string url = $"{AppSettings.ApiEndpoint}?action=query&meta=authmanagerinfo&amirequestsfor=create&format=json";
                string json = await tempWorker.GetJsonFromApiAsync(url);
                var response = JsonConvert.DeserializeObject<AuthManagerInfoResponse>(json);

                if (response?.Query?.AuthManagerInfo?.Requests == null)
                {
                    throw new Exception("Could not retrieve required fields for account creation.");
                }
                return response.Query.AuthManagerInfo.Requests;
            }
        }

        public static async Task<CreateAccountResult> PerformCreateAccountAsync(Dictionary<string, string> fieldData)
        {
            using (var tempWorker = await CreateAndInitApiWorker())
            {
                string tokenUrl = $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&type=createaccount&format=json";
                string tokenJson = await tempWorker.GetJsonFromApiAsync(tokenUrl);
                var tokenResponse = JObject.Parse(tokenJson);
                string createToken = tokenResponse?["query"]?["tokens"]?["createaccounttoken"]?.ToString();
                if (string.IsNullOrEmpty(createToken))
                {
                    throw new Exception("Failed to retrieve a createaccount token.");
                }

                var postData = new Dictionary<string, string>(fieldData)
                {
                    { "action", "createaccount" },
                    { "createtoken", createToken },
                    { "format", "json" },
                    { "createreturnurl", AppSettings.BaseUrl }
                };

                string resultJson = await tempWorker.PostAndGetJsonFromApiAsync(AppSettings.ApiEndpoint, postData);
                var result = JsonConvert.DeserializeObject<CreateAccountResponse>(resultJson);

                if (result?.CreateAccount == null)
                {
                    throw new Exception("Received an invalid response from the server.");
                }

                return result.CreateAccount;
            }
        }
    }
}