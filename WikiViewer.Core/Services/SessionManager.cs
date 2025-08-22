using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Shared.Uwp;
using WikiViewer.Shared.Uwp.Services;

namespace WikiViewer.Core.Services
{
    public static class SessionManager
    {
        public static WikiInstance CurrentWiki { get; private set; }
        public static Account CurrentAccount { get; private set; }
        private static ApiWorkerProvider _workerProvider;
        public static bool IsResetPending { get; set; } = false;

        public static bool IsLoggedIn => CurrentAccount?.IsLoggedIn ?? false;
        public static string Username => CurrentAccount?.Username;

        public static IApiWorker CurrentApiWorker
        {
            get
            {
                if (CurrentWiki == null) throw new InvalidOperationException("Session not initialized.");
                if (IsLoggedIn) return CurrentAccount.AuthenticatedApiWorker;
                return _workerProvider.GetWorkerForWiki(CurrentWiki);
            }
        }

        public static void SetCurrentWiki(WikiInstance wiki)
        {
            if (wiki == null) throw new ArgumentNullException(nameof(wiki));
            CurrentWiki = wiki;
            CurrentAccount = AccountManager.GetAccountsForWiki(wiki.Id).FirstOrDefault(a => a.IsLoggedIn);
        }

        public static async Task InitializeAsync()
        {
            if (IsResetPending)
            {
                DisposeAndReset();
                IsResetPending = false;
            }

            _workerProvider = new ApiWorkerProvider(App.ApiWorkerFactory);

            await WikiManager.InitializeAsync();
            await AccountManager.InitializeAsync();
            await FavouritesService.InitializeAsync();

            CurrentWiki = WikiManager.GetWikis().FirstOrDefault();
            if (CurrentWiki == null)
            {
                Debug.WriteLine("[SessionManager] CRITICAL: No wikis configured after initialization.");
                return;
            }

            var allWikis = WikiManager.GetWikis();
            var loginTasks = new List<Task>();

            foreach (var wiki in allWikis)
            {
                var accountsForWiki = AccountManager.GetAccountsForWiki(wiki.Id);
                foreach (var account in accountsForWiki)
                {
                    loginTasks.Add(Task.Run(async () =>
                    {
                        var credentials = CredentialService.LoadCredentials(account.Id);
                        if (credentials != null)
                        {
                            Debug.WriteLine($"[SessionManager] Attempting auto-login for '{account.Username}' on '{wiki.Name}'...");
                            var authService = new AuthenticationService(account, wiki, App.ApiWorkerFactory);
                            try
                            {
                                await authService.LoginAsync(credentials.Password);
                                Debug.WriteLine($"[SessionManager] Auto-login SUCCESS for '{account.Username}' on '{wiki.Name}'.");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[SessionManager] Auto-login FAILED for '{account.Username}' on '{wiki.Name}': {ex.Message}");
                                CredentialService.ClearCredentials(account.Id);
                            }
                        }
                    }));
                }
            }

            await Task.WhenAll(loginTasks);

            CurrentAccount = AccountManager.GetAccountsForWiki(CurrentWiki.Id).FirstOrDefault(a => a.IsLoggedIn);
        }

        public static void DisposeAndReset()
        {
            _workerProvider?.DisposeAll();
            var allAccounts = WikiManager.GetWikis().SelectMany(w => AccountManager.GetAccountsForWiki(w.Id));
            foreach (var account in allAccounts)
            {
                account.AuthenticatedApiWorker?.Dispose();
                account.AuthenticatedApiWorker = null;
            }
            CurrentWiki = null;
            CurrentAccount = null;
        }
    }
}