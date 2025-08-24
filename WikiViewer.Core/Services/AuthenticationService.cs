using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public class AuthenticationService
    {
        private readonly Account _account;
        private readonly WikiInstance _wiki;
        private readonly IApiWorkerFactory _workerFactory;

        public static event EventHandler<AuthenticationStateChangedEventArgs> AuthenticationStateChanged;

        public AuthenticationService(
            Account account,
            WikiInstance wiki,
            IApiWorkerFactory workerFactory
        )
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _wiki = wiki ?? throw new ArgumentNullException(nameof(wiki));
            _workerFactory =
                workerFactory ?? throw new ArgumentNullException(nameof(workerFactory));
        }

        private string ExtractJsonFromHtmlWrapper(string rawResponse)
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

        public async Task LoginAsync(string password)
        {
            if (_account.IsLoggedIn)
                return;

            using (var loginWorker = _workerFactory.CreateApiWorker(_wiki))
            {
                await loginWorker.InitializeAsync(_wiki.BaseUrl);

                string rawTokenResponse = await loginWorker.GetJsonFromApiAsync(
                    $"{_wiki.ApiEndpoint}?action=query&meta=tokens&type=login&format=json&_={DateTime.Now.Ticks}"
                );
                string tokenJson = ExtractJsonFromHtmlWrapper(rawTokenResponse);
                if (string.IsNullOrEmpty(tokenJson))
                    throw new Exception(
                        "Could not get a valid response for login token (response was null or empty)."
                    );

                var tokenResponse = JsonConvert.DeserializeObject<LoginApiTokenResponse>(tokenJson);
                string loginToken = tokenResponse?.query?.tokens?.logintoken;
                if (string.IsNullOrEmpty(loginToken))
                    throw new Exception("Failed to retrieve a login token from the JSON response.");

                var loginPostData = new Dictionary<string, string>
                {
                    { "action", "clientlogin" },
                    { "format", "json" },
                    { "username", _account.Username },
                    { "password", password },
                    { "logintoken", loginToken },
                    { "loginreturnurl", _wiki.BaseUrl },
                };

                string resultJson = await loginWorker.PostAndGetJsonFromApiAsync(
                    _wiki.ApiEndpoint,
                    loginPostData
                );
                string cleanResultJson = ExtractJsonFromHtmlWrapper(resultJson);
                var resultResponse = JsonConvert.DeserializeObject<ClientLoginResponse>(
                    cleanResultJson
                );

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

                var authenticatedWorker = SessionManager.GetAnonymousWorkerForWiki(_wiki);
                await authenticatedWorker.InitializeAsync(_wiki.BaseUrl);
                await authenticatedWorker.CopyApiCookiesFromAsync(loginWorker);
                _account.AuthenticatedApiWorker = authenticatedWorker;
            }

            await SyncAndMergeFavouritesOnLogin();
            AuthenticationStateChanged?.Invoke(
                this,
                new AuthenticationStateChangedEventArgs(_account, _wiki, true)
            );
        }

        public void Logout()
        {
            if (!_account.IsLoggedIn)
                return;

            _ = Task.Run(async () =>
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
                        await _account.AuthenticatedApiWorker.PostAndGetJsonFromApiAsync(
                            _wiki.ApiEndpoint,
                            logoutPostData
                        );
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[AuthService] Logout API call failed, but proceeding with local state change: {ex.Message}"
                    );
                }
            });

            _account.AuthenticatedApiWorker?.Dispose();
            _account.AuthenticatedApiWorker = null;

            AuthenticationStateChanged?.Invoke(
                this,
                new AuthenticationStateChangedEventArgs(_account, _wiki, false)
            );
        }

        private async Task SyncAndMergeFavouritesOnLogin()
        {
            if (!_account.IsLoggedIn)
                return;

            var pendingDeletes = FavouritesService.GetAndClearPendingDeletes(_wiki.Id);
            if (pendingDeletes.Any())
                await SyncMultipleFavouritesToServerAsync(pendingDeletes, add: false);

            var pendingAdds = FavouritesService.GetAndClearPendingAdds(_wiki.Id);
            if (pendingAdds.Any())
                await SyncMultipleFavouritesToServerAsync(pendingAdds, add: true);

            var serverFavourites = await FetchWatchlistAsync();
            await FavouritesService.OverwriteLocalFavouritesAsync(_wiki.Id, serverFavourites);
        }

        public async Task SyncMultipleFavouritesToServerAsync(List<string> titles, bool add)
        {
            if (!_account.IsLoggedIn || !titles.Any())
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

                await _account.AuthenticatedApiWorker.PostAndGetJsonFromApiAsync(
                    _wiki.ApiEndpoint,
                    postData
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync batch Favourites: {ex.Message}");
            }
        }

        public async Task<HashSet<string>> FetchWatchlistAsync()
        {
            if (!_account.IsLoggedIn)
                return new HashSet<string>();

            string url =
                $"{_wiki.ApiEndpoint}?action=query&list=watchlistraw&wrlimit=max&format=json&_={DateTime.Now.Ticks}";
            string watchlistJson = await _account.AuthenticatedApiWorker.GetJsonFromApiAsync(url);
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

        public async Task<string> GetCsrfTokenAsync()
        {
            if (!_account.IsLoggedIn)
                return null;
            string url =
                $"{_wiki.ApiEndpoint}?action=query&meta=tokens&format=json&_={DateTime.Now.Ticks}";
            string json = await _account.AuthenticatedApiWorker.GetJsonFromApiAsync(url);
            if (string.IsNullOrEmpty(json))
                return null;

            var tokenResponse = JObject.Parse(json);
            string csrfToken = tokenResponse?["query"]?["tokens"]?["csrftoken"]?.ToString();
            return (string.IsNullOrEmpty(csrfToken) || csrfToken == "+\\") ? null : csrfToken;
        }

        private async Task<string> GetWatchTokenAsync()
        {
            if (!_account.IsLoggedIn)
                return null;
            string url =
                $"{_wiki.ApiEndpoint}?action=query&meta=tokens&type=watch&format=json&_={DateTime.Now.Ticks}";
            string json = await _account.AuthenticatedApiWorker.GetJsonFromApiAsync(url);
            if (string.IsNullOrEmpty(json))
                return null;

            var tokenResponse = JObject.Parse(json);
            string watchToken = tokenResponse?["query"]?["tokens"]?["watchtoken"]?.ToString();
            return (string.IsNullOrEmpty(watchToken) || watchToken == "+\\") ? null : watchToken;
        }

        public async Task<bool> SavePageAsync(string title, string content, string summary, bool isMinorEdit)
        {
            if (!_account.IsLoggedIn)
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
            string resultJson = await _account.AuthenticatedApiWorker.PostAndGetJsonFromApiAsync(
                _wiki.ApiEndpoint,
                postData
            );
            var result = JObject.Parse(resultJson);
            if (result?["edit"]?["result"]?.ToString() == "Success")
            {
                await ArticleCacheManager.ClearCacheForItemAsync(title, _wiki.Id);
                return true;
            }

            string errorMessage =
                result?["error"]?["info"]?.ToString() ?? "Unknown error during save.";
            throw new Exception(errorMessage);
        }
    }

    public class AuthenticationStateChangedEventArgs : EventArgs
    {
        public Account Account { get; }
        public WikiInstance Wiki { get; }
        public bool IsLoggedIn { get; }

        public AuthenticationStateChangedEventArgs(
            Account account,
            WikiInstance wiki,
            bool isLoggedIn
        )
        {
            Account = account;
            Wiki = wiki;
            IsLoggedIn = isLoggedIn;
        }
    }
}
