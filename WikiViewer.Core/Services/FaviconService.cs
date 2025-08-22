using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using Windows.Storage;

namespace WikiViewer.Core.Services
{
    public static class FaviconService
    {
        public static async Task<string> FetchAndCacheFaviconUrlAsync(WikiInstance wiki, IApiWorker worker)
        {
            if (wiki == null || worker == null) return null;

            if (!string.IsNullOrEmpty(wiki.IconUrl) && wiki.IconUrl.StartsWith("ms-appdata:///"))
            {
                return wiki.IconUrl;
            }

            var faviconsFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("favicons", CreationCollisionOption.OpenIfExists);
            var targetFile = await faviconsFolder.TryGetItemAsync(wiki.Id.ToString());
            if (targetFile != null)
            {
                return $"ms-appdata:///local/favicons/{wiki.Id.ToString()}";
            }

            string iconUrl = null;
            byte[] iconBytes = null;

            try
            {
                var conventionalUrl = new Uri(new Uri(wiki.BaseUrl), "/favicon.ico").ToString();
                iconBytes = await worker.GetRawBytesFromUrlAsync(conventionalUrl);
                if (iconBytes?.Length > 0)
                {
                    iconUrl = conventionalUrl;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FaviconService] /favicon.ico not found for {wiki.Name}: {ex.Message}");
            }

            if (iconBytes == null)
            {
                try
                {
                    string html = await worker.GetRawHtmlFromUrlAsync(wiki.BaseUrl);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var linkNode = doc.DocumentNode.SelectSingleNode("//link[@rel='icon' or @rel='shortcut icon']");
                    if (linkNode != null)
                    {
                        string href = linkNode.GetAttributeValue("href", null);
                        if (!string.IsNullOrEmpty(href))
                        {
                            var fullUri = new Uri(new Uri(wiki.BaseUrl), href);
                            iconBytes = await worker.GetRawBytesFromUrlAsync(fullUri.ToString());
                            iconUrl = fullUri.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FaviconService] Could not parse main page for icon for {wiki.Name}: {ex.Message}");
                }
            }

            if (iconBytes?.Length > 0)
            {
                try
                {
                    var file = await faviconsFolder.CreateFileAsync(wiki.Id.ToString(), CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBytesAsync(file, iconBytes);
                    wiki.IconUrl = $"ms-appdata:///local/favicons/{wiki.Id.ToString()}";
                    await WikiManager.SaveAsync();
                    return wiki.IconUrl;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FaviconService] Failed to save icon to disk for {wiki.Name}: {ex.Message}");
                }
            }

            return null;
        }
    }
}