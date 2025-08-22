using WikiViewer.Core.Enums;

namespace WikiViewer.Core.Interfaces
{
    public interface IApiWorkerFactory
    {
        IApiWorker CreateApiWorker(ConnectionMethod method);
    }
}
