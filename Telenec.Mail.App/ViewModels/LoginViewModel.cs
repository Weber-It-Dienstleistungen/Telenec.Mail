using System.Net.Mail;
using Telenec.Mail.App.Models;
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

    private string _emailAddress =
        string.Empty;

    private bool _hasPassword;
    private bool _isBusy;

    private string _statusMessage =
        string.Empty;

    public LoginViewModel(
        IMailAuthenticationService mailAuthenticationService,
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        _mailAuthenticationService =
            mailAuthenticationService;

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;
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
        var existingAccount =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        var account =
            existingAccount is null
                ? new MailAccount
                {
                    AccountId =
                        Guid.NewGuid(),

                    EmailAddress =
                        emailAddress,

                    DisplayName =
                        null,

                    IsActive =
                        true,

                    CreatedAtUtc =
                        DateTime.UtcNow
                }
                : new MailAccount
                {
                    AccountId =
                        existingAccount.AccountId,

                    EmailAddress =
                        emailAddress,

                    DisplayName =
                        existingAccount.DisplayName,

                    IsActive =
                        true,

                    CreatedAtUtc =
                        existingAccount.CreatedAtUtc
                };

        var accountWasNew =
            existingAccount is null;

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
                if (accountWasNew)
                {
                    await _mailAccountStore.DeleteAsync(
                        account.AccountId,
                        CancellationToken.None);
                }

                throw;
            }
        }
        catch
        {
            StatusMessage =
                "Die Anmeldung war erfolgreich, die Zugangsdaten konnten jedoch nicht sicher gespeichert werden.";

            return false;
        }

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