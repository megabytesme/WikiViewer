using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace _1809_UWP
{
    public static class FavouritesService
    {
        private const string FavouritesFileName = "Favourites.json";
        private const string PendingAddsFileName = "PendingAdds.json";
        private const string PendingDeletesFileName = "PendingDeletes.json";

        private static HashSet<string> _Favourites;
        private static HashSet<string> _pendingAdds;
        private static HashSet<string> _pendingDeletes;

        private static bool _isInitialized = false;

        public static event EventHandler FavouritesChanged;

        public static async Task InitializeAsync()
        {
            if (_isInitialized) return;

            _Favourites = await LoadSetFromFileAsync(FavouritesFileName);

            _pendingAdds = await LoadSetFromFileAsync(PendingAddsFileName);
            _pendingDeletes = await LoadSetFromFileAsync(PendingDeletesFileName);

            _isInitialized = true;
        }

        private static async Task<HashSet<string>> LoadSetFromFileAsync(string fileName)
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(fileName) as StorageFile;
                if (file != null)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    return JsonConvert.DeserializeObject<HashSet<string>>(json) ?? new HashSet<string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavouritesService] Failed to load {fileName}: {ex.Message}");
            }
            return new HashSet<string>();
        }

        private static async Task SaveSetToFileAsync(HashSet<string> data, string fileName)
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                string json = JsonConvert.SerializeObject(data);
                await FileIO.WriteTextAsync(file, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavouritesService] Failed to save {fileName}: {ex.Message}");
            }
        }

        private static async Task SaveAsync()
        {
            await SaveSetToFileAsync(_Favourites, FavouritesFileName);
            await SaveSetToFileAsync(_pendingAdds, PendingAddsFileName);
            await SaveSetToFileAsync(_pendingDeletes, PendingDeletesFileName);
            FavouritesChanged?.Invoke(null, EventArgs.Empty);
        }

        public static List<string> GetFavourites()
        {
            var sortedList = _Favourites.ToList();
            sortedList.Sort();
            return sortedList;
        }

        public static bool IsFavourite(string pageTitle)
        {
            if (_Favourites == null) return false;
            return _Favourites.Contains(pageTitle.Replace('_', ' '));
        }

        private static List<string> GetAssociatedTitles(string pageTitle)
        {
            var titles = new HashSet<string>();
            string normalizedTitle = pageTitle.Replace('_', ' ');

            if (normalizedTitle.StartsWith("Talk:"))
            {
                string baseTitle = normalizedTitle.Substring("Talk:".Length);
                titles.Add(baseTitle);
                titles.Add(normalizedTitle);
            }
            else if (normalizedTitle.StartsWith("User talk:"))
            {
                string baseTitle = normalizedTitle.Substring("User talk:".Length);
                titles.Add($"User:{baseTitle}");
                titles.Add(normalizedTitle);
            }
            else if (normalizedTitle.StartsWith("User:"))
            {
                string baseTitle = normalizedTitle.Substring("User:".Length);
                titles.Add(normalizedTitle);
                titles.Add($"User talk:{baseTitle}");
            }
            else
            {
                titles.Add(normalizedTitle);
                titles.Add($"Talk:{normalizedTitle}");
            }
            return titles.ToList();
        }

        public static async Task AddFavoriteAsync(string pageTitle)
        {
            var associatedTitles = GetAssociatedTitles(pageTitle);
            bool wasChanged = false;

            foreach (var title in associatedTitles)
            {
                if (_Favourites.Add(title))
                {
                    wasChanged = true;
                    if (!AuthService.IsLoggedIn)
                    {
                        _pendingDeletes.Remove(title);
                        _pendingAdds.Add(title);
                    }
                }
            }

            if (wasChanged)
            {
                if (AuthService.IsLoggedIn)
                {
                    var primaryTitle = associatedTitles.First(t => !t.StartsWith("Talk:") && !t.StartsWith("User talk:"));
                    await AuthService.SyncSingleFavoriteToServerAsync(primaryTitle, add: true);
                }
                await SaveAsync();
            }
        }

        public static async Task RemoveFavoriteAsync(string pageTitle)
        {
            var associatedTitles = GetAssociatedTitles(pageTitle);
            bool wasChanged = false;

            foreach (var title in associatedTitles)
            {
                if (_Favourites.Remove(title))
                {
                    wasChanged = true;
                    if (!AuthService.IsLoggedIn)
                    {
                        _pendingAdds.Remove(title);
                        _pendingDeletes.Add(title);
                    }
                }
            }

            if (wasChanged)
            {
                if (AuthService.IsLoggedIn)
                {
                    await AuthService.SyncMultipleFavouritesToServerAsync(associatedTitles, add: false);
                }
                await SaveAsync();
            }
        }

        public static List<string> GetAndClearPendingAdds()
        {
            var adds = _pendingAdds.ToList();
            _pendingAdds.Clear();
            return adds;
        }

        public static List<string> GetAndClearPendingDeletes()
        {
            var deletes = _pendingDeletes.ToList();
            _pendingDeletes.Clear();
            return deletes;
        }

        public static async Task OverwriteLocalFavouritesAsync(HashSet<string> serverFavourites)
        {
            _Favourites = serverFavourites;
            _pendingAdds.Clear();
            _pendingDeletes.Clear();
            await SaveAsync();
        }

        public static async Task ClearAllLocalFavouritesAsync()
        {
            _Favourites.Clear();
            _pendingAdds.Clear();
            _pendingDeletes.Clear();
            await SaveAsync();
        }
    }
}