using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Services.Archive;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string ArchiveNavigationMenuItemTag =
        "Telenec.Mail.Archive.Open";

    private const string ArchiveShortcutFileName =
        "Telenec Mail Archiv.lnk";

    private static readonly bool
        ArchiveNavigationMenuClassHandlerRegistered =
            RegisterArchiveNavigationMenuClassHandler();

    private bool
        _archiveNavigationMenuInstalled;

    private static bool
        RegisterArchiveNavigationMenuClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnArchiveNavigationMenuLoaded));

        return true;
    }

    private static void
        MainWindow_OnArchiveNavigationMenuLoaded(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        if (!ReferenceEquals(
                e.OriginalSource,
                window))
        {
            return;
        }

        window.InstallMigrationMenu();
        window.InstallSettingsMenu();
        window.InstallFeedbackMenu();

        window.InstallArchiveNavigationMenu();
    }

    private void InstallArchiveNavigationMenu()
    {
        if (_archiveNavigationMenuInstalled)
        {
            return;
        }

        var contextMenu =
            AccountMenuButton.ContextMenu;

        if (contextMenu is null)
        {
            return;
        }

        var existingArchiveItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag as string,
                            ArchiveNavigationMenuItemTag,
                            StringComparison.Ordinal));

        if (existingArchiveItem is not null)
        {
            _archiveNavigationMenuInstalled =
                true;

            return;
        }

        var archiveMenuItem =
            new MenuItem
            {
                Header =
                    "Lokales Archiv öffnen …",

                Tag =
                    ArchiveNavigationMenuItemTag
            };

        archiveMenuItem.Click +=
            ArchiveNavigationMenuItem_OnClick;

        var feedbackIndex =
            FindMenuItemIndexByTag(
                contextMenu,
                FeedbackMenuItemTag);

        if (feedbackIndex >= 0)
        {
            contextMenu.Items.Insert(
                feedbackIndex,
                archiveMenuItem);

            _archiveNavigationMenuInstalled =
                true;

            return;
        }

        var logoutIndex =
            FindLogoutMenuItemIndex(
                contextMenu);

        if (logoutIndex >= 0)
        {
            contextMenu.Items.Insert(
                logoutIndex,
                archiveMenuItem);
        }
        else
        {
            contextMenu.Items.Add(
                archiveMenuItem);
        }

        _archiveNavigationMenuInstalled =
            true;
    }

    private static int FindMenuItemIndexByTag(
        ContextMenu contextMenu,
        string tag)
    {
        for (var index = 0;
             index < contextMenu.Items.Count;
             index++)
        {
            if (contextMenu.Items[index]
                    is not MenuItem menuItem)
            {
                continue;
            }

            if (string.Equals(
                    menuItem.Tag as string,
                    tag,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void ArchiveNavigationMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            /*
             * Der Menüpunkt öffnet nun die echte lokale
             * Archivansicht.
             *
             * Das Fenster selbst lädt beim Öffnen einen
             * frischen read-only Snapshot aus archive.db.
             */
            var archiveWindow =
                _serviceProvider
                    .GetRequiredService<
                        ArchiveWindow>();

            archiveWindow.Owner =
                this;

            archiveWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Das lokale Mailarchiv konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Lokales Mailarchiv",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task<LocalMailArchiveLocation>
        GetArchiveLocationAsync()
    {
        var archiveStorage =
            _serviceProvider
                .GetRequiredService<
                    LocalMailArchiveStorage>();

        return await archiveStorage
            .EnsureArchiveForActiveAccountAsync();
    }

    /*
     * Komfortfunktion für den Archivworkflow.
     *
     * Sie wird nach mindestens einer erfolgreich archivierten
     * Nachricht aufgerufen.
     *
     * Existiert die Desktop-Verknüpfung bereits, geschieht
     * nichts.
     *
     * Ein Fehler beim Erstellen der Verknüpfung darf niemals
     * den eigentlichen Archivierungsvorgang fehlschlagen
     * lassen.
     */
    private async Task<ArchiveDesktopShortcutResult>
        EnsureArchiveDesktopShortcutAsync()
    {
        try
        {
            var archiveLocation =
                await GetArchiveLocationAsync();

            var desktopDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .DesktopDirectory);

            if (string.IsNullOrWhiteSpace(
                    desktopDirectory))
            {
                return ArchiveDesktopShortcutResult.Failed(
                    "Der Windows-Desktop konnte nicht ermittelt werden.");
            }

            var shortcutPath =
                Path.Combine(
                    desktopDirectory,
                    ArchiveShortcutFileName);

            if (File.Exists(
                    shortcutPath))
            {
                return ArchiveDesktopShortcutResult
                    .AlreadyExists();
            }

            Directory.CreateDirectory(
                desktopDirectory);

            CreateWindowsShortcut(
                shortcutPath,
                archiveLocation.AccountDirectory,
                archiveLocation.EmailAddress);

            if (!File.Exists(
                    shortcutPath))
            {
                return ArchiveDesktopShortcutResult.Failed(
                    "Die Desktop-Verknüpfung wurde nicht angelegt.");
            }

            return ArchiveDesktopShortcutResult.Created();
        }
        catch (Exception exception)
        {
            return ArchiveDesktopShortcutResult.Failed(
                exception.Message);
        }
    }

    private static void CreateWindowsShortcut(
        string shortcutPath,
        string targetDirectory,
        string emailAddress)
    {
        var shellType =
            Type.GetTypeFromProgID(
                "WScript.Shell");

        if (shellType is null)
        {
            throw new InvalidOperationException(
                "Die Windows-Verknüpfungsfunktion ist auf diesem System nicht verfügbar.");
        }

        object? shellObject =
            null;

        object? shortcutObject =
            null;

        try
        {
            shellObject =
                Activator.CreateInstance(
                    shellType);

            if (shellObject is null)
            {
                throw new InvalidOperationException(
                    "Die Windows-Verknüpfungsfunktion konnte nicht gestartet werden.");
            }

            dynamic shell =
                shellObject;

            dynamic shortcut =
                shell.CreateShortcut(
                    shortcutPath);

            shortcutObject =
                shortcut;

            shortcut.TargetPath =
                targetDirectory;

            shortcut.WorkingDirectory =
                targetDirectory;

            shortcut.Description =
                $"Lokales Telenec-Mail-Archiv für {emailAddress}";

            /*
             * Aktuell verwenden wir das Programmsymbol von
             * Telenec Mail.
             *
             * Ein eigenes Archivsymbol kann später als reines
             * Polishing ergänzt werden.
             */
            var applicationPath =
                Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(
                    applicationPath))
            {
                try
                {
                    applicationPath =
                        Process
                            .GetCurrentProcess()
                            .MainModule?
                            .FileName;
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    applicationPath) &&
                File.Exists(
                    applicationPath))
            {
                shortcut.IconLocation =
                    $"{applicationPath},0";
            }

            shortcut.Save();
        }
        finally
        {
            if (shortcutObject is not null &&
                Marshal.IsComObject(
                    shortcutObject))
            {
                Marshal.FinalReleaseComObject(
                    shortcutObject);
            }

            if (shellObject is not null &&
                Marshal.IsComObject(
                    shellObject))
            {
                Marshal.FinalReleaseComObject(
                    shellObject);
            }
        }
    }

    private sealed record ArchiveDesktopShortcutResult(
        bool WasCreated,
        bool ExistedAlready,
        string? ErrorMessage)
    {
        public bool Succeeded =>
            string.IsNullOrWhiteSpace(
                ErrorMessage);

        public static ArchiveDesktopShortcutResult
            Created()
        {
            return new ArchiveDesktopShortcutResult(
                WasCreated:
                    true,
                ExistedAlready:
                    false,
                ErrorMessage:
                    null);
        }

        public static ArchiveDesktopShortcutResult
            AlreadyExists()
        {
            return new ArchiveDesktopShortcutResult(
                WasCreated:
                    false,
                ExistedAlready:
                    true,
                ErrorMessage:
                    null);
        }

        public static ArchiveDesktopShortcutResult
            Failed(
                string errorMessage)
        {
            return new ArchiveDesktopShortcutResult(
                WasCreated:
                    false,
                ExistedAlready:
                    false,
                ErrorMessage:
                    errorMessage);
        }
    }
}