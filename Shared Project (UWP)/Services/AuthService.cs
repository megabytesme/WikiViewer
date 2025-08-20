using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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
            IApiWorker worker;
            if (AppSettings.ConnectionBackend == ConnectionMethod.HttpClientProxy)
            {
                worker = new HttpClientApiWorker();
            }
            else
            {
#if UWP_1703
                worker = (IApiWorker)
                    Activator.CreateInstance(Type.GetType("_1703_UWP.WebViewApiWorker, 1703 UWP"));
#else
                worker = (IApiWorker)
                    Activator.CreateInstance(Type.GetType("_1809_UWP.WebView2ApiWorker, 1809 UWP"));
#endif
            }
            await worker.InitializeAsync();
            return worker;
        }

        private static string ExtractJsonFromHtmlWrapper(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return rawResponse;
            string trimmedResponse = rawResponse.Trim();
            if (trimmedResponse.StartsWith("{") && trimmedResponse.EndsWith("}"))
                return trimmedResponse;
            try
            {
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(rawResponse);
                var preNode = doc.DocumentNode.SelectSingleNode("//pre");
                if (preNode != null && !string.IsNullOrWhiteSpace(preNode.InnerText))
                {
                    string potentialJson = preNode.InnerText.Trim();
                    if (potentialJson.StartsWith("{") && potentialJson.EndsWith("}"))
                        return potentialJson;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[AuthService] HTML parsing failed during JSON extraction: {ex.Message}"
                );
            }
            return rawResponse;
        }

        public static async Task PerformLoginAsync(string username, string password)
        {
            _authenticatedWorker = await CreateAndInitApiWorker();
            string rawTokenResponse = await _authenticatedWorker.GetJsonFromApiAsync(
                $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&type=login&format=json&_={DateTime.Now.Ticks}"
            );
            string tokenJson = ExtractJsonFromHtmlWrapper(rawTokenResponse);
            if (string.IsNullOrEmpty(tokenJson))
                throw new Exception(
                    "Could not get a valid response for login token (response was null or empty)."
                );
            LoginApiTokenResponse tokenResponse;
            try
            {
                tokenResponse = JsonConvert.DeserializeObject<LoginApiTokenResponse>(tokenJson);
            }
            catch (JsonReaderException ex)
            {
                throw new Exception(
                    $"The server's response for the login token was not valid JSON. Raw response: '{tokenJson}'",
                    ex
                );
            }
            string loginToken = tokenResponse?.query?.tokens?.logintoken;
            if (string.IsNullOrEmpty(loginToken))
                throw new Exception("Failed to retrieve a login token from the JSON response.");
            var loginPostData = new Dictionary<string, string>
            {
                { "action", "clientlogin" },
                { "format", "json" },
                { "username", username },
                { "password", password },
                { "logintoken", loginToken },
                { "loginreturnurl", AppSettings.BaseUrl },
            };
            string resultJson = await _authenticatedWorker.PostAndGetJsonFromApiAsync(
                AppSettings.ApiEndpoint,
                loginPostData
            );
            string cleanResultJson = ExtractJsonFromHtmlWrapper(resultJson);
            ClientLoginResponse resultResponse;
            try
            {
                resultResponse = JsonConvert.DeserializeObject<ClientLoginResponse>(
                    cleanResultJson
                );
            }
            catch (JsonReaderException ex)
            {
                throw new Exception(
                    $"The server's response after posting credentials was not valid JSON. Raw response: '{resultJson}'",
                    ex
                );
            }
            if (resultResponse?.clientlogin?.status != "PASS")
            {
                _authenticatedWorker?.Dispose();
                _authenticatedWorker = null;
                throw new Exception(
                    $"Login failed: {resultResponse?.clientlogin?.status ?? "Unknown API response"}"
                );
            }
            IsLoggedIn = true;
            Username = resultResponse.clientlogin.username;
            _csrfToken = await GetCsrfTokenAsync();
            await SyncAndMergeFavouritesOnLogin(_authenticatedWorker);
            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        public static async void Logout()
        {
            if (!IsLoggedIn)
                return;
            if (!string.IsNullOrEmpty(_csrfToken) && _authenticatedWorker != null)
            {
                var logoutPostData = new Dictionary<string, string>
                {
                    { "action", "logout" },
                    { "format", "json" },
                    { "token", _csrfToken },
                };
                try
                {
                    await _authenticatedWorker.PostAndGetJsonFromApiAsync(
                        AppSettings.ApiEndpoint,
                        logoutPostData
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[AuthService] Logout API call failed, but proceeding with local logout: {ex.Message}"
                    );
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
            var pendingDeletes = FavouritesService.GetAndClearPendingDeletes();
            if (pendingDeletes.Any())
                await SyncMultipleFavouritesToServerInternalAsync(
                    worker,
                    pendingDeletes,
                    add: false
                );
            var pendingAdds = FavouritesService.GetAndClearPendingAdds();
            if (pendingAdds.Any())
                await SyncMultipleFavouritesToServerInternalAsync(worker, pendingAdds, add: true);
            var serverFavourites = await FetchWatchlistAsync(worker);
            await FavouritesService.OverwriteLocalFavouritesAsync(serverFavourites);
            await BackgroundCacheService.CacheFavouritesAsync(serverFavourites);
        }

        public static async Task SyncSingleFavoriteToServerAsync(string pageTitle, bool add) =>
            await SyncMultipleFavouritesToServerAsync(new List<string> { pageTitle }, add);

        public static async Task SyncMultipleFavouritesToServerAsync(List<string> titles, bool add)
        {
            if (!IsLoggedIn || _authenticatedWorker == null)
                return;
            await SyncMultipleFavouritesToServerInternalAsync(_authenticatedWorker, titles, add);
        }

        private static async Task SyncMultipleFavouritesToServerInternalAsync(
            IApiWorker worker,
            List<string> titles,
            bool add
        )
        {
            if (!IsLoggedIn || string.IsNullOrEmpty(_csrfToken) || !titles.Any() || worker == null)
                return;
            string batchedTitles = string.Join("|", titles);
            var postData = new Dictionary<string, string>
            {
                { "action", "watch" },
                { "format", "json" },
                { "titles", batchedTitles },
                { "token", _csrfToken },
            };
            if (!add)
                postData.Add("unwatch", "");
            try
            {
                string rawResponseJson = await worker.PostAndGetJsonFromApiAsync(
                    AppSettings.ApiEndpoint,
                    postData
                );
                string responseJson = ExtractJsonFromHtmlWrapper(rawResponseJson);
                var response = JsonConvert.DeserializeObject<WatchActionResponse>(responseJson);
                if (add && (response?.Watch == null || !response.Watch.Any()))
                    throw new Exception("Server did not confirm watch action.");
                else if (!add && (response?.Unwatch == null || !response.Unwatch.Any()))
                    throw new Exception("Server did not confirm unwatch action.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync batch Favourites: {ex.Message}");
            }
        }

        private static async Task<HashSet<string>> FetchWatchlistAsync(IApiWorker worker)
        {
            string url =
                $"{AppSettings.ApiEndpoint}?action=query&list=watchlistraw&wrlimit=max&format=json&_={DateTime.Now.Ticks}";
            string watchlistJson = await worker.GetJsonFromApiAsync(url);
            if (string.IsNullOrEmpty(watchlistJson))
                return new HashSet<string>();
            var watchlistResponse = JsonConvert.DeserializeObject<WatchlistApiResponse>(
                watchlistJson
            );
            var serverFavourites = new HashSet<string>();
            if (watchlistResponse?.WatchlistRaw != null)
            {
                foreach (var item in watchlistResponse.WatchlistRaw)
                    serverFavourites.Add(item.Title);
            }
            return serverFavourites;
        }

        public static async Task<string> GetCsrfTokenAsync()
        {
            if (!IsLoggedIn || _authenticatedWorker == null)
                throw new InvalidOperationException("User must be logged in to get a CSRF token.");
            string url =
                $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&format=json&_={DateTime.Now.Ticks}";
            string json = await _authenticatedWorker.GetJsonFromApiAsync(url);
            var tokenResponse = JObject.Parse(json);
            string csrfToken = tokenResponse?["query"]?["tokens"]?["csrftoken"]?.ToString();
            if (string.IsNullOrEmpty(csrfToken) || csrfToken == "+\\")
                throw new Exception("Failed to retrieve a valid CSRF token for editing.");
            return csrfToken;
        }

        public static async Task<bool> SavePageAsync(string title, string content, string summary)
        {
            if (!IsLoggedIn || _authenticatedWorker == null)
                throw new InvalidOperationException("User must be logged in to save a page.");
            string token = _csrfToken;
            var postData = new Dictionary<string, string>
            {
                { "action", "edit" },
                { "format", "json" },
                { "title", title },
                { "text", content },
                { "summary", summary },
                { "token", token },
            };
            string resultJson = await _authenticatedWorker.PostAndGetJsonFromApiAsync(
                AppSettings.ApiEndpoint,
                postData
            );
            var result = JObject.Parse(resultJson);
            if (result?["edit"]?["result"]?.ToString() == "Success")
            {
                await ArticleCacheManager.ClearCacheForItemAsync(title);
                return true;
            }
            else
            {
                string errorMessage =
                    result?["error"]?["info"]?.ToString() ?? "Unknown error during save.";
                throw new Exception(errorMessage);
            }
        }

        public static async Task<List<AuthRequest>> GetCreateAccountFieldsAsync()
        {
            using (var tempWorker = await CreateAndInitApiWorker())
            {
                string url =
                    $"{AppSettings.ApiEndpoint}?action=query&meta=authmanagerinfo&amirequestsfor=create&format=json";
                string json = await tempWorker.GetJsonFromApiAsync(url);
                var response = JsonConvert.DeserializeObject<AuthManagerInfoResponse>(json);
                if (response?.Query?.AuthManagerInfo?.Requests == null)
                    throw new Exception("Could not retrieve required fields for account creation.");
                return response.Query.AuthManagerInfo.Requests;
            }
        }

        public static async Task<CreateAccountResult> PerformCreateAccountAsync(
            Dictionary<string, string> fieldData
        )
        {
            using (var tempWorker = await CreateAndInitApiWorker())
            {
                string tokenUrl =
                    $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&type=createaccount&format=json";
                string tokenJson = await tempWorker.GetJsonFromApiAsync(tokenUrl);
                var tokenResponse = JObject.Parse(tokenJson);
                string createToken = tokenResponse?["query"]?["tokens"]?[
                    "createaccounttoken"
                ]?.ToString();
                if (string.IsNullOrEmpty(createToken))
                    throw new Exception("Failed to retrieve a createaccount token.");
                var postData = new Dictionary<string, string>(fieldData)
                {
                    { "action", "createaccount" },
                    { "createtoken", createToken },
                    { "format", "json" },
                    { "createreturnurl", AppSettings.BaseUrl },
                };
                string resultJson = await tempWorker.PostAndGetJsonFromApiAsync(
                    AppSettings.ApiEndpoint,
                    postData
                );
                var result = JsonConvert.DeserializeObject<CreateAccountResponse>(resultJson);
                if (result?.CreateAccount == null)
                    throw new Exception("Received an invalid response from the server.");
                return result.CreateAccount;
            }
        }
    }
}