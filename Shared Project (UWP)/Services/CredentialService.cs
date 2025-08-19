using System;
using System.Linq;
using Windows.Security.Credentials;

namespace _1809_UWP
{
    public class UserCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public static class CredentialService
    {
        private const string ResourceName = "WikiViewerAppCredentials";

        public static void SaveCredentials(string username, string password)
        {
            try
            {
                var vault = new PasswordVault();
                var credential = new PasswordCredential(ResourceName, username, password);
                vault.Add(credential);
            }
            catch (Exception)
            {
                ClearCredentials();
                SaveCredentials(username, password);
            }
        }

        public static UserCredentials LoadCredentials()
        {
            try
            {
                var vault = new PasswordVault();
                var credentials = vault.FindAllByResource(ResourceName);
                if (credentials.Any())
                {
                    credentials[0].RetrievePassword();
                    return new UserCredentials
                    {
                        Username = credentials[0].UserName,
                        Password = credentials[0].Password
                    };
                }
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }

        public static void ClearCredentials()
        {
            try
            {
                var vault = new PasswordVault();
                var credentials = vault.FindAllByResource(ResourceName);
                foreach (var cred in credentials)
                {
                    vault.Remove(cred);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}