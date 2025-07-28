using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace _1809_UWP
{
    public static class FavouritesService
    {
        private const string FavouritesFileName = "Favourites.json";
        private static HashSet<string> _Favourites;
        private static bool _isInitialized = false;

        public static event EventHandler FavouritesChanged;

        public static async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FavouritesFileName) as StorageFile;
                if (file != null)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    _Favourites = JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>();
                }
                else
                {
                    _Favourites = new HashSet<string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavouritesService] Failed to load Favourites: {ex.Message}");
                _Favourites = new HashSet<string>();
            }
            _isInitialized = true;
        }

        private static async Task SaveAsync()
        {
            if (!_isInitialized) return;
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FavouritesFileName, CreationCollisionOption.ReplaceExisting);
                string json = JsonSerializer.Serialize(_Favourites);
                await FileIO.WriteTextAsync(file, json);
                FavouritesChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavouritesService] Failed to save Favourites: {ex.Message}");
            }
        }

        public static List<string> GetFavourites()
        {
            var sortedList = _Favourites.ToList();
            sortedList.Sort();
            return sortedList;
        }

        public static bool IsFavourite(string pageTitle)
        {
            return _Favourites.Contains(pageTitle.Replace('_', ' '));
        }

        public static async Task AddFavoriteAsync(string pageTitle)
        {
            var cleanTitle = pageTitle.Replace('_', ' ');
            if (_Favourites.Add(cleanTitle))
            {
                await SaveAsync();
                if (AuthService.IsLoggedIn)
                {
                    await AuthService.SyncSingleFavoriteToServerAsync(cleanTitle, add: true);
                }
            }
        }

        public static async Task RemoveFavoriteAsync(string pageTitle)
        {
            var cleanTitle = pageTitle.Replace('_', ' ');
            if (_Favourites.Remove(cleanTitle))
            {
                await SaveAsync();
                if (AuthService.IsLoggedIn)
                {
                    await AuthService.SyncSingleFavoriteToServerAsync(cleanTitle, add: false);
                }
            }
        }

        public static async Task OverwriteLocalFavouritesAsync(HashSet<string> serverFavourites)
        {
            _Favourites = serverFavourites;
            await SaveAsync();
        }

        public static async Task ClearAllLocalFavouritesAsync()
        {
            _Favourites.Clear();
            await SaveAsync();
        }
    }
}