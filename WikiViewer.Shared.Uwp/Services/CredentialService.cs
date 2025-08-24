using System;
using System.Linq;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using Windows.Security.Credentials;

namespace WikiViewer.Shared.Uwp.Services
{
    public class CredentialService : ICredentialService
    {
        private const string ResourceName = "WikiViewerAppCredentials";

        public void SaveCredentials(Guid accountId, string username, string password)
        {
            var vault = new PasswordVault();
            var credential = new PasswordCredential(accountId.ToString(), username, password);
            vault.Add(credential);
        }

        public UserCredentials LoadCredentials(Guid accountId)
        {
            try
            {
                var vault = new PasswordVault();
                var credentialsList = vault.FindAllByResource(accountId.ToString());
                var credential = credentialsList.FirstOrDefault();

                if (credential != null)
                {
                    credential.RetrievePassword();
                    return new UserCredentials
                    {
                        Username = credential.UserName,
                        Password = credential.Password,
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CredentialService] Failed to load credential for {accountId}: {ex.Message}"
                );
            }

            return null;
        }

        public void ClearCredentials(Guid accountId)
        {
            try
            {
                var vault = new PasswordVault();
                var credentials = vault.FindAllByResource(accountId.ToString());
                foreach (var cred in credentials)
                {
                    vault.Remove(cred);
                }
            }
            catch (Exception) { }
        }
    }
}
