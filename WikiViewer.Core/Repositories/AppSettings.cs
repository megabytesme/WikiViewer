using System;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core
{
    public static class AppSettings
    {
        public static ISettingsProvider SettingsProvider { get; set; }

        private const string CachingEnabledKey = "IsCachingEnabled";
        private const string DisclaimerShownKey = "HasShownDisclaimer";
        private const string MaxConcurrentDownloadsKey = "MaxConcurrentDownloads";
        private const string MediaWikiUrlKey = "MediaWikiUrl";
        private const string ScriptPathKey = "MediaWiki_ScriptPath";
        private const string ArticlePathKey = "MediaWiki_ArticlePath";
        private const string ConnectionMethodKey = "ConnectionMethod";
        private const string DefaultMediaWikiUrl = "https://en.wikipedia.org/";
        private const string DefaultScriptPath = "w/";
        private const string DefaultArticlePath = "wiki/";
        private const string DefaultMainPageName = "Main Page";

        public static ConnectionMethod ConnectionBackend
        {
            get =>
                (ConnectionMethod)
                    SettingsProvider.GetValue(ConnectionMethodKey, (int)ConnectionMethod.WebView);
            set => SettingsProvider.SetValue(ConnectionMethodKey, (int)value);
        }
        public static bool IsCachingEnabled
        {
            get => SettingsProvider.GetValue(CachingEnabledKey, true);
            set => SettingsProvider.SetValue(CachingEnabledKey, value);
        }
        public static bool HasShownDisclaimer
        {
            get => SettingsProvider.GetValue(DisclaimerShownKey, false);
            set => SettingsProvider.SetValue(DisclaimerShownKey, value);
        }
        public static int MaxConcurrentDownloads
        {
            get => SettingsProvider.GetValue(MaxConcurrentDownloadsKey, Environment.ProcessorCount);
            set => SettingsProvider.SetValue(MaxConcurrentDownloadsKey, value);
        }
        public static string BaseUrl
        {
            get
            {
                string url = SettingsProvider.GetValue(MediaWikiUrlKey, DefaultMediaWikiUrl);
                return url.EndsWith("/") ? url : url + "/";
            }
            set
            {
                if (
                    Uri.TryCreate(value, UriKind.Absolute, out var uriResult)
                    && (uriResult.Scheme == "http" || uriResult.Scheme == "https")
                )
                {
                    SettingsProvider.SetValue(MediaWikiUrlKey, value);
                }
            }
        }
        public static string ScriptPath
        {
            get => SettingsProvider.GetValue(ScriptPathKey, DefaultScriptPath);
            set => SettingsProvider.SetValue(ScriptPathKey, value);
        }
        public static string ArticlePath
        {
            get => SettingsProvider.GetValue(ArticlePathKey, DefaultArticlePath);
            set => SettingsProvider.SetValue(ArticlePathKey, value);
        }
        public static string Host => new Uri(BaseUrl).Host;
        public static string ApiEndpoint => $"{BaseUrl}{ScriptPath}api.php";
        public static string IndexEndpoint => $"{BaseUrl}{ScriptPath}index.php";
        public static string MainPageName => DefaultMainPageName;

        public static string GetWikiPageUrl(string pageTitle) =>
            $"{BaseUrl}{ArticlePath}{Uri.EscapeDataString(pageTitle)}";

        public static string GetEditPageUrl(string pageTitle) =>
            $"{IndexEndpoint}?title={Uri.EscapeDataString(pageTitle)}&action=edit";

        public static string GetVirtualHostName() => $"local-content.{Host}";
    }
}
