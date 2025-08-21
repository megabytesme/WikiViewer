namespace WikiViewer.Core.Interfaces
{
    public interface ISettingsProvider
    {
        T GetValue<T>(string key, T defaultValue);
        void SetValue<T>(string key, T value);
    }
}
