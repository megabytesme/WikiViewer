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
        public ConnectionMethod PreferredConnectionMethod { get; set; } = ConnectionMethod.WebView;

        [JsonIgnore]
        public string ApiEndpoint => $"{BaseUrl.TrimEnd('/')}/{ScriptPath.Trim('/')}/api.php";

        [JsonIgnore]
        public string IndexEndpoint => $"{BaseUrl.TrimEnd('/')}/{ScriptPath.Trim('/')}/index.php";

        [JsonIgnore]
        public string Host => new Uri(BaseUrl).Host;

        public string GetWikiPageUrl(string pageTitle) =>
            $"{BaseUrl.TrimEnd('/')}/{ArticlePath.Trim('/')}/{Uri.EscapeDataString(pageTitle)}";
    }
}
