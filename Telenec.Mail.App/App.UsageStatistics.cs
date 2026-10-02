using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Windows;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.Services.UsageStatistics;

namespace Telenec.Mail.App;

public partial class App
{
    private CancellationTokenSource?
        _usageStatisticsHeartbeatCancellation;

    private Task?
        _usageStatisticsHeartbeatTask;

    private bool
        _usageStatisticsExitHandlerRegistered;

    internal async Task
        ShowUsageStatisticsConsentIfNeededAsync(
            Window owner)
    {
        ArgumentNullException.ThrowIfNull(
            owner);

        UsageStatisticsSettingsService
            usageStatisticsSettingsService;

        UsageStatisticsHeartbeatService
            heartbeatService;

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

            heartbeatService =
                new UsageStatisticsHeartbeatService(
                    usageStatisticsSettingsService,
                    new UsageStatisticsApiClient());

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
             */
            Trace.WriteLine(
                $"Could not load usage statistics consent state: {exception}");

            return;
        }

        /*
         * Bereits erteilte Zustimmung:
         *
         * Bei jedem normalen Programmstart wird unmittelbar
         * ein Heartbeat gesendet.
         *
         * Danach hält ein sechs-stündlicher Hintergrundlauf
         * LastSeen auch bei über mehrere Tage geöffneten
         * Clients aktuell.
         */
        if (consentStatus ==
            UsageStatisticsConsentStatus.Granted)
        {
            await heartbeatService
                .TrySendHeartbeatAsync();

            EnsureUsageStatisticsHeartbeatLoopStarted(
                heartbeatService);

            return;
        }

        if (consentStatus ==
            UsageStatisticsConsentStatus.Declined)
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
                 * Erst die ausdrückliche positive Entscheidung
                 * erzeugt bzw. aktiviert die Installations-ID.
                 */
                await usageStatisticsSettingsService
                    .GrantConsentAsync();

                /*
                 * Direkt nach der Zustimmung wird der erste
                 * Heartbeat gesendet.
                 *
                 * Ein Fehler hierbei ändert die gespeicherte
                 * Einwilligung nicht und beeinträchtigt den
                 * Mailclient nicht.
                 */
                await heartbeatService
                    .TrySendHeartbeatAsync();

                EnsureUsageStatisticsHeartbeatLoopStarted(
                    heartbeatService);
            }
            else
            {
                /*
                 * "Nicht teilnehmen", Escape und das Schließen
                 * über X werden gleichermaßen als
                 * Nicht-Zustimmung behandelt.
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

    private void EnsureUsageStatisticsHeartbeatLoopStarted(
        UsageStatisticsHeartbeatService heartbeatService)
    {
        if (_usageStatisticsHeartbeatTask is
            {
                IsCompleted: false
            })
        {
            return;
        }

        _usageStatisticsHeartbeatCancellation?
            .Dispose();

        _usageStatisticsHeartbeatCancellation =
            new CancellationTokenSource();

        _usageStatisticsHeartbeatTask =
            heartbeatService
                .RunPeriodicHeartbeatAsync(
                    _usageStatisticsHeartbeatCancellation.Token);

        /*
         * Der Exit-Handler wird genau einmal registriert.
         *
         * Dadurch wird der Timer beim normalen Programmende
         * beendet, ohne den bestehenden App.OnExit-Code
         * verändern zu müssen.
         */
        if (!_usageStatisticsExitHandlerRegistered)
        {
            Exit +=
                (_, _) =>
                {
                    try
                    {
                        _usageStatisticsHeartbeatCancellation?
                            .Cancel();
                    }
                    catch
                    {
                    }

                    _usageStatisticsHeartbeatCancellation?
                        .Dispose();

                    _usageStatisticsHeartbeatCancellation =
                        null;
                };

            _usageStatisticsExitHandlerRegistered =
                true;
        }
    }
}