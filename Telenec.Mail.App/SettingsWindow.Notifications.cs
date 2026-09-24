using System.Windows;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App;

public partial class SettingsWindow
{
    private bool
        _notificationSettingsLoaded;

    private bool
        _isSavingNotificationSettings;

    private Guid?
        _notificationAccountId;

    protected override async void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(
            e);

        if (_notificationSettingsLoaded)
        {
            return;
        }

        await LoadNotificationSettingsAsync();
    }

    private async Task LoadNotificationSettingsAsync()
    {
        if (_notificationSettingsLoaded)
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
                NotificationAccountText.Text =
                    "Das aktuell angemeldete Telenec-Mail-Konto konnte nicht ermittelt werden.";

                NotificationStatusText.Text =
                    "Die Benachrichtigungseinstellungen können derzeit nicht bearbeitet werden.";

                return;
            }

            _notificationAccountId =
                account.AccountId;

            NotificationAccountText.Text =
                $"Benachrichtigungen für {account.EmailAddress}";

            var desktopNotificationsEnabled =
                await _settingsStore
                    .GetAccountSettingAsync(
                        account.AccountId,
                        SettingsKeys.NotificationsDesktopEnabled);

            var closeToTrayEnabled =
                await _settingsStore
                    .GetApplicationSettingAsync(
                        SettingsKeys.ApplicationCloseToTrayEnabled);

            var startWithWindowsEnabled =
                await _settingsStore
                    .GetApplicationSettingAsync(
                        SettingsKeys.ApplicationStartWithWindowsEnabled);

            /*
             * Desktop-Benachrichtigungen sind bei einer
             * frischen Installation standardmäßig aktiv.
             *
             * Tray und Autostart bleiben dagegen zunächst
             * bewusst deaktiviert, damit ein Update das
             * bisherige Programmverhalten nicht ungefragt
             * verändert.
             */
            DesktopNotificationsEnabledCheckBox.IsChecked =
                ReadStoredBoolean(
                    desktopNotificationsEnabled,
                    defaultValue:
                        true);

            CloseToTrayEnabledCheckBox.IsChecked =
                ReadStoredBoolean(
                    closeToTrayEnabled,
                    defaultValue:
                        false);

            StartWithWindowsEnabledCheckBox.IsChecked =
                ReadStoredBoolean(
                    startWithWindowsEnabled,
                    defaultValue:
                        false);

            _notificationSettingsLoaded =
                true;

            NotificationSettingsControls.IsEnabled =
                true;

            NotificationStatusText.Text =
                string.Empty;
        }
        catch
        {
            NotificationStatusText.Text =
                "Die Einstellungen konnten nicht geladen werden.";

            MessageBox.Show(
                this,
                "Die Benachrichtigungs- und Hintergrundeinstellungen konnten nicht geladen werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void NotificationSetting_OnChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (!_notificationSettingsLoaded ||
            _isSavingNotificationSettings)
        {
            return;
        }

        NotificationStatusText.Text =
            string.Empty;
    }

    private async void SaveNotificationSettingsButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSavingNotificationSettings ||
            !_notificationAccountId.HasValue)
        {
            return;
        }

        _isSavingNotificationSettings =
            true;

        NotificationSettingsControls.IsEnabled =
            false;

        NotificationStatusText.Text =
            "Wird gespeichert …";

        try
        {
            /*
             * Desktop-Benachrichtigungen sind kontobezogen.
             */
            await _settingsStore
                .SetAccountSettingAsync(
                    _notificationAccountId.Value,
                    SettingsKeys.NotificationsDesktopEnabled,
                    DesktopNotificationsEnabledCheckBox.IsChecked == true
                        ? "true"
                        : "false");

            /*
             * Tray und Autostart gelten für die gesamte
             * lokale Anwendung.
             */
            await _settingsStore
                .SetApplicationSettingAsync(
                    SettingsKeys.ApplicationCloseToTrayEnabled,
                    CloseToTrayEnabledCheckBox.IsChecked == true
                        ? "true"
                        : "false");

            await _settingsStore
                .SetApplicationSettingAsync(
                    SettingsKeys.ApplicationStartWithWindowsEnabled,
                    StartWithWindowsEnabledCheckBox.IsChecked == true
                        ? "true"
                        : "false");

            NotificationStatusText.Text =
                "Gespeichert.";
        }
        catch
        {
            NotificationStatusText.Text =
                "Speichern fehlgeschlagen.";

            MessageBox.Show(
                this,
                "Die Einstellungen konnten nicht gespeichert werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isSavingNotificationSettings =
                false;

            NotificationSettingsControls.IsEnabled =
                _notificationAccountId.HasValue;
        }
    }

    private static bool ReadStoredBoolean(
        string? value,
        bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return defaultValue;
        }

        return bool.TryParse(
                   value,
                   out var parsedValue)
            ? parsedValue
            : defaultValue;
    }
}