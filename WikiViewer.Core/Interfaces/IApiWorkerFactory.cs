using WikiViewer.Core.Models;

namespace WikiViewer.Core.Interfaces
{
    public interface IApiWorkerFactory
    {
        IApiWorker CreateApiWorker(WikiInstance wiki);
    }
}