using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Services;

namespace _1703_UWP.Services
{
    public class ApiWorkerFactory : IApiWorkerFactory
    {
        public IApiWorker CreateApiWorker(ConnectionMethod method)
        {
            if (method == ConnectionMethod.HttpClientProxy)
            {
                return new HttpClientApiWorker();
            }
            else
            {
                return new WebViewApiWorker();
            }
        }
    }
}
