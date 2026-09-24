using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App;

public partial class SettingsWindow : Window
{
    private const string SignatureEnabledSettingKey =
        "Compose.Signature.Enabled";

    private const string SignaturePlainTextSettingKey =
        "Compose.Signature.PlainText";

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
                        SignaturePlainTextSettingKey);

            var signatureEnabled =
                await _settingsStore
                    .GetAccountSettingAsync(
                        account.AccountId,
                        SignatureEnabledSettingKey);

            SignatureTextBox.Text =
                signatureText
                ?? string.Empty;

            SignatureEnabledCheckBox.IsChecked =
                string.Equals(
                    signatureEnabled,
                    "true",
                    StringComparison.OrdinalIgnoreCase);

            _signatureSettingsLoaded =
                true;

            SignatureStatusText.Text =
                string.Empty;

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

    private void SignatureTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ClearSignatureSaveStatus();
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
            string.Empty;
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
            /*
             * Zuerst wird der Signaturtext gespeichert.
             *
             * Erst danach schreiben wir den Aktiv-Status.
             * Sollte der erste Schreibvorgang fehlschlagen,
             * kann dadurch nicht versehentlich eine noch nicht
             * gespeicherte Signatur aktiviert werden.
             */
            await _settingsStore
                .SetAccountSettingAsync(
                    _activeAccountId.Value,
                    SignaturePlainTextSettingKey,
                    SignatureTextBox.Text);

            await _settingsStore
                .SetAccountSettingAsync(
                    _activeAccountId.Value,
                    SignatureEnabledSettingKey,
                    SignatureEnabledCheckBox.IsChecked == true
                        ? "true"
                        : "false");

            SignatureStatusText.Text =
                "Gespeichert.";
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