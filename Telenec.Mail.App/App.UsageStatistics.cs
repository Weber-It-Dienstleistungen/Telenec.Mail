using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Windows;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.Services.UsageStatistics;

namespace Telenec.Mail.App;

public partial class App
{
    internal async Task
        ShowUsageStatisticsConsentIfNeededAsync(
            Window owner)
    {
        ArgumentNullException.ThrowIfNull(
            owner);

        UsageStatisticsSettingsService
            usageStatisticsSettingsService;

        UsageStatisticsConsentStatus
            consentStatus;

        try
        {
            var settingsStore =
                _host.Services
                    .GetRequiredService<
                        ISettingsStore>();

            usageStatisticsSettingsService =
                new UsageStatisticsSettingsService(
                    settingsStore);

            consentStatus =
                await usageStatisticsSettingsService
                    .GetConsentStatusAsync();
        }
        catch (Exception exception)
        {
            /*
             * Kann der lokale Zustimmungsstatus nicht sicher
             * ermittelt werden, wird kein Dialog erzwungen und
             * insbesondere keine Zustimmung angenommen.
             *
             * Spätere Statistikübertragungen dürfen ohnehin
             * ausschließlich bei eindeutigem Status Granted
             * stattfinden.
             */
            Trace.WriteLine(
                $"Could not load usage statistics consent state: {exception}");

            return;
        }

        if (consentStatus !=
            UsageStatisticsConsentStatus.Unknown)
        {
            return;
        }

        var consentWindow =
            new UsageStatisticsConsentWindow
            {
                Owner =
                    owner
            };

        var dialogResult =
            consentWindow.ShowDialog();

        var consentGranted =
            dialogResult == true &&
            consentWindow.ConsentGranted;

        try
        {
            if (consentGranted)
            {
                /*
                 * Nur eine ausdrücklich positive Entscheidung
                 * darf die zufällige Installations-ID erzeugen.
                 */
                await usageStatisticsSettingsService
                    .GrantConsentAsync();
            }
            else
            {
                /*
                 * "Nicht teilnehmen", Escape und das Schließen
                 * des Dialogs über X werden gleichermaßen als
                 * Nicht-Zustimmung behandelt.
                 *
                 * Dadurch entsteht in keinem dieser Fälle eine
                 * Installations-ID und die Frage erscheint beim
                 * nächsten Start nicht erneut.
                 */
                await usageStatisticsSettingsService
                    .DeclineConsentAsync();
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine(
                $"Could not save usage statistics consent state: {exception}");

            MessageBox.Show(
                owner,
                "Ihre Entscheidung zur freiwilligen Nutzungsstatistik konnte nicht gespeichert werden.\n\n" +
                "Bis zur erfolgreichen Speicherung werden keine Daten für die Nutzungsstatistik verwendet.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}