using Microsoft.Win32;
using System.IO;

namespace Telenec.Mail.App.Services.Startup;

public static class WindowsStartupRegistration
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string StartupValueName =
        "Telenec Mail";

    private const string VelopackCurrentDirectoryName =
        "current";

    public static void Apply(
        bool enabled)
    {
        if (enabled)
        {
            Enable();

            return;
        }

        Disable();
    }

    public static bool IsRegistered()
    {
        var expectedCommand =
            CreateStartupCommand();

        var existingCommand =
            ReadRegisteredCommand();

        if (string.IsNullOrWhiteSpace(
                existingCommand))
        {
            return false;
        }

        return string.Equals(
            existingCommand,
            expectedCommand,
            StringComparison.OrdinalIgnoreCase);
    }

    public static string?
        ReadRegisteredCommand()
    {
        using var baseKey =
            RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser,
                RegistryView.Default);

        using var runKey =
            baseKey.OpenSubKey(
                RunKeyPath,
                writable:
                    false);

        if (runKey is null)
        {
            return null;
        }

        return runKey.GetValue(
                   StartupValueName,
                   defaultValue:
                       null,
                   options:
                       RegistryValueOptions
                           .DoNotExpandEnvironmentNames)
               as string;
    }

    private static void Enable()
    {
        var startupCommand =
            CreateStartupCommand();

        using var baseKey =
            RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser,
                RegistryView.Default);

        using var runKey =
            baseKey.CreateSubKey(
                RunKeyPath,
                writable:
                    true);

        if (runKey is null)
        {
            throw new InvalidOperationException(
                "Der Windows-Run-Schlüssel konnte nicht geöffnet werden.");
        }

        runKey.SetValue(
            StartupValueName,
            startupCommand,
            RegistryValueKind.String);

        runKey.Flush();

        /*
         * Der Registry-Eintrag wird unmittelbar nach dem
         * Schreiben erneut gelesen.
         *
         * Dadurch melden wir "Gespeichert" nur dann, wenn
         * Windows den gewünschten Autostart tatsächlich
         * übernommen hat.
         */
        var registeredCommand =
            ReadRegisteredCommand();

        if (!string.Equals(
                registeredCommand,
                startupCommand,
                StringComparison.OrdinalIgnoreCase))
        {
            var actualValue =
                string.IsNullOrWhiteSpace(
                    registeredCommand)
                    ? "<nicht vorhanden>"
                    : registeredCommand;

            throw new InvalidOperationException(
                "Der Windows-Autostart konnte nicht verifiziert werden.\n\n" +
                $"Erwartet: {startupCommand}\n" +
                $"Gefunden: {actualValue}");
        }
    }

    private static void Disable()
    {
        using var baseKey =
            RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser,
                RegistryView.Default);

        using var runKey =
            baseKey.OpenSubKey(
                RunKeyPath,
                writable:
                    true);

        if (runKey is null)
        {
            return;
        }

        runKey.DeleteValue(
            StartupValueName,
            throwOnMissingValue:
                false);

        runKey.Flush();

        var remainingValue =
            ReadRegisteredCommand();

        if (!string.IsNullOrWhiteSpace(
                remainingValue))
        {
            throw new InvalidOperationException(
                "Der Windows-Autostart konnte nicht entfernt werden.\n\n" +
                $"Gefundener Registry-Wert: {remainingValue}");
        }
    }

    private static string
        CreateStartupCommand()
    {
        var executablePath =
            GetStartupExecutablePath();

        return
            $"\"{executablePath}\"";
    }

    private static string
        GetStartupExecutablePath()
    {
        var processPath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                processPath))
        {
            throw new InvalidOperationException(
                "Der Pfad zur laufenden Telenec-Mail-Anwendung konnte nicht ermittelt werden.");
        }

        var executablePath =
            Path.GetFullPath(
                processPath);

        if (!File.Exists(
                executablePath))
        {
            throw new InvalidOperationException(
                "Die ausführbare Datei von Telenec Mail wurde nicht gefunden.\n\n" +
                executablePath);
        }

        /*
         * Velopack installiert die eigentliche Anwendung in:
         *
         * <Installationsordner>\current\<App>.exe
         *
         * Zusätzlich liegt eine stabile Start-EXE direkt im
         * Installationsordner.
         *
         * Der komplette "current"-Ordner wird bei Updates
         * ersetzt. Deshalb darf der Windows-Autostart nicht
         * auf die EXE innerhalb von "current" zeigen.
         */
        var executableDirectory =
            Directory.GetParent(
                executablePath);

        if (executableDirectory is null ||
            !string.Equals(
                executableDirectory.Name,
                VelopackCurrentDirectoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            /*
             * Normaler Entwicklungs-/Debugstart oder eine
             * andere nicht-Velopack-Umgebung.
             */
            return executablePath;
        }

        var installationDirectory =
            executableDirectory.Parent;

        if (installationDirectory is null)
        {
            return executablePath;
        }

        var stableLauncherPath =
            Path.Combine(
                installationDirectory.FullName,
                Path.GetFileName(
                    executablePath));

        /*
         * Nur verwenden, wenn der Velopack-Launcher wirklich
         * existiert.
         *
         * Damit bleibt auch eine ungewöhnliche portable oder
         * manuell gestartete Umgebung defensiv funktionsfähig.
         */
        if (!File.Exists(
                stableLauncherPath))
        {
            return executablePath;
        }

        return stableLauncherPath;
    }
}