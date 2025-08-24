using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class AccountManager
    {
        public static IStorageProvider StorageProvider { get; set; }
        public static ICredentialService CredentialService { get; set; }

        private const string AccountsFileName = "accounts.json";
        private static List<Account> _accounts;

        public static async Task InitializeAsync()
        {
            if (_accounts != null)
                return;
            if (StorageProvider == null || CredentialService == null)
                throw new InvalidOperationException("Providers not set for AccountManager.");

            try
            {
                string json = await StorageProvider.ReadTextAsync(AccountsFileName);
                if (!string.IsNullOrEmpty(json))
                {
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
            string json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
            await StorageProvider.WriteTextAsync(AccountsFileName, json);
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
