using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

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
        [JsonProperty("*")]
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
        [JsonProperty("revisions")]
        public List<RevisionInfo> Revisions { get; set; }
    }

    public class RevisionInfo
    {
        [JsonProperty("timestamp")]
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

    public class ClientLoginResponse
    {
        public ClientLoginResult clientlogin { get; set; }
    }

    public class ClientLoginResult
    {
        public string status { get; set; }
        public string username { get; set; }
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

    public class WatchlistQueryResponse
    {
        [JsonProperty("query")]
        public WatchlistQuery query { get; set; }
    }

    public class WatchlistQuery
    {
        [JsonProperty("watchlistraw")]
        public List<WatchlistItem> watchlistraw { get; set; }
    }

    public class WatchlistResult
    {
        public List<WatchlistItem> watchlistraw { get; set; }
    }

    public class WatchlistItem
    {
        [JsonProperty("ns")]
        public int Ns { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }
    }

    public class WatchlistApiResponse
    {
        [JsonProperty("watchlistraw")]
        public List<WatchlistItem> WatchlistRaw { get; set; }
    }

    public class WatchActionResponse
    {
        [JsonProperty("watch")]
        public List<WatchResult> Watch { get; set; }

        [JsonProperty("unwatch")]
        public List<WatchResult> Unwatch { get; set; }
    }

    public class WatchResult
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("watched")]
        public string Watched { get; set; }

        [JsonProperty("unwatched")]
        public string Unwatched { get; set; }
    }

    public class WatchTokenResponse
    {
        [JsonProperty("query")]
        public WatchQueryResponse Query { get; set; }
    }

    public class WatchQueryResponse
    {
        [JsonProperty("tokens")]
        public WatchTokens Tokens { get; set; }
    }

    public class WatchTokens
    {
        [JsonProperty("watchtoken")]
        public string WatchToken { get; set; }
    }

    public class FavouriteItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _imageUrl;
        public string ImageUrl
        {
            get => _imageUrl;
            set
            {
                _imageUrl = value;
                OnPropertyChanged();
            }
        }

        public string DisplayTitle { get; set; }

        public string ArticlePageTitle { get; set; }

        public string TalkPageTitle { get; set; }

        public bool IsArticleAvailable => !string.IsNullOrEmpty(ArticlePageTitle);

        public bool IsTalkAvailable => !string.IsNullOrEmpty(TalkPageTitle);

        public FavouriteItem(string baseTitle)
        {
            DisplayTitle = baseTitle;
        }
    }

    public class ArticleCachedEventArgs : EventArgs
    {
        public string PageTitle { get; }

        public ArticleCachedEventArgs(string pageTitle)
        {
            PageTitle = pageTitle;
        }
    }
}
