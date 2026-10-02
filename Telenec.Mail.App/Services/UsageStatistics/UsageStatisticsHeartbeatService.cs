using System.Diagnostics;
using System.Reflection;

namespace Telenec.Mail.App.Services.UsageStatistics;

public sealed class UsageStatisticsHeartbeatService
{
    public const int CurrentConsentVersion =
        1;

    private static readonly TimeSpan HeartbeatInterval =
        TimeSpan.FromHours(6);

    private readonly UsageStatisticsSettingsService
        _settingsService;

    private readonly UsageStatisticsApiClient
        _apiClient;

    public UsageStatisticsHeartbeatService(
        UsageStatisticsSettingsService settingsService,
        UsageStatisticsApiClient apiClient)
    {
        _settingsService =
            settingsService;

        _apiClient =
            apiClient;
    }

    public async Task<bool> TrySendHeartbeatAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var installationId =
                await _settingsService
                    .GetInstallationIdAsync(
                        cancellationToken);

            /*
             * GetInstallationIdAsync gibt ausschließlich bei
             * eindeutig erteilter Einwilligung eine ID zurück.
             *
             * Damit kann diese Übertragung nicht versehentlich
             * bei Unknown oder Declined stattfinden.
             */
            if (!installationId.HasValue)
            {
                return false;
            }

            await _apiClient
                .SendHeartbeatAsync(
                    installationId.Value,
                    GetApplicationVersion(),
                    CurrentConsentVersion,
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
            /*
             * Die Nutzungsstatistik ist eine optionale
             * Hintergrundfunktion.
             *
             * Netzwerk-, Server- oder lokale Fehler dürfen
             * niemals verhindern, dass Telenec Mail normal
             * verwendet werden kann.
             */
            Trace.WriteLine(
                $"Usage statistics heartbeat failed: {exception}");

            return false;
        }
    }

    public async Task RunPeriodicHeartbeatAsync(
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(
                HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(
                       cancellationToken))
            {
                await TrySendHeartbeatAsync(
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            /*
             * Normales Programmende.
             */
        }
    }

    private static string GetApplicationVersion()
    {
        var assembly =
            typeof(UsageStatisticsHeartbeatService)
                .Assembly;

        var informationalVersion =
            assembly
                .GetCustomAttribute<
                    AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(
                informationalVersion))
        {
            var metadataSeparatorIndex =
                informationalVersion.IndexOf(
                    '+');

            if (metadataSeparatorIndex >= 0)
            {
                informationalVersion =
                    informationalVersion[
                        ..metadataSeparatorIndex];
            }

            return informationalVersion;
        }

        return assembly
                   .GetName()
                   .Version?
                   .ToString()
               ?? "0.0.0";
    }
}