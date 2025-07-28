using Windows.Storage;

namespace _1809_UWP
{
    public static class AppSettings
    {
        private const string CachingEnabledKey = "IsCachingEnabled";
        private const string DisclaimerShownKey = "HasShownDisclaimer";

        private static ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        public static bool IsCachingEnabled
        {
            get
            {
                object value = _localSettings.Values[CachingEnabledKey];
                return (value == null) ? true : (bool)value;
            }
            set
            {
                _localSettings.Values[CachingEnabledKey] = value;
            }
        }

        public static bool HasShownDisclaimer
        {
            get
            {
                object value = _localSettings.Values[DisclaimerShownKey];
                return (value == null) ? false : (bool)value;
            }
            set
            {
                _localSettings.Values[DisclaimerShownKey] = value;
            }
        }
    }
}