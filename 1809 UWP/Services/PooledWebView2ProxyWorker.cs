using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace _1809_UWP.Services
{
    public class PooledWebView2ProxyWorker : IApiWorker
    {
        public WikiInstance WikiContext { get; set; }

        private readonly WikiInstance _wiki;

        public PooledWebView2ProxyWorker(WikiInstance wiki)
        {
            _wiki = wiki;
            WikiContext = wiki;
        }

        public Task InitializeAsync(string baseUrl = null) => Task.CompletedTask;

        public bool IsInitialized => true;

        public Task<string> GetJsonFromApiAsync(string url)
        {
            return WebView2WorkerPool.UseWorkerAsync(
                _wiki,
                worker => worker.GetJsonFromApiAsync(url)
            );
        }

        public Task<string> PostAndGetJsonFromApiAsync(
            string url,
            Dictionary<string, string> postData
        )
        {
            return WebView2WorkerPool.UseWorkerAsync(
                _wiki,
                worker => worker.PostAndGetJsonFromApiAsync(url, postData)
            );
        }

        public Task<string> GetRawHtmlFromUrlAsync(string url)
        {
            return WebView2WorkerPool.UseWorkerAsync(
                _wiki,
                worker => worker.GetRawHtmlFromUrlAsync(url)
            );
        }

        public Task<byte[]> GetRawBytesFromUrlAsync(string url)
        {
            return WebView2WorkerPool.UseWorkerAsync(
                _wiki,
                worker => worker.GetRawBytesFromUrlAsync(url)
            );
        }

        public Task CopyApiCookiesFromAsync(IApiWorker source) => Task.CompletedTask;

        public void Dispose() { }
    }
}
