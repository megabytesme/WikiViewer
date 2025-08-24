using System;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Interfaces
{
    public interface ICredentialService
    {
        void SaveCredentials(Guid accountId, string username, string password);
        UserCredentials LoadCredentials(Guid accountId);
        void ClearCredentials(Guid accountId);
    }
}
