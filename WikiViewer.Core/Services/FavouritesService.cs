using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Windows.Storage;

namespace WikiViewer.Core.Services
{
    public static class FavouritesService
    {
        private const string FavouritesFileName = "Favourites.json";
        private const string PendingAddsFileName = "PendingAdds.json";
        private const string PendingDeletesFileName = "PendingDeletes.json";

        private static Dictionary<Guid, HashSet<string>> _favouritesByWiki;
        private static Dictionary<Guid, HashSet<string>> _pendingAddsByWiki;
        private static Dictionary<Guid, HashSet<string>> _pendingDeletesByWiki;

        private static bool _isInitialized = false;

        public static event EventHandler FavouritesChanged;

        public static async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            _favouritesByWiki = await LoadDictionaryFromFileAsync(FavouritesFileName);
            _pendingAddsByWiki = await LoadDictionaryFromFileAsync(PendingAddsFileName);
            _pendingDeletesByWiki = await LoadDictionaryFromFileAsync(PendingDeletesFileName);

            _isInitialized = true;
        }

        private static async Task<Dictionary<Guid, HashSet<string>>> LoadDictionaryFromFileAsync(
            string fileName
        )
        {
            try
            {
                var file =
                    await ApplicationData.Current.LocalFolder.TryGetItemAsync(fileName)
                    as StorageFile;
                if (file != null)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    return JsonConvert.DeserializeObject<Dictionary<Guid, HashSet<string>>>(json)
                        ?? new Dictionary<Guid, HashSet<string>>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavouritesService] Failed to load {fileName}: {ex.Message}");
            }
            return new Dictionary<Guid, HashSet<string>>();
        }

        private static async Task SaveAllAsync()
        {
            await SaveDictionaryToFileAsync(_favouritesByWiki, FavouritesFileName);
            await SaveDictionaryToFileAsync(_pendingAddsByWiki, PendingAddsFileName);
            await SaveDictionaryToFileAsync(_pendingDeletesByWiki, PendingDeletesFileName);
            FavouritesChanged?.Invoke(null, EventArgs.Empty);
        }

        private static async Task SaveDictionaryToFileAsync(
            Dictionary<Guid, HashSet<string>> data,
            string fileName
        )
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                fileName,
                CreationCollisionOption.ReplaceExisting
            );
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            await FileIO.WriteTextAsync(file, json);
        }

        public static List<string> GetFavourites(Guid wikiId)
        {
            return _favouritesByWiki.TryGetValue(wikiId, out var favourites)
                ? favourites.OrderBy(f => f).ToList()
                : new List<string>();
        }

        public static bool IsFavourite(string pageTitle, Guid wikiId)
        {
            return _favouritesByWiki.TryGetValue(wikiId, out var favourites)
                && favourites.Contains(pageTitle.Replace('_', ' '));
        }

        private static List<string> GetAssociatedTitles(string pageTitle)
        {
            var titles = new HashSet<string>();
            string normalizedTitle = pageTitle.Replace('_', ' ');
            if (normalizedTitle.StartsWith("Talk:"))
                titles.Add(normalizedTitle.Substring("Talk:".Length));
            else if (normalizedTitle.StartsWith("User talk:"))
                titles.Add($"User:{normalizedTitle.Substring("User talk:".Length)}");
            else if (normalizedTitle.StartsWith("User:"))
                titles.Add($"User talk:{normalizedTitle.Substring("User:".Length)}");
            else
                titles.Add($"Talk:{normalizedTitle}");
            titles.Add(normalizedTitle);
            return titles.ToList();
        }

        public static async Task AddFavoriteAsync(
            string pageTitle,
            Guid wikiId,
            AuthenticationService authService
        )
        {
            var associatedTitles = GetAssociatedTitles(pageTitle);

            if (!_favouritesByWiki.ContainsKey(wikiId))
                _favouritesByWiki[wikiId] = new HashSet<string>();
            if (!_pendingAddsByWiki.ContainsKey(wikiId))
                _pendingAddsByWiki[wikiId] = new HashSet<string>();
            if (!_pendingDeletesByWiki.ContainsKey(wikiId))
                _pendingDeletesByWiki[wikiId] = new HashSet<string>();

            bool wasChanged = false;
            foreach (var title in associatedTitles)
            {
                if (_favouritesByWiki[wikiId].Add(title))
                {
                    wasChanged = true;
                    if (authService == null || !SessionManager.IsLoggedIn) // Check global session state
                    {
                        _pendingDeletesByWiki[wikiId].Remove(title);
                        _pendingAddsByWiki[wikiId].Add(title);
                    }
                }
            }

            if (wasChanged)
            {
                if (authService != null && SessionManager.IsLoggedIn)
                {
                    await authService.SyncMultipleFavouritesToServerAsync(
                        associatedTitles,
                        add: true
                    );
                }
                await SaveAllAsync();
            }
        }

        public static async Task RemoveFavoriteAsync(
            string pageTitle,
            Guid wikiId,
            AuthenticationService authService
        )
        {
            var associatedTitles = GetAssociatedTitles(pageTitle);

            if (!_favouritesByWiki.ContainsKey(wikiId))
                return;
            if (!_pendingAddsByWiki.ContainsKey(wikiId))
                _pendingAddsByWiki[wikiId] = new HashSet<string>();
            if (!_pendingDeletesByWiki.ContainsKey(wikiId))
                _pendingDeletesByWiki[wikiId] = new HashSet<string>();

            bool wasChanged = false;
            foreach (var title in associatedTitles)
            {
                if (_favouritesByWiki[wikiId].Remove(title))
                {
                    wasChanged = true;
                    if (authService == null || !SessionManager.IsLoggedIn)
                    {
                        _pendingAddsByWiki[wikiId].Remove(title);
                        _pendingDeletesByWiki[wikiId].Add(title);
                    }
                }
            }

            if (wasChanged)
            {
                if (authService != null && SessionManager.IsLoggedIn)
                {
                    await authService.SyncMultipleFavouritesToServerAsync(
                        associatedTitles,
                        add: false
                    );
                }
                await SaveAllAsync();
            }
        }

        public static List<string> GetAndClearPendingAdds(Guid wikiId)
        {
            if (_pendingAddsByWiki.TryGetValue(wikiId, out var adds))
            {
                var list = adds.ToList();
                adds.Clear();
                return list;
            }
            return new List<string>();
        }

        public static List<string> GetAndClearPendingDeletes(Guid wikiId)
        {
            if (_pendingDeletesByWiki.TryGetValue(wikiId, out var deletes))
            {
                var list = deletes.ToList();
                deletes.Clear();
                return list;
            }
            return new List<string>();
        }

        public static async Task OverwriteLocalFavouritesAsync(
            Guid wikiId,
            HashSet<string> serverFavourites
        )
        {
            _favouritesByWiki[wikiId] = serverFavourites;
            if (_pendingAddsByWiki.ContainsKey(wikiId))
                _pendingAddsByWiki[wikiId].Clear();
            if (_pendingDeletesByWiki.ContainsKey(wikiId))
                _pendingDeletesByWiki[wikiId].Clear();
            await SaveAllAsync();
        }

        public static async Task RemoveAllFavouritesForWikiAsync(Guid wikiId)
        {
            _favouritesByWiki.Remove(wikiId);
            _pendingAddsByWiki.Remove(wikiId);
            _pendingDeletesByWiki.Remove(wikiId);
            await SaveAllAsync();
        }
    }
}
