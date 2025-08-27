using System;
using Newtonsoft.Json;
using WikiViewer.Core.Enums;

namespace WikiViewer.Core.Models
{
    public class WikiInstance
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public string ScriptPath { get; set; } = "w/";
        public string ArticlePath { get; set; } = "wiki/";
        public ConnectionMethod PreferredConnectionMethod { get; set; } = ConnectionMethod.Auto;
        public string IconUrl { get; set; }
        public bool IsIconUserSet { get; set; } = false;

        [JsonIgnore]
        public ConnectionMethod? ResolvedConnectionMethod { get; set; }

        [JsonIgnore]
        public string ApiEndpoint
        {
            get
            {
                var baseUri = BaseUrl.TrimEnd('/');
                var script = ScriptPath?.Trim('/');
                return string.IsNullOrEmpty(script)
                    ? $"{baseUri}/api.php"
                    : $"{baseUri}/{script}/api.php";
            }
        }

        [JsonIgnore]
        public string IndexEndpoint
        {
            get
            {
                var baseUri = BaseUrl.TrimEnd('/');
                var script = ScriptPath?.Trim('/');
                return string.IsNullOrEmpty(script)
                    ? $"{baseUri}/index.php"
                    : $"{baseUri}/{script}/index.php";
            }
        }

        [JsonIgnore]
        public string Host
        {
            get
            {
                if (
                    string.IsNullOrEmpty(BaseUrl)
                    || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
                )
                {
                    return null;
                }
                return uri.Host;
            }
        }

        public string GetWikiPageUrl(string pageTitle) =>
            $"{BaseUrl.TrimEnd('/')}/{ArticlePath.Trim('/')}/{Uri.EscapeDataString(pageTitle)}";
    }
}
