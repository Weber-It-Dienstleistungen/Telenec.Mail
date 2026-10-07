using System.ComponentModel;
using System.Windows;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Account;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Security;

namespace Telenec.Mail.App;

public partial class ChangePasswordWindow : Window
{
    private readonly MailAccount
        _account;

    private readonly ICredentialStore
        _credentialStore;

    private readonly IAccountPasswordChangeService
        _passwordChangeService;

    private readonly IMailAuthenticationService
        _mailAuthenticationService;

    private bool
        _isBusy;

    private bool
        _serverPasswordChanged;

    private bool
        _credentialStored;

    private bool
        _allowClose;

    private string?
        _pendingNewPassword;

    public ChangePasswordWindow(
        MailAccount account,
        ICredentialStore credentialStore,
        IAccountPasswordChangeService passwordChangeService,
        IMailAuthenticationService mailAuthenticationService)
    {
        ArgumentNullException.ThrowIfNull(
            account);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        ArgumentNullException.ThrowIfNull(
            passwordChangeService);

        ArgumentNullException.ThrowIfNull(
            mailAuthenticationService);

        InitializeComponent();

        _account =
            account;

        _credentialStore =
            credentialStore;

        _passwordChangeService =
            passwordChangeService;

        _mailAuthenticationService =
            mailAuthenticationService;

        AccountEmailText.Text =
            account.EmailAddress;

        UpdateControlState();
    }

