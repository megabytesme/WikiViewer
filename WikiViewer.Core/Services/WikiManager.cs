using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Models;
using Windows.Storage;

namespace WikiViewer.Core.Services
{
    public static class WikiManager
    {
        private const string WikisFileName = "wikis.json";
        private static List<WikiInstance> _wikis;

        public static event EventHandler WikisChanged;

        public static async Task InitializeAsync()
        {
            if (_wikis != null)
                return;

            try
            {
                var file =
                    await ApplicationData.Current.LocalFolder.TryGetItemAsync(WikisFileName)
                    as StorageFile;
                if (file != null)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    _wikis =
                        JsonConvert.DeserializeObject<List<WikiInstance>>(json)
                        ?? new List<WikiInstance>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WikiManager] Failed to load wikis: {ex.Message}");
            }

            if (_wikis == null || !_wikis.Any())
            {
                _wikis = new List<WikiInstance>
                {
                    new WikiInstance
                    {
                        Name = "English Wikipedia",
                        BaseUrl = "https://en.wikipedia.org/",
                    },
                };
                await SaveAsync();
            }
        }

        public static async Task SaveAsync()
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                WikisFileName,
                CreationCollisionOption.ReplaceExisting
            );
            string json = JsonConvert.SerializeObject(_wikis, Formatting.Indented);
            await FileIO.WriteTextAsync(file, json);
            WikisChanged?.Invoke(null, EventArgs.Empty);
        }

        public static List<WikiInstance> GetWikis() => _wikis;

        public static WikiInstance GetWikiById(Guid id) => _wikis.FirstOrDefault(w => w.Id == id);

        public static async Task AddWikiAsync(WikiInstance wiki)
        {
            _wikis.Add(wiki);
            await SaveAsync();
        }

        public static async Task RemoveWikiAsync(Guid wikiId)
        {
            _wikis.RemoveAll(w => w.Id == wikiId);
            await AccountManager.RemoveAllAccountsForWikiAsync(wikiId);
            await FavouritesService.RemoveAllFavouritesForWikiAsync(wikiId);
            await SaveAsync();
        }
    }
}