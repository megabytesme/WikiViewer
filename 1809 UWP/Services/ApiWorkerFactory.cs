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
            if (wiki.PreferredConnectionMethod == ConnectionMethod.HttpClientProxy)
            {
                return new ProxyHttpClientApiWorker();
            }

            return new PooledWebView2ProxyWorker(wiki);
        }

        public Task<IApiWorker> CreateApiWorkerAsync(WikiInstance wiki)
        {
            return Task.FromResult(CreateApiWorker(wiki));
        }
    }
}
