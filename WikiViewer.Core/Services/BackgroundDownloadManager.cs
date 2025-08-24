using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class BackgroundDownloadManager
    {
        public static IApiWorkerFactory ApiWorkerFactory { get; set; }
        public static IStorageProvider StorageProvider { get; set; }

        private class DownloadQueueItem
        {
            public string Url { get; set; }
            public Guid WikiId { get; set; }
        }

        private const string QueueFileName = "media_download_queue.json";
        private static List<DownloadQueueItem> _downloadQueue;
        private static bool _isInitialized = false;
        private static bool _isProcessing = false;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public static event Action<string, string> MediaDownloaded; // originalUrl, localPath

        public static async Task InitializeAsync()
        {
            if (_isInitialized)
                return;
            if (StorageProvider == null)
                throw new InvalidOperationException(
                    "StorageProvider not set for BackgroundDownloadManager."
                );

            await _semaphore.WaitAsync();
            try
            {
                if (_isInitialized)
                    return;

                try
                {
                    string json = await StorageProvider.ReadTextAsync(QueueFileName);
                    if (!string.IsNullOrEmpty(json))
                    {
                        _downloadQueue =
                            JsonConvert.DeserializeObject<List<DownloadQueueItem>>(json)
                            ?? new List<DownloadQueueItem>();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BG D/L] Failed to load queue: {ex.Message}");
                }

                if (_downloadQueue == null)
                    _downloadQueue = new List<DownloadQueueItem>();

                _isInitialized = true;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static async Task SaveQueueAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                string json = JsonConvert.SerializeObject(_downloadQueue, Formatting.None);
                await StorageProvider.WriteTextAsync(QueueFileName, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BG D/L] Failed to save queue: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public static async Task AddToQueueAsync(IEnumerable<string> urls, WikiInstance wiki)
        {
            if (!_isInitialized || !urls.Any() || wiki == null)
                return;

            bool added = false;
            await _semaphore.WaitAsync();
            try
            {
                foreach (var url in urls)
                {
                    if (!_downloadQueue.Any(i => i.Url == url))
                    {
                        _downloadQueue.Add(new DownloadQueueItem { Url = url, WikiId = wiki.Id });
                        added = true;
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }

            if (added)
            {
                await SaveQueueAsync();
                ProcessQueueAsync();
            }
        }

        public static async Task ProcessQueueAsync()
        {
            if (!_isInitialized || _isProcessing || ApiWorkerFactory == null)
                return;

            await _semaphore.WaitAsync();
            if (_isProcessing)
            {
                _semaphore.Release();
                return;
            }
            _isProcessing = true;
            _semaphore.Release();

            try
            {
                while (true)
                {
                    List<DownloadQueueItem> itemsToProcess;
                    await _semaphore.WaitAsync();
                    if (_downloadQueue.Count == 0)
                    {
                        _semaphore.Release();
                        break;
                    }
                    itemsToProcess = new List<DownloadQueueItem>(_downloadQueue);
                    _downloadQueue.Clear();
                    _semaphore.Release();

                    await SaveQueueAsync(); // Save the now-empty queue

                    Debug.WriteLine(
                        $"[BG D/L] Starting queue processing. Items to process: {itemsToProcess.Count}"
                    );

                    var groupedByWiki = itemsToProcess.GroupBy(i => i.WikiId);

                    foreach (var group in groupedByWiki)
                    {
                        var wiki = WikiManager.GetWikiById(group.Key);
                        if (wiki == null)
                            continue;

                        // Create ONE worker per wiki and reuse it for all downloads in the group
                        using (var worker = ApiWorkerFactory.CreateApiWorker(wiki))
                        {
                            await worker.InitializeAsync(wiki.BaseUrl);
                            var downloadSemaphore = new SemaphoreSlim(
                                AppSettings.MaxConcurrentDownloads
                            );

                            var downloadTasks = group.Select(async item =>
                            {
                                await downloadSemaphore.WaitAsync();
                                try
                                {
                                    string localPath = await DownloadAndCacheMediaAsync(
                                        item.Url,
                                        wiki,
                                        worker
                                    );
                                    if (!string.IsNullOrEmpty(localPath))
                                    {
                                        MediaDownloaded?.Invoke(item.Url, localPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(
                                        $"[BG D/L] Failed to download {item.Url}: {ex.Message}"
                                    );
                                }
                                finally
                                {
                                    downloadSemaphore.Release();
                                }
                            });

                            await Task.WhenAll(downloadTasks);
                        }
                    }
                }
            }
            finally
            {
                Debug.WriteLine("[BG D/L] Queue processing finished.");
                _isProcessing = false;
            }
        }

        private static async Task<string> DownloadAndCacheMediaAsync(
            string absoluteMediaUrl,
            WikiInstance wiki,
            IApiWorker worker
        )
        {
            if (!Uri.TryCreate(absoluteMediaUrl, UriKind.Absolute, out Uri mediaUrl))
                return null;

            byte[] mediaBytes = await worker.GetRawBytesFromUrlAsync(mediaUrl.AbsoluteUri);

            if (mediaBytes == null || mediaBytes.Length == 0)
                return null;

            var extension = Path.GetExtension(mediaUrl.AbsolutePath).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
                extension = ".dat";

            var hash = System
                .Security.Cryptography.SHA1.Create()
                .ComputeHash(System.Text.Encoding.UTF8.GetBytes(mediaUrl.AbsoluteUri));
            var baseFileName = hash.Aggregate("", (s, b) => s + b.ToString("x2"));
            var finalFileName = baseFileName + extension;

            var relativePath = $"/cache/{finalFileName}";
            var fullPath = Path.Combine("cache", finalFileName);

            try
            {
                await StorageProvider.WriteBytesAsync(fullPath, mediaBytes);
                ImageUpgradeManager.SetUpgradePath(absoluteMediaUrl, relativePath);
                return relativePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BG D/L] FAILED to save file {finalFileName}: {ex.Message}");
                return null;
            }
        }
    }
}
