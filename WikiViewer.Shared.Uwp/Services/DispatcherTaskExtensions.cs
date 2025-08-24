using System;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace WikiViewer.Shared.Uwp.Services
{
    public static class DispatcherTaskExtensions
    {
        public static Task<T> RunTaskAsync<T>(
            this CoreDispatcher dispatcher,
            Func<Task<T>> func,
            CoreDispatcherPriority priority = CoreDispatcherPriority.Normal
        )
        {
            var tcs = new TaskCompletionSource<T>();
            _ = dispatcher.RunAsync(
                priority,
                async () =>
                {
                    try
                    {
                        tcs.SetResult(await func());
                    }
                    catch (Exception e)
                    {
                        tcs.SetException(e);
                    }
                }
            );
            return tcs.Task;
        }

        public static Task<T> RunTaskAsync<T>(
            this CoreDispatcher dispatcher,
            Func<T> func,
            CoreDispatcherPriority priority = CoreDispatcherPriority.Normal
        )
        {
            var tcs = new TaskCompletionSource<T>();
            _ = dispatcher.RunAsync(
                priority,
                () =>
                {
                    try
                    {
                        tcs.SetResult(func());
                    }
                    catch (Exception e)
                    {
                        tcs.SetException(e);
                    }
                }
            );
            return tcs.Task;
        }
    }
}
