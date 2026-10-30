using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
#if WINDOWS_UWP
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
#else
using System.Linq;
using System.Security.Cryptography;
using System.Text;
#endif

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

#if WINDOWS_UWP
            var algProvider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            IBuffer buffer = CryptographicBuffer.ConvertStringToBinary(
                uniqueCacheKey,
                BinaryStringEncoding.Utf8
            );
            var hashedBuffer = algProvider.HashData(buffer);
            return CryptographicBuffer.EncodeToHexString(hashedBuffer);
#else
            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(uniqueCacheKey));
                return hash.Aggregate("", (s, b) => s + b.ToString("x2"));
            }
#endif
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

        public static async Task<ArticleContent> GetCachedArticleContentAsync(string pageTitle, Guid wikiId)
        {
            string name = GetHashedFileName(pageTitle, wikiId);
            string pairedPath = Path.Combine(ArticleCacheFolderName, name + ".article.json");
            if (await StorageProvider.FileExistsAsync(pairedPath))
            {
                try
                {
                    var content = JsonConvert.DeserializeObject<ArticleContent>(await StorageProvider.ReadTextAsync(pairedPath));
                    if (content?.HtmlContent != null) return content;
                }
                catch (Exception) { }
            }
            // Old caches remain readable, without inventing source/revision information.
            if (!await StorageProvider.FileExistsAsync(Path.Combine(ArticleCacheFolderName, name + ".html"))) return null;
            return new ArticleContent { Title = pageTitle, HtmlContent = await GetCachedArticleHtmlAsync(pageTitle, wikiId) };
        }

        public static async Task SaveArticleContentAsync(string pageTitle, Guid wikiId, ArticleContent content, DateTime lastUpdated)
        {
            if (content == null || content.HtmlContent == null) throw new ArgumentException("Article content is required.", nameof(content));
            // The complete pair is a single record; legacy HTML remains available to existing cache consumers.
            string name = GetHashedFileName(pageTitle, wikiId);
            await StorageProvider.WriteTextAsync(Path.Combine(ArticleCacheFolderName, name + ".article.json"), JsonConvert.SerializeObject(content));
            await WriteLegacyCacheAsync(pageTitle, wikiId, content.HtmlContent, lastUpdated);
        }

        public static async Task SaveArticleHtmlAsync(
            string pageTitle,
            Guid wikiId,
            string htmlContent,
            DateTime lastUpdated
        )
        {
            string pairedPath = Path.Combine(ArticleCacheFolderName, GetHashedFileName(pageTitle, wikiId) + ".article.json");
            if (await StorageProvider.FileExistsAsync(pairedPath)) await StorageProvider.DeleteFileAsync(pairedPath);
            await WriteLegacyCacheAsync(pageTitle, wikiId, htmlContent, lastUpdated);
        }

        private static async Task WriteLegacyCacheAsync(string pageTitle, Guid wikiId, string htmlContent, DateTime lastUpdated)
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
                string pairedPath = Path.Combine(ArticleCacheFolderName, baseFileName + ".article.json");
                if (await StorageProvider.FileExistsAsync(pairedPath)) await StorageProvider.DeleteFileAsync(pairedPath);
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
