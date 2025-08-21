using WikiViewer.Core.Interfaces;
using Windows.Storage;

namespace WikiViewer.Shared.Uwp.Services
{
    public class UwpSettingsProvider : ISettingsProvider
    {
        private readonly ApplicationDataContainer _localSettings = ApplicationData
            .Current
            .LocalSettings;

        public T GetValue<T>(string key, T defaultValue)
        {
            if (_localSettings.Values.TryGetValue(key, out object value))
            {
                return (T)value;
            }
            return defaultValue;
        }

        public void SetValue<T>(string key, T value)
        {
            _localSettings.Values[key] = value;
        }
    }
}
