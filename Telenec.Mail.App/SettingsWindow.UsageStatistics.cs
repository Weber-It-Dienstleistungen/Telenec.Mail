using System.Windows;
using Telenec.Mail.App.Services.UsageStatistics;

namespace Telenec.Mail.App;

public partial class SettingsWindow
{
    private UsageStatisticsSettingsService?
        _usageStatisticsSettingsService;

    private bool
        _usageStatisticsSettingsLoaded;

    private bool
        _isSavingUsageStatisticsSettings;

    private UsageStatisticsSettingsService
        UsageStatisticsSettingsService =>
            _usageStatisticsSettingsService ??=
                new UsageStatisticsSettingsService(
                    _settingsStore);

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

            UsageStatisticsEnabledCheckBox.IsChecked =
                consentStatus ==
                UsageStatisticsConsentStatus.Granted;

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
                 * Erst durch diese ausdrücklich bestätigte
                 * Einstellung wird eine zufällige
                 * Installations-ID erzeugt.
                 */
                await UsageStatisticsSettingsService
                    .GrantConsentAsync();

                UsageStatisticsStatusText.Text =
                    "Teilnahme ist aktiviert.";
            }
            else
            {
                /*
                 * Ablehnung und Widerruf entfernen die lokal
                 * gespeicherte Installations-ID.
                 *
                 * Solange noch kein Statistik-Backend
                 * angebunden ist, existiert zusätzlich kein
                 * serverseitiger Datensatz, der gelöscht
                 * werden müsste.
                 */
                await UsageStatisticsSettingsService
                    .DeclineConsentAsync();

                UsageStatisticsStatusText.Text =
                    "Teilnahme ist deaktiviert.";
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