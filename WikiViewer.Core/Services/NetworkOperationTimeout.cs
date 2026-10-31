using System;
using System.Threading;
using System.Threading.Tasks;

namespace WikiViewer.Core.Services
{
    public static class NetworkOperationTimeout
    {
        public static async Task<T> RunAsync<T>(Func<Task<T>> operation, TimeSpan timeout, string message)
        {
            var task = operation();
            using (var cancel = new CancellationTokenSource())
            {
                if (await Task.WhenAny(task, Task.Delay(timeout, cancel.Token)) != task)
                {
                    // Observe an eventual failure from an operation whose platform API has no cancellation.
                    _ = task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException(message);
                }
                cancel.Cancel(); return await task;
            }
        }
        public static async Task RunAsync(Func<Task> operation, TimeSpan timeout, string message)
            => await RunAsync(async () => { await operation(); return true; }, timeout, message);
    }
}
