using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;

namespace WikiViewer.Core.Services
{
    public static class FaviconService
    {
        public static async Task<string> FetchAndCacheFaviconUrlAsync(WikiInstance wiki, IApiWorker worker, bool forceRefresh = false)
        {
            if (wiki == null || worker == null) return null;
            if (wiki.IsIconUserSet && !forceRefresh) return wiki.IconUrl;
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("favicons", CreationCollisionOption.OpenIfExists);
            if (!forceRefresh && !string.IsNullOrEmpty(wiki.IconUrl) && wiki.IconUrl.StartsWith("ms-appdata:///", StringComparison.Ordinal))
            {
                try
                {
                    var existing = await StorageFile.GetFileFromApplicationUriAsync(new Uri(wiki.IconUrl));
                    if ((await existing.GetBasicPropertiesAsync()).Size > 0) return wiki.IconUrl;
                }
                catch (Exception) { }
            }
            var result = await FaviconDiscoveryService.FetchAsync(new Uri(wiki.BaseUrl), wiki.ApiEndpoint,
                worker.GetJsonFromApiAsync, worker.GetRawHtmlFromUrlAsync, worker.GetRawBytesFromUrlAsync);
            if (result.Bytes == null)
            {
                await RecordAsync(wiki, "Unavailable\n" + string.Join("\n", result.Failures));
                return null;
            }
            try
            {
                var provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256);
                string hash = CryptographicBuffer.EncodeToHexString(provider.HashData(CryptographicBuffer.CreateFromByteArray(result.Bytes))).Substring(0, 16);
                string fileName = wiki.Id + "-" + hash + "." + result.Format;
                string uri = "ms-appdata:///local/favicons/" + fileName;
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(file, result.Bytes);
                wiki.IconUrl = uri;
                // Only auto-generated icons for this wiki are obsolete. Preserve user-imported files.
                foreach (var prior in (await folder.GetFilesAsync()).Where(f => f.Name != fileName && (f.Name == wiki.Id.ToString() || f.Name.StartsWith(wiki.Id + "-", StringComparison.Ordinal))))
                {
                    try { await prior.DeleteAsync(); } catch (Exception) { }
                }
                await RecordAsync(wiki, "Cached " + result.Source.Host + result.Source.AbsolutePath + "\n" + fileName + "\n" + result.Bytes.Length + " bytes (" + result.Format + ")");
                return uri;
            }
            catch (Exception ex) { await RecordAsync(wiki, "Cache failed: " + ex.GetType().Name); return null; }
        }
        private static async Task RecordAsync(WikiInstance wiki, string message)
        {
            Debug.WriteLine("[Favicon] " + wiki.Name + ": " + message);
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("favicon-" + wiki.Id + ".log", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, DateTime.UtcNow.ToString("O") + "\n" + message);
            }
            catch (Exception) { }
        }
    }
}
