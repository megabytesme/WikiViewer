using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public class ApiWorkerProvider
    {
        private readonly IApiWorkerFactory _factory;
        private readonly Dictionary<Guid, IApiWorker> _anonymousWorkers =
            new Dictionary<Guid, IApiWorker>();

        public ApiWorkerProvider(IApiWorkerFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public IApiWorker GetWorkerForWiki(WikiInstance wiki)
        {
            if (wiki == null)
                throw new ArgumentNullException(nameof(wiki));

            if (_anonymousWorkers.TryGetValue(wiki.Id, out var worker))
            {
                return worker;
            }

            var newWorker = _factory.CreateApiWorker(wiki);
            _anonymousWorkers[wiki.Id] = newWorker;
            return newWorker;
        }

        public void DisposeAll()
        {
            foreach (var worker in _anonymousWorkers.Values)
            {
                worker.Dispose();
            }
            _anonymousWorkers.Clear();
        }
    }
}