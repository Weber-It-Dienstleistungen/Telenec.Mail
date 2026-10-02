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
         */
        if (consentStatus !=
            UsageStatisticsConsentStatus.Granted)
        {
            return null;
        }

        return await GetStoredInstallationIdAsync(
            cancellationToken);
    }

    public async Task<Guid?> GetPendingRevokeInstallationIdAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetStoredGuidAsync(
            SettingsKeys
                .UsageStatisticsPendingRevokeInstallationId,
            cancellationToken);
    }

    public async Task<Guid> GrantConsentAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Ein noch nicht abgeschlossener alter Widerruf muss
         * zuerst serverseitig verarbeitet werden.
         *
         * Dadurch kann eine alte pseudonyme Installation
         * nicht versehentlich mit einer neuen Einwilligung
         * vermischt werden.
         */
        var pendingRevokeInstallationId =
            await GetPendingRevokeInstallationIdAsync(
                cancellationToken);

        if (pendingRevokeInstallationId.HasValue)
        {
            throw new InvalidOperationException(
                "Ein vorheriger Widerruf der Nutzungsstatistik ist noch nicht abgeschlossen.");
        }

        var previousConsentStatus =
            await GetConsentStatusAsync(
                cancellationToken);

        Guid? installationId = null;

        /*
         * Nur eine bereits aktive Einwilligung darf ihre
         * bestehende Installations-ID behalten.
         *
         * Nach einer früheren Ablehnung oder einem Widerruf
         * entsteht bei erneuter Zustimmung bewusst eine neue
         * zufällige ID.
         */
        if (previousConsentStatus ==
            UsageStatisticsConsentStatus.Granted)
        {
            installationId =
                await GetStoredInstallationIdAsync(
                    cancellationToken);
        }

        if (!installationId.HasValue)
        {
            await _settingsStore
                .DeleteApplicationSettingAsync(
                    SettingsKeys
                        .UsageStatisticsInstallationId,
                    cancellationToken);

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
         * Schlägt das Schreiben des Status fehl, bleibt der
         * Zustand nicht eindeutig Granted und damit für eine
         * Übertragung gesperrt.
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
         * Der Status wird zuerst auf Declined gesetzt.
         *
         * Damit gibt GetInstallationIdAsync unmittelbar keine
         * ID mehr frei, selbst wenn das anschließende Entfernen
         * des lokalen ID-Werts fehlschlagen sollte.
         */
        await _settingsStore
            .SetApplicationSettingAsync(
                SettingsKeys
                    .UsageStatisticsConsentStatus,
                ConsentDeclinedValue,
                cancellationToken);

        await _settingsStore
            .DeleteApplicationSettingAsync(
                SettingsKeys
                    .UsageStatisticsInstallationId,
                cancellationToken);
    }

    public async Task<Guid?> PrepareRevokeAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * GetInstallationIdAsync liefert nur bei tatsächlich
         * aktiver Einwilligung eine ID.
         *
         * Bei einer bloßen Ablehnung vor der ersten Teilnahme
         * existiert daher nichts, was serverseitig widerrufen
         * werden müsste.
         */
        var installationId =
            await GetInstallationIdAsync(
                cancellationToken);

        if (!installationId.HasValue)
        {
            await DeclineConsentAsync(
                cancellationToken);

            return null;
        }

        /*
         * Die bisherige ID wird zuerst als Pending-Revoke
         * gesichert.
         *
         * Erst danach wird die lokale Teilnahme deaktiviert.
         * Sollte der Prozess zwischen diesen Schritten
         * abbrechen, kann der Widerruf beim nächsten Start
         * anhand dieses Markers wieder aufgenommen werden.
         */
        await _settingsStore
            .SetApplicationSettingAsync(
                SettingsKeys
                    .UsageStatisticsPendingRevokeInstallationId,
                installationId.Value.ToString("D"),
                cancellationToken);

        await DeclineConsentAsync(
            cancellationToken);

        return installationId;
    }

    public async Task CompletePendingRevokeAsync(
        Guid installationId,
        CancellationToken cancellationToken = default)
    {
        var pendingInstallationId =
            await GetPendingRevokeInstallationIdAsync(
                cancellationToken);

        /*
         * Defensiv nur genau den Marker entfernen, der
         * tatsächlich serverseitig verarbeitet wurde.
         */
        if (!pendingInstallationId.HasValue ||
            pendingInstallationId.Value != installationId)
        {
            return;
        }

        await _settingsStore
            .DeleteApplicationSettingAsync(
                SettingsKeys
                    .UsageStatisticsPendingRevokeInstallationId,
                cancellationToken);
    }

    private async Task<Guid?>
        GetStoredInstallationIdAsync(
            CancellationToken cancellationToken)
    {
        return await GetStoredGuidAsync(
            SettingsKeys
                .UsageStatisticsInstallationId,
            cancellationToken);
    }

    private async Task<Guid?> GetStoredGuidAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var storedValue =
            await _settingsStore
                .GetApplicationSettingAsync(
                    key,
                    cancellationToken);

        if (!Guid.TryParseExact(
                storedValue,
                "D",
                out var installationId) ||
            installationId == Guid.Empty)
        {
            return null;
        }

        return installationId;
    }
}