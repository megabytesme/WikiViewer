using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Shared.Uwp.Managers;
using WikiViewer.Shared.Uwp.Services;

namespace WikiViewer.Core.Services
{
    public static class AuthService
    {
        public static event EventHandler AuthenticationStateChanged;
        public static bool IsLoggedIn { get; private set; }
        public static string Username { get; private set; }

        private static IApiWorker _authenticatedWorker;

        public static IApiWorker CurrentOrAnonymousWorker =>
            IsLoggedIn && _authenticatedWorker != null
                ? _authenticatedWorker
                : WikiViewer.Shared.Uwp.Pages.MainPageBase.ApiWorker;

        private static string ExtractJsonFromHtmlWrapper(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return null;
            string trimmedResponse = rawResponse.Trim();
            if (
                (trimmedResponse.StartsWith("{") && trimmedResponse.EndsWith("}"))
                || (trimmedResponse.StartsWith("[") && trimmedResponse.EndsWith("]"))
            )
                return trimmedResponse;
            try
            {
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(rawResponse);
                var preNode = doc.DocumentNode.SelectSingleNode("//pre");
                if (preNode != null && !string.IsNullOrWhiteSpace(preNode.InnerText))
                {
                    string potentialJson = preNode.InnerText.Trim();
                    if (
                        (potentialJson.StartsWith("{") && potentialJson.EndsWith("}"))
                        || (potentialJson.StartsWith("[") && potentialJson.EndsWith("]"))
                    )
                        return potentialJson;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[AuthService] HTML parsing failed during JSON extraction: {ex.Message}"
                );
            }
            return null;
        }

