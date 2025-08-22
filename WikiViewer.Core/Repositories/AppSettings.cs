using System;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core
{
    public static class AppSettings
    {
        public static ISettingsProvider SettingsProvider { get; set; }

        private const string CachingEnabledKey = "IsCachingEnabled";
        private const string DisclaimerShownKey = "HasShownDisclaimer";
        private const string MaxConcurrentDownloadsKey = "MaxConcurrentDownloads";

        private const string DefaultMainPageName = "Main Page";

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

        public static string MainPageName => DefaultMainPageName;
    }
}