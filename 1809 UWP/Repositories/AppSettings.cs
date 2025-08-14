using System;
using Windows.Storage;

namespace _1809_UWP
{
    public static class AppSettings
    {
        private const string CachingEnabledKey = "IsCachingEnabled";
        private const string DisclaimerShownKey = "HasShownDisclaimer";
        private const string MaxConcurrentDownloadsKey = "MaxConcurrentDownloads";
        private const string MediaWikiUrlKey = "MediaWikiUrl";
        private const string ScriptPathKey = "MediaWiki_ScriptPath";
        private const string ArticlePathKey = "MediaWiki_ArticlePath";
        private const string DefaultMediaWikiUrl = "https://en.wikipedia.org/";
        private const string DefaultScriptPath = "w/";
        private const string DefaultArticlePath = "wiki/";
        private const string DefaultMainPageName = "Main Page";

        private static readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        public static bool IsCachingEnabled
        {
            get => _localSettings.Values[CachingEnabledKey] as bool? ?? true;
            set => _localSettings.Values[CachingEnabledKey] = value;
        }

        public static bool HasShownDisclaimer
        {
            get => _localSettings.Values[DisclaimerShownKey] as bool? ?? false;
            set => _localSettings.Values[DisclaimerShownKey] = value;
        }

        public static int MaxConcurrentDownloads
        {
            get => _localSettings.Values[MaxConcurrentDownloadsKey] as int? ?? Environment.ProcessorCount;
            set => _localSettings.Values[MaxConcurrentDownloadsKey] = value;
        }

        public static string BaseUrl
        {
            get
            {
                if (_localSettings.Values.ContainsKey(MediaWikiUrlKey))
                {
                    string url = _localSettings.Values[MediaWikiUrlKey] as string;
                    return url.EndsWith("/") ? url : url + "/";
                }
                return DefaultMediaWikiUrl;
            }
            set
            {
                if (Uri.TryCreate(value, UriKind.Absolute, out var uriResult) &&
                    (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    _localSettings.Values[MediaWikiUrlKey] = value;
                }
            }
        }

        public static string ScriptPath
        {
            get
            {
                if (_localSettings.Values.ContainsKey(ScriptPathKey))
                {
                    return _localSettings.Values[ScriptPathKey] as string;
                }
                return DefaultScriptPath;
            }
            set => _localSettings.Values[ScriptPathKey] = value;
        }

        public static string ArticlePath
        {
            get
            {
                if (_localSettings.Values.ContainsKey(ArticlePathKey))
                {
                    return _localSettings.Values[ArticlePathKey] as string;
                }
                return DefaultArticlePath;
            }
            set => _localSettings.Values[ArticlePathKey] = value;
        }

        public static string Host => new Uri(BaseUrl).Host;

        public static string ApiEndpoint => $"{BaseUrl}{ScriptPath}api.php";
        public static string IndexEndpoint => $"{BaseUrl}{ScriptPath}index.php";
        public static string MainPageName => DefaultMainPageName;

        public static string GetWikiPageUrl(string pageTitle) => $"{BaseUrl}{ArticlePath}{Uri.EscapeDataString(pageTitle)}";
        public static string GetEditPageUrl(string pageTitle) => $"{IndexEndpoint}?title={Uri.EscapeDataString(pageTitle)}&action=edit";
    }
}