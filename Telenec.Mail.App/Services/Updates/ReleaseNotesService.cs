using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Telenec.Mail.App.Services.Updates;

public sealed record ReleaseNotesInfo(
    string Version,
    string Title,
    string Intro,
    IReadOnlyList<string> Changes,
    string Footer,
    string? ActionText = null,
    string? ActionUri = null);

public sealed class ReleaseNotesService
{
    public ReleaseNotesInfo? TryGetPendingReleaseNotes()
    {
        var pendingVersion =
            ReleaseNotesUpdateMarker
                .TryReadPendingVersion();

        if (string.IsNullOrWhiteSpace(
                pendingVersion))
        {
            return null;
        }

        var currentVersion =
            GetApplicationVersion();

        /*
         * Ein alter Marker darf niemals Release Notes
         * einer falschen Programmversion anzeigen.
         */
        if (!string.Equals(
                pendingVersion,
                currentVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            ReleaseNotesUpdateMarker.Clear();

            return null;
        }

        var releaseNotes =
            GetReleaseNotes(
                pendingVersion);

        /*
         * Für eine Version ohne hinterlegte Release Notes
         * wird der Marker verworfen. Dadurch entsteht keine
         * Endlosschleife bei zukünftigen Releases.
         */
        if (releaseNotes is null)
        {
            ReleaseNotesUpdateMarker.Clear();

            return null;
        }

        return releaseNotes;
    }

    public void MarkAsShown()
    {
        ReleaseNotesUpdateMarker.Clear();
    }

    private static ReleaseNotesInfo?
        GetReleaseNotes(
            string version)
    {
        return version switch
        {
            "1.0.0" =>
                new ReleaseNotesInfo(
                    Version:
                        "1.0.0",
                    Title:
                        "Telenec Mail 1.0 ist da",
                    Intro:
                        "Mit Version 1.0.0 verlässt Telenec Mail die Testphase. " +
                        "Die erste Produktivversion bündelt die Funktionen und " +
                        "Verbesserungen aus dem bisherigen Betatest.",
                    Changes:
                    [
                        "Mehrere Telenec-Mailkonten können auf einem Rechner " +
                        "getrennt verwaltet und direkt innerhalb der Anwendung " +
                        "gewechselt werden.",

                        "Das Passwort eines Telenec-Mailkontos kann direkt in " +
                        "Telenec Mail geändert werden. Zugangsdaten werden " +
                        "kontobezogen im Windows Credential Manager gespeichert.",

                        "Das persönliche Adressbuch wird über CardDAV mit der " +
                        "Telenec-Infrastruktur synchronisiert und unterstützt " +
                        "unter anderem mehrere E-Mail-Adressen, Telefonnummern, " +
                        "Adressen, Notizen, Kontaktfotos und Gruppen.",

                        "Telenec Mail unterstützt Signaturen, Mailregeln, " +
                        "Kategorien, Wichtigkeit, Lesebestätigungen, Drucken, " +
                        "Suche, Unterordner, Benachrichtigungen, Autostart, " +
                        "Tray-Betrieb und die Anzeige des verfügbaren " +
                        "Postfachspeichers.",

                        "Der Offline-Betrieb ermöglicht weiterhin den Zugriff " +
                        "auf bereits geladene Ordner und Nachrichten, wenn " +
                        "vorübergehend keine Verbindung zum Mailserver besteht.",

                        "Die freiwillige Nutzungsstatistik bleibt vollständig " +
                        "optional. Sie überträgt ausschließlich technische " +
                        "Installations- und Versionsinformationen und kann " +
                        "jederzeit widerrufen werden.",

                        "Programmupdates werden ab Version 1.0 direkt über die " +
                        "Telenec-Infrastruktur bereitgestellt."
                    ],
                    Footer:
                        "Vielen Dank an alle, die Telenec Mail während des " +
                        "Betatests begleitet und mit ihrem Feedback verbessert haben!"),

            "0.1.0-test.9" =>
                new ReleaseNotesInfo(
                    Version:
                        "0.1.0-test.9",
                    Title:
                        "Telenec Mail wurde aktualisiert",
                    Intro:
                        "Diese letzte Testversion bereitet Telenec Mail auf die " +
                        "erste Produktivversion vor und ergänzt wichtige Funktionen " +
                        "für die Verwaltung von Mailkonten.",
                    Changes:
                    [
                        "Telenec Mail kann jetzt mehrere Telenec-Mailkonten auf " +
                        "einem Rechner verwalten. Die Konten werden getrennt " +
                        "gespeichert und können direkt innerhalb der Anwendung " +
                        "gewechselt werden.",

                        "Für jedes eingerichtete Konto werden Zugangsdaten, " +
                        "Einstellungen, Signaturen, Regeln und lokale Daten " +
                        "getrennt verwaltet.",

                        "Das Passwort eines Telenec-Mailkontos kann jetzt direkt " +
                        "in Telenec Mail geändert werden. Nach erfolgreicher " +
                        "Änderung wird das neue Passwort sicher im Windows " +
                        "Credential Manager gespeichert und die Mailverbindung " +
                        "automatisch neu aufgebaut.",

                        "Die Update-Infrastruktur wurde auf den zukünftigen " +
                        "Produktivbetrieb vorbereitet. Zukünftige Programmupdates " +
                        "werden direkt über die Telenec-Infrastruktur bereitgestellt."
                    ],
                    Footer:
                        "Vielen Dank für eure Unterstützung während des Betatests!"),

            "0.1.0-test.8" =>
                new ReleaseNotesInfo(
                    Version:
                        "0.1.0-test.8",
                    Title:
                        "Telenec Mail wurde aktualisiert",
                    Intro:
                        "Diese Testversion erweitert Telenec Mail vor allem bei den " +
                        "Einstellungen, der Automatisierung und der Nutzung ohne aktive " +
                        "Serververbindung. Zusätzlich wurde die freiwillige " +
                        "Nutzungsstatistik eingeführt.",
                    Changes:
                    [
                        "Signaturen können jetzt direkt in den Einstellungen verwaltet " +
                        "und formatiert werden. Sie werden automatisch bei neuen " +
                        "Nachrichten sowie beim Antworten und Weiterleiten eingefügt.",

                        "Telenec Mail zeigt jetzt den verfügbaren Speicherplatz des " +
                        "Postfachs an. Zusätzlich können Nachrichten global nach Name " +
                        "und Datum auf- oder absteigend sortiert werden.",

                        "Neue Nachrichten können über Windows-Benachrichtigungen gemeldet " +
                        "werden. Telenec Mail unterstützt außerdem den Betrieb im " +
                        "Infobereich der Taskleiste sowie einen optionalen automatischen " +
                        "Start mit Windows.",

                        "Mit den neuen Mailregeln können eingehende Nachrichten " +
                        "automatisch verarbeitet werden. Regeln lassen sich verwalten, " +
                        "manuell auf vorhandene Nachrichten anwenden und bei neuen " +
                        "Nachrichten automatisch ausführen.",

                        "Der Offline-Betrieb wurde deutlich erweitert. Bereits geladene " +
                        "Ordner und Nachrichten können bei einer unterbrochenen Verbindung " +
                        "weiter angezeigt werden. Aktionen, die das Postfach auf dem Server " +
                        "verändern würden, werden im Offline-Modus sicher verhindert.",

                        "Neu ist außerdem eine freiwillige Nutzungsstatistik. Beim ersten " +
                        "Start nach dem Update kann selbst entschieden werden, ob daran " +
                        "teilgenommen werden soll. Die Einstellung kann jederzeit geändert " +
                        "und die Teilnahme widerrufen werden.",

                        "Für die Nutzungsstatistik werden ausschließlich eine zufällig " +
                        "erzeugte Installationskennung, die verwendete Programmversion und " +
                        "die Version der Einwilligung übertragen. E-Mail-Adressen, " +
                        "Nachrichten, Kontakte, Kalenderdaten oder andere Inhalte werden " +
                        "nicht übertragen. Bei einem Widerruf wird der zugehörige " +
                        "Statistikdatensatz serverseitig gelöscht."
                    ],
                    Footer:
                        "Vielen Dank fürs Testen und für euer Feedback!"),

            "0.1.0-test.7" =>
                new ReleaseNotesInfo(
                    Version:
                        "0.1.0-test.7",
                    Title:
                        "Telenec Mail wurde aktualisiert",
                    Intro:
                        "Diese Testversion setzt zahlreiche Wünsche aus dem Betatest um " +
                        "und erweitert vor allem das Schreiben, Organisieren und " +
                        "Nachverfolgen von E-Mails.",
                    Changes:
                    [
                        "Beim Schreiben stehen jetzt deutlich mehr Formatierungen zur Verfügung: " +
                        "Fett, Kursiv, Unterstrichen, Aufzählungen, verschiedene Schriftarten " +
                        "und Schriftgrößen sowie unterschiedliche Textfarben.",

                        "Das automatische Speichern von Entwürfen wurde beruhigt. " +
                        "Während des Schreibens wartet Telenec Mail jetzt kurz, bevor ein " +
                        "Entwurf erneut gespeichert wird. Formatierte Entwürfe bleiben beim " +
                        "erneuten Öffnen erhalten.",

                        "E-Mails können jetzt direkt aus Telenec Mail gedruckt werden. " +
                        "Auch die Darstellung großer eingebetteter Bilder wurde verbessert.",

                        "Beim Schreiben kann die Wichtigkeit einer Nachricht festgelegt werden. " +
                        "Außerdem können Lesebestätigungen angefordert werden. Eingehende " +
                        "Lesebestätigungen werden der ursprünglichen gesendeten Nachricht " +
                        "zugeordnet und dort angezeigt.",

                        "E-Mails können jetzt mit mehreren farbigen Kategorien versehen werden. " +
                        "Die Kategorien werden serverseitig gespeichert und erscheinen auch " +
                        "in den Suchergebnissen, wo sie ebenfalls geändert werden können.",

                        "Die Darstellung und Navigation von Unterordnern wurde verbessert. " +
                        "Zusätzlich wurden weitere Fehler aus dem laufenden Betatest behoben."
                    ],
                    Footer:
                        "Vielen Dank fürs Testen und für euer Feedback!"),

            "0.1.0-test.6" =>
                new ReleaseNotesInfo(
                    Version:
                        "0.1.0-test.6",
                    Title:
                        "Telenec Mail wurde aktualisiert",
                    Intro:
                        "Diese Testversion erweitert Telenec Mail " +
                        "vor allem um eine vollständige Kontaktverwaltung " +
                        "und verbessert weitere wichtige Arbeitsabläufe.",
                    Changes:
                    [
                        "Telenec Mail besitzt jetzt ein persönliches " +
                        "Adressbuch. Kontakte werden über CardDAV " +
                        "synchronisiert und stehen damit auch in " +
                        "Roundcube zur Verfügung.",

                        "Kontakte können angelegt, bearbeitet und gelöscht " +
                        "werden. Unterstützt werden unter anderem mehrere " +
                        "E-Mail-Adressen und Telefonnummern, Firma, Adressen, " +
                        "Notizen, Kontaktfotos sowie Gruppen und Kategorien.",

                        "Absender einer geöffneten E-Mail können jetzt direkt " +
                        "zu den Kontakten hinzugefügt werden. Bereits vorhandene " +
                        "E-Mail-Adressen werden erkannt, damit nicht versehentlich " +
                        "doppelte Kontakte entstehen.",

                        "Beim Schreiben einer E-Mail schlägt Telenec Mail jetzt " +
                        "passende Kontakte für An, Cc und Bcc vor. Gesucht werden " +
                        "kann unter anderem nach Name, Firma und E-Mail-Adresse.",

                        "IMAP-Ordner können jetzt direkt in Telenec Mail erstellt, " +
                        "als Unterordner angelegt und sicher gelöscht werden. " +
                        "Die Ordnerstruktur wird dabei serverseitig übernommen.",

                        "Freigaben für externe Bilder können pro Nachricht " +
                        "dauerhaft gespeichert werden. Außerdem wurden mehrere " +
                        "Verbesserungen aus dem bisherigen Betatest umgesetzt, " +
                        "unter anderem bei Bedienung, Darstellung und " +
                        "Ungelesen-Anzeige."
                    ],
                    Footer:
                        "Vielen Dank fürs Testen und für euer Feedback!"),

            "0.1.0-test.5" =>
                new ReleaseNotesInfo(
                    Version:
                        "0.1.0-test.5",
                    Title:
                        "Telenec Mail wurde aktualisiert",
                    Intro:
                        "Diese Testversion erweitert Telenec Mail " +
                        "vor allem um eine leistungsfähige Suche und " +
                        "weitere Verbesserungen bei Stabilität und Bedienung.",
                    Changes:
                    [
                        "E-Mails können jetzt im aktuellen Ordner oder " +
                        "im gesamten Postfach durchsucht werden. " +
                        "Suchtreffer lassen sich direkt öffnen, beantworten, " +
                        "weiterleiten, verschieben und löschen.",

                        "Auch mehrere Suchtreffer können gemeinsam markiert " +
                        "und per Drag & Drop, Kontextmenü oder Entf-Taste " +
                        "verschoben beziehungsweise gelöscht werden.",

                        "Der Papierkorb kann jetzt vollständig geleert werden. " +
                        "Lösch- und Verschiebevorgänge wurden zusätzlich " +
                        "gegen unerwartete Serverzustände abgesichert.",

                        "Verbindungsstatus, Synchronisierung und Fehlerdiagnose " +
                        "wurden verbessert. Außerdem wurde die Darstellung " +
                        "von Programmsymbolen für Windows 10 robuster gemacht.",

                        "Das Erscheinungsbild von Telenec Mail wurde mit " +
                        "überarbeitetem Branding weiter vereinheitlicht."
                    ],
                    Footer:
                        "Vielen Dank fürs Testen und für euer Feedback!"),

            "0.1.0-test.4" =>
                new ReleaseNotesInfo(
                    Version:
                        "0.1.0-test.4",
                    Title:
                        "Telenec Mail wurde aktualisiert",
                    Intro:
                        "In dieser Version haben wir Telenec Mail " +
                        "sichtbar weiterentwickelt.",
                    Changes:
                    [
                        "Telenec Mail hat jetzt ein eigenes App-Symbol. " +
                        "Die Telenec-Spirale erscheint unter anderem in " +
                        "der Taskleiste und bei den Programmverknüpfungen.",

                        "Nach zukünftigen Updates informiert Telenec Mail " +
                        "jetzt einmalig darüber, was sich geändert hat."
                    ],
                    Footer:
                        "Vielen Dank fürs Testen und für euer Feedback!"),

            _ =>
                null
        };
    }

    private static string GetApplicationVersion()
    {
        var assembly =
            Assembly.GetEntryAssembly();

        var informationalVersion =
            assembly?
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

        return assembly?
                   .GetName()
                   .Version?
                   .ToString()
               ?? "unbekannt";
    }
}

internal static class ReleaseNotesUpdateMarker
{
    private const string MarkerFileName =
        "pending-release-notes.txt";

    public static void MarkPending(
        string version)
    {
        try
        {
            var directory =
                GetRootDirectory();

            Directory.CreateDirectory(
                directory);

            File.WriteAllText(
                GetMarkerPath(),
                version);
        }
        catch (Exception exception)
        {
            /*
             * Release Notes sind eine Komfortfunktion.
             * Ein Fehler hier darf niemals ein Update
             * oder den Programmstart gefährden.
             */
            Trace.WriteLine(
                $"Could not create release notes marker: {exception}");
        }
    }

    public static string?
        TryReadPendingVersion()
    {
        try
        {
            var path =
                GetMarkerPath();

            if (!File.Exists(path))
            {
                return null;
            }

            return File
                .ReadAllText(path)
                .Trim();
        }
        catch (Exception exception)
        {
            Trace.WriteLine(
                $"Could not read release notes marker: {exception}");

            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            var path =
                GetMarkerPath();

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine(
                $"Could not remove release notes marker: {exception}");
        }
    }

    private static string GetMarkerPath()
    {
        return Path.Combine(
            GetRootDirectory(),
            MarkerFileName);
    }

    private static string GetRootDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Telenec",
            "Mail");
    }
}