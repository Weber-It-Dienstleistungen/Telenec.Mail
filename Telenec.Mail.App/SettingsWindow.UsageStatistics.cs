using System.Windows;
using Telenec.Mail.App.Services.UsageStatistics;

namespace Telenec.Mail.App;

public partial class SettingsWindow
{
    private UsageStatisticsSettingsService?
        _usageStatisticsSettingsService;

    private UsageStatisticsApiClient?
        _usageStatisticsApiClient;

    private UsageStatisticsRevocationService?
        _usageStatisticsRevocationService;

    private bool
        _usageStatisticsSettingsLoaded;

    private bool
        _isSavingUsageStatisticsSettings;

    private UsageStatisticsSettingsService
        UsageStatisticsSettingsService =>
            _usageStatisticsSettingsService ??=
                new UsageStatisticsSettingsService(
                    _settingsStore);

    private UsageStatisticsApiClient
        UsageStatisticsApiClient =>
            _usageStatisticsApiClient ??=
                new UsageStatisticsApiClient();

    private UsageStatisticsRevocationService
        UsageStatisticsRevocationService =>
            _usageStatisticsRevocationService ??=
                new UsageStatisticsRevocationService(
                    UsageStatisticsSettingsService,
                    UsageStatisticsApiClient);

    private async void
        UsageStatisticsSettingsCard_OnLoaded(
            object sender,
            RoutedEventArgs e)
    {
        if (_usageStatisticsSettingsLoaded)
        {
            return;
        }

        await LoadUsageStatisticsSettingsAsync();
    }

    private async Task
        LoadUsageStatisticsSettingsAsync()
    {
        if (_usageStatisticsSettingsLoaded)
        {
            return;
        }

        try
        {
            var consentStatus =
                await UsageStatisticsSettingsService
                    .GetConsentStatusAsync();

            var pendingRevokeInstallationId =
                await UsageStatisticsSettingsService
                    .GetPendingRevokeInstallationIdAsync();

            UsageStatisticsEnabledCheckBox.IsChecked =
                consentStatus ==
                UsageStatisticsConsentStatus.Granted &&
                !pendingRevokeInstallationId.HasValue;

            if (pendingRevokeInstallationId.HasValue)
            {
                UsageStatisticsStatusText.Text =
                    "Teilnahme ist deaktiviert. Die serverseitige Löschung wird automatisch nachgeholt.";
            }
            else
            {
                UsageStatisticsStatusText.Text =
                    consentStatus switch
                    {
                        UsageStatisticsConsentStatus.Granted =>
                            "Teilnahme ist aktiviert.",

                        UsageStatisticsConsentStatus.Declined =>
                            "Teilnahme ist deaktiviert.",

                        _ =>
                            "Noch keine Entscheidung gespeichert."
                    };
            }

            _usageStatisticsSettingsLoaded =
                true;

            UsageStatisticsSettingsControls.IsEnabled =
                true;
        }
        catch
        {
            UsageStatisticsStatusText.Text =
                "Die Einstellung konnte nicht geladen werden.";

            MessageBox.Show(
                this,
                "Die Einstellung für die freiwillige Nutzungsstatistik konnte nicht geladen werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UsageStatisticsSetting_OnChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (!_usageStatisticsSettingsLoaded ||
            _isSavingUsageStatisticsSettings)
        {
            return;
        }

        UsageStatisticsStatusText.Text =
            string.Empty;
    }

    private async void
        SaveUsageStatisticsSettingsButton_OnClick(
            object sender,
            RoutedEventArgs e)
    {
        if (!_usageStatisticsSettingsLoaded ||
            _isSavingUsageStatisticsSettings)
        {
            return;
        }

        _isSavingUsageStatisticsSettings =
            true;

        UsageStatisticsSettingsControls.IsEnabled =
            false;

        UsageStatisticsStatusText.Text =
            "Wird gespeichert …";

        try
        {
            if (UsageStatisticsEnabledCheckBox.IsChecked ==
                true)
            {
                /*
                 * Ein eventuell älterer, noch ausstehender
                 * Widerruf wird vor einer erneuten Zustimmung
                 * zuerst verarbeitet.
                 */
                await UsageStatisticsRevocationService
                    .TryProcessPendingRevokeAsync();

                var pendingRevokeInstallationId =
                    await UsageStatisticsSettingsService
                        .GetPendingRevokeInstallationIdAsync();

                if (pendingRevokeInstallationId.HasValue)
                {
                    UsageStatisticsEnabledCheckBox.IsChecked =
                        false;

                    UsageStatisticsStatusText.Text =
                        "Die frühere serverseitige Löschung konnte noch nicht abgeschlossen werden.";

                    MessageBox.Show(
                        this,
                        "Die Nutzungsstatistik kann erst wieder aktiviert werden, nachdem der vorherige Widerruf serverseitig abgeschlossen wurde.\n\n" +
                        "Bitte prüfen Sie die Internetverbindung und versuchen Sie es später erneut.",
                        "Telenec Mail",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                await UsageStatisticsSettingsService
                    .GrantConsentAsync();

                /*
                 * Auch bei einer erneuten Aktivierung über die
                 * Einstellungen wird unmittelbar ein
                 * Heartbeat gesendet und der periodische Lauf
                 * für die aktuelle Programmsitzung gestartet.
                 */
                if (Application.Current is App app)
                {
                    await app
                        .StartUsageStatisticsHeartbeatIfEnabledAsync();
                }

                UsageStatisticsStatusText.Text =
                    "Teilnahme ist aktiviert.";
            }
            else
            {
                /*
                 * Die lokale Teilnahme wird sofort deaktiviert.
                 *
                 * Die bisherige Installations-ID bleibt nur in
                 * einem lokalen Pending-Revoke-Marker erhalten,
                 * bis der Server die Löschung bestätigt hat.
                 */
                var revokeCompleted =
                    await UsageStatisticsRevocationService
                        .RevokeConsentAsync();

                UsageStatisticsStatusText.Text =
                    revokeCompleted
                        ? "Teilnahme ist deaktiviert."
                        : "Teilnahme ist deaktiviert. Die serverseitige Löschung wird automatisch nachgeholt.";
            }
        }
        catch (Exception exception)
        {
            UsageStatisticsStatusText.Text =
                "Speichern fehlgeschlagen.";

            MessageBox.Show(
                this,
                "Die Einstellung für die freiwillige Nutzungsstatistik konnte nicht gespeichert werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isSavingUsageStatisticsSettings =
                false;

            UsageStatisticsSettingsControls.IsEnabled =
                true;
        }
    }
}