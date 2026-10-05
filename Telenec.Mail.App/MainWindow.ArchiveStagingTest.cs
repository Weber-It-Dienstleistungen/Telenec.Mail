using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Telenec.Mail.App.Services.Archive;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string ArchiveStagingTestActionTag =
        "ArchiveStagingTestAction";

    /*
     * Dieser temporäre Entwicklungsschritt erweitert bewusst
     * ausschließlich das bereits vorhandene Kontextmenü einer
     * Nachricht.
     *
     * Es wird:
     *
     * - keine Nachricht verschoben,
     * - keine Nachricht gelöscht,
     * - kein IMAP-Flag verändert,
     * - kein finaler Archiveintrag erzeugt.
     *
     * Der Test ruft ausschließlich den bereits vorhandenen
     * MailArchiveMessageStagingService auf.
     *
     * Die erzeugte .eml.tmp bleibt nach erfolgreichem Test
     * absichtlich liegen, damit sie manuell kontrolliert werden
     * kann.
     */

    protected override void OnPreviewMouseRightButtonDown(
        MouseButtonEventArgs e)
    {
        /*
         * MainWindow.PermanentDelete.cs enthält bereits die
         * zentrale Erkennung des Nachrichten-Kontextmenüs.
         *
         * Da beide Dateien Teile derselben partial class
         * MainWindow sind, können wir diesen vorhandenen
         * privaten Helper wiederverwenden.
         *
         * Dadurch müssen weder MainWindow.xaml noch
         * MainWindow.xaml.cs verändert werden.
         */
        var contextMenu =
            FindMessageContextMenu(
                e.OriginalSource as DependencyObject);

        if (contextMenu is not null)
        {
            EnsureArchiveStagingTestAction(
                contextMenu);
        }

        base.OnPreviewMouseRightButtonDown(
            e);
    }

    private void EnsureArchiveStagingTestAction(
        ContextMenu contextMenu)
    {
        var existingItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag?.ToString(),
                            ArchiveStagingTestActionTag,
                            StringComparison.Ordinal));

        /*
         * Das Kontextmenü kann beliebig oft geöffnet werden.
         * Der temporäre Testeintrag darf trotzdem nur einmal
         * vorhanden sein.
         */
        if (existingItem is not null)
        {
            return;
        }

        var archiveTestItem =
            new MenuItem
            {
                Header =
                    "Archiv-Test: lokale Kopie prüfen",

                Tag =
                    ArchiveStagingTestActionTag
            };

        archiveTestItem.Click +=
            ArchiveStagingTestMenuItem_OnClick;

        /*
         * Der bestehende normale Löschen-/Wiederherstellen-
         * Eintrag trägt den Tag "MessageAction".
         *
         * Wir setzen den Archiv-Test unmittelbar davor.
         *
         * Dadurch bleibt die vorhandene Menüstruktur erhalten
         * und PermanentDelete kann sein eigenes Kontextmenü
         * anschließend weiterhin unabhängig ergänzen.
         */
        var messageActionItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag?.ToString(),
                            "MessageAction",
                            StringComparison.Ordinal));

        if (messageActionItem is null)
        {
            contextMenu.Items.Add(
                archiveTestItem);

            return;
        }

        var messageActionIndex =
            contextMenu.Items.IndexOf(
                messageActionItem);

        if (messageActionIndex < 0)
        {
            contextMenu.Items.Add(
                archiveTestItem);

            return;
        }

        contextMenu.Items.Insert(
            messageActionIndex,
            archiveTestItem);
    }

    private async void ArchiveStagingTestMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsLoading ||
            sender is not MenuItem menuItem)
        {
            return;
        }

        /*
         * Wir verwenden ausdrücklich die Nachricht, auf der
         * tatsächlich rechtsgeklickt wurde.
         *
         * Eine eventuell vorhandene Mehrfachauswahl spielt
         * für diesen kontrollierten Einzelmail-Test keine Rolle.
         */
        var contextMenu =
            ItemsControl.ItemsControlFromItemContainer(
                menuItem)
            as ContextMenu;

        var placementTarget =
            contextMenu?.PlacementTarget
            as FrameworkElement;

        var clickedMessage =
            placementTarget?.DataContext
            as MailMessageItemViewModel;

        if (clickedMessage is null)
        {
            MessageBox.Show(
                this,
                "Die ausgewählte Nachricht konnte nicht eindeutig bestimmt werden.",
                "Archiv-Test",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var selectedFolder =
            _viewModel.SelectedFolder;

        if (selectedFolder is null ||
            string.IsNullOrWhiteSpace(
                selectedFolder.FolderId))
        {
            MessageBox.Show(
                this,
                "Der aktuelle Mailordner konnte nicht eindeutig bestimmt werden.",
                "Archiv-Test",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (clickedMessage.UniqueId == 0)
        {
            MessageBox.Show(
                this,
                "Für die ausgewählte Nachricht liegt keine gültige IMAP-UID vor.",
                "Archiv-Test",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var confirmation =
            MessageBox.Show(
                this,
                "Es wird ausschließlich eine lokale Testkopie dieser Nachricht heruntergeladen und geprüft.\n\n" +
                "Die Mail wird NICHT verschoben und NICHT gelöscht.\n\n" +
                "Auf dem Mailserver werden durch diesen Test keine Änderungen vorgenommen.",
                "Archiv-Test: lokale Kopie prüfen",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

        if (confirmation !=
            MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            /*
             * Falls der Staging-Service bereits im DI-Container
             * registriert ist, verwenden wir genau diese
             * Instanz.
             *
             * Für den aktuellen Entwicklungsschritt funktioniert
             * der Test aber auch dann, wenn der neue Service
             * noch nicht explizit registriert wurde:
             *
             * ActivatorUtilities erzeugt ihn anhand seines
             * Konstruktors und löst seine bekannten
             * Abhängigkeiten über den bestehenden
             * IServiceProvider auf.
             */
            var stagingService =
                _serviceProvider
                    .GetService<
                        MailArchiveMessageStagingService>();

            stagingService ??=
                ActivatorUtilities
                    .CreateInstance<
                        MailArchiveMessageStagingService>(
                            _serviceProvider);

            var stagedMessage =
                await stagingService
                    .StageMessageAsync(
                        selectedFolder.FolderId,
                        clickedMessage.UniqueId);

            /*
             * Der Staging-Service ist derzeit noch lokaler,
             * nicht veröffentlichter Entwicklungscode.
             *
             * Für diesen bewusst temporären UI-Test vermeiden
             * wir eine unnötige Kopplung an den exakten Namen
             * seines Ergebnis-Records.
             *
             * StageMessageAsync selbst bleibt vollständig
             * typsicher. Lediglich die Anzeige der bereits
             * verifizierten Ergebnisdaten wird defensiv über
             * Property-Namen ausgelesen.
             */
            var subject =
                GetArchiveStagingString(
                    stagedMessage,
                    "Subject",
                    "MessageSubject");

            if (string.IsNullOrWhiteSpace(
                    subject))
            {
                subject =
                    clickedMessage.Subject;
            }

            var folderId =
                GetArchiveStagingString(
                    stagedMessage,
                    "FolderId",
                    "SourceFolderId");

            if (string.IsNullOrWhiteSpace(
                    folderId))
            {
                folderId =
                    selectedFolder.FolderId;
            }

            var uidValidity =
                GetArchiveStagingDisplayValue(
                    stagedMessage,
                    "UidValidity",
                    "UIDValidity",
                    "SourceUidValidity");

            var uniqueId =
                GetArchiveStagingDisplayValue(
                    stagedMessage,
                    "UniqueId",
                    "Uid",
                    "UID");

            if (string.IsNullOrWhiteSpace(
                    uniqueId) ||
                string.Equals(
                    uniqueId,
                    "nicht verfügbar",
                    StringComparison.Ordinal))
            {
                uniqueId =
                    clickedMessage
                        .UniqueId
                        .ToString();
            }

            var fileSize =
                GetArchiveStagingFileSize(
                    stagedMessage);

            var sha256 =
                GetArchiveStagingString(
                    stagedMessage,
                    "Sha256",
                    "Sha256Hex",
                    "Sha256Hash",
                    "Hash");

            if (string.IsNullOrWhiteSpace(
                    sha256))
            {
                sha256 =
                    "nicht verfügbar";
            }

            var filePath =
                GetArchiveStagingString(
                    stagedMessage,
                    "FilePath",
                    "StagingFilePath",
                    "StagedFilePath",
                    "TemporaryFilePath",
                    "TemporaryPath",
                    "TempFilePath",
                    "Path");

            if (string.IsNullOrWhiteSpace(
                    filePath))
            {
                filePath =
                    "nicht verfügbar";
            }

            MessageBox.Show(
                this,
                "Lokale Kopie wurde erstellt und geprüft.\n\n" +
                $"Betreff:\n{subject}\n\n" +
                $"FolderId:\n{folderId}\n\n" +
                $"UIDVALIDITY:\n{uidValidity}\n\n" +
                $"UID:\n{uniqueId}\n\n" +
                $"Dateigröße:\n{fileSize}\n\n" +
                $"SHA-256:\n{sha256}\n\n" +
                $".eml.tmp:\n{filePath}\n\n" +
                "Die Servermail wurde nicht verändert.",
                "Archiv-Test erfolgreich",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            /*
             * Wichtig für diesen Test:
             *
             * Ein Fehler führt zu keinerlei automatischer
             * Wiederholung und zu keiner serverseitigen
             * Ersatzaktion.
             *
             * Insbesondere wird niemals versucht, die Mail
             * anschließend trotzdem zu löschen oder zu
             * verschieben.
             */
            MessageBox.Show(
                this,
                "Die lokale Testkopie konnte nicht erfolgreich erstellt und geprüft werden.\n\n" +
                "Auf dem Mailserver wurde durch diesen Test keine Nachricht gelöscht oder verschoben.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Archiv-Test fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string GetArchiveStagingString(
        object stagedMessage,
        params string[] propertyNames)
    {
        var value =
            GetArchiveStagingPropertyValue(
                stagedMessage,
                propertyNames);

        if (value is null)
        {
            return string.Empty;
        }

        if (value is byte[] bytes)
        {
            return Convert.ToHexString(
                bytes);
        }

        return value.ToString()
            ?? string.Empty;
    }

    private static string GetArchiveStagingDisplayValue(
        object stagedMessage,
        params string[] propertyNames)
    {
        var value =
            GetArchiveStagingPropertyValue(
                stagedMessage,
                propertyNames);

        return value?.ToString()
            ?? "nicht verfügbar";
    }

    private static string GetArchiveStagingFileSize(
        object stagedMessage)
    {
        var value =
            GetArchiveStagingPropertyValue(
                stagedMessage,
                "FileSizeBytes",
                "FileSize",
                "SizeBytes",
                "Length");

        if (value is null)
        {
            return "nicht verfügbar";
        }

        try
        {
            var byteCount =
                Convert.ToInt64(
                    value);

            return
                $"{byteCount:N0} Bytes";
        }
        catch
        {
            return value.ToString()
                ?? "nicht verfügbar";
        }
    }

    private static object? GetArchiveStagingPropertyValue(
        object stagedMessage,
        params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(
            stagedMessage);

        var stagedMessageType =
            stagedMessage.GetType();

        foreach (var propertyName
                 in propertyNames)
        {
            var property =
                stagedMessageType.GetProperty(
                    propertyName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.IgnoreCase);

            if (property is null ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                return property.GetValue(
                    stagedMessage);
            }
            catch
            {
                /*
                 * Die Ergebnisanzeige darf den eigentlichen
                 * erfolgreichen Staging-Vorgang nicht wegen
                 * einer rein diagnostischen Property blockieren.
                 */
            }
        }

        return null;
    }
}