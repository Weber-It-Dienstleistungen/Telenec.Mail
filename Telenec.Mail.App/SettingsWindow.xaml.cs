using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Controls;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App;

public partial class SettingsWindow : Window
{
    private readonly ISettingsStore
        _settingsStore;

    private readonly IMailAccountStore
        _mailAccountStore;

    private Guid?
        _activeAccountId;

    private bool
        _signatureSettingsLoaded;

    private bool
        _isSavingSignatureSettings;

    private bool
        _signatureRichTextAvailable;

    public SettingsWindow(
        ISettingsStore settingsStore,
        IMailAccountStore mailAccountStore)
    {
        _settingsStore =
            settingsStore;

        _mailAccountStore =
            mailAccountStore;

        InitializeComponent();
    }

    private async void SettingsWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_signatureSettingsLoaded)
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
                SignatureAccountText.Text =
                    "Das aktuell angemeldete Telenec-Mail-Konto konnte nicht ermittelt werden.";

                SignatureStatusText.Text =
                    "Die Signatur kann derzeit nicht bearbeitet werden.";

                return;
            }

            _activeAccountId =
                account.AccountId;

            SignatureAccountText.Text =
                $"Signatur für {account.EmailAddress}";

            var signatureText =
                await _settingsStore
                    .GetAccountSettingAsync(
                        account.AccountId,
                        SettingsKeys.ComposeSignaturePlainText);

            var signatureHtml =
                await _settingsStore
                    .GetAccountSettingAsync(
                        account.AccountId,
                        SettingsKeys.ComposeSignatureHtml);

            var signatureEnabled =
                await _settingsStore
                    .GetAccountSettingAsync(
                        account.AccountId,
                        SettingsKeys.ComposeSignatureEnabled);

            var normalizedSignatureText =
                signatureText
                ?? string.Empty;

            SignatureFallbackTextBox.Text =
                normalizedSignatureText;

            SignatureEnabledCheckBox.IsChecked =
                string.Equals(
                    signatureEnabled,
                    "true",
                    StringComparison.OrdinalIgnoreCase);

            try
            {
                await SignatureHtmlEditor
                    .EnsureInitializedAsync();

                await SignatureHtmlEditor
                    .SetContentAsync(
                        normalizedSignatureText,
                        signatureHtml);

                _signatureRichTextAvailable =
                    true;

                SignatureHtmlEditor.Visibility =
                    Visibility.Visible;

                SignatureFallbackTextBox.Visibility =
                    Visibility.Collapsed;

                SignatureFormattingToolbar.Visibility =
                    Visibility.Visible;

                SignatureStatusText.Text =
                    string.Empty;
            }
            catch
            {
                /*
                 * Die Signaturverwaltung soll auch dann
                 * benutzbar bleiben, wenn WebView2 auf einem
                 * einzelnen Kundensystem nicht gestartet
                 * werden kann.
                 *
                 * In diesem Fall fällt ausschließlich der
                 * Signatureditor auf Klartext zurück.
                 */
                _signatureRichTextAvailable =
                    false;

                SignatureHtmlEditor.Visibility =
                    Visibility.Collapsed;

                SignatureFallbackTextBox.Visibility =
                    Visibility.Visible;

                SignatureFormattingToolbar.Visibility =
                    Visibility.Collapsed;

                SignatureStatusText.Text =
                    "Formatierter Editor nicht verfügbar – Klartextbearbeitung aktiv.";
            }

            _signatureSettingsLoaded =
                true;

            SignatureSettingsControls.IsEnabled =
                true;
        }
        catch
        {
            SignatureAccountText.Text =
                "Die Signatur-Einstellungen konnten nicht geladen werden.";

            SignatureStatusText.Text =
                "Bitte schließen Sie die Einstellungen und versuchen Sie es erneut.";

            MessageBox.Show(
                this,
                "Die Signatur-Einstellungen konnten nicht geladen werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SettingsNavigation_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        /*
         * SelectionChanged kann bereits während
         * InitializeComponent ausgelöst werden.
         *
         * Zu diesem Zeitpunkt sind möglicherweise noch nicht
         * alle benannten Elemente des Fensters erzeugt.
         */
        if (GeneralSettingsPanel is null ||
            ComposeSettingsPanel is null ||
            NotificationSettingsPanel is null)
        {
            return;
        }

        GeneralSettingsPanel.Visibility =
            SettingsNavigation.SelectedIndex == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        ComposeSettingsPanel.Visibility =
            SettingsNavigation.SelectedIndex == 1
                ? Visibility.Visible
                : Visibility.Collapsed;

        NotificationSettingsPanel.Visibility =
            SettingsNavigation.SelectedIndex == 2
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void SignatureSetting_OnChanged(
        object sender,
        RoutedEventArgs e)
    {
        ClearSignatureSaveStatus();
    }

    private void SignatureHtmlEditor_OnContentChanged(
        object? sender,
        ComposeHtmlEditorContentChangedEventArgs e)
    {
        ClearSignatureSaveStatus();
    }

    private void SignatureFallbackTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ClearSignatureSaveStatus();
    }

    private async void SignatureRichTextCommandButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.Tag is not string command ||
            string.IsNullOrWhiteSpace(
                command))
        {
            return;
        }

        await ExecuteSignatureRichTextCommandAsync(
            command);
    }

    private async void SignatureRichTextCommandComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_signatureRichTextAvailable ||
            sender is not ComboBox comboBox ||
            comboBox.Tag is not string command ||
            comboBox.SelectedItem
                is not ComboBoxItem selectedItem ||
            selectedItem.Tag is not string commandValue ||
            string.IsNullOrWhiteSpace(
                commandValue))
        {
            return;
        }

        /*
         * Wie beim Composer springt die Auswahl danach
         * wieder auf den neutralen Platzhalter zurück.
         */
        comboBox.SelectedIndex =
            0;

        await ExecuteSignatureRichTextCommandAsync(
            command,
            commandValue);
    }

    private async Task ExecuteSignatureRichTextCommandAsync(
        string command,
        string? value = null)
    {
        if (!_signatureRichTextAvailable ||
            _isSavingSignatureSettings)
        {
            return;
        }

        try
        {
            await SignatureHtmlEditor
                .ExecuteCommandAsync(
                    command,
                    value);
        }
        catch
        {
            MessageBox.Show(
                this,
                "Die gewünschte Signaturformatierung konnte nicht angewendet werden.",
                "Formatierung nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ClearSignatureSaveStatus()
    {
        /*
         * Während des initialen Ladens lösen das Setzen
         * von Text und Checkbox selbst Änderungsereignisse aus.
         *
         * Erst nach vollständig geladenen Einstellungen
         * darf eine Benutzeränderung den Status zurücksetzen.
         */
        if (!_signatureSettingsLoaded ||
            _isSavingSignatureSettings)
        {
            return;
        }

        SignatureStatusText.Text =
            _signatureRichTextAvailable
                ? string.Empty
                : "Formatierter Editor nicht verfügbar – Klartextbearbeitung aktiv.";
    }

    private async void SaveSignatureButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSavingSignatureSettings ||
            !_activeAccountId.HasValue)
        {
            return;
        }

        _isSavingSignatureSettings =
            true;

        SignatureSettingsControls.IsEnabled =
            false;

        SignatureStatusText.Text =
            "Wird gespeichert …";

        try
        {
            string signaturePlainText;
            string signatureHtml;

            if (_signatureRichTextAvailable)
            {
                var content =
                    await SignatureHtmlEditor
                        .GetContentAsync();

                signaturePlainText =
                    content.PlainText
                    ?? string.Empty;

                signatureHtml =
                    content.HtmlBody
                    ?? string.Empty;
            }
            else
            {
                /*
                 * Im Fallbackmodus wird bewusst nur Klartext
                 * gespeichert.
                 *
                 * Eine eventuell ältere HTML-Version wird
                 * dabei geleert, damit Text und HTML nicht
                 * voneinander abweichen.
                 */
                signaturePlainText =
                    SignatureFallbackTextBox.Text;

                signatureHtml =
                    string.Empty;
            }

            /*
             * Klartext bleibt immer vorhanden.
             *
             * Er dient sowohl als Fallback als auch als
             * text/plain-Version einer versendeten E-Mail.
             */
            await _settingsStore
                .SetAccountSettingAsync(
                    _activeAccountId.Value,
                    SettingsKeys.ComposeSignaturePlainText,
                    signaturePlainText);

            await _settingsStore
                .SetAccountSettingAsync(
                    _activeAccountId.Value,
                    SettingsKeys.ComposeSignatureHtml,
                    signatureHtml);

            /*
             * Der Aktiv-Status wird bewusst zuletzt
             * geschrieben.
             *
             * Damit kann nicht versehentlich eine nur
             * teilweise gespeicherte Signatur aktiviert
             * werden.
             */
            await _settingsStore
                .SetAccountSettingAsync(
                    _activeAccountId.Value,
                    SettingsKeys.ComposeSignatureEnabled,
                    SignatureEnabledCheckBox.IsChecked == true
                        ? "true"
                        : "false");

            SignatureStatusText.Text =
                _signatureRichTextAvailable
                    ? "Gespeichert."
                    : "Gespeichert (Klartextmodus).";
        }
        catch
        {
            SignatureStatusText.Text =
                "Speichern fehlgeschlagen.";

            MessageBox.Show(
                this,
                "Die Signatur konnte nicht gespeichert werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isSavingSignatureSettings =
                false;

            SignatureSettingsControls.IsEnabled =
                _activeAccountId.HasValue;
        }
    }

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}