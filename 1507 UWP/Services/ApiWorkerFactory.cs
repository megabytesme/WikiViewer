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
            IApiWorker worker;
            if (wiki.PreferredConnectionMethod == ConnectionMethod.HttpClientProxy)
            {
                worker = new HttpClientApiWorker();
            }
            else
            {
                var webViewWorker = new WebViewApiWorker();
                webViewWorker.Wiki = wiki;
                worker = webViewWorker;
            }
            return worker;
        }

        public Task<IApiWorker> CreateApiWorkerAsync(WikiInstance wiki)
        {
            return Task.FromResult(CreateApiWorker(wiki));
        }
    }
}