using System;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core.Models
{
    public class Account
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid WikiInstanceId { get; set; }
        public string Username { get; set; }

        [JsonIgnore]
        public IApiWorker AuthenticatedApiWorker { get; set; }

        [JsonIgnore]
        public bool IsLoggedIn => AuthenticatedApiWorker != null;
    }
}
