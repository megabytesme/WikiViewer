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

        public async Task<ulong> GetFolderSizeAsync(string folderName)
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                var targetFolder = await localFolder.GetFolderAsync(folderName);

                return await GetFolderSizeRecursiveAsync(targetFolder);
            }
            catch (System.IO.FileNotFoundException)
            {
                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[StorageProvider] Failed to get size for folder '{folderName}': {ex.Message}"
                );
                return 0;
            }
        }

        private async Task<ulong> GetFolderSizeRecursiveAsync(IStorageFolder folder)
        {
            ulong totalSize = 0;

            var files = await folder.GetFilesAsync();
            foreach (var file in files)
            {
                var properties = await file.GetBasicPropertiesAsync();
                totalSize += properties.Size;
            }

            var subFolders = await folder.GetFoldersAsync();
            foreach (var subFolder in subFolders)
            {
                totalSize += await GetFolderSizeRecursiveAsync(subFolder);
            }

            return totalSize;
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

        public async Task RecreateFolderAsync(string folderName)
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.GetFolderAsync(folderName);

                await folder.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch (FileNotFoundException) { }

            await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                folderName,
                CreationCollisionOption.OpenIfExists
            );
        }
    }
}
