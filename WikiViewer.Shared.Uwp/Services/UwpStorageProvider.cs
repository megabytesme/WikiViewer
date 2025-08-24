using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using Windows.Storage;

namespace WikiViewer.Shared.Uwp.Services
{
    public class UwpStorageProvider : IStorageProvider
    {
        private readonly StorageFolder _folder = ApplicationData.Current.LocalFolder;

        public async Task<string> ReadTextAsync(string relativePath)
        {
            try
            {
                var file = await _folder.GetFileAsync(relativePath);
                return await FileIO.ReadTextAsync(file);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        public async Task WriteTextAsync(string relativePath, string content)
        {
            var directory = Path.GetDirectoryName(relativePath);
            var fileName = Path.GetFileName(relativePath);
            var targetFolder = _folder;

            if (!string.IsNullOrEmpty(directory))
            {
                targetFolder = await _folder.CreateFolderAsync(
                    directory,
                    CreationCollisionOption.OpenIfExists
                );
            }

            var file = await targetFolder.CreateFileAsync(
                fileName,
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(file, content);
        }

        public async Task WriteBytesAsync(string relativePath, byte[] content)
        {
            var directory = Path.GetDirectoryName(relativePath);
            var fileName = Path.GetFileName(relativePath);
            var targetFolder = _folder;

            if (!string.IsNullOrEmpty(directory))
            {
                targetFolder = await _folder.CreateFolderAsync(
                    directory,
                    CreationCollisionOption.OpenIfExists
                );
            }

            var file = await targetFolder.CreateFileAsync(
                fileName,
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteBytesAsync(file, content);
        }

        public async Task<bool> FileExistsAsync(string relativePath)
        {
            return await _folder.TryGetItemAsync(relativePath) != null;
        }

        public async Task DeleteFileAsync(string relativePath)
        {
            var item = await _folder.TryGetItemAsync(relativePath);
            if (item is StorageFile file)
            {
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
        }

        public async Task<ulong> GetFolderSizeAsync(string relativePath)
        {
            try
            {
                var targetFolder = await _folder.GetFolderAsync(relativePath);
                var properties = await targetFolder.GetBasicPropertiesAsync();
                return properties.Size;
            }
            catch (FileNotFoundException)
            {
                return 0;
            }
        }

        public async Task ClearFolderAsync(string relativePath)
        {
            try
            {
                var targetFolder = await _folder.GetFolderAsync(relativePath);
                await targetFolder.DeleteAsync(StorageDeleteOption.PermanentDelete);
                await _folder.CreateFolderAsync(relativePath);
            }
            catch (FileNotFoundException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing folder {relativePath}: {ex.Message}");
            }
        }
    }
}
