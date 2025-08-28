using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Managers
{
    public class ArticleCacheItem
    {
        public string Title { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public static class ArticleCacheManager
    {
        public static IStorageProvider StorageProvider { get; set; }
        private const string ArticleCacheFolderName = "ArticleCache";
        private const string ImageCacheFolderName = "cache";

        public static event EventHandler<ArticleCachedEventArgs> ArticleCached;

        public static async Task<ulong> GetCacheSizeAsync()
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            ulong articleSize = await StorageProvider.GetFolderSizeAsync(ArticleCacheFolderName);
            ulong imageSize = await StorageProvider.GetFolderSizeAsync(ImageCacheFolderName);
            return articleSize + imageSize;
        }

        public static async Task ClearCacheAsync()
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");

            await StorageProvider.RecreateFolderAsync(ArticleCacheFolderName);
            await StorageProvider.RecreateFolderAsync(ImageCacheFolderName);
        }

        private static string GetHashedFileName(string pageTitle, Guid wikiId)
        {
            string uniqueCacheKey = $"{wikiId.ToString()}_{pageTitle.ToLowerInvariant()}";
            var hash = System
                .Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(uniqueCacheKey));
            return hash.Aggregate("", (s, b) => s + b.ToString("x2"));
        }

        public static async Task<bool> IsArticleCachedAsync(string pageTitle, Guid wikiId)
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            string fileName = GetHashedFileName(pageTitle, wikiId) + ".html";
            return await StorageProvider.FileExistsAsync(
                Path.Combine(ArticleCacheFolderName, fileName)
            );
        }

        public static async Task<ArticleCacheItem> GetCacheMetadataAsync(
            string pageTitle,
            Guid wikiId
        )
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            string fileName = GetHashedFileName(pageTitle, wikiId) + ".json";
            var filePath = Path.Combine(ArticleCacheFolderName, fileName);
            if (await StorageProvider.FileExistsAsync(filePath))
            {
                try
                {
                    var json = await StorageProvider.ReadTextAsync(filePath);
                    return JsonConvert.DeserializeObject<ArticleCacheItem>(json);
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
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            string fileName = GetHashedFileName(pageTitle, wikiId) + ".html";
            var filePath = Path.Combine(ArticleCacheFolderName, fileName);
            return await StorageProvider.ReadTextAsync(filePath);
        }

        public static async Task SaveArticleHtmlAsync(
            string pageTitle,
            Guid wikiId,
            string htmlContent,
            DateTime lastUpdated
        )
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            string baseFileName = GetHashedFileName(pageTitle, wikiId);
            var metadata = new ArticleCacheItem { Title = pageTitle, LastUpdated = lastUpdated };

            var metadataPath = Path.Combine(ArticleCacheFolderName, baseFileName + ".json");
            await StorageProvider.WriteTextAsync(
                metadataPath,
                JsonConvert.SerializeObject(metadata)
            );

            var htmlPath = Path.Combine(ArticleCacheFolderName, baseFileName + ".html");
            await StorageProvider.WriteTextAsync(htmlPath, htmlContent);

            ArticleCached?.Invoke(null, new ArticleCachedEventArgs(pageTitle));
        }

        public static async Task ClearCacheForItemAsync(string pageTitle, Guid wikiId)
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            string baseFileName = GetHashedFileName(pageTitle, wikiId);
            try
            {
                await StorageProvider.DeleteFileAsync(
                    Path.Combine(ArticleCacheFolderName, baseFileName + ".json")
                );
                await StorageProvider.DeleteFileAsync(
                    Path.Combine(ArticleCacheFolderName, baseFileName + ".html")
                );
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
