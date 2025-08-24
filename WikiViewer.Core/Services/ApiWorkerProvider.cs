using System;
using System.Collections.Generic;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public class ApiWorkerProvider
    {
        private readonly IApiWorkerFactory _factory;
        private readonly Dictionary<Guid, IApiWorker> _persistentWorkers =
            new Dictionary<Guid, IApiWorker>();
        private readonly object _lock = new object();

        public ApiWorkerProvider(IApiWorkerFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public IApiWorker GetWorkerForWiki(WikiInstance wiki)
        {
            if (wiki == null)
                throw new ArgumentNullException(nameof(wiki));

            lock (_lock)
            {
                if (_persistentWorkers.TryGetValue(wiki.Id, out var worker))
                {
                    return worker;
                }

                var newWorker = _factory.CreateApiWorker(wiki);
                _persistentWorkers[wiki.Id] = newWorker;
                return newWorker;
            }
        }

        public void DisposeAll()
        {
            lock (_lock)
            {
                foreach (var worker in _persistentWorkers.Values)
                {
                    worker.Dispose();
                }
                _persistentWorkers.Clear();
            }
        }
    }
}
