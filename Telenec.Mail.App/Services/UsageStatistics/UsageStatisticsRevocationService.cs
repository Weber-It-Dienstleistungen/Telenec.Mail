using System.Diagnostics;

namespace Telenec.Mail.App.Services.UsageStatistics;

public sealed class UsageStatisticsRevocationService
{
    private readonly UsageStatisticsSettingsService
        _settingsService;

    private readonly UsageStatisticsApiClient
        _apiClient;

    public UsageStatisticsRevocationService(
        UsageStatisticsSettingsService settingsService,
        UsageStatisticsApiClient apiClient)
    {
        _settingsService =
            settingsService;

        _apiClient =
            apiClient;
    }

    public async Task<bool> RevokeConsentAsync(
        CancellationToken cancellationToken = default)
    {
        var pendingInstallationId =
            await _settingsService
                .GetPendingRevokeInstallationIdAsync(
                    cancellationToken);

        /*
         * Existiert noch kein Pending-Revoke, wird die
         * aktuell aktive ID zunächst sicher dorthin
         * verschoben und die lokale Teilnahme deaktiviert.
         */
        if (!pendingInstallationId.HasValue)
        {
            await _settingsService
                .PrepareRevokeAsync(
                    cancellationToken);
        }

        return await TryProcessPendingRevokeAsync(
            cancellationToken);
    }

    public async Task<bool> TryProcessPendingRevokeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingInstallationId =
                await _settingsService
                    .GetPendingRevokeInstallationIdAsync(
                        cancellationToken);

            if (!pendingInstallationId.HasValue)
            {
                return true;
            }

            /*
             * Ein vorhandener Pending-Marker ist maßgeblich:
             *
             * Selbst nach einem Prozessabbruch mitten im
             * ursprünglichen Widerruf wird die lokale
             * Teilnahme zuerst sicher deaktiviert.
             */
            await _settingsService
                .DeclineConsentAsync(
                    cancellationToken);

            await _apiClient
                .SendRevokeAsync(
                    pendingInstallationId.Value,
                    cancellationToken);

            /*
             * Erst nach erfolgreicher Serverantwort wird der
             * lokale Wiederholungsmarker entfernt.
             *
             * Schlägt dieses Löschen lokal fehl, wird der
             * idempotente Server-Revoke beim nächsten Start
             * einfach erneut ausgeführt.
             */
            await _settingsService
                .CompletePendingRevokeAsync(
                    pendingInstallationId.Value,
                    cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.WriteLine(
                $"Usage statistics revoke failed: {exception}");

            return false;
        }
    }
}