    private void PasswordInput_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        UpdateControlState();
    }

    private async void ChangeButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_serverPasswordChanged)
        {
            if (string.IsNullOrEmpty(
                    _pendingNewPassword))
            {
                return;
            }

            await RunBusyAsync(
                PersistAndVerifyAsync);

            return;
        }

        var currentPassword =
            CurrentPasswordInput.Password;

        var newPassword =
            NewPasswordInput.Password;

        var confirmation =
            ConfirmPasswordInput.Password;

        if (string.IsNullOrEmpty(
                currentPassword) ||
            string.IsNullOrEmpty(
                newPassword) ||
            string.IsNullOrEmpty(
                confirmation))
        {
            StatusText.Text =
                "Bitte füllen Sie alle Passwortfelder aus.";

            return;
        }

        if (!string.Equals(
                newPassword,
                confirmation,
                StringComparison.Ordinal))
        {
            StatusText.Text =
                "Die neuen Passwörter stimmen nicht überein.";

            return;
        }

        if (string.Equals(
                currentPassword,
                newPassword,
                StringComparison.Ordinal))
        {
            StatusText.Text =
                "Das neue Passwort muss sich vom aktuellen Passwort unterscheiden.";

            return;
        }

        await RunBusyAsync(
            async () =>
            {
                /*
                 * Vor dem serverseitigen Passwortwechsel
                 * prüfen wir, ob der lokale Credential Store
                 * vorhanden und beschreibbar ist.
                 *
                 * Dabei wird ausschließlich das bereits
                 * vorhandene Credential erneut gespeichert.
                 * Sein Inhalt wird nicht verändert.
                 */
                StoredCredential?
                    existingCredential;

                try
                {
                    existingCredential =
                        await _credentialStore
                            .ReadAsync(
                                _account.AccountId);

                    if (existingCredential is null)
                    {
                        StatusText.Text =
                            "Die gespeicherten Zugangsdaten dieses Kontos konnten nicht gefunden werden.";

                        return;
                    }

                    await _credentialStore
                        .SaveAsync(
                            _account.AccountId,
                            existingCredential.UserName,
                            existingCredential.Password);
                }
                catch
                {
                    StatusText.Text =
                        "Der Windows-Zugangsspeicher ist momentan nicht verfügbar.";

                    MessageBox.Show(
                        this,
                        "Das Passwort wurde nicht geändert.\n\n" +
                        "Die lokalen Zugangsdaten konnten nicht sicher aktualisiert werden.",
                        "Passwort ändern",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                StatusText.Text =
                    "Das Passwort wird geändert …";

                var result =
                    await _passwordChangeService
                        .ChangePasswordAsync(
                            _account.EmailAddress,
                            currentPassword,
                            newPassword);

                switch (result.Status)
                {
                    case AccountPasswordChangeStatus.Success:
                        /*
                         * Ab diesem Moment ist das neue Passwort
                         * serverseitig maßgeblich.
                         *
                         * Wir dürfen deshalb keinesfalls mehr
                         * auf das alte Credential zurückfallen.
                         */
                        _serverPasswordChanged =
                            true;

                        _pendingNewPassword =
                            newPassword;

                        StatusText.Text =
                            "Passwort geändert. Gespeicherte Zugangsdaten werden aktualisiert …";

                        await PersistAndVerifyAsync();
                        return;

                    case AccountPasswordChangeStatus.InvalidCredentials:
                        StatusText.Text =
                            "Das aktuelle Passwort ist nicht korrekt.";
                        return;

                    case AccountPasswordChangeStatus.PasswordRejected:
                        StatusText.Text =
                            "Das neue Passwort wurde vom Mailserver abgelehnt.";
                        return;

                    case AccountPasswordChangeStatus.RateLimited:
                        StatusText.Text =
                            "Zu viele Passwortversuche. Bitte warten Sie einige Minuten und versuchen Sie es erneut.";
                        return;

                    case AccountPasswordChangeStatus.ServiceUnavailable:
                        StatusText.Text =
                            "Der Passwortdienst ist momentan nicht erreichbar.";
                        return;

                    default:
                        StatusText.Text =
                            "Das Passwort konnte nicht geändert werden.";
                        return;
                }
            });
    }

    private async Task PersistAndVerifyAsync()
    {
        var newPassword =
            _pendingNewPassword;

        if (string.IsNullOrEmpty(
                newPassword))
        {
            return;
        }

        try
        {
            await _credentialStore
                .SaveAsync(
                    _account.AccountId,
                    _account.EmailAddress,
                    newPassword);

            _credentialStored =
                true;
        }
        catch
        {
            _credentialStored =
                false;

            StatusText.Text =
                "Das Passwort wurde auf dem Mailserver geändert, konnte aber lokal noch nicht gespeichert werden.";

            MessageBox.Show(
                this,
                "Das Passwort wurde auf dem Mailserver bereits erfolgreich geändert.\n\n" +
                "Die neuen Zugangsdaten konnten jedoch noch nicht im Windows-Zugangsspeicher gespeichert werden.\n\n" +
                "Bitte lassen Sie dieses Fenster geöffnet und klicken Sie anschließend erneut auf „Lokale Zugangsdaten erneut speichern“.",
                "Lokale Speicherung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        StatusText.Text =
            "Die Verbindung mit dem neuen Passwort wird geprüft …";

        var authenticationResult =
            await _mailAuthenticationService
                .AuthenticateAsync(
                    _account.EmailAddress,
                    newPassword);

        if (authenticationResult.Status ==
            MailAuthenticationStatus.Success)
        {
            CompleteSuccessfully(
                "Das Passwort wurde erfolgreich geändert und die neuen Zugangsdaten wurden gespeichert.");

            return;
        }

        if (authenticationResult.Status ==
            MailAuthenticationStatus.InvalidCredentials)
        {
            StatusText.Text =
                "Das Passwort wurde geändert und gespeichert, die IMAP-Prüfung wurde jedoch abgelehnt.";

            MessageBox.Show(
                this,
                "Das Passwort wurde vom Passwortdienst erfolgreich geändert und lokal gespeichert.\n\n" +
                "Die anschließende IMAP-Anmeldung mit dem neuen Passwort wurde jedoch abgelehnt.\n\n" +
                "Sie können die Prüfung erneut ausführen oder das Fenster schließen und die Verbindung später erneut herstellen.",
                "Passwort geändert – Prüfung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        /*
         * Eine Netzwerk-, Zertifikats- oder Timeout-Störung
         * darf einen bereits erfolgreichen Passwortwechsel
         * nicht rückgängig machen.
         */
        CompleteSuccessfully(
            "Das Passwort wurde geändert und gespeichert.\n\n" +
            "Die Verbindung konnte momentan nicht zusätzlich geprüft werden.",
            warning:
                true);
    }

    private void CompleteSuccessfully(
        string message,
        bool warning = false)
    {
        CurrentPasswordInput.Clear();
        NewPasswordInput.Clear();
        ConfirmPasswordInput.Clear();

        _pendingNewPassword =
            null;

        StatusText.Text =
            warning
                ? "Passwort geändert und gespeichert. Verbindungsprüfung momentan nicht möglich."
                : "Passwort erfolgreich geändert.";

        MessageBox.Show(
            this,
            message,
            "Passwort ändern",
            MessageBoxButton.OK,
            warning
                ? MessageBoxImage.Information
                : MessageBoxImage.Information);

        _allowClose =
            true;

        DialogResult =
            true;
    }

    private async Task RunBusyAsync(
        Func<Task> action)
    {
        _isBusy =
            true;

        UpdateControlState();

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusText.Text =
                "Der Vorgang konnte nicht abgeschlossen werden.";

            MessageBox.Show(
                this,
                "Der Passwortwechsel konnte nicht abgeschlossen werden.\n\n" +
                $"Fehlerdetails:\n{exception.GetType().Name}: {exception.Message}",
                "Passwort ändern",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isBusy =
                false;

            UpdateControlState();
        }
    }

    private void UpdateControlState()
    {
        PasswordFieldsPanel.IsEnabled =
            !_isBusy &&
            !_serverPasswordChanged;

        CancelButton.IsEnabled =
            !_isBusy &&
            (!_serverPasswordChanged ||
             _credentialStored);

        if (_serverPasswordChanged)
        {
            ChangeButton.Content =
                _credentialStored
                    ? "Verbindung erneut prüfen"
                    : "Lokale Zugangsdaten erneut speichern";

            ChangeButton.IsEnabled =
                !_isBusy &&
                !string.IsNullOrEmpty(
                    _pendingNewPassword);

            return;
        }

        ChangeButton.Content =
            "Passwort ändern";

        ChangeButton.IsEnabled =
            !_isBusy &&
            !string.IsNullOrEmpty(
                CurrentPasswordInput.Password) &&
            !string.IsNullOrEmpty(
                NewPasswordInput.Password) &&
            !string.IsNullOrEmpty(
                ConfirmPasswordInput.Password);
    }

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_serverPasswordChanged &&
            !_credentialStored)
        {
            MessageBox.Show(
                this,
                "Das Passwort wurde auf dem Mailserver bereits geändert, aber noch nicht lokal gespeichert.\n\n" +
                "Bitte versuchen Sie die lokale Speicherung erneut.",
                "Passwort ändern",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        _allowClose =
            true;

        DialogResult =
            false;
    }

    private void ChangePasswordWindow_OnClosing(
        object? sender,
        CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (!_serverPasswordChanged ||
            _credentialStored)
        {
            return;
        }

        e.Cancel =
            true;

        MessageBox.Show(
            this,
            "Das Passwort wurde auf dem Mailserver bereits geändert, aber noch nicht im Windows-Zugangsspeicher gespeichert.\n\n" +
            "Das Fenster kann deshalb momentan nicht geschlossen werden. Bitte versuchen Sie die lokale Speicherung erneut.",
            "Passwort ändern",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}