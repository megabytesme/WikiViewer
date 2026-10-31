using System;
using System.Threading.Tasks;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public sealed class ArticleLoadCoordinator
    {
        private readonly AsyncRequestCoordinator<ArticleContent> _requests = new AsyncRequestCoordinator<ArticleContent>();
        public async Task<ArticleContent> LoadAsync(string key, Func<Task<ArticleContent>> readCache, Func<Task<ArticleContent>> fetch, bool forceRefresh = false)
        {
            if (!forceRefresh)
            {
                var cached = await readCache();
                if (cached?.HtmlContent != null) return cached;
            }
            return await _requests.RunAsync(key, fetch);
        }
        public static bool NeedsValidation(ArticleCacheItem metadata, DateTime utcNow)
            => metadata == null || utcNow - (metadata.LastCheckedUtc ?? metadata.LastUpdated.ToUniversalTime()) >= TimeSpan.FromMinutes(5);
        public static bool NeedsExpansion(ArticleCacheItem metadata, ArticleContent cached, long revision, DateTime utcNow)
            => cached == null || cached.RevisionId != revision || !cached.HasRawSource || cached.NativeFormatVersion < ArticleContent.CurrentNativeFormatVersion ||
                metadata == null || utcNow - metadata.LastUpdated.ToUniversalTime() >= TimeSpan.FromDays(1);
    }
}
