using System;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class ArticleContentResponse
    {
        public static string BuildUrl(string endpoint, string title) => endpoint +
            "?action=parse&page=" + Uri.EscapeDataString(title) +
            "&prop=text%7Cwikitext%7Crevid&format=json&disableeditsection=true";

        public static ArticleContent Parse(string json, string requestedTitle)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("The wiki returned an empty article response.");
            var response = JObject.Parse(json);
            if (response["error"] != null)
                throw new InvalidOperationException((string)response["error"]["info"] ?? "The wiki could not parse this article.");
            var parsed = response["parse"];
            var html = ReadText(parsed?["text"]);
            if (html == null) throw new InvalidOperationException("The wiki response did not contain article HTML.");
            return new ArticleContent
            {
                Title = (string)parsed["title"] ?? requestedTitle,
                RevisionId = (long?)parsed["revid"] ?? 0,
                HtmlContent = html,
                RawWikitext = ReadText(parsed["wikitext"])
            };
        }

        private static string ReadText(JToken token) => token is JObject ? (string)token["*"] : (string)token;
    }
}
