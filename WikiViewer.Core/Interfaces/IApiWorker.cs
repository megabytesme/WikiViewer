using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WikiViewer.Core.Interfaces
{
    public interface IApiWorker : IDisposable
    {
        bool IsInitialized { get; }

        Task InitializeAsync(string baseUrl = null);
        Task<string> GetJsonFromApiAsync(string url);
        Task<string> PostAndGetJsonFromApiAsync(string url, Dictionary<string, string> postData);
        Task<string> GetRawHtmlFromUrlAsync(string url);
        Task<byte[]> GetRawBytesFromUrlAsync(string url);
        Task CopyApiCookiesFromAsync(IApiWorker source);
    }
}
