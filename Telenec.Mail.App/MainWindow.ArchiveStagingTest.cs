using Microsoft.Extensions.DependencyInjection;
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
     * Temporärer Entwicklungstest für den vollständigen
     * Einzelmail-Archivworkflow.
     *
     * Ablauf:
     *
     * 1. Die rechtsgeklickte Servermail wird read-only in den
     *    lokalen Stagingbereich geladen.
     *
     * 2. Die Stagingdatei wird geprüft.
     *
     * 3. Die Nachricht wird endgültig als lokale .eml-Datei
     *    gespeichert.
     *
     * 4. Die endgültige Datei wird erneut über Dateigröße und
     *    SHA-256 geprüft.
     *
     * 5. archive.db wird auf LocalStored gesetzt.
     *
     * 6. Erst danach wird die Servermail erneut vollständig
     *    gelesen und mit der lokalen Archivkopie verglichen.
     *
     * 7. Nur wenn FolderId, UIDVALIDITY, UID, Dateigröße und
     *    SHA-256 weiterhin exakt stimmen, wird genau diese
     *    Servermail selektiv per UID EXPUNGE entfernt.
     *
     * 8. Nach bestätigter Serverlöschung wird archive.db auf
     *    Completed gesetzt.
     *
     * Dieser Test ist damit erstmals serververändernd.
     */

    protected override void OnPreviewMouseRightButtonDown(
        MouseButtonEventArgs e)
    {
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

        if (existingItem is not null)
        {
            return;
        }

        var archiveTestItem =
            new MenuItem
            {
                Header =
                    "Archiv-Test: lokal archivieren + Servermail löschen",

                Tag =
                    ArchiveStagingTestActionTag
            };

        archiveTestItem.Click +=
            ArchiveStagingTestMenuItem_OnClick;

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
         * Ausschließlich die tatsächlich rechtsgeklickte
         * Nachricht wird verarbeitet.
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

        /*
         * Dieser Schritt ist erstmals destruktiv auf dem
         * Mailserver.
         *
         * Deshalb wird sehr deutlich darauf hingewiesen, dass
         * die Mail nach erfolgreicher lokaler Archivierung
         * endgültig aus ihrem bisherigen Serverordner entfernt
         * wird.
         */
        var confirmation =
            MessageBox.Show(
                this,
                "ACHTUNG: Dieser Test entfernt die ausgewählte Mail nach erfolgreicher lokaler Archivierung endgültig vom Mailserver.\n\n" +
                $"Betreff:\n{clickedMessage.Subject}\n\n" +
                $"Ordner:\n{selectedFolder.DisplayName}\n\n" +
                "Vor der Löschung wird die vollständige Mail lokal als .eml gespeichert, mehrfach geprüft und in archive.db eingetragen.\n\n" +
                "Danach wird die Servermail nochmals vollständig gelesen und per SHA-256 mit der lokalen Archivkopie verglichen.\n\n" +
                "Nur bei vollständiger Übereinstimmung wird exakt diese eine IMAP-UID endgültig vom Server gelöscht.\n\n" +
                "Die Mail wird NICHT in den Papierkorb verschoben.\n\n" +
                "Bitte nur mit einer entbehrlichen Testmail fortfahren.",
                "Archiv-Test: Servermail endgültig entfernen",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

        if (confirmation !=
            MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var stagingService =
                _serviceProvider
                    .GetService<
                        MailArchiveMessageStagingService>();

            stagingService ??=
                ActivatorUtilities
                    .CreateInstance<
                        MailArchiveMessageStagingService>(
                            _serviceProvider);

            var finalizationService =
                _serviceProvider
                    .GetService<
                        LocalMailArchiveFinalizationService>();

            finalizationService ??=
                ActivatorUtilities
                    .CreateInstance<
                        LocalMailArchiveFinalizationService>(
                            _serviceProvider);

            var serverDeletionService =
                _serviceProvider
                    .GetService<
                        MailArchiveServerDeletionService>();

            serverDeletionService ??=
                ActivatorUtilities
                    .CreateInstance<
                        MailArchiveServerDeletionService>(
                            _serviceProvider);

            /*
             * Staging:
             *
             * Die Servermail wird ausschließlich read-only
             * geladen und lokal verifiziert.
             */
            using var stagedMessage =
                await stagingService
                    .StageMessageAsync(
                        selectedFolder.FolderId,
                        clickedMessage.UniqueId);

            /*
             * Lokale Finalisierung:
             *
             * Erst danach existiert eine endgültige,
             * erneut geprüfte .eml-Datei und archive.db steht
             * auf LocalStored.
             */
            var finalizedMessage =
                await finalizationService
                    .FinalizeMessageAsync(
                        stagedMessage);

            /*
             * Serverlöschung:
             *
             * Der Löschservice erhält bewusst nur die
             * ArchiveMessageId.
             *
             * FolderId, UIDVALIDITY, UID, Dateipfad,
             * Dateigröße und SHA-256 werden anschließend
             * unabhängig aus archive.db geladen.
             */
            var deletionResult =
                await serverDeletionService
                    .DeleteArchivedServerMessageAsync(
                        finalizedMessage.ArchiveMessageId);

            if (!deletionResult
                    .ServerDeletionConfirmed)
            {
                throw new InvalidOperationException(
                    "Die Serverlöschung wurde nicht eindeutig bestätigt.");
            }

            if (!deletionResult
                    .DatabaseCompletionRecorded)
            {
                throw new InvalidOperationException(
                    "Die Serverlöschung wurde bestätigt, aber der Abschlussstatus wurde nicht vollständig gespeichert.");
            }

            MessageBox.Show(
                this,
                "Die Nachricht wurde vollständig archiviert.\n\n" +
                $"Betreff:\n{clickedMessage.Subject}\n\n" +
                $"FolderId:\n{finalizedMessage.SourceFolderId}\n\n" +
                $"UIDVALIDITY:\n{finalizedMessage.SourceUidValidity}\n\n" +
                $"UID:\n{finalizedMessage.SourceUniqueId}\n\n" +
                $"Dateigröße:\n{finalizedMessage.FileSizeBytes:N0} Bytes\n\n" +
                $"SHA-256:\n{finalizedMessage.Sha256}\n\n" +
                $"Endgültige .eml:\n{finalizedMessage.FinalFilePath}\n\n" +
                "Lokale Archivdatei: VERIFIZIERT\n" +
                "archive.db: COMPLETED\n" +
                "Servermail: GELÖSCHT\n\n" +
                "Die Nachricht befindet sich jetzt ausschließlich im lokalen Telenec-Mail-Archiv.",
                "Archivierung vollständig",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                "bereits im lokalen Mailarchiv",
                StringComparison.OrdinalIgnoreCase))
        {
            /*
             * Eine bereits archivierte Nachricht wird durch
             * diesen temporären Test nicht automatisch erneut
             * verarbeitet.
             */
            MessageBox.Show(
                this,
                "Diese Nachricht befindet sich bereits im lokalen Mailarchiv.\n\n" +
                "Es wurde keine zweite Archivkopie angelegt und kein neuer Löschvorgang gestartet.",
                "Bereits archiviert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (MailArchiveServerDeletionException exception)
            when (exception.ServerDeletionConfirmed)
        {
            /*
             * Besonders wichtiger Sonderfall:
             *
             * Die Servermail ist sicher gelöscht worden, aber
             * anschließend konnte beispielsweise archive.db
             * nicht mehr auf Completed gesetzt werden.
             *
             * Die lokale Archivdatei bleibt dabei erhalten.
             *
             * Ein automatischer zweiter Löschversuch wäre hier
             * falsch.
             */
            MessageBox.Show(
                this,
                "Die Servermail wurde nachweislich erfolgreich gelöscht und die lokale Archivkopie ist vorhanden.\n\n" +
                "Der lokale Abschlussstatus konnte jedoch nicht vollständig gespeichert werden.\n\n" +
                "Bitte KEINEN erneuten Archivierungs- oder Löschversuch mit dieser Nachricht starten.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Archivierung: Statusprüfung erforderlich",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (MailArchiveServerDeletionException exception)
            when (exception.ServerStateUncertain)
        {
            /*
             * Bei einem Verbindungsabbruch mitten während der
             * Löschoperation kann weder blind behauptet werden,
             * dass die Mail gelöscht wurde, noch darf einfach
             * erneut gelöscht werden.
             */
            MessageBox.Show(
                this,
                "Die lokale Archivkopie wurde erfolgreich erstellt und bleibt erhalten.\n\n" +
                "Während der Serverlöschung konnte der endgültige Serverzustand jedoch nicht sicher festgestellt werden.\n\n" +
                "Bitte das Postfach neu synchronisieren und NICHT sofort erneut archivieren.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Archivierung: Serverzustand unklar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (MailArchiveServerDeletionException exception)
        {
            /*
             * Die lokale Kopie bleibt erhalten, die
             * Serverlöschung wurde aber sicher nicht als
             * erfolgreich bestätigt.
             */
            MessageBox.Show(
                this,
                "Die Nachricht wurde lokal archiviert, konnte aber nicht sicher vom Mailserver entfernt werden.\n\n" +
                "Die lokale .eml bleibt erhalten.\n" +
                "Die Servermail wurde nicht als erfolgreich gelöscht bestätigt.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Serverlöschung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            /*
             * Jeder sonstige Fehler beendet den Ablauf.
             *
             * Es gibt keinerlei Fallback, das anschließend
             * trotzdem versucht, eine Servermail zu löschen.
             */
            MessageBox.Show(
                this,
                "Die Archivierung konnte nicht vollständig abgeschlossen werden.\n\n" +
                "Ein nicht bestätigter Löschschritt wird niemals automatisch wiederholt.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Archivierung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}