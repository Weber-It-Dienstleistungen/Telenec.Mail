using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.UsageStatistics;

public sealed class UsageStatisticsSettingsService
{
    private const string ConsentGrantedValue =
        "granted";

    private const string ConsentDeclinedValue =
        "declined";

    private readonly ISettingsStore
        _settingsStore;

    public UsageStatisticsSettingsService(
        ISettingsStore settingsStore)
    {
        _settingsStore =
            settingsStore;
    }

    public async Task<UsageStatisticsConsentStatus>
        GetConsentStatusAsync(
            CancellationToken cancellationToken = default)
    {
        var storedValue =
            await _settingsStore
                .GetApplicationSettingAsync(
                    SettingsKeys
                        .UsageStatisticsConsentStatus,
                    cancellationToken);

        if (string.Equals(
                storedValue,
                ConsentGrantedValue,
                StringComparison.OrdinalIgnoreCase))
        {
            return UsageStatisticsConsentStatus
                .Granted;
        }

        if (string.Equals(
                storedValue,
                ConsentDeclinedValue,
                StringComparison.OrdinalIgnoreCase))
        {
            return UsageStatisticsConsentStatus
                .Declined;
        }

        /*
         * Ein fehlender oder unbekannter Wert wird bewusst
         * nicht als Zustimmung interpretiert.
         *
         * Damit bleibt das Verhalten auch bei beschädigten
         * oder manuell veränderten Programmeinstellungen
         * datenschutzfreundlich:
         *
         * Keine eindeutige Zustimmung = keine Statistik.
         */
        return UsageStatisticsConsentStatus
            .Unknown;
    }

    public async Task<Guid?> GetInstallationIdAsync(
        CancellationToken cancellationToken = default)
    {
        var consentStatus =
            await GetConsentStatusAsync(
                cancellationToken);

        /*
         * Die Installations-ID darf für nachgelagerte
         * Statistikfunktionen ausschließlich sichtbar sein,
         * wenn eine ausdrückliche Zustimmung vorliegt.
         *
         * Selbst ein eventuell verwaister lokaler Wert wird
         * bei Unknown oder Declined daher nicht ausgegeben.
         */
        if (consentStatus !=
            UsageStatisticsConsentStatus.Granted)
        {
            return null;
        }

        return await GetStoredInstallationIdAsync(
            cancellationToken);
    }

    public async Task<Guid> GrantConsentAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Diese Methode darf ausschließlich als Folge einer
         * ausdrücklichen positiven Benutzerentscheidung
         * aufgerufen werden.
         *
         * Erst an dieser Stelle darf überhaupt eine zufällige
         * Installations-ID entstehen.
         */
        var installationId =
            await GetStoredInstallationIdAsync(
                cancellationToken);

        if (!installationId.HasValue)
        {
            installationId =
                Guid.NewGuid();

            await _settingsStore
                .SetApplicationSettingAsync(
                    SettingsKeys
                        .UsageStatisticsInstallationId,
                    installationId.Value.ToString("D"),
                    cancellationToken);
        }

        /*
         * Die ID wird bewusst vor dem Zustimmungsstatus
         * gespeichert.
         *
         * Falls das Schreiben des Status wider Erwarten
         * fehlschlägt, bleibt der Zustand Unknown und damit
         * für jede spätere Übertragung gesperrt.
         */
        await _settingsStore
            .SetApplicationSettingAsync(
                SettingsKeys
                    .UsageStatisticsConsentStatus,
                ConsentGrantedValue,
                cancellationToken);

        return installationId.Value;
    }

    public async Task DeclineConsentAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Bei Ablehnung darf lokal keine Installations-ID
         * bestehen bleiben.
         *
         * Dieselbe Reihenfolge ist später auch für einen
         * Widerruf geeignet, nachdem ein höherer Dienst eine
         * eventuell notwendige serverseitige Löschung
         * durchgeführt hat.
         */
        await _settingsStore
            .DeleteApplicationSettingAsync(
                SettingsKeys
                    .UsageStatisticsInstallationId,
                cancellationToken);

        await _settingsStore
            .SetApplicationSettingAsync(
                SettingsKeys
                    .UsageStatisticsConsentStatus,
                ConsentDeclinedValue,
                cancellationToken);
    }

    private async Task<Guid?>
        GetStoredInstallationIdAsync(
            CancellationToken cancellationToken)
    {
        var storedValue =
            await _settingsStore
                .GetApplicationSettingAsync(
                    SettingsKeys
                        .UsageStatisticsInstallationId,
                    cancellationToken);

        if (!Guid.TryParse(
                storedValue,
                out var installationId) ||
            installationId == Guid.Empty)
        {
            return null;
        }

        return installationId;
    }
}