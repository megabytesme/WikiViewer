using System.Threading.Tasks;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;

namespace _1507_UWP.Services
{
    public class ApiWorkerFactory : IApiWorkerFactory
    {
        public IApiWorker CreateApiWorker(WikiInstance wiki)
        {
            switch (wiki.PreferredConnectionMethod)
            {
                case ConnectionMethod.HttpClient:
                    return new HttpClientApiWorker();
                case ConnectionMethod.HttpClientProxy:
                    return new ProxyHttpClientApiWorker();
                case ConnectionMethod.WebView:
                default:
                    return new WebViewApiWorker { Wiki = wiki };
            }
        }

        public Task<IApiWorker> CreateApiWorkerAsync(WikiInstance wiki)
        {
            return Task.FromResult(CreateApiWorker(wiki));
        }
    }
}