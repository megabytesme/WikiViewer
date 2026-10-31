using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;

namespace WikiViewer.Core.Managers
{
    public class ArticleCacheItem
    {
        public string Title { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime? LastCheckedUtc { get; set; }
    }

    public static class ArticleCacheManager
    {
        private static IStorageProvider _storage;
        public static IStorageProvider StorageProvider { get => _storage; set { if (!ReferenceEquals(_storage, value)) ClearMemory(); _storage = value; } }
        private sealed class MemoryEntry { public string Key; public ArticleContent Content; public long Size; }
        private static readonly object MemoryGate = new object();
        private static readonly LinkedList<MemoryEntry> Memory = new LinkedList<MemoryEntry>();
        private static long _memorySize;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Writes = new ConcurrentDictionary<string, SemaphoreSlim>();
        private static void ClearMemory() { lock (MemoryGate) { Memory.Clear(); _memorySize = 0; } }
        private static void Remember(string key, ArticleContent content)
        {
            long size = 2L * ((content.HtmlContent?.Length ?? 0) + (content.RawWikitext?.Length ?? 0) + (content.HeadHtml?.Length ?? 0) + (content.ResolvedStyleSheets?.Sum(s => s.Length) ?? 0));
            lock (MemoryGate)
            {
                var old = Memory.First;
                while (old != null) { var next = old.Next; if (old.Value.Key == key) { _memorySize -= old.Value.Size; Memory.Remove(old); } old = next; }
                if (size > 16 * 1024 * 1024) return;
                Memory.AddFirst(new MemoryEntry { Key = key, Content = content.Copy(), Size = size }); _memorySize += size;
                while (Memory.Count > 8 || _memorySize > 16 * 1024 * 1024) { _memorySize -= Memory.Last.Value.Size; Memory.RemoveLast(); }
            }
        }
        private const string ArticleCacheFolderName = "ArticleCache";
        private const string ImageCacheFolderName = "cache";

        public static event EventHandler<ArticleCachedEventArgs> ArticleCached;

        public static async Task<ulong> GetCacheSizeAsync()
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            ulong articleSize = await StorageProvider.GetFolderSizeAsync(ArticleCacheFolderName);
            ulong imageSize = await StorageProvider.GetFolderSizeAsync(ImageCacheFolderName);
            ulong resourceSize = await StorageProvider.GetFolderSizeAsync("ResourceCache");
            return articleSize + imageSize + resourceSize;
        }

        public static async Task ClearCacheAsync()
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");

            await StorageProvider.RecreateFolderAsync(ArticleCacheFolderName);
            await StorageProvider.RecreateFolderAsync(ImageCacheFolderName);
            await StorageProvider.RecreateFolderAsync("ResourceCache");
            ClearMemory(); WikiResourceCache.Shared?.ClearMemory(); NativeDocumentCache.Clear();
        }

        private static string GetHashedFileName(string pageTitle, Guid wikiId)
        {
            return CacheKeyService.Hash(CacheKeyService.Article(wikiId, pageTitle));
        }

        private static IEnumerable<string> CandidateNames(string pageTitle, Guid wikiId) => new[] { GetHashedFileName(pageTitle, wikiId), CacheKeyService.Hash(wikiId + "_" + CacheKeyService.Title(pageTitle).ToLowerInvariant()),
                CacheKeyService.Hash(wikiId + "_" + pageTitle.ToLowerInvariant()), CacheKeyService.Hash(wikiId + "_" + CacheKeyService.Title(pageTitle).Replace(' ', '_').ToLowerInvariant()) }.Distinct();

        private static async Task<string> FindNameAsync(string pageTitle, Guid wikiId, string suffix)
        {
            var names = CandidateNames(pageTitle, wikiId);
            foreach (string name in names) if (await StorageProvider.FileExistsAsync(Path.Combine(ArticleCacheFolderName, name + suffix))) return name;
            return null;
        }

