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

    private const int MaximumPreviewMessageCount =
        5;

    private const int MaximumPreviewSubjectLength =
        80;

    private bool
        _isArchiveOperationInProgress;

    /*
     * Produktive Benutzerfunktion für das lokale
     * Telenec-Mail-Archiv.
     *
     * Mehrere ausgewählte Nachrichten werden bewusst
     * nacheinander verarbeitet.
     *
     * Jede einzelne Nachricht durchläuft vollständig den
     * bereits getesteten Sicherheitsablauf:
     *
     * 1. Servermail read-only herunterladen.
     * 2. Stagingdatei verifizieren.
     * 3. Endgültige lokale .eml erzeugen.
     * 4. Endgültige Datei erneut verifizieren.
     * 5. archive.db auf LocalStored setzen.
     * 6. Servermail erneut vollständig lesen.
     * 7. Identität, Dateigröße und SHA-256 vergleichen.
     * 8. Exakt diese UID selektiv entfernen.
     * 9. Serverlöschung bestätigen.
     * 10. archive.db auf Completed setzen.
     *
     * Erst danach beginnt die nächste ausgewählte Nachricht.
     *
     * Tritt bei einer Nachricht ein Fehler auf, wird der
     * gesamte Auswahlvorgang an dieser Stelle gestoppt.
     *
     * Bereits vollständig archivierte vorherige Nachrichten
     * bleiben korrekt archiviert.
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

        /*
         * Verhalten analog zu anderen Mehrfachaktionen:
         *
         * Rechtsklick auf eine bereits ausgewählte Nachricht:
         * gesamte aktuelle Auswahl verwenden.
         *
         * Rechtsklick auf eine nicht ausgewählte Nachricht:
         * ausschließlich diese eine Nachricht verwenden.
         */
        IReadOnlyList<MailMessageItemViewModel>
            messagesToArchive;

        if (MessageListBox
            .SelectedItems
            .Contains(
                clickedMessage))
        {
            messagesToArchive =
                GetSelectedMessages();
        }
        else
        {
            messagesToArchive =
                new[]
                {
                    clickedMessage
                };
        }

        if (messagesToArchive.Count == 0)
        {
            return;
        }

        /*
         * Wir filtern ungültige UIDs niemals stillschweigend
         * aus einer Mehrfachauswahl heraus.
         *
         * Entweder ist die gesamte Auswahl eindeutig
         * identifizierbar oder der Vorgang beginnt gar nicht.
         */
        var invalidMessages =
            messagesToArchive
                .Where(
                    message =>
                        message.UniqueId == 0)
                .ToList();

        if (invalidMessages.Count > 0)
        {
            MessageBox.Show(
                this,
                invalidMessages.Count == 1
                    ? "Eine ausgewählte Nachricht besitzt keine gültige Server-ID.\n\n" +
                      "Die Archivierung wurde nicht gestartet."
                    : $"{invalidMessages.Count} ausgewählte Nachrichten besitzen keine gültige Server-ID.\n\n" +
                      "Die Archivierung wurde nicht gestartet.",
                "Archivieren",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var confirmation =
            MessageBox.Show(
                this,
                CreateArchiveConfirmationText(
                    messagesToArchive,
                    selectedFolder.DisplayName),
                messagesToArchive.Count == 1
                    ? "Nachricht archivieren"
                    : "Nachrichten archivieren",
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

        Mouse.OverrideCursor =
            Cursors.Wait;

        var successfullyArchivedCount =
            0;

        var successfullyArchivedFiles =
            new List<string>();

        ArchiveBatchFailure?
            failure =
                null;

        try
        {
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

            foreach (var message in
                     messagesToArchive)
            {
                try
                {
                    /*
                     * Phase 1:
                     *
                     * Vollständige Servermail read-only
                     * herunterladen und lokal verifizieren.
                     */
                    using var stagedMessage =
                        await stagingService
                            .StageMessageAsync(
                                selectedFolder.FolderId,
                                message.UniqueId);

                    /*
                     * Phase 2:
                     *
                     * Endgültige lokale .eml erzeugen,
                     * erneut verifizieren und archive.db
                     * auf LocalStored setzen.
                     */
                    var finalizedMessage =
                        await finalizationService
                            .FinalizeMessageAsync(
                                stagedMessage);

                    /*
                     * Phase 3:
                     *
                     * Servermail erneut vollständig prüfen,
                     * exakt diese UID entfernen und
                     * archive.db auf Completed setzen.
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

                    successfullyArchivedCount++;

                    successfullyArchivedFiles.Add(
                        finalizedMessage
                            .FinalFilePath);
                }
                catch (InvalidOperationException exception)
                    when (exception.Message.Contains(
                        "bereits im lokalen Mailarchiv",
                        StringComparison.OrdinalIgnoreCase))
                {
                    failure =
                        new ArchiveBatchFailure(
                            Subject:
                                message.Subject,

                            Title:
                                "Bereits archiviert",

                            Message:
                                "Diese Nachricht befindet sich bereits im lokalen Mailarchiv.\n\n" +
                                "Es wurde keine zweite Archivkopie angelegt.",

                            Image:
                                MessageBoxImage.Information);

                    break;
                }
                catch (MailArchiveServerDeletionException exception)
                    when (exception.ServerDeletionConfirmed)
                {
                    /*
                     * Die Servermail wurde sicher entfernt und
                     * die lokale Datei existiert.
                     *
                     * Lediglich der abschließende DB-Status
                     * konnte nicht vollständig persistiert
                     * werden.
                     *
                     * Der Batch wird deshalb sofort gestoppt.
                     */
                    failure =
                        new ArchiveBatchFailure(
                            Subject:
                                message.Subject,

                            Title:
                                "Archivstatus prüfen",

                            Message:
                                "Die Nachricht wurde lokal archiviert und die Serverkopie wurde erfolgreich entfernt.\n\n" +
                                "Der lokale Archivstatus konnte anschließend jedoch nicht vollständig gespeichert werden.\n\n" +
                                "Bitte archivieren Sie diese Nachricht nicht erneut.\n\n" +
                                "Fehler:\n" +
                                exception.Message,

                            Image:
                                MessageBoxImage.Warning);

                    break;
                }
                catch (MailArchiveServerDeletionException exception)
                    when (exception.ServerStateUncertain)
                {
                    /*
                     * Sobald der Serverzustand nicht eindeutig
                     * feststeht, darf keine weitere Nachricht
                     * dieses Auswahlvorgangs verarbeitet werden.
                     */
                    failure =
                        new ArchiveBatchFailure(
                            Subject:
                                message.Subject,

                            Title:
                                "Serverzustand prüfen",

                            Message:
                                "Die Nachricht wurde erfolgreich lokal archiviert.\n\n" +
                                "Während der Entfernung der Serverkopie konnte der endgültige Serverzustand jedoch nicht sicher festgestellt werden.\n\n" +
                                "Bitte aktualisieren Sie das Postfach und archivieren Sie diese Nachricht nicht sofort erneut.\n\n" +
                                "Fehler:\n" +
                                exception.Message,

                            Image:
                                MessageBoxImage.Warning);

                    break;
                }
                catch (MailArchiveServerDeletionException exception)
                {
                    failure =
                        new ArchiveBatchFailure(
                            Subject:
                                message.Subject,

                            Title:
                                "Serverkopie konnte nicht entfernt werden",

                            Message:
                                "Die Nachricht wurde lokal archiviert, konnte aber nicht sicher vom Mailserver entfernt werden.\n\n" +
                                "Die lokale Archivkopie bleibt erhalten.\n\n" +
                                "Fehler:\n" +
                                exception.Message,

                            Image:
                                MessageBoxImage.Error);

                    break;
                }
                catch (Exception exception)
                {
                    /*
                     * Jeder sonstige Fehler stoppt die
                     * Mehrfachverarbeitung.
                     *
                     * Bereits vollständig abgeschlossene
                     * vorherige Nachrichten werden nicht
                     * zurückgerollt.
                     */
                    failure =
                        new ArchiveBatchFailure(
                            Subject:
                                message.Subject,

                            Title:
                                "Archivierung fehlgeschlagen",

                            Message:
                                "Die Nachricht konnte nicht vollständig archiviert werden.\n\n" +
                                "Eine Serverkopie wird nur dann entfernt, wenn die lokale Archivierung zuvor vollständig erfolgreich und verifiziert war.\n\n" +
                                "Fehler:\n" +
                                exception.Message,

                            Image:
                                MessageBoxImage.Error);

                    break;
                }
            }
        }
        catch (Exception exception)
        {
            /*
             * Fehler außerhalb der Verarbeitung einer
             * konkreten Nachricht, beispielsweise beim
             * Auflösen eines benötigten Dienstes.
             */
            failure =
                new ArchiveBatchFailure(
                    Subject:
                        string.Empty,

                    Title:
                        "Archivierung fehlgeschlagen",

                    Message:
                        "Die Archivierung konnte nicht gestartet oder vollständig durchgeführt werden.\n\n" +
                        "Fehler:\n" +
                        exception.Message,

                    Image:
                        MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor =
                null;

            _isArchiveOperationInProgress =
                false;
        }

        /*
         * Nach einem Batch wird das Postfach genau einmal
         * aktualisiert.
         *
         * Dadurch vermeiden wir nach jeder einzelnen Mail
         * einen kompletten UI-Reload.
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

        if (failure is not null)
        {
            ShowArchiveBatchFailure(
                messagesToArchive.Count,
                successfullyArchivedCount,
                failure,
                refreshNotice);

            return;
        }

        ShowArchiveSuccess(
            messagesToArchive,
            successfullyArchivedFiles,
            refreshNotice);
    }

    private string CreateArchiveConfirmationText(
        IReadOnlyList<MailMessageItemViewModel> messages,
        string folderDisplayName)
    {
        if (messages.Count == 1)
        {
            var message =
                messages[0];

            return
                "Die Nachricht wird vollständig im lokalen Telenec-Mail-Archiv auf diesem Computer gespeichert.\n\n" +
                $"Betreff:\n{message.Subject}\n\n" +
                $"Ordner:\n{folderDisplayName}\n\n" +
                "Nach erfolgreicher Speicherung und Prüfung wird die Nachricht endgültig vom Mailserver entfernt. " +
                "Sie wird nicht in den Papierkorb verschoben.\n\n" +
                "Das lokale Mailarchiv befindet sich im Ordner\n" +
                "„Dokumente\\Telenec Mail Archiv“.\n\n" +
                "Wichtig: Das lokale Archiv ist kein Server- oder Cloud-Backup. " +
                "Stellen Sie sicher, dass der Archivordner in Ihrer Datensicherung enthalten ist.\n\n" +
                "Möchten Sie die Nachricht jetzt archivieren?";
        }

        var messagePreview =
            CreateArchiveSelectionPreview(
                messages);

        return
            $"{messages.Count} ausgewählte Nachrichten werden vollständig im lokalen Telenec-Mail-Archiv auf diesem Computer gespeichert.\n\n" +
            $"Ordner:\n{folderDisplayName}\n\n" +
            $"Auswahl:\n{messagePreview}\n\n" +
            "Jede Nachricht wird einzeln vollständig gespeichert und geprüft. " +
            "Erst danach wird ihre jeweilige Serverkopie endgültig entfernt. " +
            "Die Nachrichten werden nicht in den Papierkorb verschoben.\n\n" +
            "Falls bei einer Nachricht ein Fehler auftritt, wird der Vorgang an dieser Stelle gestoppt. " +
            "Bereits erfolgreich archivierte Nachrichten bleiben archiviert.\n\n" +
            "Das lokale Mailarchiv befindet sich im Ordner\n" +
            "„Dokumente\\Telenec Mail Archiv“.\n\n" +
            "Wichtig: Das lokale Archiv ist kein Server- oder Cloud-Backup. " +
            "Stellen Sie sicher, dass der Archivordner in Ihrer Datensicherung enthalten ist.\n\n" +
            $"Möchten Sie die {messages.Count} Nachrichten jetzt archivieren?";
    }

    private static string
        CreateArchiveSelectionPreview(
            IReadOnlyList<MailMessageItemViewModel> messages)
    {
        var previewMessages =
            messages
                .Take(
                    MaximumPreviewMessageCount)
                .Select(
                    message =>
                        "• " +
                        CreateArchivePreviewSubject(
                            message.Subject))
                .ToList();

        if (messages.Count >
            MaximumPreviewMessageCount)
        {
            previewMessages.Add(
                $"… und {messages.Count - MaximumPreviewMessageCount} weitere");
        }

        return string.Join(
            Environment.NewLine,
            previewMessages);
    }

    private static string
        CreateArchivePreviewSubject(
            string? subject)
    {
        var normalizedSubject =
            string.IsNullOrWhiteSpace(
                subject)
                ? "(Ohne Betreff)"
                : subject
                    .Trim()
                    .Replace(
                        "\r",
                        " ")
                    .Replace(
                        "\n",
                        " ");

        if (normalizedSubject.Length <=
            MaximumPreviewSubjectLength)
        {
            return normalizedSubject;
        }

        return
            normalizedSubject[
                ..MaximumPreviewSubjectLength]
                .TrimEnd() +
            "…";
    }

    private void ShowArchiveSuccess(
        IReadOnlyList<MailMessageItemViewModel> messages,
        IReadOnlyList<string> archivedFiles,
        string refreshNotice)
    {
        if (messages.Count == 1)
        {
            var archivedFile =
                archivedFiles.Count > 0
                    ? archivedFiles[0]
                    : "Dokumente\\Telenec Mail Archiv";

            MessageBox.Show(
                this,
                "Die Nachricht wurde erfolgreich archiviert und vom Mailserver entfernt.\n\n" +
                $"Betreff:\n{messages[0].Subject}\n\n" +
                $"Archivdatei:\n{archivedFile}\n\n" +
                "Bitte berücksichtigen Sie den Ordner „Telenec Mail Archiv“ in Ihrer regelmäßigen Datensicherung." +
                refreshNotice,
                "Archivierung abgeschlossen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        MessageBox.Show(
            this,
            $"{messages.Count} Nachrichten wurden erfolgreich archiviert und vom Mailserver entfernt.\n\n" +
            "Alle ausgewählten Nachrichten wurden vollständig lokal gespeichert und verifiziert.\n\n" +
            "Bitte berücksichtigen Sie den Ordner „Telenec Mail Archiv“ in Ihrer regelmäßigen Datensicherung." +
            refreshNotice,
            "Archivierung abgeschlossen",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowArchiveBatchFailure(
        int totalMessageCount,
        int successfullyArchivedCount,
        ArchiveBatchFailure failure,
        string refreshNotice)
    {
        var progressText =
            successfullyArchivedCount == 0
                ? "Vor dem Fehler wurde noch keine Nachricht dieses Vorgangs vollständig archiviert."
                : successfullyArchivedCount == 1
                    ? "1 Nachricht wurde vor dem Fehler bereits vollständig archiviert."
                    : $"{successfullyArchivedCount} Nachrichten wurden vor dem Fehler bereits vollständig archiviert.";

        var remainingCount =
            totalMessageCount -
            successfullyArchivedCount -
            1;

        var remainingText =
            remainingCount <= 0
                ? string.Empty
                : remainingCount == 1
                    ? "\n\n1 weitere ausgewählte Nachricht wurde anschließend nicht mehr verarbeitet."
                    : $"\n\n{remainingCount} weitere ausgewählte Nachrichten wurden anschließend nicht mehr verarbeitet.";

        var subjectText =
            string.IsNullOrWhiteSpace(
                failure.Subject)
                ? string.Empty
                : "\n\nBetroffene Nachricht:\n" +
                  failure.Subject;

        MessageBox.Show(
            this,
            failure.Message +
            subjectText +
            "\n\n" +
            progressText +
            remainingText +
            "\n\nBereits vollständig archivierte Nachrichten bleiben archiviert." +
            refreshNotice,
            failure.Title,
            MessageBoxButton.OK,
            failure.Image);
    }

    private sealed record ArchiveBatchFailure(
        string Subject,
        string Title,
        string Message,
        MessageBoxImage Image);
}