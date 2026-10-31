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
        public static async Task CacheArticlesAsync(
            IEnumerable<KeyValuePair<string, WikiInstance>> articlesToCache
        )
        {
            var requests = articlesToCache?.Where(item => item.Value != null && !string.IsNullOrWhiteSpace(item.Key)).ToList();
            if (requests == null || requests.Count == 0)
                return;

            await App.UIReady;

            int maxConcurrency = AppSettings.MaxConcurrentDownloads;
            using (var semaphore = new SemaphoreSlim(maxConcurrency))
            {

            var cachingTasks = requests.GroupBy(kvp => CacheKeyService.Article(kvp.Value.Id, kvp.Key)).Select(group => group.First()).Select(
                async (kvp) =>
                {
                    string title = kvp.Key;
                    WikiInstance wiki = kvp.Value;

                    await semaphore.WaitAsync();
                    try
                    {
                        var content = await ArticleProcessingService.LoadAsync(title, wiki);
                        content = await ArticleProcessingService.RevalidateAsync(title, wiki, content);
                        await ArticleProcessingService.EnrichAsync(title, wiki, content);
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
}