        public static async Task<bool> IsArticleCachedAsync(string pageTitle, Guid wikiId)
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            return await FindNameAsync(pageTitle, wikiId, ".html") != null;
        }

        public static async Task<ArticleCacheItem> GetCacheMetadataAsync(
            string pageTitle,
            Guid wikiId
        )
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            string name = await FindNameAsync(pageTitle, wikiId, ".json");
            if (name == null) return null;
            string fileName = name + ".json";
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
            string name = await FindNameAsync(pageTitle, wikiId, ".html");
            if (name == null) return null;
            string fileName = name + ".html";
            var filePath = Path.Combine(ArticleCacheFolderName, fileName);
            return await StorageProvider.ReadTextAsync(filePath);
        }

        public static async Task<ArticleContent> GetCachedArticleContentAsync(string pageTitle, Guid wikiId)
        {
            string name = GetHashedFileName(pageTitle, wikiId);
            lock (MemoryGate)
            {
                var hit = Memory.First;
                while (hit != null)
                {
                    if (hit.Value.Key == name) { var content = hit.Value.Content.Copy(); Memory.Remove(hit); Memory.AddFirst(hit); return content; }
                    hit = hit.Next;
                }
            }
            string storedName = await FindNameAsync(pageTitle, wikiId, ".article.json");
            string pairedPath = Path.Combine(ArticleCacheFolderName, (storedName ?? name) + ".article.json");
            if (storedName != null)
            {
                try
                {
                    var json = await StorageProvider.ReadTextAsync(pairedPath);
                    var content = await Task.Run(() => JsonConvert.DeserializeObject<ArticleContent>(json));
                    if (storedName != name && content?.Title != null && !CacheKeyService.EquivalentTitle(content.Title, pageTitle)) return null;
                    if (content?.HtmlContent != null) { Remember(name, content); return content; }
                }
                catch (Exception) { }
            }
            // Old caches remain readable, without inventing source/revision information.
            string html = await GetCachedArticleHtmlAsync(pageTitle, wikiId);
            if (html == null) return null;
            var metadata = await GetCacheMetadataAsync(pageTitle, wikiId);
            if (metadata?.Title != null && !CacheKeyService.EquivalentTitle(metadata.Title, pageTitle)) return null;
            var legacy = new ArticleContent { Title = pageTitle, HtmlContent = html }; Remember(name, legacy); return legacy;
        }

        public static async Task SaveArticleContentAsync(string pageTitle, Guid wikiId, ArticleContent content, DateTime lastUpdated)
        {
            if (content == null || content.HtmlContent == null) throw new ArgumentException("Article content is required.", nameof(content));
            var gate = Writes.GetOrAdd(GetHashedFileName(pageTitle, wikiId), key => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try { await WriteContentAsync(pageTitle, wikiId, content, lastUpdated); }
            finally { gate.Release(); }
        }

        private static async Task WriteContentAsync(string pageTitle, Guid wikiId, ArticleContent content, DateTime lastUpdated)
        {
            // The complete pair is a single record; legacy HTML remains available to existing cache consumers.
            string name = GetHashedFileName(pageTitle, wikiId);
            string json = await Task.Run(() => JsonConvert.SerializeObject(content));
            await StorageProvider.WriteTextAsync(Path.Combine(ArticleCacheFolderName, name + ".article.json"), json);
            Remember(name, content);
            await WriteLegacyCacheAsync(pageTitle, wikiId, content.HtmlContent, lastUpdated);
        }

        public static async Task<bool> UpdateResourcesIfCurrentAsync(string title, Guid wikiId, ArticleContent expected, ArticleContent enriched)
        {
            var gate = Writes.GetOrAdd(GetHashedFileName(title, wikiId), key => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var current = await GetCachedArticleContentAsync(title, wikiId);
                if (current == null || current.RevisionId != expected.RevisionId || current.HtmlContent != expected.HtmlContent || current.RawWikitext != expected.RawWikitext) return false;
                // Resource completion must not reset the revision-validation or template-expansion age.
                string name = GetHashedFileName(title, wikiId);
                string json = await Task.Run(() => JsonConvert.SerializeObject(enriched));
                await StorageProvider.WriteTextAsync(Path.Combine(ArticleCacheFolderName, name + ".article.json"), json);
                Remember(name, enriched); return true;
            }
            finally { gate.Release(); }
        }

        public static async Task SaveArticleHtmlAsync(
            string pageTitle,
            Guid wikiId,
            string htmlContent,
            DateTime lastUpdated
        )
        {
            foreach (string name in CandidateNames(pageTitle, wikiId))
            {
                string pairedPath = Path.Combine(ArticleCacheFolderName, name + ".article.json");
                if (await StorageProvider.FileExistsAsync(pairedPath)) await StorageProvider.DeleteFileAsync(pairedPath);
            }
            ClearMemory();
            await WriteLegacyCacheAsync(pageTitle, wikiId, htmlContent, lastUpdated);
        }

        private static async Task WriteLegacyCacheAsync(string pageTitle, Guid wikiId, string htmlContent, DateTime lastUpdated)
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            string baseFileName = GetHashedFileName(pageTitle, wikiId);
            var metadata = new ArticleCacheItem { Title = pageTitle, LastUpdated = lastUpdated, LastCheckedUtc = DateTime.UtcNow };

            var metadataPath = Path.Combine(ArticleCacheFolderName, baseFileName + ".json");
            await StorageProvider.WriteTextAsync(
                metadataPath,
                JsonConvert.SerializeObject(metadata)
            );

            var htmlPath = Path.Combine(ArticleCacheFolderName, baseFileName + ".html");
            await StorageProvider.WriteTextAsync(htmlPath, htmlContent);

            ArticleCached?.Invoke(null, new ArticleCachedEventArgs(pageTitle, wikiId));
        }

        public static async Task ClearCacheForItemAsync(string pageTitle, Guid wikiId)
        {
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider not set.");
            ClearMemory();
            var names = CandidateNames(pageTitle, wikiId);
            try
            {
                foreach (string name in names) foreach (string suffix in new[] { ".article.json", ".json", ".html" })
                {
                    string path = Path.Combine(ArticleCacheFolderName, name + suffix);
                    if (await StorageProvider.FileExistsAsync(path)) await StorageProvider.DeleteFileAsync(path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[CACHE] Failed to invalidate cache for '{pageTitle}': {ex.Message}"
                );
            }
        }

        public static async Task MarkCheckedAsync(string pageTitle, Guid wikiId, DateTime checkedUtc)
        {
            var gate = Writes.GetOrAdd(GetHashedFileName(pageTitle, wikiId), key => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
            var metadata = await GetCacheMetadataAsync(pageTitle, wikiId);
            if (metadata == null) return;
            metadata.LastCheckedUtc = checkedUtc;
            string name = await FindNameAsync(pageTitle, wikiId, ".json") ?? GetHashedFileName(pageTitle, wikiId);
            await StorageProvider.WriteTextAsync(Path.Combine(ArticleCacheFolderName, name + ".json"), JsonConvert.SerializeObject(metadata));
            }
            finally { gate.Release(); }
        }
    }
}
