using System;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using Windows.Storage;

namespace WikiViewer.Shared.Uwp.Services
{
    public class UwpStorageProvider : IStorageProvider
    {
        private readonly StorageFolder _folder = ApplicationData.Current.LocalFolder;

        public async Task<string> ReadTextAsync(string fileName)
        {
            var item = await _folder.TryGetItemAsync(fileName);
            if (item is StorageFile file)
            {
                return await FileIO.ReadTextAsync(file);
            }
            return null;
        }

        public async Task WriteTextAsync(string fileName, string content)
        {
            var file = await _folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, content);
        }
    }
}