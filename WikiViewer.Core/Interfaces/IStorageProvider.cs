using System.Threading.Tasks;

namespace WikiViewer.Core.Interfaces
{
    public interface IStorageProvider
    {
        Task<string> ReadTextAsync(string relativePath);
        Task WriteTextAsync(string relativePath, string content);
        Task WriteBytesAsync(string relativePath, byte[] content);
        Task<bool> FileExistsAsync(string relativePath);
        Task DeleteFileAsync(string relativePath);
        Task RecreateFolderAsync(string folderName);
        Task<ulong> GetFolderSizeAsync(string relativePath);
        Task ClearFolderAsync(string relativePath);
    }
}
