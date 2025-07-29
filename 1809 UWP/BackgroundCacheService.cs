using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Microsoft.UI.Xaml.Controls;

namespace _1809_UWP
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

            Debug.WriteLine($"[BG CACHE] Starting PARALLEL caching for {titlesToCache.Count} favourites with a concurrency of {Environment.ProcessorCount}.");

            var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

            var cachingTasks = titlesToCache.Select(async title =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                    {
                        WebView2 tempWorker = null;
                        try
                        {
                            tempWorker = new WebView2();
                            App.UIHost.Children.Add(tempWorker);
                            await tempWorker.EnsureCoreWebView2Async();

                            if (MainPage.ApiWorker?.CoreWebView2 != null)
                            {
                                await ApiRequestService.CopyApiCookiesAsync(MainPage.ApiWorker.CoreWebView2, tempWorker.CoreWebView2);
                            }

                            var stopwatch = Stopwatch.StartNew();
                            await ArticleProcessingService.FetchAndCacheArticleAsync(title, stopwatch, false, tempWorker);

                            ArticleCached?.Invoke(null, new ArticleCachedEventArgs(title));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[BG CACHE] Failed to cache '{title}': {ex.Message}");
                        }
                        finally
                        {
                            if (tempWorker != null)
                            {
                                App.UIHost.Children.Remove(tempWorker);
                                tempWorker.Close();
                            }
                        }
                    });
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