using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core.Services
{
    public static class ImageUpgradeManager
    {
        private const string MapFileName = "ImageUpgradeMap.json";
        private static Dictionary<string, string> _upgradeMap;
        private static bool _isInitialized = false;

        public static IStorageProvider StorageProvider { get; set; }

        private static Timer _saveTimer;
        private static readonly object _lock = new object();
        private static bool _isDirty = false;

        public static async Task InitializeAsync()
        {
            if (_isInitialized) return;
            if (StorageProvider == null)
                throw new InvalidOperationException("StorageProvider has not been initialized in ImageUpgradeManager.");

            _saveTimer = new Timer(async (state) => await SaveAsync(), null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                string json = await StorageProvider.ReadTextAsync(MapFileName);
                if (!string.IsNullOrEmpty(json))
                {
                    _upgradeMap = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                                  ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageUpgradeManager] Failed to load map: {ex.Message}");
            }

            if (_upgradeMap == null)
                _upgradeMap = new Dictionary<string, string>();
            _isInitialized = true;
        }

        public static string GetUpgradePath(string originalUrl)
        {
            return _upgradeMap.TryGetValue(originalUrl, out var path) ? path : null;
        }

        public static void SetUpgradePath(string originalUrl, string highResLocalPath)
        {
            if (string.IsNullOrEmpty(originalUrl) || string.IsNullOrEmpty(highResLocalPath)) return;

            lock (_lock)
            {
                _upgradeMap[originalUrl] = highResLocalPath;
                _isDirty = true;
            }

            _saveTimer.Change(2000, Timeout.Infinite);
        }

        private static async Task SaveAsync()
        {
            if (StorageProvider == null) return;

            Dictionary<string, string> mapToSave;
            lock (_lock)
            {
                if (!_isDirty) return;
                mapToSave = new Dictionary<string, string>(_upgradeMap);
                _isDirty = false;
            }

            try
            {
                Debug.WriteLine("[ImageUpgradeManager] Debounced save triggered. Saving upgrade map...");
                string json = JsonConvert.SerializeObject(mapToSave, Formatting.Indented);
                await StorageProvider.WriteTextAsync(MapFileName, json);
                Debug.WriteLine("[ImageUpgradeManager] Upgrade map saved successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageUpgradeManager] Failed to save map: {ex.Message}");
                lock (_lock) { _isDirty = true; }
            }
        }
    }
}