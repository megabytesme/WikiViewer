using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WikiViewer.Core.Services
{
    /// <summary>Shares an operation without starting losing dictionary factories or retaining failures.</summary>
    public sealed class AsyncRequestCoordinator<T>
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, Lazy<Task<T>>> _pending = new Dictionary<string, Lazy<Task<T>>>(StringComparer.Ordinal);
        public async Task<T> RunAsync(string key, Func<Task<T>> operation)
        {
            Lazy<Task<T>> pending;
            lock (_gate)
            {
                if (!_pending.TryGetValue(key, out pending))
                    _pending[key] = pending = new Lazy<Task<T>>(operation, true);
            }
            try { return await pending.Value; }
            finally
            {
                lock (_gate)
                {
                    Lazy<Task<T>> current;
                    if (_pending.TryGetValue(key, out current) && ReferenceEquals(current, pending)) _pending.Remove(key);
                }
            }
        }
    }
}
