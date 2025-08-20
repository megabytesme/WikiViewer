using _1809_UWP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Shared_Code
{
    public static class BackgroundCacheService
    {
        public static event EventHandler<ArticleCachedEventArgs> ArticleCached;

        public static async Task CacheFavouritesAsync(IEnumerable<string> titles)
        {
            var titlesToCache = titles.Where(t => !t.StartsWith("Talk:") && !t.StartsWith("User talk:")).ToList();
            if (!titlesToCache.Any())
            {
                Debug.WriteLine("[BG CACHE] No articles in favourites list to cache.");
                return;
            }

            int maxConcurrency = AppSettings.MaxConcurrentDownloads;

            Debug.WriteLine($"[BG CACHE] Starting PARALLEL caching for {titlesToCache.Count} favourites with a concurrency of {maxConcurrency}.");

            var semaphore = new SemaphoreSlim(maxConcurrency);

            var cachingTasks = titlesToCache.Select(async title =>
            {
                await semaphore.WaitAsync();
                try
                {
                    IApiWorker tempWorker = AppSettings.ConnectionBackend == ConnectionMethod.HttpClientProxy
                        ? (IApiWorker)new HttpClientApiWorker()
                        : new WebView2ApiWorker();

                    try
                    {
                        await tempWorker.InitializeAsync();

                        if (MainPage.ApiWorker != null)
                        {
                            await tempWorker.CopyApiCookiesFromAsync(MainPage.ApiWorker);
                        }

                        var stopwatch = Stopwatch.StartNew();
                        await ArticleProcessingService.FetchAndCacheArticleAsync(title, stopwatch, false, tempWorker, semaphore);

                        ArticleCached?.Invoke(null, new ArticleCachedEventArgs(title));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[BG CACHE] Failed to cache '{title}': {ex.Message}");
                    }
                    finally
                    {
                        tempWorker.Dispose();
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(cachingTasks);
            Debug.WriteLine("[BG CACHE] Background caching queue finished.");
        }
    }
}