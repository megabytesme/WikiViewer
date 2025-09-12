using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
#if WINDOWS_UWP
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
#else
using System.Linq;
using System.Security.Cryptography;
using System.Text;
#endif

namespace WikiViewer.Core.Services
{
    public static class MediaCacheService
    {
        public static IApiWorkerFactory ApiWorkerFactory { get; set; }
        public static IStorageProvider StorageProvider { get; set; }
        public static Func<Func<Task>, Task> DispatcherInvoker { get; set; }

        private const string MapFileName = "MediaCacheMap.json";
        private static ConcurrentDictionary<string, string> _urlToLocalPathMap;
        private static readonly ConcurrentDictionary<string, Task<string>> _inFlightDownloads =
            new ConcurrentDictionary<string, Task<string>>();
        private static SemaphoreSlim _downloadSemaphore;
        private static bool _isInitialized = false;

        private static Timer _saveTimer;
        private static readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private static bool _isMapDirty = false;

        public static async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            if (StorageProvider == null || ApiWorkerFactory == null || DispatcherInvoker == null)
                throw new InvalidOperationException("Dependencies for MediaCacheService not set.");

            _downloadSemaphore = new SemaphoreSlim(AppSettings.MaxConcurrentDownloads);
            _saveTimer = new Timer(
                async (state) => await SaveMapAsync(),
                null,
                Timeout.Infinite,
                Timeout.Infinite
            );

            try
            {
                string json = await StorageProvider.ReadTextAsync(MapFileName);
                if (!string.IsNullOrEmpty(json))
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    _urlToLocalPathMap = new ConcurrentDictionary<string, string>(
                        dict ?? new Dictionary<string, string>()
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaCacheService] Failed to load map: {ex.Message}");
            }

            if (_urlToLocalPathMap == null)
                _urlToLocalPathMap = new ConcurrentDictionary<string, string>();

            _isInitialized = true;
        }

        private static void RequestSave()
        {
            _isMapDirty = true;
            _saveTimer.Change(2000, Timeout.Infinite);
        }

        private static async Task SaveMapAsync()
        {
            if (!_isInitialized || !_isMapDirty)
                return;

            await _saveLock.WaitAsync();
            try
            {
                if (!_isMapDirty)
                    return;

                Debug.WriteLine("[MediaCacheService] Debounced save triggered. Saving map...");
                string json = JsonConvert.SerializeObject(_urlToLocalPathMap, Formatting.None);
                await StorageProvider.WriteTextAsync(MapFileName, json);
                _isMapDirty = false;
                Debug.WriteLine("[MediaCacheService] Map saved successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaCacheService] Failed to save map: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public static async Task<string> GetLocalUriAsync(
            string remoteUrl,
            WikiInstance wiki,
            string originalThumbnailUrl = null
        )
        {
            if (string.IsNullOrEmpty(remoteUrl) || wiki == null)
                return null;

            string mapKey = remoteUrl;

            if (
                !string.IsNullOrEmpty(originalThumbnailUrl)
                && _urlToLocalPathMap.ContainsKey(originalThumbnailUrl)
            )
            {
                mapKey = originalThumbnailUrl;
            }

            if (_urlToLocalPathMap.TryGetValue(mapKey, out var localPath))
            {
                if (
                    await StorageProvider.FileExistsAsync(
                        $"cache\\{System.IO.Path.GetFileName(localPath)}"
                    )
                )
                {
                    if (mapKey != remoteUrl)
                    {
                        _urlToLocalPathMap[remoteUrl] = localPath;
                        RequestSave();
                    }
                    return $"ms-appdata:///local{localPath}";
                }
                else
                {
                    _urlToLocalPathMap.TryRemove(mapKey, out _);
                    if (mapKey != remoteUrl)
                        _urlToLocalPathMap.TryRemove(remoteUrl, out _);
                    RequestSave();
                }
            }

            string finalPath = await _inFlightDownloads.GetOrAdd(
                remoteUrl,
                (url) => DownloadAndCacheAsync(url, wiki)
            );

            _inFlightDownloads.TryRemove(remoteUrl, out _);

            return finalPath;
        }

        private static string GetHashedString(string input)
        {
#if WINDOWS_UWP
            var algProvider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            IBuffer buffer = CryptographicBuffer.ConvertStringToBinary(
                input,
                BinaryStringEncoding.Utf8
            );
            var hashedBuffer = algProvider.HashData(buffer);
            return CryptographicBuffer.EncodeToHexString(hashedBuffer);
#else
            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
                return hash.Aggregate("", (s, b) => s + b.ToString("x2"));
            }
#endif
        }

        private static async Task<string> DownloadAndCacheAsync(string remoteUrl, WikiInstance wiki)
        {
            await _downloadSemaphore.WaitAsync();
            try
            {
                using (var worker = ApiWorkerFactory.CreateApiWorker(wiki))
                {
                    await worker.InitializeAsync(wiki.BaseUrl);
                    byte[] imageBytes = await worker.GetRawBytesFromUrlAsync(remoteUrl);

                    if (imageBytes?.Length > 0)
                    {
                        string hashedUrl = GetHashedString(remoteUrl);

                        string fileExtension = System.IO.Path.GetExtension(
                            new Uri(remoteUrl).AbsolutePath
                        );
                        if (string.IsNullOrEmpty(fileExtension))
                            fileExtension = ".jpg";

                        var fileName = hashedUrl + fileExtension;
                        var relativePath = $"/cache/{fileName}";

                        await StorageProvider.WriteBytesAsync($"cache\\{fileName}", imageBytes);

                        _urlToLocalPathMap[remoteUrl] = relativePath;
                        RequestSave();

                        return $"ms-appdata:///local{relativePath}";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[MediaCacheService] Download failed for {remoteUrl}: {ex.Message}"
                );
            }
            finally
            {
                _downloadSemaphore.Release();
            }
            return null;
        }
    }
}
