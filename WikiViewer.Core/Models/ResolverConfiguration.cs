using System;

namespace WikiViewer.Core.Models
{
    // Only held in memory. Settings persist the endpoint and a vault reference, never these credentials.
    public sealed class ResolverConfiguration
    {
        public Uri Endpoint { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
