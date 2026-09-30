namespace Telenec.Mail.App.Services.UsageStatistics;

public enum UsageStatisticsConsentStatus
{
    /*
     * Es wurde noch keine Entscheidung getroffen.
     *
     * In diesem Zustand darf weder eine neue
     * Installations-ID erzeugt noch eine Statistikmeldung
     * übertragen werden.
     */
    Unknown = 0,

    /*
     * Der Benutzer hat der freiwilligen Nutzungsstatistik
     * ausdrücklich zugestimmt.
     */
    Granted = 1,

    /*
     * Der Benutzer hat die Teilnahme ausdrücklich
     * abgelehnt.
     */
    Declined = 2
}