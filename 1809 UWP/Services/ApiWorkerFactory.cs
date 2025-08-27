using System.Diagnostics;
using System.Threading.Tasks;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;

namespace _1809_UWP.Services
{
    public class ApiWorkerFactory : IApiWorkerFactory
    {
        public IApiWorker CreateApiWorker(WikiInstance wiki)
        {
            var methodToUse = wiki.PreferredConnectionMethod;

            if (methodToUse == ConnectionMethod.Auto)
            {
                if (wiki.ResolvedConnectionMethod.HasValue)
                {
                    methodToUse = wiki.ResolvedConnectionMethod.Value;
                }
                else
                {
                    methodToUse = ConnectionMethod.WebView;
                }
            }

            switch (methodToUse)
            {
                case ConnectionMethod.HttpClient:
                    return new HttpClientApiWorker();
                case ConnectionMethod.HttpClientProxy:
                    return new ProxyHttpClientApiWorker();
                case ConnectionMethod.WebView:
                default:
                    return new PooledWebView2ProxyWorker(wiki);
            }
        }

        public async Task<IApiWorker> CreateApiWorkerAsync(WikiInstance wiki)
        {
            if (wiki.PreferredConnectionMethod == ConnectionMethod.Auto && !wiki.ResolvedConnectionMethod.HasValue)
            {
                Debug.WriteLine($"[ApiWorkerFactory] Auto-detecting best connection method for {wiki.Host}...");
                var testResult = await ConnectionTesterService.FindWorkingMethodAndPathsAsync(wiki.BaseUrl, this);
                if (testResult.IsSuccess)
                {
                    Debug.WriteLine($"[ApiWorkerFactory] Auto-detection successful: {testResult.Method}");
                    wiki.ResolvedConnectionMethod = testResult.Method;
                }
                else
                {
                    Debug.WriteLine($"[ApiWorkerFactory] Auto-detection failed. Falling back to WebView2.");
                    wiki.ResolvedConnectionMethod = ConnectionMethod.WebView;
                }
            }
            return CreateApiWorker(wiki);
        }
    }
}
