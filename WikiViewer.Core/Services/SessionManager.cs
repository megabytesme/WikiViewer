using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class SessionManager
    {
        private static ApiWorkerProvider _workerProvider;
        public static bool IsResetPending { get; set; } = false;
        public static event EventHandler<AutoLoginFailedEventArgs> AutoLoginFailed;

        public static IApiWorkerFactory ApiWorkerFactory { get; set; }
        public static Task PlatformReady { get; set; } = Task.CompletedTask;
        public static ICredentialService CredentialService { get; set; }

        public static IApiWorker GetAnonymousWorkerForWiki(WikiInstance wiki)
        {
            if (wiki == null)
                throw new InvalidOperationException("Wiki instance cannot be null.");
            return _workerProvider.GetWorkerForWiki(wiki);
        }

        public static async Task InitializeAsync()
        {
            if (ApiWorkerFactory == null || CredentialService == null)
                throw new InvalidOperationException("Dependencies for SessionManager not set.");

            if (IsResetPending)
            {
                DisposeAndReset();
                IsResetPending = false;
            }

            if (_workerProvider == null)
            {
                _workerProvider = new ApiWorkerProvider(ApiWorkerFactory);
            }

            await Task.CompletedTask;
        }

        public static async Task PerformAutoLoginAsync()
        {
            await PlatformReady;
            var allWikis = WikiManager.GetWikis();

            foreach (var wiki in allWikis)
            {
                var accountsForWiki = AccountManager.GetAccountsForWiki(wiki.Id);
                foreach (var account in accountsForWiki)
                {
                    var credentials = CredentialService.LoadCredentials(account.Id);
                    if (credentials != null)
                    {
                        Debug.WriteLine(
                            $"[SessionManager] Attempting auto-login for '{account.Username}' on '{wiki.Name}'..."
                        );
                        var authService = new AuthenticationService(
                            account,
                            wiki,
                            ApiWorkerFactory
                        );
                        try
                        {
                            await authService.LoginAsync(credentials.Password);
                            Debug.WriteLine(
                                $"[SessionManager] Auto-login SUCCESS for '{account.Username}' on '{wiki.Name}'."
                            );
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(
                                $"[SessionManager] Auto-login FAILED for '{account.Username}' on '{wiki.Name}': {ex.Message}"
                            );
                            AutoLoginFailed?.Invoke(null, new AutoLoginFailedEventArgs(wiki, ex));
                        }
                    }
                }
            }
        }

        public static async Task PerformSingleLoginAsync(WikiInstance wiki)
        {
            await PlatformReady;

            var accountsForWiki = AccountManager.GetAccountsForWiki(wiki.Id);
            foreach (var account in accountsForWiki)
            {
                var credentials = CredentialService.LoadCredentials(account.Id);
                if (credentials != null)
                {
                    Debug.WriteLine(
                        $"[SessionManager] Retrying login for '{account.Username}' on '{wiki.Name}'..."
                    );
                    var authService = new AuthenticationService(account, wiki, ApiWorkerFactory);
                    try
                    {
                        await authService.LoginAsync(credentials.Password);
                        Debug.WriteLine(
                            $"[SessionManager] Retry-login SUCCESS for '{account.Username}' on '{wiki.Name}'."
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[SessionManager] Retry-login FAILED for '{account.Username}' on '{wiki.Name}': {ex.Message}"
                        );
                        AutoLoginFailed?.Invoke(null, new AutoLoginFailedEventArgs(wiki, ex));
                    }
                }
            }
        }

        public static void DisposeAndReset()
        {
            _workerProvider?.DisposeAll();
            _workerProvider = null;

            var allAccounts = WikiManager
                .GetWikis()
                .SelectMany(w => AccountManager.GetAccountsForWiki(w.Id));
            foreach (var account in allAccounts)
            {
                account.AuthenticatedApiWorker?.Dispose();
                account.AuthenticatedApiWorker = null;
            }
        }
    }
}
