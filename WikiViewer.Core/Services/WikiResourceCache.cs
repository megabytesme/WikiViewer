using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core.Services
{
    /// <summary>Bounded shared cache for public styles and syntax metadata, including across restarts.</summary>
    public sealed class WikiResourceCache
    {
        public static WikiResourceCache Shared { get; set; }
        private readonly IStorageProvider _storage;
        private readonly AsyncRequestCoordinator<string> _requests = new AsyncRequestCoordinator<string>();
        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _memory = new Dictionary<string, Entry>();
        private readonly Queue<string> _order = new Queue<string>();
        private sealed class Entry { public DateTime SavedUtc { get; set; } public string Text { get; set; } }
        public WikiResourceCache(IStorageProvider storage) { _storage = storage; }
        public void Seed(string key, string text, DateTime savedUtc)
        {
            lock (_gate) { if (_memory.ContainsKey(key) || string.IsNullOrWhiteSpace(text)) return; }
            Remember(key, new Entry { Text = text, SavedUtc = savedUtc });
        }
        public async Task<string> TryReadAsync(string key, bool allowStale = true)
        {
            Entry entry;
            lock (_gate) _memory.TryGetValue(key, out entry);
            if (entry == null && _storage != null)
            {
                try { entry = JsonConvert.DeserializeObject<Entry>(await _storage.ReadTextAsync("ResourceCache\\" + CacheKeyService.Hash(key) + ".json") ?? ""); }
                catch (Exception) { }
                if (entry?.Text != null) Remember(key, entry);
            }
            return entry?.Text != null && (allowStale || DateTime.UtcNow - entry.SavedUtc < TimeSpan.FromDays(1)) ? entry.Text : null;
        }
        public Task<string> GetAsync(string key, Func<Task<string>> fetch) => _requests.RunAsync(key, async () =>
        {
            string fresh = await TryReadAsync(key, false);
            if (fresh != null) return fresh;
            try
            {
                string value = await fetch();
                if (string.IsNullOrWhiteSpace(value) || value.Length > 1000000) return await TryReadAsync(key);
                var entry = new Entry { SavedUtc = DateTime.UtcNow, Text = value }; Remember(key, entry);
                if (_storage != null)
                {
                    try { await _storage.WriteTextAsync("ResourceCache\\" + CacheKeyService.Hash(key) + ".json", JsonConvert.SerializeObject(entry)); }
                    catch (Exception) { /* Usable resources do not depend on a successful disk write. */ }
                }
                return value;
            }
            catch { var stale = await TryReadAsync(key); if (stale != null) return stale; throw; }
        });
        private void Remember(string key, Entry entry)
        {
            lock (_gate)
            {
                if (!_memory.ContainsKey(key)) _order.Enqueue(key);
                _memory[key] = entry;
                while (_memory.Count > 16) _memory.Remove(_order.Dequeue());
            }
        }
        public void ClearMemory() { lock (_gate) { _memory.Clear(); _order.Clear(); } }
    }
}
