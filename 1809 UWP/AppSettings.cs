using Windows.Storage;

namespace _1809_UWP
{
    public static class AppSettings
    {
        private const string CachingEnabledKey = "IsCachingEnabled";
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
    }
}