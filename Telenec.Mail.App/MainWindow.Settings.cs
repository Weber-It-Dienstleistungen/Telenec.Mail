using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string SettingsMenuItemTag =
        "Telenec.Mail.Settings";

    private static readonly bool
        SettingsMenuClassHandlerRegistered =
            RegisterSettingsMenuClassHandler();

    private bool
        _settingsMenuInstalled;

    private static bool
        RegisterSettingsMenuClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnSettingsMenuLoaded));

        return true;
    }

    private static void MainWindow_OnSettingsMenuLoaded(
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

        /*
         * Die Migration besitzt bereits eine eigene
         * dynamische Menüinstallation.
         *
         * Wir stellen hier zunächst sicher, dass sie
         * vorhanden ist. Dadurch erhalten wir unabhängig
         * von der Reihenfolge der Loaded-Class-Handler
         * immer dieselbe Menüstruktur:
         *
         * Einstellungen …
         * ----------------
         * Postfach migrieren …
         * ----------------
         * Konto abmelden
         */
        window.InstallMigrationMenu();

        window.InstallSettingsMenu();
    }

    private void InstallSettingsMenu()
    {
        if (_settingsMenuInstalled)
        {
            return;
        }

        var contextMenu =
            AccountMenuButton.ContextMenu;

        if (contextMenu is null)
        {
            return;
        }

        var existingSettingsItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag as string,
                            SettingsMenuItemTag,
                            StringComparison.Ordinal));

        if (existingSettingsItem is not null)
        {
            _settingsMenuInstalled =
                true;

            return;
        }

        var settingsMenuItem =
            new MenuItem
            {
                Header =
                    "Einstellungen …",

                Tag =
                    SettingsMenuItemTag
            };

        settingsMenuItem.Click +=
            SettingsMenuItem_OnClick;

        contextMenu.Items.Insert(
            0,
            settingsMenuItem);

        contextMenu.Items.Insert(
            1,
            new Separator());

        _settingsMenuInstalled =
            true;
    }

    private async void SettingsMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settingsWindow =
                _serviceProvider
                    .GetRequiredService<
                        SettingsWindow>();

            settingsWindow.Owner =
                this;

            settingsWindow.ShowDialog();

            /*
             * Tray- und Benachrichtigungseinstellungen
             * sollen unmittelbar nach dem Schließen des
             * Einstellungsfensters gelten.
             *
             * Ein Programmneustart ist nicht erforderlich.
             */
            await RefreshTrayStateAsync();

            await RefreshDesktopNotificationStateAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Die Einstellungen konnten nicht geöffnet werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}