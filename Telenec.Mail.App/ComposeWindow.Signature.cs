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
        _signatureApplied;

    private string
        _signatureText =
            string.Empty;

    private string?
        _signatureFollowingPlainText;

    private Task?
        _signaturePreparationTask;

    private Task EnsureSignaturePreparedAsync()
    {
        /*
         * Bestehende Entwürfe enthalten bereits ihren
         * vollständigen Nachrichtentext.
         *
         * Sie dürfen deshalb niemals beim erneuten Öffnen
         * automatisch eine weitere Signatur erhalten.
         */
        if (_viewModel.IsEditingDraft)
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
         * Neue Nachrichten dürfen nur dann automatisch
         * vorbereitet werden, wenn ihr Body tatsächlich
         * noch leer ist.
         *
         * Antworten und Weiterleitungen besitzen dagegen
         * bereits den automatisch erzeugten Original- bzw.
         * Zitatbereich.
         */
        if (_isNewMessage)
        {
            if (!string.IsNullOrEmpty(
                    _viewModel.Body) ||
                !string.IsNullOrWhiteSpace(
                    _viewModel.HtmlBody))
            {
                return;
            }
        }
        else
        {
            /*
             * Antworten und Weiterleitungen werden aktuell
             * bewusst als Klartext aufgebaut.
             *
             * Sollte dieser Bereich später selbst HTML
             * enthalten, greifen wir hier defensiv nicht ein,
             * bis dafür eine eigene HTML-Signaturlogik
             * implementiert wurde.
             */
            if (!string.IsNullOrWhiteSpace(
                    _viewModel.HtmlBody))
            {
                return;
            }
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

            _signatureText =
                signatureText;

            if (_isNewMessage)
            {
                _signatureFollowingPlainText =
                    null;
            }
            else
            {
                /*
                 * Antworten und Weiterleitungen besitzen
                 * bereits zwei freie Anfangszeilen als
                 * Schreibbereich.
                 *
                 * Diese werden entfernt und anschließend
                 * strukturiert neu aufgebaut:
                 *
                 * Schreibbereich
                 * Leerzeile
                 * Signatur
                 * Leerzeile
                 * Original / Weiterleitung
                 */
                var existingBody =
                    _viewModel.Body
                    ?? string.Empty;

                _signatureFollowingPlainText =
                    existingBody
                        .TrimStart(
                            '\r',
                            '\n');
            }

            _viewModel.Body =
                CreateSignaturePlainTextBody(
                    _signatureText,
                    _signatureFollowingPlainText);

            _viewModel.HtmlBody =
                null;

            _signatureApplied =
                true;
        }
        catch
        {
            /*
             * Eine lokal nicht lesbare Signatur darf niemals
             * verhindern, dass eine E-Mail geschrieben,
             * beantwortet oder weitergeleitet werden kann.
             *
             * Im Fehlerfall bleibt deshalb einfach der
             * bisherige Nachrichtentext erhalten.
             */
        }
    }

    private static string CreateSignaturePlainTextBody(
        string signatureText,
        string? followingPlainText)
    {
        var result =
            Environment.NewLine +
            Environment.NewLine +
            signatureText;

        if (string.IsNullOrEmpty(
                followingPlainText))
        {
            return result;
        }

        return
            result +
            Environment.NewLine +
            Environment.NewLine +
            followingPlainText;
    }
}