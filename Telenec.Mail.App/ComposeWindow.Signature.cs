using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App;

public partial class ComposeWindow
{
    private readonly ISettingsStore
        _settingsStore;

    private readonly IMailAccountStore
        _mailAccountStore;

    private bool
        _isNewMessage =
            true;

    private bool
        _signatureAppliedToNewMessage;

    private string
        _newMessageSignatureText =
            string.Empty;

    private Task?
        _signaturePreparationTask;

    private Task EnsureSignaturePreparedAsync()
    {
        if (!_isNewMessage)
        {
            return Task.CompletedTask;
        }

        _signaturePreparationTask ??=
            LoadAndApplySignatureAsync();

        return _signaturePreparationTask;
    }

    private async Task LoadAndApplySignatureAsync()
    {
        /*
         * Die Signatur darf bestehende Inhalte niemals
         * überschreiben.
         *
         * Das ist insbesondere eine zusätzliche defensive
         * Absicherung für spätere neue Aufrufwege, die
         * eventuell bereits Text in einer neuen Nachricht
         * vorbereiten.
         */
        if (!string.IsNullOrEmpty(
                _viewModel.Body) ||
            !string.IsNullOrWhiteSpace(
                _viewModel.HtmlBody))
        {
            return;
        }

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is null)
            {
                return;
            }

            var signatureEnabled =
                await _settingsStore
                    .GetAccountSettingAsync(
                        account.AccountId,
                        SettingsKeys.ComposeSignatureEnabled);

            if (!string.Equals(
                    signatureEnabled,
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var signatureText =
                await _settingsStore
                    .GetAccountSettingAsync(
                        account.AccountId,
                        SettingsKeys.ComposeSignaturePlainText);

            if (string.IsNullOrWhiteSpace(
                    signatureText))
            {
                return;
            }

            _newMessageSignatureText =
                signatureText;

            /*
             * Das ViewModel behält die vollständige
             * Klartextrepräsentation der Nachricht.
             *
             * Zwei Zeilenumbrüche bedeuten:
             *
             * Nachricht
             * <Leerzeile>
             * Signatur
             *
             * Der Rich-Text-Editor stellt diesen Inhalt
             * anschließend strukturiert dar, damit kein
             * führender Leerraum oberhalb der Nachricht
             * entsteht.
             */
            _viewModel.Body =
                Environment.NewLine +
                Environment.NewLine +
                signatureText;

            _viewModel.HtmlBody =
                null;

            _signatureAppliedToNewMessage =
                true;
        }
        catch
        {
            /*
             * Eine lokal nicht lesbare Signatur darf niemals
             * verhindern, dass eine neue E-Mail geschrieben
             * werden kann.
             *
             * Im Fehlerfall startet der Composer deshalb
             * einfach ohne Signatur.
             */
        }
    }
}