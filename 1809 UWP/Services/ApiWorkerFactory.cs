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
            IApiWorker worker;
            if (wiki.PreferredConnectionMethod == ConnectionMethod.HttpClientProxy)
            {
                worker = new HttpClientApiWorker();
            }
            else
            {
                var webView2Worker = new WebView2ApiWorker();
                webView2Worker.Wiki = wiki;
                worker = webView2Worker;
            }
            return worker;
        }
    }
}