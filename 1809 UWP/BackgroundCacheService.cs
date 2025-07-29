using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Microsoft.UI.Xaml.Controls;

namespace _1809_UWP
{
    public static class BackgroundCacheService
    {
        public static event EventHandler<ArticleCachedEventArgs> ArticleCached;

        public static async Task CacheFavouritesAsync(IEnumerable<string> titles, WebView2 authenticatedWorker)
        {
            Debug.WriteLine($"[BG CACHE] Starting serial caching for {titles.Count()} favourites.");
            foreach (var title in titles)
            {
                try
                {
                    if (!title.StartsWith("Talk:") && !title.StartsWith("User talk:"))
                    {
                        var stopwatch = Stopwatch.StartNew();

                        await ArticleProcessingService.FetchAndCacheArticleAsync(title, stopwatch, false, authenticatedWorker);

                        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            ArticleCached?.Invoke(null, new ArticleCachedEventArgs(title));
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BG CACHE] Failed to cache '{title}': {ex.Message}");
                }
            }
            Debug.WriteLine("[BG CACHE] Background caching queue finished.");
        }
    }
}