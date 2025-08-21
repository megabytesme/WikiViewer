using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;

namespace WikiViewer.Shared.Uwp.Services
{
    public static class BackgroundCacheService
    {
        public static event EventHandler<ArticleCachedEventArgs> ArticleCached;

        public static async Task CacheFavouritesAsync(IEnumerable<string> titles)
        {
            var titlesToCache = titles
                .Where(t => !t.StartsWith("Talk:") && !t.StartsWith("User talk:"))
                .ToList();
            if (!titlesToCache.Any())
                return;

            int maxConcurrency = AppSettings.MaxConcurrentDownloads;
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var cachingTasks = titlesToCache.Select(async title =>
            {
                await semaphore.WaitAsync();
                try
                {
                    IApiWorker tempWorker;
                    if (AppSettings.ConnectionBackend == ConnectionMethod.HttpClientProxy)
                    {
                        tempWorker = new HttpClientApiWorker();
                    }
                    else
                    {
#if UWP_1703
                        tempWorker = (IApiWorker)
                            Activator.CreateInstance(
                                Type.GetType("_1703_UWP.Services.WebViewApiWorker, 1703 UWP")
                            );
#else
                        tempWorker = (IApiWorker)
                            Activator.CreateInstance(
                                Type.GetType("_1809_UWP.Services.WebView2ApiWorker, 1809 UWP")
                            );
#endif
                    }

                    try
                    {
                        await tempWorker.InitializeAsync();
                        var stopwatch = Stopwatch.StartNew();
                        await ArticleProcessingService.FetchAndCacheArticleAsync(
                            title,
                            stopwatch,
                            tempWorker,
                            false,
                            semaphore
                        );
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
        }
    }
}
