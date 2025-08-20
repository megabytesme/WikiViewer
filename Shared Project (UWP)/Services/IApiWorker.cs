using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IApiWorker : IDisposable
{
    Task InitializeAsync(string baseUrl = null);
    Task<string> GetJsonFromApiAsync(string url);
    Task<string> PostAndGetJsonFromApiAsync(string url, Dictionary<string, string> postData);
    Task<string> GetRawHtmlFromUrlAsync(string url);
    Task<byte[]> GetRawBytesFromUrlAsync(string url);
    Task CopyApiCookiesFromAsync(IApiWorker source);
}