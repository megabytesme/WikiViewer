using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly AsyncRequestCoordinator<string> Downloads = new AsyncRequestCoordinator<string>();
        private static readonly ConcurrentDictionary<string, bool> VerifiedPaths = new ConcurrentDictionary<string, bool>();
        private static int _generation;
        private static SemaphoreSlim _downloadSemaphore;
        private static bool _isInitialized = false;

        private static Timer _saveTimer;
        private static readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private static long _mapVersion;
        private static long _savedVersion;

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
            Interlocked.Increment(ref _mapVersion);
            _saveTimer.Change(2000, Timeout.Infinite);
        }

        private static async Task SaveMapAsync()
        {
            if (!_isInitialized || Interlocked.Read(ref _mapVersion) == Interlocked.Read(ref _savedVersion))
                return;

            await _saveLock.WaitAsync();
            try
            {
                long version = Interlocked.Read(ref _mapVersion);
                if (version == Interlocked.Read(ref _savedVersion))
                    return;

                Debug.WriteLine("[MediaCacheService] Debounced save triggered. Saving map...");
                string json = JsonConvert.SerializeObject(_urlToLocalPathMap, Formatting.None);
                await StorageProvider.WriteTextAsync(MapFileName, json);
                Interlocked.Exchange(ref _savedVersion, version);
                if (version != Interlocked.Read(ref _mapVersion)) _saveTimer.Change(2000, Timeout.Infinite);
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
            string originalThumbnailUrl = null,
            CancellationToken cancellationToken = default(CancellationToken)
        )
        {
            if (string.IsNullOrEmpty(remoteUrl) || wiki == null)
                return null;
            cancellationToken.ThrowIfCancellationRequested();

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
                    VerifiedPaths.ContainsKey(localPath) || await StorageProvider.FileExistsAsync(
                        $"cache\\{System.IO.Path.GetFileName(localPath)}"
                    )
                )
                {
                    VerifiedPaths[localPath] = true;
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

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { return await Downloads.RunAsync(_generation + ":" + remoteUrl, () => DownloadAndCacheAsync(remoteUrl, wiki, cancellationToken)); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { /* Another page canceled a queued request. This live consumer still needs it. */ }
            }
        }

        public static async Task ClearMappingsAsync()
        {
            Interlocked.Increment(ref _generation); _urlToLocalPathMap.Clear(); VerifiedPaths.Clear(); RequestSave(); await SaveMapAsync();
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

        private static async Task<string> DownloadAndCacheAsync(string remoteUrl, WikiInstance wiki, CancellationToken cancellationToken)
        {
            int generation = _generation;
            await _downloadSemaphore.WaitAsync(cancellationToken);
            try
            {
                byte[] bytes = null;
                var method = wiki.ResolvedConnectionMethod ?? wiki.PreferredConnectionMethod;
                string extension = System.IO.Path.GetExtension(new Uri(remoteUrl).AbsolutePath).ToLowerInvariant();
                bool rasterOrVector = new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".svg", ".ico" }.Contains(extension);
                if (rasterOrVector && method != WikiViewer.Core.Enums.ConnectionMethod.HttpClient && method != WikiViewer.Core.Enums.ConnectionMethod.HttpClientProxy)
                {
                    try
                    {
                        using (var direct = new HttpClientApiWorker())
                        {
                            await direct.InitializeAsync(wiki.BaseUrl);
                            bytes = await NetworkOperationTimeout.RunAsync(() => direct.GetRawBytesFromUrlAsync(remoteUrl), TimeSpan.FromSeconds(3), "Direct image download timed out.");
                            if (NativeMediaService.IsErrorDocument(bytes)) bytes = null;
                        }
                    }
                    catch (Exception) { /* Protected files keep the selected wiki backend. */ }
                }
                if (bytes == null) using (var worker = ApiWorkerFactory.CreateApiWorker(wiki))
                {
                    await worker.InitializeAsync(wiki.BaseUrl);
                    bytes = await worker.GetRawBytesFromUrlAsync(remoteUrl);
                }
                    byte[] imageBytes = bytes;

                    if (generation == _generation && imageBytes?.Length > 0 && !NativeMediaService.IsErrorDocument(imageBytes))
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
                        if (generation != _generation) return null;
                        _urlToLocalPathMap[remoteUrl] = relativePath;
                        VerifiedPaths[relativePath] = true;
                        RequestSave();

                        return $"ms-appdata:///local{relativePath}";
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
