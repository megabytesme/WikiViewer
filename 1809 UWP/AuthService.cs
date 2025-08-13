using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Newtonsoft.Json;

namespace _1809_UWP
{
    public static class AuthService
    {
        public static event EventHandler AuthenticationStateChanged;
        public static bool IsLoggedIn { get; private set; }
        public static string Username { get; private set; }
        private static string _csrfToken;
        private static WebView2 _authenticatedWorker;
        private const string ApiUrl = "https://betawiki.net/api.php";

        private static async Task<WebView2> CreateAndInitWebView2()
        {
            WebView2 worker = null;
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                if (App.UIHost == null) throw new InvalidOperationException("App.UIHost is not available.");
                worker = new WebView2();
                App.UIHost.Children.Add(worker);
                await worker.EnsureCoreWebView2Async();
            });
            if (worker == null) throw new Exception("Failed to create WebView2 on UI thread.");
            return worker;
        }

        public static async Task PerformLoginAsync(string username, string password)
        {
            _authenticatedWorker = await CreateAndInitWebView2();

            string tokenJson = await ApiRequestService.GetJsonFromApiAsync(
                $"{ApiUrl}?action=query&meta=tokens&type=login&format=json&_={DateTime.Now.Ticks}",
                _authenticatedWorker
            );

            if (string.IsNullOrEmpty(tokenJson)) throw new Exception("Could not get a valid response for login token.");

            var tokenResponse = JsonConvert.DeserializeObject<LoginApiTokenResponse>(tokenJson);
            string loginToken = tokenResponse?.query?.tokens?.logintoken;
            if (string.IsNullOrEmpty(loginToken)) throw new Exception("Failed to retrieve a login token from the JSON response.");

            var loginPostData = new Dictionary<string, string>
            {
                { "action", "clientlogin" }, { "format", "json" }, { "username", username }, { "password", password }, { "logintoken", loginToken }, { "loginreturnurl", "https://betawiki.net" }
            };

            string resultJson = await ApiRequestService.PostAndGetJsonFromApiAsync(_authenticatedWorker, ApiUrl, loginPostData);

            var resultResponse = JsonConvert.DeserializeObject<ClientLoginResponse>(resultJson);
            if (resultResponse?.clientlogin?.status != "PASS")
            {
                _authenticatedWorker?.Close();
                _authenticatedWorker = null;
                throw new Exception($"Login failed: {resultResponse?.clientlogin?.status ?? "Unknown API response"}");
            }

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
                    await ApiRequestService.PostAndGetJsonFromApiAsync(_authenticatedWorker, ApiUrl, logoutPostData);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AuthService] Logout API call failed, but proceeding with local logout: {ex.Message}");
                }
            }

            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (_authenticatedWorker != null)
                {
                    App.UIHost?.Children.Remove(_authenticatedWorker);
                    _authenticatedWorker.Close();
                    _authenticatedWorker = null;
                }
            });

            IsLoggedIn = false;
            Username = null;
            _csrfToken = null;
            CredentialService.ClearCredentials();
            await FavouritesService.ClearAllLocalFavouritesAsync();
            AuthenticationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static async Task SyncAndMergeFavouritesOnLogin(WebView2 worker)
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

        private static async Task SyncMultipleFavouritesToServerInternalAsync(WebView2 worker, List<string> titles, bool add)
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
                string responseJson = await ApiRequestService.PostAndGetJsonFromApiAsync(worker, ApiUrl, postData);
                var response = JsonConvert.DeserializeObject<WatchActionResponse>(responseJson);
                if (add && (response?.Watch == null || !response.Watch.Any())) throw new Exception("Server did not confirm watch action.");
                else if (!add && (response?.Unwatch == null || !response.Unwatch.Any())) throw new Exception("Server did not confirm unwatch action.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync batch Favourites: {ex.Message}");
            }
        }

        private static async Task<HashSet<string>> FetchWatchlistAsync(WebView2 worker)
        {
            string url = $"{ApiUrl}?action=query&list=watchlistraw&wrlimit=max&format=json&_={DateTime.Now.Ticks}";
            string watchlistJson = await ApiRequestService.GetJsonFromApiAsync(url, worker);

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

        private static async Task FetchCsrfTokenAsync(WebView2 worker)
        {
            string url = $"{ApiUrl}?action=query&meta=tokens&type=watch&format=json&_={DateTime.Now.Ticks}";
            string responseJson = await ApiRequestService.GetJsonFromApiAsync(url, worker);
            if (string.IsNullOrEmpty(responseJson)) throw new Exception("Failed to retrieve valid JSON for token.");

            var tokenResponse = JsonConvert.DeserializeObject<WatchTokenResponse>(responseJson);
            string rawToken = tokenResponse?.Query?.Tokens?.WatchToken;
            if (string.IsNullOrEmpty(rawToken)) throw new Exception("Failed to parse a watch token from the server response.");

            _csrfToken = rawToken;
            Debug.WriteLine($"[AuthService] Storing complete raw watch token. Value is: [{_csrfToken}]");
        }
    }
}