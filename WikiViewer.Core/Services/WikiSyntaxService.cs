using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class WikiSyntaxService
    {
        private static readonly WikiResourceCache FallbackCache = new WikiResourceCache(null);
        private static string Url(string endpoint) => endpoint + "?action=query&meta=siteinfo&siprop=general%7Cnamespaces%7Cnamespacealiases%7Cmagicwords&format=json";
        public static async Task<WikiSyntaxConfiguration> GetCachedAsync(string endpoint)
        {
            try { string json = await (WikiResourceCache.Shared ?? FallbackCache).TryReadAsync(Url(endpoint)); return json != null ? Parse(json) : null; }
            catch (Exception) { return null; }
        }

        public static async Task<WikiSyntaxConfiguration> GetAsync(string endpoint, Func<string, Task<string>> fetch)
        {
            try { string url = Url(endpoint); return Parse(await (WikiResourceCache.Shared ?? FallbackCache).GetAsync(url, async () => { string json = await fetch(url); Parse(json); return json; })); }
            catch (Exception) { return new WikiSyntaxConfiguration { UsesFallbackLinkTrail = true }; }
        }

        public static WikiSyntaxConfiguration Parse(string json)
        {
            var query = JObject.Parse(json)["query"];
            if (query == null) throw new InvalidOperationException("The wiki did not return site information.");
            var result = new WikiSyntaxConfiguration { Language = (string)query["general"]?["lang"] };
            string trail = (string)query["general"]?["linktrail"] ?? "";
            // Convert only a plain character-class PCRE wrapper. Other PCRE syntax is not assumed compatible with .NET.
            var match = Regex.Match(trail, @"^/\^\((\[[^\]\\]+\]\+)\)\(\.\*\)\$/[A-Za-z]*$");
            if (match.Success) result.LinkTrailPattern = match.Groups[1].Value;
            else result.UsesFallbackLinkTrail = true;
            var namespaces = query["namespaces"] as JObject;
            foreach (var item in (namespaces?.Properties().Select(p => p.Value) ?? Enumerable.Empty<JToken>()).Concat(query["namespacealiases"] ?? new JArray()))
            {
                int id = (int?)item["id"] ?? int.MinValue;
                foreach (string name in new[] { (string)item["*"], (string)item["name"], (string)item["canonical"] }.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    if (id == 6 && !result.FileNamespaces.Contains(name)) result.FileNamespaces.Add(name);
                    if (id == 14 && !result.CategoryNamespaces.Contains(name)) result.CategoryNamespaces.Add(name);
                }
            }
            var imageWords = new Dictionary<string, string> { { "img_thumbnail", "thumb" }, { "img_right", "right" }, { "img_left", "left" },
                { "img_center", "center" }, { "img_none", "none" }, { "img_framed", "frame" }, { "img_frameless", "frameless" }, { "img_border", "border" } };
            foreach (var word in query["magicwords"] ?? new JArray())
            {
                string name = (string)word["name"], canonical;
                foreach (string alias in (word["aliases"] ?? new JArray()).Select(a => (string)a).Where(a => !string.IsNullOrWhiteSpace(a) && !a.Contains("$")))
                {
                    if (name == "redirect" && !result.RedirectAliases.Contains(alias)) result.RedirectAliases.Add(alias);
                    if (name != null && imageWords.TryGetValue(name, out canonical)) result.ImageOptions[alias] = canonical;
                }
            }
            return result;
        }
    }
}