        public static async Task PerformLoginAsync(string username, string password)
        {
            ClientLoginResponse resultResponse;
            using (var loginWorker = WikiViewer.Shared.Uwp.App.ApiWorkerFactory.CreateApiWorker())
            {
                await loginWorker.InitializeAsync();
                string rawTokenResponse = await loginWorker.GetJsonFromApiAsync(
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
                    Debug.WriteLine(
                        $"[AuthService-LOGIN] Failed to parse login token JSON. Raw text was: '{tokenJson}'"
                    );
                    throw new Exception(
                        $"The server's response for the login token was not valid JSON.",
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

                string resultJson = await loginWorker.PostAndGetJsonFromApiAsync(
                    AppSettings.ApiEndpoint,
                    loginPostData
                );
                string cleanResultJson = ExtractJsonFromHtmlWrapper(resultJson);

                try
                {
                    resultResponse = JsonConvert.DeserializeObject<ClientLoginResponse>(
                        cleanResultJson
                    );
                }
                catch (JsonReaderException ex)
                {
                    Debug.WriteLine(
                        $"[AuthService-LOGIN] Failed to parse login result JSON. Raw text was: '{cleanResultJson}'"
                    );
                    throw new Exception(
                        $"The server's response after posting credentials was not valid JSON.",
                        ex
                    );
                }

                if (resultResponse?.clientlogin?.status == "UI")
                {
                    throw new AuthUiRequiredException(resultResponse.clientlogin);
                }

                if (resultResponse?.clientlogin?.status != "PASS")
                {
                    throw new Exception(
                        $"Login failed: {resultResponse?.clientlogin?.status ?? "Unknown API response"}"
                    );
                }

                _authenticatedWorker = WikiViewer.Shared.Uwp.App.ApiWorkerFactory.CreateApiWorker();
                await _authenticatedWorker.InitializeAsync();
                await _authenticatedWorker.CopyApiCookiesFromAsync(loginWorker);
            }

            IsLoggedIn = true;
            Username = resultResponse.clientlogin.username;
            await SyncAndMergeFavouritesOnLogin();
            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        public static async Task ContinueLoginAsync(Dictionary<string, string> fieldData)
        {
            ClientLoginResponse resultResponse;
            using (
                var continueWorker = WikiViewer.Shared.Uwp.App.ApiWorkerFactory.CreateApiWorker()
            )
            {
                await continueWorker.InitializeAsync();

                string rawTokenResponse = await continueWorker.GetJsonFromApiAsync(
                    $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&type=login&format=json&_={DateTime.Now.Ticks}"
                );
                string tokenJson = ExtractJsonFromHtmlWrapper(rawTokenResponse);
                var tokenResponse = JsonConvert.DeserializeObject<LoginApiTokenResponse>(tokenJson);
                string loginToken = tokenResponse?.query?.tokens?.logintoken;

                var continuePostData = new Dictionary<string, string>(fieldData)
                {
                    { "action", "clientlogin" },
                    { "format", "json" },
                    { "logincontinue", "1" },
                    { "logintoken", loginToken },
                };

                string resultJson = await continueWorker.PostAndGetJsonFromApiAsync(
                    AppSettings.ApiEndpoint,
                    continuePostData
                );
                resultResponse = JsonConvert.DeserializeObject<ClientLoginResponse>(
                    ExtractJsonFromHtmlWrapper(resultJson)
                );

                if (resultResponse?.clientlogin?.status != "PASS")
                {
                    throw new Exception(
                        $"Login failed: {resultResponse?.clientlogin?.status ?? "Unknown API response"}"
                    );
                }

                _authenticatedWorker = WikiViewer.Shared.Uwp.App.ApiWorkerFactory.CreateApiWorker();
                await _authenticatedWorker.InitializeAsync();
                await _authenticatedWorker.CopyApiCookiesFromAsync(continueWorker);
            }

            IsLoggedIn = true;
            Username = resultResponse.clientlogin.username;
            await SyncAndMergeFavouritesOnLogin();
            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void Logout()
        {
            if (!IsLoggedIn)
                return;

            // Fire-and-forget the API logout call. The local state is more important.
            _ = Task.Run(async () =>
            {
                if (_authenticatedWorker != null)
                {
                    try
                    {
                        var csrfToken = await GetCsrfTokenAsync();
                        if (!string.IsNullOrEmpty(csrfToken))
                        {
                            var logoutPostData = new Dictionary<string, string>
                            {
                                { "action", "logout" },
                                { "format", "json" },
                                { "token", csrfToken },
                            };
                            await _authenticatedWorker.PostAndGetJsonFromApiAsync(
                                AppSettings.ApiEndpoint,
                                logoutPostData
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[AuthService] Logout API call failed, but proceeding with local logout: {ex.Message}"
                        );
                    }
                }
            });

            _authenticatedWorker?.Dispose();
            _authenticatedWorker = null;
            IsLoggedIn = false;
            Username = null;
            CredentialService.ClearCredentials();
            _ = FavouritesService.ClearAllLocalFavouritesAsync();
            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static async Task SyncAndMergeFavouritesOnLogin()
        {
            if (_authenticatedWorker == null)
                return;
            var pendingDeletes = FavouritesService.GetAndClearPendingDeletes();
            if (pendingDeletes.Any())
                await SyncMultipleFavouritesToServerInternalAsync(
                    _authenticatedWorker,
                    pendingDeletes,
                    add: false
                );
            var pendingAdds = FavouritesService.GetAndClearPendingAdds();
            if (pendingAdds.Any())
                await SyncMultipleFavouritesToServerInternalAsync(
                    _authenticatedWorker,
                    pendingAdds,
                    add: true
                );
            var serverFavourites = await FetchWatchlistAsync(_authenticatedWorker);
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
            if (!IsLoggedIn || !titles.Any() || worker == null)
                return;

            try
            {
                var watchToken = await GetWatchTokenAsync();
                if (string.IsNullOrEmpty(watchToken))
                {
                    Debug.WriteLine(
                        "[AuthService-Sync] Could not retrieve a valid WATCH token. Aborting sync."
                    );
                    return;
                }

                string batchedTitles = string.Join("|", titles);
                var postData = new Dictionary<string, string>
                {
                    { "action", "watch" },
                    { "format", "json" },
                    { "titles", batchedTitles },
                    { "token", watchToken },
                };
                if (!add)
                    postData.Add("unwatch", "");

                string rawResponseJson = await worker.PostAndGetJsonFromApiAsync(
                    AppSettings.ApiEndpoint,
                    postData
                );
                string responseJson = ExtractJsonFromHtmlWrapper(rawResponseJson);
                Debug.WriteLine(
                    $"[AuthService-Sync] Sync response for '{(add ? "add" : "remove")}': {responseJson}"
                );

                var response = JsonConvert.DeserializeObject<WatchActionResponse>(responseJson);
                if (
                    response == null
                    || (response.error != null && !string.IsNullOrEmpty(response.error.code))
                )
                {
                    Debug.WriteLine(
                        $"[AuthService-Sync] Server returned an error: {response?.error?.info ?? "Unknown error."}"
                    );
                    return;
                }
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
                {
                    serverFavourites.Add(item.Title);
                }
            }
            return serverFavourites;
        }

        private static async Task<string> GetWatchTokenAsync()
        {
            if (!IsLoggedIn || _authenticatedWorker == null)
                return null;
            string url =
                $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&type=watch&format=json&_={DateTime.Now.Ticks}";
            string json = await _authenticatedWorker.GetJsonFromApiAsync(url);
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                var tokenResponse = JObject.Parse(json);
                string watchToken = tokenResponse?["query"]?["tokens"]?["watchtoken"]?.ToString();
                return (string.IsNullOrEmpty(watchToken) || watchToken == "+\\")
                    ? null
                    : watchToken;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[AuthService-Token] Exception while parsing WATCH token JSON: {ex.Message}"
                );
                return null;
            }
        }

        public static async Task<string> GetCsrfTokenAsync()
        {
            if (!IsLoggedIn || _authenticatedWorker == null)
                return null;
            string url =
                $"{AppSettings.ApiEndpoint}?action=query&meta=tokens&format=json&_={DateTime.Now.Ticks}";
            string json = await _authenticatedWorker.GetJsonFromApiAsync(url);
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                var tokenResponse = JObject.Parse(json);
                string csrfToken = tokenResponse?["query"]?["tokens"]?["csrftoken"]?.ToString();
                return (string.IsNullOrEmpty(csrfToken) || csrfToken == "+\\") ? null : csrfToken;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[AuthService-Token] Exception while parsing CSRF token JSON: {ex.Message}"
                );
                return null;
            }
        }

        public static async Task<bool> SavePageAsync(string title, string content, string summary)
        {
            if (!IsLoggedIn || _authenticatedWorker == null)
                throw new InvalidOperationException("User must be logged in to save a page.");

            string token = await GetCsrfTokenAsync();
            if (string.IsNullOrEmpty(token))
                throw new Exception("Could not get a valid CSRF token to save the page.");

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
            using (var tempWorker = WikiViewer.Shared.Uwp.App.ApiWorkerFactory.CreateApiWorker())
            {
                await tempWorker.InitializeAsync();
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
            using (var tempWorker = WikiViewer.Shared.Uwp.App.ApiWorkerFactory.CreateApiWorker())
            {
                await tempWorker.InitializeAsync();
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

        public static void ResetStateForReload()
        {

            if (!IsLoggedIn)
                return;

            _authenticatedWorker?.Dispose();
            _authenticatedWorker = null;
            IsLoggedIn = false;
            Username = null;

            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
