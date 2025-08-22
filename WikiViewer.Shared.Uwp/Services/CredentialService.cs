using System;
using System.Linq;
using Windows.Security.Credentials;

namespace WikiViewer.Shared.Uwp.Services
{
    public class UserCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public static class CredentialService
    {
        private const string ResourceName = "WikiViewerAppCredentials";

        public static void SaveCredentials(Guid accountId, string username, string password)
        {
            var vault = new PasswordVault();
            var credential = new PasswordCredential(accountId.ToString(), username, password);
            vault.Add(credential);
        }

        public static UserCredentials LoadCredentials(Guid accountId)
        {
            try
            {
                var vault = new PasswordVault();
                var credential = vault.Retrieve(accountId.ToString(), "anything");
                credential.RetrievePassword();
                return new UserCredentials
                {
                    Username = credential.UserName,
                    Password = credential.Password,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void ClearCredentials(Guid accountId)
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
