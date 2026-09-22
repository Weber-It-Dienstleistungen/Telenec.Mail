using System.Reflection;
using System.Windows;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    protected override void OnInitialized(
        EventArgs e)
    {
        base.OnInitialized(e);

        Loaded +=
            MainWindowVersionInfo_OnLoaded;

        /*
         * Die Suchoberfläche wird ebenfalls erst bei Loaded
         * aufgebaut.
         *
         * Zu diesem Zeitpunkt ist der vollständige
         * Nachrichtenbereich einschließlich des vorhandenen
         * Suchplatzhalters sicher verfügbar.
         */
        Loaded +=
            MainWindowSearch_OnLoaded;

        /*
         * Plaintext-URLs werden nach dem Aufbau der normalen
         * Oberfläche aktiviert.
         */
        Loaded +=
            MainWindowPlainTextLinks_OnLoaded;

        /*
         * Auch die Ordnerverwaltung wird erst bei Loaded
         * ergänzt.
         */
        Loaded +=
            MainWindowFolderManagement_OnLoaded;

        /*
         * Die Kontakte-Navigation wird nach der
         * Ordnerverwaltung ergänzt.
         *
         * Dadurch kennt sie bereits die endgültige
         * Zeilenstruktur der linken Navigation.
         */
        Loaded +=
            MainWindowContacts_OnLoaded;

        /*
         * Die Freigabe externer Bilder wird lokal pro
         * Nachricht gespeichert.
         *
         * Dadurch muss ein Benutzer bei einer bereits
         * freigegebenen Nachricht nicht bei jedem erneuten
         * Öffnen wieder auf "Trotzdem laden" klicken.
         */
        Loaded +=
            MainWindowExternalImageMemory_OnLoaded;

        /*
         * Drucken wird bewusst ebenfalls erst nach dem
         * Aufbau des vollständigen MainWindow ergänzt.
         *
         * Dadurch kann sich die Funktion an die bereits
         * existierende Aktionsleiste der ausgewählten
         * Nachricht anhängen, ohne MainWindow.xaml selbst
         * verändern zu müssen.
         */
        Loaded +=
            MainWindowPrinting_OnLoaded;
    }

    private void MainWindowVersionInfo_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -=
            MainWindowVersionInfo_OnLoaded;

        Title =
            $"Telenec Mail — Version {GetApplicationVersion()}";
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