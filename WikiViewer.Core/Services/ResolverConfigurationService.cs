using System;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public sealed class ResolverConfigurationService
    {
        private const string SettingsKey = "ResolverConfiguration";
        public static ResolverConfigurationService Current { get; set; }
        private readonly ISettingsProvider _settings;
        private readonly ICredentialService _credentials;
        public event EventHandler Changed;
        private sealed class Reference
        {
            public string Endpoint { get; set; }
            public Guid CredentialId { get; set; }
        }

        public ResolverConfigurationService(ISettingsProvider settings, ICredentialService credentials)
        { _settings = settings ?? throw new ArgumentNullException(nameof(settings)); _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials)); }

        public static Uri NormalizeEndpoint(string url)
        {
            Uri uri;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out uri) || uri.Scheme != "https" ||
                uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new ArgumentException("Enter an HTTPS resolver URL without embedded credentials, a query or a fragment.");
            string path = uri.AbsolutePath.TrimEnd('/');
            if (!path.EndsWith("/v1/request", StringComparison.Ordinal)) path += "/v1/request";
            return new UriBuilder(uri) { Path = path }.Uri;
        }

        private Reference ReadReference()
        {
            try { return JsonConvert.DeserializeObject<Reference>(_settings.GetValue(SettingsKey, "")); }
            catch (JsonException) { return null; }
        }

        public ResolverConfiguration Load()
        {
            var reference = ReadReference();
            if (reference == null || reference.CredentialId == Guid.Empty) return null;
            try
            {
                var endpoint = NormalizeEndpoint(reference.Endpoint);
                var credential = _credentials.LoadCredentials(reference.CredentialId);
                if (string.IsNullOrWhiteSpace(credential?.Username) || string.IsNullOrEmpty(credential.Password)) return null;
                return new ResolverConfiguration { Endpoint = endpoint, Username = credential.Username, Password = credential.Password };
            }
            catch (ArgumentException) { return null; }
        }

        public void Save(string url, string username, string password)
        {
            var endpoint = NormalizeEndpoint(url);
            username = (username ?? "").Trim();
            if (username.Length == 0 || username.Contains(":")) throw new ArgumentException("Enter a resolver username without a colon.");
            var oldReference = ReadReference();
            if (string.IsNullOrEmpty(password))
            {
                var previous = Load();
                if (previous == null || previous.Endpoint != endpoint || previous.Username != username)
                    throw new ArgumentException("Enter the password for this resolver URL and username.");
                password = previous.Password;
            }
            var reference = new Reference { Endpoint = endpoint.AbsoluteUri, CredentialId = Guid.NewGuid() };
            _credentials.SaveCredentials(reference.CredentialId, username, password);
            try { _settings.SetValue(SettingsKey, JsonConvert.SerializeObject(reference)); }
            catch { _credentials.ClearCredentials(reference.CredentialId); throw; }
            if (oldReference != null) _credentials.ClearCredentials(oldReference.CredentialId);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            var reference = ReadReference();
            _settings.SetValue(SettingsKey, "");
            if (reference != null) _credentials.ClearCredentials(reference.CredentialId);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
