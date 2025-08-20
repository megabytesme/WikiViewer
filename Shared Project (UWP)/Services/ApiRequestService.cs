using System;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace Shared_Code
{
    public static class ApiRequestService { }

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
    }
}
