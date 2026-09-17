using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Contacts;
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

    private readonly IContactProvisioningService
        _contactProvisioningService;

    public ApplicationStartupService(
        DatabaseInitializer databaseInitializer,
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        IContactProvisioningService contactProvisioningService)
    {
        _databaseInitializer =
            databaseInitializer;

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _contactProvisioningService =
            contactProvisioningService;
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

            /*
             * Die gespeicherten Zugangsdaten werden hier
             * tatsächlich gelesen und nicht nur auf ihre
             * Existenz geprüft.
             *
             * Dadurch können wir beim normalen Autostart
             * dieselben Zugangsdaten auch für CardDAV
             * verwenden.
             */
            var credential =
                await _credentialStore.ReadAsync(
                    account.AccountId,
                    cancellationToken);

            if (credential is null)
            {
                return StartupResult
                    .AuthenticationRequired(account);
            }

            /*
             * CardDAV wird auch beim normalen Programmstart
             * automatisch vorbereitet.
             *
             * Der Benutzer muss sich also nicht erst manuell
             * ab- und wieder anmelden.
             *
             * EnsureDefaultAddressBookAsync behandelt seine
             * eigenen Netzwerkfehler bewusst intern.
             * Ein nicht erreichbarer Kontakte-Server verhindert
             * deshalb nicht den normalen Mail-Start.
             */
            await _contactProvisioningService
                .EnsureDefaultAddressBookAsync(
                    credential.UserName,
                    credential.Password,
                    cancellationToken);

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