using System;
using System.Linq;
using System.Threading.Tasks;
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

        public static bool IsLoggedIn => CurrentAccount?.IsLoggedIn ?? false;
        public static string Username => CurrentAccount?.Username;
        public static bool IsResetPending { get; set; } = false;

        public static IApiWorker CurrentApiWorker
        {
            get
            {
                if (CurrentWiki == null)
                    throw new InvalidOperationException("Session not initialized.");
                if (IsLoggedIn)
                    return CurrentAccount.AuthenticatedApiWorker;

                return _workerProvider.GetWorkerForWiki(CurrentWiki);
            }
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
                throw new Exception("No wikis configured.");
            }

            var firstAccount = AccountManager.GetAccountsForWiki(CurrentWiki.Id).FirstOrDefault();
            if (firstAccount != null)
            {
                var credentials = CredentialService.LoadCredentials(firstAccount.Id);
                if (credentials != null)
                {
                    CurrentAccount = firstAccount;
                    var authService = new AuthenticationService(
                        CurrentAccount,
                        CurrentWiki,
                        App.ApiWorkerFactory
                    );
                    try
                    {
                        await authService.LoginAsync(credentials.Password);
                    }
                    catch (Exception)
                    {
                        CurrentAccount = null;
                    }
                }
            }
        }

        public static void DisposeAndReset()
        {
            _workerProvider?.DisposeAll();
            CurrentAccount?.AuthenticatedApiWorker?.Dispose();
            CurrentWiki = null;
            CurrentAccount = null;
        }
    }
}
