using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Interfaces
{
    public interface IApiWorker : IDisposable
    {
        bool IsInitialized { get; }
        WikiInstance WikiContext { get; set; }
        Task InitializeAsync(string baseUrl);
        Task<string> GetJsonFromApiAsync(string url);
        Task<string> PostAndGetJsonFromApiAsync(string url, Dictionary<string, string> postData);
        Task<string> GetRawHtmlFromUrlAsync(string url);
        Task<byte[]> GetRawBytesFromUrlAsync(string url);
        Task CopyApiCookiesFromAsync(IApiWorker source);
    }
}
