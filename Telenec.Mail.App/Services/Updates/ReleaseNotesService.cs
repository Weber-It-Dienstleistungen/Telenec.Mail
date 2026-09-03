using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Telenec.Mail.App.Services.Updates;

public sealed record ReleaseNotesInfo(
    string Version,
    string Title,
    string Intro,
    IReadOnlyList<string> Changes,
    string Footer);

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