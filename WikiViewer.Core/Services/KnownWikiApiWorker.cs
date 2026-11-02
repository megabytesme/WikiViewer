using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Selects a connection using an existing API endpoint, without rediscovering site paths.</summary>
    public static class KnownWikiApiWorker
    {
        public static async Task<IApiWorker> CreateAsync(WikiInstance wiki, IApiWorkerFactory factory)
        {
            if (wiki.PreferredConnectionMethod != ConnectionMethod.Auto || wiki.ResolvedConnectionMethod.HasValue)
            {
                var selected = factory.CreateApiWorker(wiki);
                try { await selected.InitializeAsync(wiki.BaseUrl); return selected; }
                catch { selected.Dispose(); throw; }
            }
            foreach (var method in new[] { ConnectionMethod.HttpClient, ConnectionMethod.WebView, ConnectionMethod.HttpClientProxy })
            {
                if (method == ConnectionMethod.HttpClientProxy && ResolverConfigurationService.Current?.Load() == null) continue;
                var candidate = new WikiInstance { Id = wiki.Id, BaseUrl = wiki.BaseUrl, ScriptPath = wiki.ScriptPath,
                    ArticlePath = wiki.ArticlePath, PreferredConnectionMethod = method };
                var worker = factory.CreateApiWorker(candidate);
                bool accepted = false;
                try
                {
                    string json = await NetworkOperationTimeout.RunAsync(async () =>
                    {
                        await worker.InitializeAsync(wiki.BaseUrl);
                        return await worker.GetJsonFromApiAsync(wiki.ApiEndpoint + "?action=query&meta=siteinfo&siprop=general&format=json");
                    }, TimeSpan.FromSeconds(method == ConnectionMethod.HttpClient ? 10 : 30), "The wiki API connection timed out.");
                    if (string.IsNullOrWhiteSpace(json) || !(JObject.Parse(json)["query"]?["general"] is JObject)) continue;
                    wiki.ResolvedConnectionMethod = method;
                    worker.WikiContext = wiki;
                    accepted = true;
                    return worker;
                }
                catch (Exception) { }
                finally { if (!accepted) worker.Dispose(); }
            }
            throw new InvalidOperationException("The wiki API could not be reached using the available connections.");
        }
    }
}
