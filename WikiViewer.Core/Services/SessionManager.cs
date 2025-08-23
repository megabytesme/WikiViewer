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
        private static ApiWorkerProvider _workerProvider;
        public static bool IsResetPending { get; set; } = false;

        public static IApiWorker GetAnonymousWorkerForWiki(WikiInstance wiki)
        {
            if (wiki == null) throw new InvalidOperationException("Wiki instance cannot be null.");
            return _workerProvider.GetWorkerForWiki(wiki);
        }

        public static async Task InitializeAsync()
        {
            if (IsResetPending)
            {
                DisposeAndReset();
                IsResetPending = false;
            }

            if (_workerProvider == null)
            {
                _workerProvider = new ApiWorkerProvider(App.ApiWorkerFactory);
            }

            await WikiManager.InitializeAsync();
            await AccountManager.InitializeAsync();
            await FavouritesService.InitializeAsync();

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
                            }
                        }
                    }));
                }
            }
            await Task.WhenAll(loginTasks);
        }

        public static void DisposeAndReset()
        {
            _workerProvider?.DisposeAll();
            _workerProvider = null;

            var allAccounts = WikiManager.GetWikis().SelectMany(w => AccountManager.GetAccountsForWiki(w.Id));
            foreach (var account in allAccounts)
            {
                account.AuthenticatedApiWorker?.Dispose();
                account.AuthenticatedApiWorker = null;
            }
        }
    }
}