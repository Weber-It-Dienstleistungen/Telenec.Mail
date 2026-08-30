using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Startup;

public sealed class ApplicationStartupService
{
    private readonly DatabaseInitializer
        _databaseInitializer;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    public ApplicationStartupService(
        DatabaseInitializer databaseInitializer,
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        _databaseInitializer =
            databaseInitializer;

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;
    }

    public async Task<StartupResult> DetermineStartupStateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _databaseInitializer.InitializeAsync(
                cancellationToken);

            var account =
                await _mailAccountStore.GetActiveAccountAsync(
                    cancellationToken);

            if (account is null)
            {
                return StartupResult.NoAccount();
            }

            if (!IsValid(account))
            {
                return StartupResult
                    .AccountConfigurationInvalid(account);
            }

            var credentialExists =
                await _credentialStore.ExistsAsync(
                    account.AccountId,
                    cancellationToken);

            if (!credentialExists)
            {
                return StartupResult
                    .AuthenticationRequired(account);
            }

            return StartupResult
                .AccountReady(account);
        }
        catch (Exception ex)
        {
            return StartupResult.StartupFailure(
                ex.Message);
        }
    }

    private static bool IsValid(
        MailAccount account)
    {
        if (account.AccountId == Guid.Empty)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                account.EmailAddress))
        {
            return false;
        }

        if (!account.IsActive)
        {
            return false;
        }

        return true;
    }
}