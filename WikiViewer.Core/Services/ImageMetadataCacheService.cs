using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class ImageMetadataCacheService
    {
        public static IStorageProvider StorageProvider { get; set; }
        private const string CacheFileName = "ImageMetadataCache.json";
        private static ConcurrentDictionary<string, ImageMetadata> _metadataCache;
        private static bool _isInitialized = false;

        private static Timer _saveTimer;
        private static readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private static bool _isCacheDirty = false;

        public static async Task InitializeAsync()
        {
            if (_isInitialized)
                return;
            if (StorageProvider == null)
                throw new InvalidOperationException(
                    "StorageProvider not set for ImageMetadataCacheService."
                );

            _saveTimer = new Timer(
                async (state) => await SaveCacheAsync(),
                null,
                Timeout.Infinite,
                Timeout.Infinite
            );

            try
            {
                string json = await StorageProvider.ReadTextAsync(CacheFileName);
                if (!string.IsNullOrEmpty(json))
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, ImageMetadata>>(
                        json
                    );
                    _metadataCache = new ConcurrentDictionary<string, ImageMetadata>(
                        dict ?? new Dictionary<string, ImageMetadata>()
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageMetadataCache] Failed to load cache: {ex.Message}");
            }

            if (_metadataCache == null)
                _metadataCache = new ConcurrentDictionary<string, ImageMetadata>();

            _isInitialized = true;
        }

        private static void RequestSave()
        {
            _isCacheDirty = true;
            _saveTimer.Change(3000, Timeout.Infinite);
        }

        private static async Task SaveCacheAsync()
        {
            if (!_isInitialized || !_isCacheDirty)
                return;

            await _saveLock.WaitAsync();
            try
            {
                if (!_isCacheDirty)
                    return;
                Debug.WriteLine("[ImageMetadataCache] Saving metadata cache to disk...");
                string json = JsonConvert.SerializeObject(_metadataCache, Formatting.None);
                await StorageProvider.WriteTextAsync(CacheFileName, json);
                _isCacheDirty = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageMetadataCache] Failed to save cache: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private static string GetCacheKey(Guid wikiId, string filePageTitle)
        {
            string normalizedTitle = filePageTitle.Replace(' ', '_');
            return $"{wikiId}:{normalizedTitle}";
        }

        public static ImageMetadata GetMetadata(Guid wikiId, string filePageTitle)
        {
            if (!_isInitialized)
                return null;

            _metadataCache.TryGetValue(GetCacheKey(wikiId, filePageTitle), out var metadata);
            return metadata;
        }

        public static void StoreMetadata(Guid wikiId, string filePageTitle, ImageMetadata metadata)
        {
            if (!_isInitialized || metadata == null || string.IsNullOrEmpty(filePageTitle))
                return;

            _metadataCache[GetCacheKey(wikiId, filePageTitle)] = metadata;
            RequestSave();
        }
    }
}
