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

        UsageStatisticsConsentStatus
            consentStatus;

        Guid?
            pendingRevokeInstallationId;

        try
        {
            var settingsStore =
                _host.Services
                    .GetRequiredService<
                        ISettingsStore>();

            usageStatisticsSettingsService =
                new UsageStatisticsSettingsService(
                    settingsStore);

            var apiClient =
                new UsageStatisticsApiClient();

            var revocationService =
                new UsageStatisticsRevocationService(
                    usageStatisticsSettingsService,
                    apiClient);

            /*
             * Ein eventuell früher fehlgeschlagener Widerruf
             * wird bei jedem normalen Programmstart erneut
             * versucht.
             *
             * Netzwerkfehler blockieren den Programmstart
             * nicht.
             */
            await revocationService
                .TryProcessPendingRevokeAsync();

            pendingRevokeInstallationId =
                await usageStatisticsSettingsService
                    .GetPendingRevokeInstallationIdAsync();

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
         * Solange ein Widerruf noch aussteht, darf unabhängig
         * von einem eventuell inkonsistenten älteren
         * Zustimmungswert kein neuer Heartbeat gesendet werden.
         */
        if (pendingRevokeInstallationId.HasValue)
        {
            return;
        }

        if (consentStatus ==
            UsageStatisticsConsentStatus.Granted)
        {
            await StartUsageStatisticsHeartbeatIfEnabledAsync();

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

                await StartUsageStatisticsHeartbeatIfEnabledAsync();
            }
            else
            {
                /*
                 * "Nicht teilnehmen", Escape und das Schließen
                 * über X werden gleichermaßen als
                 * Nicht-Zustimmung behandelt.
                 *
                 * Da noch keine Statistikübertragung
                 * stattgefunden hat, ist hier kein
                 * serverseitiger Revoke erforderlich.
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

    internal async Task
        StartUsageStatisticsHeartbeatIfEnabledAsync()
    {
        try
        {
            var settingsStore =
                _host.Services
                    .GetRequiredService<
                        ISettingsStore>();

            var settingsService =
                new UsageStatisticsSettingsService(
                    settingsStore);

            var pendingRevokeInstallationId =
                await settingsService
                    .GetPendingRevokeInstallationIdAsync();

            /*
             * Ein ausstehender Widerruf sperrt jeden neuen
             * Heartbeat, bis er abgeschlossen wurde.
             */
            if (pendingRevokeInstallationId.HasValue)
            {
                return;
            }

            var heartbeatService =
                new UsageStatisticsHeartbeatService(
                    settingsService,
                    new UsageStatisticsApiClient());

            await heartbeatService
                .TrySendHeartbeatAsync();

            EnsureUsageStatisticsHeartbeatLoopStarted(
                heartbeatService);
        }
        catch (Exception exception)
        {
            /*
             * Auch Fehler beim Start der optionalen
             * Statistikfunktion dürfen den Mailclient
             * niemals beeinträchtigen.
             */
            Trace.WriteLine(
                $"Could not start usage statistics heartbeat: {exception}");
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