using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace _1809_UWP
{
    public static class FavoritesService
    {
        private const string FavoritesFileName = "favorites.json";
        private static List<string> _favorites;
        private static bool _isInitialized = false;

        public static async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FavoritesFileName) as StorageFile;
                if (file != null)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    _favorites = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
                else
                {
                    _favorites = new List<string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavoritesService] Failed to load favorites: {ex.Message}");
                _favorites = new List<string>();
            }
            _isInitialized = true;
        }

        private static async Task SaveAsync()
        {
            if (!_isInitialized) return;
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FavoritesFileName, CreationCollisionOption.ReplaceExisting);
                string json = JsonSerializer.Serialize(_favorites);
                await FileIO.WriteTextAsync(file, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavoritesService] Failed to save favorites: {ex.Message}");
            }
        }

        public static List<string> GetFavorites()
        {
            return _favorites.ToList();
        }

        public static bool IsFavorite(string pageTitle)
        {
            return _favorites.Contains(pageTitle.Replace('_', ' '));
        }

        public static async Task AddFavoriteAsync(string pageTitle)
        {
            var cleanTitle = pageTitle.Replace('_', ' ');
            if (!_favorites.Contains(cleanTitle))
            {
                _favorites.Add(cleanTitle);
                await SaveAsync();
            }
        }

        public static async Task RemoveFavoriteAsync(string pageTitle)
        {
            var cleanTitle = pageTitle.Replace('_', ' ');
            if (_favorites.Contains(cleanTitle))
            {
                _favorites.Remove(cleanTitle);
                await SaveAsync();
            }
        }
    }
}