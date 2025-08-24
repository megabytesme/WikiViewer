using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Windows.Storage;

namespace WikiViewer.Shared.Uwp.Managers
{
    public class ArticleCacheItem
    {
        public string Title { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public static class ArticleCacheManager
    {
        private static StorageFolder _cacheFolder;
        private static StorageFolder _imageCacheFolder;

        public static async Task InitializeAsync()
        {
            if (_cacheFolder == null)
                _cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "ArticleCache",
                    CreationCollisionOption.OpenIfExists
                );
            if (_imageCacheFolder == null)
                _imageCacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "cache",
                    CreationCollisionOption.OpenIfExists
                );
        }

        public static async Task<ulong> GetCacheSizeAsync()
        {
            await InitializeAsync();
            ulong totalSize = 0;
            try
            {
                var articleFiles = await _cacheFolder.GetFilesAsync();
                foreach (var file in articleFiles)
                    totalSize += (await file.GetBasicPropertiesAsync()).Size;
                var imageFiles = await _imageCacheFolder.GetFilesAsync();
                foreach (var file in imageFiles)
                    totalSize += (await file.GetBasicPropertiesAsync()).Size;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CACHE] Error calculating cache size: {ex.Message}");
            }
            return totalSize;
        }

        public static async Task ClearCacheAsync()
        {
            await InitializeAsync();
            try
            {
                var articleFiles = await _cacheFolder.GetFilesAsync();
                foreach (var file in articleFiles)
                    await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                var imageFiles = await _imageCacheFolder.GetFilesAsync();
                foreach (var file in imageFiles)
                    await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CACHE] Error clearing cache: {ex.Message}");
            }
        }

        private static string GetHashedFileName(string pageTitle, Guid wikiId)
        {
            string uniqueCacheKey = $"{wikiId.ToString()}_{pageTitle.ToLowerInvariant()}";
            string sanitizedKey = string.Join(
                "_",
                uniqueCacheKey.Split(Path.GetInvalidFileNameChars())
            );
            var hash = System
                .Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(sanitizedKey));
            return hash.Aggregate("", (s, b) => s + b.ToString("x2"));
        }

        public static async Task<bool> IsArticleCachedAsync(string pageTitle, Guid wikiId)
        {
            await InitializeAsync();
            string fileName = GetHashedFileName(pageTitle, wikiId) + ".html";
            return await _cacheFolder.TryGetItemAsync(fileName) is StorageFile;
        }

        public static async Task<ArticleCacheItem> GetCacheMetadataAsync(
            string pageTitle,
            Guid wikiId
        )
        {
            await InitializeAsync();
            string fileName = GetHashedFileName(pageTitle, wikiId) + ".json";
            var item = await _cacheFolder.TryGetItemAsync(fileName);
            if (item is StorageFile file)
            {
                try
                {
                    return JsonConvert.DeserializeObject<ArticleCacheItem>(
                        await FileIO.ReadTextAsync(file)
                    );
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public static async Task<string> GetCachedArticleHtmlAsync(string pageTitle, Guid wikiId)
        {
            await InitializeAsync();
            string fileName = GetHashedFileName(pageTitle, wikiId) + ".html";
            var item = await _cacheFolder.TryGetItemAsync(fileName);
            return item is StorageFile file ? await FileIO.ReadTextAsync(file) : null;
        }

        public static async Task SaveArticleHtmlAsync(
            string pageTitle,
            Guid wikiId,
            string htmlContent,
            DateTime lastUpdated
        )
        {
            await InitializeAsync();
            string baseFileName = GetHashedFileName(pageTitle, wikiId);
            var metadata = new ArticleCacheItem { Title = pageTitle, LastUpdated = lastUpdated };
            StorageFile metadataFile = await _cacheFolder.CreateFileAsync(
                baseFileName + ".json",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(metadataFile, JsonConvert.SerializeObject(metadata));
            StorageFile htmlFile = await _cacheFolder.CreateFileAsync(
                baseFileName + ".html",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(htmlFile, htmlContent);
        }

        public static async Task ClearCacheForItemAsync(string pageTitle, Guid wikiId)
        {
            await InitializeAsync();
            string baseFileName = GetHashedFileName(pageTitle, wikiId);
            try
            {
                if (
                    await _cacheFolder.TryGetItemAsync(baseFileName + ".json")
                    is StorageFile metaFile
                )
                    await metaFile.DeleteAsync();
                if (
                    await _cacheFolder.TryGetItemAsync(baseFileName + ".html")
                    is StorageFile htmlFile
                )
                    await htmlFile.DeleteAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[CACHE] Failed to invalidate cache for '{pageTitle}': {ex.Message}"
                );
            }
        }
    }
}
