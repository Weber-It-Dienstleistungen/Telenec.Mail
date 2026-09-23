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