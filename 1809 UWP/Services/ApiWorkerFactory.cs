using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Services;

namespace _1809_UWP.Services
{
    public class ApiWorkerFactory : IApiWorkerFactory
    {
        public IApiWorker CreateApiWorker()
        {
            if (WikiViewer.Core.AppSettings.ConnectionBackend == WikiViewer.Core.Enums.ConnectionMethod.HttpClientProxy)
            {
                return new HttpClientApiWorker();
            }
            else
            {
                return new WebView2ApiWorker();
            }
        }
    }
}