using System.Net.Mail;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Contacts;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.ViewModels;

public sealed class LoginViewModel : BaseViewModel
{
    private readonly IMailAuthenticationService
        _mailAuthenticationService;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly IContactProvisioningService
        _contactProvisioningService;

    private string _emailAddress =
        string.Empty;

    private bool _hasPassword;
    private bool _isBusy;

    private string _statusMessage =
        string.Empty;

    public LoginViewModel(
        IMailAuthenticationService mailAuthenticationService,
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        IContactProvisioningService contactProvisioningService)
    {
        _mailAuthenticationService =
            mailAuthenticationService;

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _contactProvisioningService =
            contactProvisioningService;
    }

    public string EmailAddress
    {
        get => _emailAddress;

        set
        {
            if (_emailAddress == value)
            {
                return;
            }

            _emailAddress = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanLogin));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;

        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanLogin));
            OnPropertyChanged(nameof(LoginButtonText));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;

        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;

            OnPropertyChanged();
        }
    }

    public string LoginButtonText =>
        IsBusy
            ? "Anmeldung läuft …"
            : "Anmelden";

    public bool CanLogin =>
        !IsBusy &&
        IsEmailAddressValid(EmailAddress) &&
        _hasPassword;

    public void SetPasswordAvailable(
        bool hasPassword)
    {
        if (_hasPassword == hasPassword)
        {
            return;
        }

        _hasPassword =
            hasPassword;

        OnPropertyChanged(nameof(CanLogin));
    }

    public void PrepareKnownAccount(
        string? emailAddress)
    {
        EmailAddress =
            emailAddress ?? string.Empty;

        StatusMessage =
            string.Empty;
    }

    public async Task<bool> LoginAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!CanLogin)
        {
            return false;
        }

        IsBusy = true;

        StatusMessage =
            "Verbindung zum Mailserver wird hergestellt …";

        try
        {
            var emailAddress =
                EmailAddress.Trim();

            var result =
                await _mailAuthenticationService
                    .AuthenticateAsync(
                        emailAddress,
                        password,
                        cancellationToken);

            switch (result.Status)
            {
                case MailAuthenticationStatus.Success:
                    return await CompleteSuccessfulLoginAsync(
                        emailAddress,
                        password,
                        cancellationToken);

                case MailAuthenticationStatus.InvalidCredentials:
                    StatusMessage =
                        "Anmeldung nicht möglich. Bitte prüfen Sie Ihre E-Mail-Adresse und Ihr Passwort.";
                    return false;

                case MailAuthenticationStatus.CertificateError:
                    StatusMessage =
                        "Sichere Verbindung nicht möglich. Die Sicherheitsprüfung des Mailservers ist fehlgeschlagen.";
                    return false;

                case MailAuthenticationStatus.Timeout:
                    StatusMessage =
                        "Der Mailserver antwortet momentan nicht. Bitte versuchen Sie es erneut.";
                    return false;

                case MailAuthenticationStatus.ServerUnavailable:
                    StatusMessage =
                        "Der Mailserver ist momentan nicht erreichbar. Bitte prüfen Sie Ihre Internetverbindung und versuchen Sie es erneut.";
                    return false;

                default:
                    StatusMessage =
                        "Die Anmeldung konnte nicht abgeschlossen werden. Bitte versuchen Sie es erneut.";
                    return false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> CompleteSuccessfulLoginAsync(
        string emailAddress,
        string password,
        CancellationToken cancellationToken)
    {
        /*
         * Mehrkonten-Unterbau:
         *
         * Eine erfolgreiche Anmeldung darf niemals einfach
         * den aktuell aktiven Account überschreiben.
         *
         * Stattdessen wird anhand der E-Mail-Adresse geprüft,
         * ob dieses Konto bereits lokal bekannt ist.
         *
         * Bestehende Konten behalten damit dauerhaft ihre
         * AccountId und damit auch alle accountbezogenen
         * Einstellungen, Regeln, Cache-Daten und Zugangsdaten.
         */
        MailAccount? existingAccount;

        try
        {
            existingAccount =
                await _mailAccountStore
                    .GetAccountByEmailAddressAsync(
                        emailAddress,
                        cancellationToken);
        }
        catch
        {
            StatusMessage =
                "Die Anmeldung war erfolgreich, das E-Mail-Konto konnte jedoch nicht lokal vorbereitet werden.";

            return false;
        }

        if (existingAccount is not null)
        {
            return await CompleteExistingAccountLoginAsync(
                existingAccount,
                emailAddress,
                password,
                cancellationToken);
        }

        return await CompleteNewAccountLoginAsync(
            emailAddress,
            password,
            cancellationToken);
    }

    private async Task<bool> CompleteExistingAccountLoginAsync(
        MailAccount existingAccount,
        string emailAddress,
        string password,
        CancellationToken cancellationToken)
    {
        /*
         * Bei einem bereits bekannten Konto werden zuerst
         * die erfolgreich geprüften Zugangsdaten aktualisiert.
         *
         * Erst danach wird dieses Konto zum aktiven Konto.
         *
         * SetActiveAccountAsync arbeitet transaktional.
         * Schlägt die Aktivierung fehl, bleibt deshalb das
         * bisher aktive Konto unverändert.
         */
        try
        {
            await _credentialStore.SaveAsync(
                existingAccount.AccountId,
                emailAddress,
                password,
                cancellationToken);

            await _mailAccountStore.SetActiveAccountAsync(
                existingAccount.AccountId,
                cancellationToken);
        }
        catch
        {
            StatusMessage =
                "Die Anmeldung war erfolgreich, das E-Mail-Konto konnte jedoch nicht vollständig aktiviert werden.";

            return false;
        }

        return await CompleteAccountPreparationAsync(
            emailAddress,
            password,
            cancellationToken);
    }

    private async Task<bool> CompleteNewAccountLoginAsync(
        string emailAddress,
        string password,
        CancellationToken cancellationToken)
    {
        var account =
            new MailAccount
            {
                AccountId =
                    Guid.NewGuid(),

                EmailAddress =
                    emailAddress,

                DisplayName =
                    null,

                /*
                 * Ein neues Konto wird absichtlich zunächst
                 * inaktiv gespeichert.
                 *
                 * Dadurch bleibt das bisher aktive Konto so
                 * lange unangetastet, bis auch die neuen
                 * Zugangsdaten sicher gespeichert wurden.
                 */
                IsActive =
                    false,

                CreatedAtUtc =
                    DateTime.UtcNow
            };

        try
        {
            await _mailAccountStore.SaveAsync(
                account,
                cancellationToken);

            try
            {
                await _credentialStore.SaveAsync(
                    account.AccountId,
                    emailAddress,
                    password,
                    cancellationToken);
            }
            catch
            {
                /*
                 * Ohne sicher gespeicherte Zugangsdaten darf
                 * kein unvollständiges neues Konto erhalten
                 * bleiben.
                 */
                await _mailAccountStore.DeleteAsync(
                    account.AccountId,
                    CancellationToken.None);

                throw;
            }

            try
            {
                await _mailAccountStore.SetActiveAccountAsync(
                    account.AccountId,
                    cancellationToken);
            }
            catch
            {
                /*
                 * Auch bei einer fehlgeschlagenen Aktivierung
                 * wird das neu angelegte Konto wieder vollständig
                 * entfernt.
                 *
                 * Das zuvor aktive Konto bleibt durch die
                 * transaktionale Aktivierung unangetastet.
                 */
                await _credentialStore.DeleteAsync(
                    account.AccountId,
                    CancellationToken.None);

                await _mailAccountStore.DeleteAsync(
                    account.AccountId,
                    CancellationToken.None);

                throw;
            }
        }
        catch
        {
            StatusMessage =
                "Die Anmeldung war erfolgreich, das neue E-Mail-Konto konnte jedoch nicht sicher gespeichert werden.";

            return false;
        }

        return await CompleteAccountPreparationAsync(
            emailAddress,
            password,
            cancellationToken);
    }

    private async Task<bool> CompleteAccountPreparationAsync(
        string emailAddress,
        string password,
        CancellationToken cancellationToken)
    {
        /*
         * CardDAV wird nach dem erfolgreichen Mail-Login
         * automatisch vorbereitet.
         *
         * Der Benutzer muss weder einen CardDAV-Server
         * kennen noch ein Adressbuch manuell anlegen.
         *
         * Ein Fehler an dieser Stelle darf den Mail-Login
         * ausdrücklich nicht verhindern.
         */
        StatusMessage =
            "Kontakte werden vorbereitet …";

        await _contactProvisioningService
            .EnsureDefaultAddressBookAsync(
                emailAddress,
                password,
                cancellationToken);

        StatusMessage =
            "Anmeldung erfolgreich.";

        return true;
    }

    private static bool IsEmailAddressValid(
        string emailAddress)
    {
        if (string.IsNullOrWhiteSpace(
                emailAddress))
        {
            return false;
        }

        try
        {
            var address =
                new MailAddress(
                    emailAddress.Trim());

            return string.Equals(
                address.Address,
                emailAddress.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}