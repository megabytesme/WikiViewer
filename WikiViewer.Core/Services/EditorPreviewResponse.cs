using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class EditorPreviewResponse
    {
        public static Dictionary<string, string> BuildRequest(string source, string title) => new Dictionary<string, string>
        {
            { "action", "parse" }, { "format", "json" }, { "text", source ?? "" }, { "title", title ?? "" },
            { "contentmodel", "wikitext" }, { "prop", "text|modules|jsconfigvars|headhtml" },
            { "pst", "true" }, { "disablelimitreport", "true" }, { "disableeditsection", "true" }, { "preview", "true" }
        };

        public static ArticlePreview Parse(string json, string source, string title)
        {
            var content = ArticleContentResponse.Parse(json, title);
            // This is an unsaved draft, not a persisted revision or a server-transformed source.
            content.RawWikitext = source ?? "";
            content.RevisionId = 0;
            var parsed = JObject.Parse(json)["parse"];
            var head = parsed["headhtml"];
            var result = new ArticlePreview { Content = content, HeadHtml = head is JObject ? (string)head["*"] : (string)head };
            result.StyleModules.AddRange((parsed["modules"] ?? new JArray()).Concat(parsed["modulestyles"] ?? new JArray())
                .Select(token => (string)token).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));
            return result;
        }
    }
}
