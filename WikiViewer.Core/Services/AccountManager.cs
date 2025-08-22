using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Models;
using WikiViewer.Shared.Uwp.Services;
using Windows.Storage;

namespace WikiViewer.Core.Services
{
    public static class AccountManager
    {
        private const string AccountsFileName = "accounts.json";
        private static List<Account> _accounts;

        public static async Task InitializeAsync()
        {
            if (_accounts != null)
                return;

            try
            {
                var file =
                    await ApplicationData.Current.LocalFolder.TryGetItemAsync(AccountsFileName)
                    as StorageFile;
                if (file != null)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    _accounts =
                        JsonConvert.DeserializeObject<List<Account>>(json) ?? new List<Account>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountManager] Failed to load accounts: {ex.Message}");
            }

            if (_accounts == null)
            {
                _accounts = new List<Account>();
            }
        }

        public static async Task SaveAsync()
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                AccountsFileName,
                CreationCollisionOption.ReplaceExisting
            );
            string json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
            await FileIO.WriteTextAsync(file, json);
        }

        public static List<Account> GetAccountsForWiki(Guid wikiId) =>
            _accounts.Where(a => a.WikiInstanceId == wikiId).ToList();

        public static Account GetAccountById(Guid accountId) =>
            _accounts.FirstOrDefault(a => a.Id == accountId);

        public static async Task AddAccountAsync(Account account, string password)
        {
            _accounts.Add(account);
            CredentialService.SaveCredentials(account.Id, account.Username, password);
            await SaveAsync();
        }

        public static async Task RemoveAccountAsync(Guid accountId)
        {
            _accounts.RemoveAll(a => a.Id == accountId);
            CredentialService.ClearCredentials(accountId);
            await SaveAsync();
        }

        public static async Task RemoveAllAccountsForWikiAsync(Guid wikiId)
        {
            var accountsToRemove = GetAccountsForWiki(wikiId);
            foreach (var account in accountsToRemove)
            {
                CredentialService.ClearCredentials(account.Id);
            }
            _accounts.RemoveAll(a => a.WikiInstanceId == wikiId);
            await SaveAsync();
        }
    }
}
