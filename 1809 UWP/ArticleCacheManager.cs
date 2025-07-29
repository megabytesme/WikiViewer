using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

public class ArticleCacheItem
{
    public string Title { get; set; }
    public DateTime LastUpdated { get; set; }
}

public static class ArticleCacheManager
{
    private static StorageFolder _cacheFolder;
    private static StorageFolder _imageCacheFolder;

    public static async Task InitializeAsync()
    {
        if (_cacheFolder == null)
        {
            _cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "ArticleCache",
                CreationCollisionOption.OpenIfExists
            );
        }
        if (_imageCacheFolder == null)
        {
            _imageCacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "cache",
                CreationCollisionOption.OpenIfExists
            );
        }
    }

    public static async Task<ulong> GetCacheSizeAsync()
    {
        await InitializeAsync();
        ulong totalSize = 0;

        try
        {
            var articleFiles = await _cacheFolder.GetFilesAsync();
            foreach (var file in articleFiles)
            {
                var properties = await file.GetBasicPropertiesAsync();
                totalSize += properties.Size;
            }

            var imageFiles = await _imageCacheFolder.GetFilesAsync();
            foreach (var file in imageFiles)
            {
                var properties = await file.GetBasicPropertiesAsync();
                totalSize += properties.Size;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CACHE] Error calculating cache size: {ex.Message}");
        }

        return totalSize;
    }

    public static async Task ClearCacheAsync()
    {
        await InitializeAsync();

        try
        {
            var articleFiles = await _cacheFolder.GetFilesAsync();
            foreach (var file in articleFiles)
            {
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }

            var imageFiles = await _imageCacheFolder.GetFilesAsync();
            foreach (var file in imageFiles)
            {
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }

            Debug.WriteLine("[CACHE] All cache folders cleared.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CACHE] Error clearing cache: {ex.Message}");
        }
    }

    private static string GetHashedFileName(string pageTitle)
    {
        string sanitizedTitle = string.Join("_", pageTitle.Split(Path.GetInvalidFileNameChars()));

        var hash = System
            .Security.Cryptography.SHA1.Create()
            .ComputeHash(Encoding.UTF8.GetBytes(sanitizedTitle.ToLowerInvariant()));
        return hash.Aggregate("", (s, b) => s + b.ToString("x2"));
    }

    public static async Task<ArticleCacheItem> GetCacheMetadataAsync(string pageTitle)
    {
        await InitializeAsync();
        string fileName = GetHashedFileName(pageTitle) + ".json";
        var item = await _cacheFolder.TryGetItemAsync(fileName);
        if (item is StorageFile file)
        {
            try
            {
                string json = await FileIO.ReadTextAsync(file);
                return JsonSerializer.Deserialize<ArticleCacheItem>(json);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public static async Task<string> GetCachedArticleHtmlAsync(string pageTitle)
    {
        await InitializeAsync();
        string fileName = GetHashedFileName(pageTitle) + ".html";
        var item = await _cacheFolder.TryGetItemAsync(fileName);
        if (item is StorageFile file)
        {
            return await FileIO.ReadTextAsync(file);
        }
        return null;
    }

    public static async Task SaveArticleToCacheAsync(
        string pageTitle,
        string htmlContent,
        DateTime lastUpdated
    )
    {
        await InitializeAsync();
        string baseFileName = GetHashedFileName(pageTitle);

        var metadata = new ArticleCacheItem { Title = pageTitle, LastUpdated = lastUpdated };
        string json = JsonSerializer.Serialize(metadata);
        StorageFile metadataFile = await _cacheFolder.CreateFileAsync(
            baseFileName + ".json",
            CreationCollisionOption.ReplaceExisting
        );
        await FileIO.WriteTextAsync(metadataFile, json);

        StorageFile htmlFile = await _cacheFolder.CreateFileAsync(
            baseFileName + ".html",
            CreationCollisionOption.ReplaceExisting
        );
        await FileIO.WriteTextAsync(htmlFile, htmlContent);

        Debug.WriteLine($"[CACHE] Saved '{pageTitle}' to cache.");
    }
}