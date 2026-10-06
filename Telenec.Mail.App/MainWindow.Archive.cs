using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Telenec.Mail.App.Services.Archive;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string ArchiveActionTag =
        "ArchiveAction";

    private bool
        _isArchiveOperationInProgress;

    /*
     * Produktive Benutzerfunktion für das lokale
     * Telenec-Mail-Archiv.
     *
     * Sicherheitsablauf:
     *
     * 1. Die Servermail wird read-only in den lokalen
     *    Stagingbereich geladen.
     *
     * 2. Die vollständige Rohmail wird lokal geprüft.
     *
     * 3. Daraus wird die endgültige .eml-Datei im lokalen
     *    Archiv erzeugt.
     *
     * 4. Die endgültige Datei wird erneut über Dateigröße
     *    und SHA-256 geprüft.
     *
     * 5. archive.db wird auf LocalStored gesetzt.
     *
     * 6. Erst danach wird die Servermail erneut vollständig
     *    gelesen und gegen die lokale Archivkopie geprüft.
     *
     * 7. Nur bei identischer FolderId, UIDVALIDITY, UID,
     *    Dateigröße und SHA-256 wird exakt diese Servermail
     *    selektiv per UID EXPUNGE entfernt.
     *
     * 8. Nach bestätigter Serverlöschung wird archive.db
     *    auf Completed gesetzt.
     */

    protected override void OnPreviewMouseRightButtonDown(
        MouseButtonEventArgs e)
    {
        var contextMenu =
            FindMessageContextMenu(
                e.OriginalSource as DependencyObject);

        if (contextMenu is not null)
        {
            EnsureArchiveAction(
                contextMenu);
        }

        base.OnPreviewMouseRightButtonDown(
            e);
    }

    private void EnsureArchiveAction(
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
                            ArchiveActionTag,
                            StringComparison.Ordinal));

        if (existingItem is not null)
        {
            return;
        }

        var archiveMenuItem =
            new MenuItem
            {
                Header =
                    "Archivieren",

                Tag =
                    ArchiveActionTag
            };

        archiveMenuItem.Click +=
            ArchiveMenuItem_OnClick;

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
                archiveMenuItem);

            return;
        }

        var messageActionIndex =
            contextMenu.Items.IndexOf(
                messageActionItem);

        if (messageActionIndex < 0)
        {
            contextMenu.Items.Add(
                archiveMenuItem);

            return;
        }

        contextMenu.Items.Insert(
            messageActionIndex,
            archiveMenuItem);
    }

    private async void ArchiveMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsLoading ||
            _isArchiveOperationInProgress ||
            sender is not MenuItem menuItem)
        {
            return;
        }

        /*
         * Vorerst wird ausschließlich die tatsächlich
         * rechtsgeklickte Nachricht archiviert.
         *
         * Mehrfachauswahl wird als eigener Entwicklungsschritt
         * ergänzt.
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
                "Archivieren",
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
                "Archivieren",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (clickedMessage.UniqueId == 0)
        {
            MessageBox.Show(
                this,
                "Für die ausgewählte Nachricht liegt keine gültige Server-ID vor.",
                "Archivieren",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var confirmation =
            MessageBox.Show(
                this,
                "Die Nachricht wird vollständig im lokalen Telenec-Mail-Archiv auf diesem Computer gespeichert.\n\n" +
                $"Betreff:\n{clickedMessage.Subject}\n\n" +
                $"Ordner:\n{selectedFolder.DisplayName}\n\n" +
                "Nach erfolgreicher Speicherung und Prüfung wird die Nachricht endgültig vom Mailserver entfernt. " +
                "Sie wird nicht in den Papierkorb verschoben.\n\n" +
                "Das lokale Mailarchiv befindet sich im Ordner\n" +
                "„Dokumente\\Telenec Mail Archiv“.\n\n" +
                "Wichtig: Das lokale Archiv ist kein Server- oder Cloud-Backup. " +
                "Stellen Sie sicher, dass der Archivordner in Ihrer Datensicherung enthalten ist.\n\n" +
                "Möchten Sie die Nachricht jetzt archivieren?",
                "Nachricht archivieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _isArchiveOperationInProgress =
            true;

        try
        {
            /*
             * Sämtliche Archivdienste sind regulär im
             * DI-Container registriert.
             *
             * Dadurch gibt es im produktiven Workflow keine
             * temporäre ActivatorUtilities-Fallbacklogik mehr.
             */
            var stagingService =
                _serviceProvider
                    .GetRequiredService<
                        MailArchiveMessageStagingService>();

            var finalizationService =
                _serviceProvider
                    .GetRequiredService<
                        LocalMailArchiveFinalizationService>();

            var serverDeletionService =
                _serviceProvider
                    .GetRequiredService<
                        MailArchiveServerDeletionService>();

            /*
             * Phase 1:
             *
             * Vollständige Servermail read-only herunterladen
             * und lokal verifizieren.
             */
            using var stagedMessage =
                await stagingService
                    .StageMessageAsync(
                        selectedFolder.FolderId,
                        clickedMessage.UniqueId);

            /*
             * Phase 2:
             *
             * Endgültige lokale .eml erzeugen,
             * erneut verifizieren und archive.db auf
             * LocalStored setzen.
             */
            var finalizedMessage =
                await finalizationService
                    .FinalizeMessageAsync(
                        stagedMessage);

            /*
             * Phase 3:
             *
             * Der Löschservice erhält ausschließlich die
             * ArchiveMessageId.
             *
             * Sämtliche sicherheitsrelevanten Daten werden
             * anschließend erneut aus archive.db gelesen.
             */
            var deletionResult =
                await serverDeletionService
                    .DeleteArchivedServerMessageAsync(
                        finalizedMessage
                            .ArchiveMessageId);

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
                    "Die Serverlöschung wurde bestätigt, aber der Abschlussstatus konnte nicht vollständig gespeichert werden.");
            }

            /*
             * Die Archivierung selbst ist an dieser Stelle
             * bereits vollständig abgeschlossen.
             *
             * Eine anschließend fehlschlagende Aktualisierung
             * der Benutzeroberfläche darf diesen Erfolg nicht
             * nachträglich in einen Archivierungsfehler
             * verwandeln.
             */
            var mailboxRefreshSucceeded =
                true;

            try
            {
                await RefreshMailboxFromUiAsync();
            }
            catch
            {
                mailboxRefreshSucceeded =
                    false;
            }

            var refreshNotice =
                mailboxRefreshSucceeded
                    ? string.Empty
                    : "\n\nDie Mailansicht konnte nicht automatisch aktualisiert werden. " +
                      "Drücken Sie bitte F5.";

            MessageBox.Show(
                this,
                "Die Nachricht wurde erfolgreich archiviert und vom Mailserver entfernt.\n\n" +
                $"Betreff:\n{clickedMessage.Subject}\n\n" +
                $"Archivdatei:\n{finalizedMessage.FinalFilePath}\n\n" +
                "Bitte berücksichtigen Sie den Ordner „Telenec Mail Archiv“ in Ihrer regelmäßigen Datensicherung." +
                refreshNotice,
                "Archivierung abgeschlossen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                "bereits im lokalen Mailarchiv",
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "Diese Nachricht befindet sich bereits im lokalen Mailarchiv.\n\n" +
                "Es wurde keine zweite Archivkopie angelegt.",
                "Bereits archiviert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (MailArchiveServerDeletionException exception)
            when (exception.ServerDeletionConfirmed)
        {
            MessageBox.Show(
                this,
                "Die Nachricht wurde lokal archiviert und die Serverkopie wurde erfolgreich entfernt.\n\n" +
                "Der lokale Archivstatus konnte anschließend jedoch nicht vollständig gespeichert werden.\n\n" +
                "Bitte archivieren Sie diese Nachricht nicht erneut.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Archivstatus prüfen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (MailArchiveServerDeletionException exception)
            when (exception.ServerStateUncertain)
        {
            MessageBox.Show(
                this,
                "Die Nachricht wurde erfolgreich lokal archiviert.\n\n" +
                "Während der Entfernung der Serverkopie konnte der endgültige Serverzustand jedoch nicht sicher festgestellt werden.\n\n" +
                "Bitte aktualisieren Sie das Postfach mit F5 und archivieren Sie die Nachricht nicht sofort erneut.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Serverzustand prüfen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (MailArchiveServerDeletionException exception)
        {
            MessageBox.Show(
                this,
                "Die Nachricht wurde lokal archiviert, konnte aber nicht sicher vom Mailserver entfernt werden.\n\n" +
                "Die lokale Archivkopie bleibt erhalten.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Serverkopie konnte nicht entfernt werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Die Nachricht konnte nicht vollständig archiviert werden.\n\n" +
                "Eine Serverkopie wird nur dann entfernt, wenn die lokale Archivierung zuvor vollständig erfolgreich und verifiziert war.\n\n" +
                "Fehler:\n" +
                exception.Message,
                "Archivierung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isArchiveOperationInProgress =
                false;
        }
    }
}