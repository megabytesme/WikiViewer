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

        public static async Task CacheArticlesAsync(
            Dictionary<string, WikiInstance> articlesToCache
        )
        {
            if (articlesToCache == null || !articlesToCache.Any())
                return;

            await App.UIReady;

            int maxConcurrency = AppSettings.MaxConcurrentDownloads;
            var semaphore = new SemaphoreSlim(maxConcurrency);

            var cachingTasks = articlesToCache.Select(
                async (kvp) =>
                {
                    string title = kvp.Key;
                    WikiInstance wiki = kvp.Value;

                    await semaphore.WaitAsync();
                    try
                    {
                        using (var tempWorker = App.ApiWorkerFactory.CreateApiWorker(wiki))
                        {
                            await tempWorker.InitializeAsync(wiki.BaseUrl);

                            var stopwatch = Stopwatch.StartNew();
                            await ArticleProcessingService.FetchAndProcessArticleAsync(
                                title,
                                stopwatch,
                                tempWorker,
                                wiki,
                                true,
                                semaphore
                            );
                            ArticleCached?.Invoke(null, new ArticleCachedEventArgs(title));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[BG CACHE] Failed to cache '{title}' from '{wiki.Name}': {ex.Message}"
                        );
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
            );

            await Task.WhenAll(cachingTasks);
        }
    }
}