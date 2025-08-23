using System.Threading.Tasks;

namespace WikiViewer.Core.Interfaces
{
    public interface IStorageProvider
    {
        Task<string> ReadTextAsync(string fileName);
        Task WriteTextAsync(string fileName, string content);
    }
}