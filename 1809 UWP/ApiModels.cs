using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace _1809_UWP
{
    public class NeedsUserVerificationException : Exception
    {
        public string Url { get; }

        public NeedsUserVerificationException(string message, string url)
            : base(message)
        {
            Url = url;
        }
    }

    public class RandomQueryResponse
    {
        public QueryResult query { get; set; }
    }

    public class QueryResult
    {
        public RandomPage[] random { get; set; }
    }

    public class RandomPage
    {
        public string title { get; set; }
    }

    public class ApiParseResponse
    {
        public ParseResult parse { get; set; }
    }

    public class ParseResult
    {
        public string title { get; set; }
        public TextContent text { get; set; }
    }

    public class TextContent
    {
        [JsonPropertyName("*")]
        public string Content { get; set; }
    }

    public class ImageQueryResponse
    {
        public ImageQueryPages query { get; set; }
    }

    public class ImageQueryPages
    {
        public Dictionary<string, ImagePage> pages { get; set; }
    }

    public class ImagePage
    {
        public string title { get; set; }
        public ImageInfo[] imageinfo { get; set; }
    }

    public class ImageInfo
    {
        public string url { get; set; }
    }

    public class TimestampQueryResponse
    {
        public TimestampQueryPages query { get; set; }
    }

    public class TimestampQueryPages
    {
        public Dictionary<string, TimestampPage> pages { get; set; }
    }

    public class TimestampPage
    {
        [JsonPropertyName("revisions")]
        public List<RevisionInfo> Revisions { get; set; }
    }

    public class RevisionInfo
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    public class LoginApiTokenResponse
    {
        public QueryLoginToken query { get; set; }
    }

    public class QueryLoginToken
    {
        public TokensLogin tokens { get; set; }
    }

    public class TokensLogin
    {
        public string logintoken { get; set; }
    }

    public class LoginResultResponse
    {
        public LoginData login { get; set; }
    }

    public class LoginData
    {
        public string result { get; set; }
        public string lgusername { get; set; }
    }

    public class CsrfTokenResponse
    {
        public QueryCsrf query { get; set; }
    }

    public class QueryCsrf
    {
        public TokensCsrf tokens { get; set; }
    }

    public class TokensCsrf
    {
        public string csrftoken { get; set; }
    }
}
