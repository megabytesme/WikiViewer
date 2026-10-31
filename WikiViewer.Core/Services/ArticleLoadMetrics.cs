using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core.Services
{
    public static class ArticleLoadMetrics
    {
        public static IStorageProvider StorageProvider { get; set; }
        private static readonly object Gate = new object();
        private static readonly List<object> Samples = new List<object>();
        private static readonly AsyncRequestCoordinator<bool> Writes = new AsyncRequestCoordinator<bool>();
        public static void Record(string phase, long milliseconds)
        {
            lock (Gate)
            {
                Samples.Add(new { Utc = DateTime.UtcNow, Phase = phase, Milliseconds = milliseconds });
                if (Samples.Count > 60) Samples.RemoveAt(0);
            }
            _ = SaveAsync();
        }
        private static async Task SaveAsync()
        {
            try
            {
                await Writes.RunAsync("timings", async () =>
                {
                    await Task.Delay(250);
                    string json; lock (Gate) json = JsonConvert.SerializeObject(Samples);
                    if (StorageProvider != null) await StorageProvider.WriteTextAsync("article-load-metrics.json", json);
                    return true;
                });
            }
            catch (Exception) { }
        }
    }
}
