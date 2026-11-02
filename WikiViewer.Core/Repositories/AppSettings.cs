using System;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;


namespace WikiViewer.Core
{
    public enum EditorThemeMode { Default, Light, Dark }
    public enum AppAppearance { Automatic, Windows10Fluent, Windows11 }

    public enum ConcurrencyLevel
    {
        Low,
        Medium,
        High,
        Double,
        Extreme,
        Unlimited,
    }

    public static class AppSettings
    {
        public static AppAppearance Appearance
        {
            get => (AppAppearance)SettingsProvider.GetValue("AppAppearance", (int)AppAppearance.Automatic);
            set => SettingsProvider.SetValue("AppAppearance", (int)value);
        }
        public static ISettingsProvider SettingsProvider { get; set; }

        private const string CachingEnabledKey = "IsCachingEnabled";
        private const string DisclaimerShownKey = "HasShownDisclaimer";
        private const string MaxConcurrentDownloadsKey = "MaxConcurrentDownloadsLevel";
        private const string ProxyDisclaimerKey = "HasAcceptedProxyDisclaimer";
        private const string EditorThemeKey = "EditorTheme";
        private const string IsCustomThemingEnabledKey = "IsCustomThemingEnabled";
        private const string ShowCssRefreshButtonKey = "ShowCssRefreshButton";
        private const string DefaultConnectionMethodKey = "DefaultConnectionMethodForNewWikis";
        private const string NativeReaderDefaultKey = "UseNativeReaderByDefault";

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

        public static bool HasAcceptedProxyDisclaimer
        {
            get => SettingsProvider.GetValue(ProxyDisclaimerKey, false);
            set => SettingsProvider.SetValue(ProxyDisclaimerKey, value);
        }

        public static ConcurrencyLevel DownloadConcurrencyLevel
        {
            get =>
                (ConcurrencyLevel)
                    SettingsProvider.GetValue(
                        MaxConcurrentDownloadsKey,
                        (int)ConcurrencyLevel.Medium
                    );
            set => SettingsProvider.SetValue(MaxConcurrentDownloadsKey, (int)value);
        }

        public static EditorThemeMode EditorTheme
        {
            get
            {
                var themeName = SettingsProvider.GetValue(
                    EditorThemeKey,
                    EditorThemeMode.Default.ToString()
                );
                return Enum.TryParse(themeName, out EditorThemeMode parsedTheme)
                    ? parsedTheme
                    : EditorThemeMode.Default;
            }
            set => SettingsProvider.SetValue(EditorThemeKey, value.ToString());
        }

        public static bool IsCustomThemingEnabled
        {
            get => SettingsProvider.GetValue(IsCustomThemingEnabledKey, true);
            set => SettingsProvider.SetValue(IsCustomThemingEnabledKey, value);
        }

        public static bool ShowCssRefreshButton
        {
            get => SettingsProvider.GetValue(ShowCssRefreshButtonKey, false);
            set => SettingsProvider.SetValue(ShowCssRefreshButtonKey, value);
        }

        public static bool UseNativeReaderByDefault
        {
            get => SettingsProvider.GetValue(NativeReaderDefaultKey, false);
            set => SettingsProvider.SetValue(NativeReaderDefaultKey, value);
        }

        public static int MaxConcurrentDownloads
        {
            get
            {
                switch (DownloadConcurrencyLevel)
                {
                    case ConcurrencyLevel.Low:
                        return 2;
                    case ConcurrencyLevel.Medium:
                        return Math.Max(2, Environment.ProcessorCount / 2);
                    case ConcurrencyLevel.High:
                        return Math.Max(2, Environment.ProcessorCount);
                    case ConcurrencyLevel.Double:
                        return Math.Max(2, Environment.ProcessorCount * 2);
                    case ConcurrencyLevel.Extreme:
                        return 256;
                    case ConcurrencyLevel.Unlimited:
                        return int.MaxValue;
                    default:
                        return Environment.ProcessorCount;
                }
            }
        }

        public static ConnectionMethod DefaultConnectionMethod
        {
            get =>
                (ConnectionMethod)
                    SettingsProvider.GetValue(
                        DefaultConnectionMethodKey,
                        (int)ConnectionMethod.Auto
                    );
            set => SettingsProvider.SetValue(DefaultConnectionMethodKey, (int)value);
        }

        public static string MainPageName => DefaultMainPageName;
    }
}
