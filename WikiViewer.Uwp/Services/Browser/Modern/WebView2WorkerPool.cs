using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Uwp.Services;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace WikiViewer.Uwp.Services
{
    public static class WebView2WorkerPool
    {
        private static readonly ConcurrentBag<WebView2ApiWorker> _workerPool =
            new ConcurrentBag<WebView2ApiWorker>();

        private static async Task<WebView2ApiWorker> AcquireWorkerAsync(WikiInstance wiki)
        {
            return await CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(() =>
            {
                if (_workerPool.TryTake(out var worker))
                {
                    worker.WikiContext = wiki;
                    return worker;
                }
                var newWorker = new WebView2ApiWorker { WikiContext = wiki };
                return newWorker;
            });
        }

        private static async Task ReleaseWorkerAsync(WebView2ApiWorker worker)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    worker.WikiContext = null;
                    _workerPool.Add(worker);
                }
            );
        }

        public static async Task<T> UseWorkerAsync<T>(
            WikiInstance wiki,
            Func<IApiWorker, Task<T>> operation
        )
        {
            WebView2ApiWorker worker = null;
            bool reusable = false;
            try
            {
                worker = await AcquireWorkerAsync(wiki);

                var result = await CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(async () =>
                {
                    await worker.InitializeAsync(wiki.BaseUrl);
                    return await operation(worker);
                });
                reusable = true; return result;
            }
            finally
            {
                if (worker != null)
                {
                    if (reusable) await ReleaseWorkerAsync(worker);
                    else worker.Dispose();
                }
            }
        }
    }
}
