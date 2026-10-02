namespace Telenec.Mail.App.Services.Storage;

public static class SettingsKeys
{
    public const string ComposeSignatureEnabled =
        "Compose.Signature.Enabled";

    public const string ComposeSignaturePlainText =
        "Compose.Signature.PlainText";

    public const string ComposeSignatureHtml =
        "Compose.Signature.Html";

    /*
     * Kontobezogen:
     * Ein späteres zweites Mailkonto kann unabhängig
     * entscheiden, ob neue Nachrichten Benachrichtigungen
     * erzeugen sollen.
     */
    public const string NotificationsDesktopEnabled =
        "Notifications.Desktop.Enabled";

    /*
     * Die gesamte Regeldefinition eines Kontos wird aktuell
     * als versioniertes JSON-Dokument gespeichert.
     *
     * Das hält den ersten Regel-Unterbau bewusst klein und
     * vermeidet eine unnötige Datenbankmigration, solange die
     * Regelmenge überschaubar bleibt.
     */
    public const string MailRulesDefinitions =
        "Mail.Rules.Definitions";

    /*
     * Anwendungsweit:
     * Tray- und Windows-Start-Verhalten gelten für die
     * gesamte lokale Telenec-Mail-Installation.
     */
    public const string ApplicationCloseToTrayEnabled =
        "Application.Tray.CloseToTray.Enabled";

    public const string ApplicationStartWithWindowsEnabled =
        "Application.Startup.StartWithWindows.Enabled";

    /*
     * Freiwillige Nutzungsstatistik:
     *
     * Der Einwilligungsstatus gilt für die gesamte lokale
     * Installation und ist ausdrücklich nicht an ein
     * Mailkonto gebunden.
     *
     * Ein fehlender Wert bedeutet:
     * Der Benutzer wurde noch nicht gefragt.
     *
     * Die Installations-ID darf erst erzeugt und gespeichert
     * werden, nachdem der Benutzer ausdrücklich zugestimmt hat.
     */
    public const string UsageStatisticsConsentStatus =
        "UsageStatistics.ConsentStatus";

    public const string UsageStatisticsInstallationId =
        "UsageStatistics.InstallationId";

    /*
     * Wird eine bereits erteilte Einwilligung widerrufen,
     * muss die bisherige Installations-ID so lange lokal
     * erhalten bleiben, bis der serverseitige Revoke
     * erfolgreich bestätigt wurde.
     *
     * Dieser Wert ist ausschließlich ein lokaler
     * Wiederholungsmarker und wird nach erfolgreichem
     * Widerruf gelöscht.
     */
    public const string UsageStatisticsPendingRevokeInstallationId =
        "UsageStatistics.PendingRevokeInstallationId";
}