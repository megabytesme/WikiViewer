using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Shared.Uwp.Services;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace _1507_UWP.Services
{
    public static class WebViewWorkerPool
    {
        private static readonly ConcurrentBag<WebViewApiWorker> _workerPool =
            new ConcurrentBag<WebViewApiWorker>();

        private static async Task<WebViewApiWorker> AcquireWorkerAsync(WikiInstance wiki)
        {
            return await CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(() =>
            {
                if (_workerPool.TryTake(out var worker))
                {
                    worker.WikiContext = wiki;
                    return worker;
                }
                var newWorker = new WebViewApiWorker { WikiContext = wiki };
                return newWorker;
            });
        }

        private static async Task ReleaseWorkerAsync(WebViewApiWorker worker)
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
            WebViewApiWorker worker = null;
            try
            {
                worker = await AcquireWorkerAsync(wiki);

                await worker.InitializeAsync(wiki.BaseUrl);
                return await operation(worker);
            }
            finally
            {
                if (worker != null)
                {
                    await ReleaseWorkerAsync(worker);
                }
            }
        }
    }
}
