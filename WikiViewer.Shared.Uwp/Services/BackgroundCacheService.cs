using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;

namespace WikiViewer.Shared.Uwp.Services
{
    public static class BackgroundCacheService
    {
        public static event EventHandler<ArticleCachedEventArgs> ArticleCached;

        public static async Task CacheFavouritesAsync(IEnumerable<string> titles)
        {
            await App.UIReady;

            var titlesToCache = titles
                .Where(t => !t.StartsWith("Talk:") && !t.StartsWith("User talk:"))
                .ToList();
            if (!titlesToCache.Any())
                return;

            if (SessionManager.CurrentWiki == null)
            {
                Debug.WriteLine("[BG CACHE] Aborting: No current wiki in session.");
                return;
            }

            int maxConcurrency = AppSettings.MaxConcurrentDownloads;
            var semaphore = new SemaphoreSlim(maxConcurrency);

            var cachingTasks = titlesToCache.Select(async title =>
            {
                using (
                    var tempWorker = App.ApiWorkerFactory.CreateApiWorker(
                        SessionManager.CurrentWiki.PreferredConnectionMethod
                    )
                )
                {
                    try
                    {
                        await tempWorker.InitializeAsync(SessionManager.CurrentWiki.BaseUrl);

                        var stopwatch = Stopwatch.StartNew();
                        await ArticleProcessingService.FetchAndCacheArticleAsync(
                            title,
                            stopwatch,
                            tempWorker,
                            forceRefresh: true,
                            semaphore
                        );
                        ArticleCached?.Invoke(null, new ArticleCachedEventArgs(title));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[BG CACHE] Failed to cache '{title}': {ex.Message}");
                    }
                }
            });

            await Task.WhenAll(cachingTasks);
        }
    }
}
