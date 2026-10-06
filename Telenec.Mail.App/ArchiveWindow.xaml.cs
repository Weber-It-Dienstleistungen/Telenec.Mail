using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.Services.Archive;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ArchiveWindow : Window
{
    private const int MaximumRestorePreviewMessageCount =
        5;

    private const int MaximumRestorePreviewSubjectLength =
        80;

    private readonly LocalMailArchiveReader
        _archiveReader;

    private readonly LocalMailArchiveMessageLoader
        _archiveMessageLoader;

    private readonly MailArchiveRestoreService
        _archiveRestoreService;

    private LocalMailArchiveSnapshot?
        _archiveSnapshot;

    private ICollectionView?
        _archiveMessagesView;

    private bool
        _isLoading;

    private bool
        _isOpeningArchiveMessage;

    private bool
        _isRestoringArchiveMessage;

    public ArchiveWindow(
        LocalMailArchiveReader archiveReader,
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        LocalMailArchiveStorage archiveStorage)
    {
        ArgumentNullException.ThrowIfNull(
            archiveReader);

        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        ArgumentNullException.ThrowIfNull(
            archiveStorage);

        _archiveReader =
            archiveReader;

        _archiveMessageLoader =
            new LocalMailArchiveMessageLoader();

        _archiveRestoreService =
            new MailArchiveRestoreService(
                mailAccountStore,
                credentialStore,
                archiveStorage);

        InitializeComponent();

        ArchiveMessageListBox.MouseDoubleClick +=
            ArchiveMessageListBox_OnMouseDoubleClick;
    }

    private async void ArchiveWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_archiveSnapshot is not null ||
            _isLoading)
        {
            return;
        }

        await LoadArchiveAsync();
    }

    private async Task LoadArchiveAsync(
        string? preferredArchiveFolderId = null)
    {
        _isLoading =
            true;

        ArchiveContentGrid.Visibility =
            Visibility.Collapsed;

        ArchiveStatusGrid.Visibility =
            Visibility.Visible;

        ArchiveStatusTitleText.Text =
            "Lokales Archiv wird geladen …";

        ArchiveStatusDetailText.Text =
            "Archivdatenbank und lokale Archivdateien werden geprüft.";

        OpenArchiveFolderButton.IsEnabled =
            false;

        RestoreArchiveMessageButton.IsEnabled =
            false;

        try
        {
            var snapshot =
                await _archiveReader
                    .LoadSnapshotAsync();

            _archiveSnapshot =
                snapshot;

            DataContext =
                snapshot;

            _archiveMessagesView =
                CollectionViewSource
                    .GetDefaultView(
                        snapshot.Messages);

            ArchiveMessageListBox.ItemsSource =
                _archiveMessagesView;

            ShowAllMessages();

            if (!string.IsNullOrWhiteSpace(
                    preferredArchiveFolderId))
            {
                var preferredFolder =
                    snapshot
                        .Folders
                        .FirstOrDefault(
                            folder =>
                                string.Equals(
                                    folder.ArchiveFolderId,
                                    preferredArchiveFolderId,
                                    StringComparison.Ordinal));

                if (preferredFolder is not null)
                {
                    ArchiveFolderListBox.SelectedItem =
                        preferredFolder;
                }
            }

            OpenArchiveFolderButton.IsEnabled =
                Directory.Exists(
                    snapshot
                        .Location
                        .AccountDirectory);

            ArchiveStatusGrid.Visibility =
                Visibility.Collapsed;

            ArchiveContentGrid.Visibility =
                Visibility.Visible;
        }
        catch (Exception exception)
        {
            _archiveSnapshot =
                null;

            _archiveMessagesView =
                null;

            ArchiveContentGrid.Visibility =
                Visibility.Collapsed;

            ArchiveStatusGrid.Visibility =
                Visibility.Visible;

            ArchiveStatusTitleText.Text =
                "Das lokale Archiv konnte nicht geladen werden.";

            ArchiveStatusDetailText.Text =
                exception.Message;
        }
        finally
        {
            _isLoading =
                false;

            UpdateRestoreButtonState();
        }
    }

    private void ArchiveFolderListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_archiveMessagesView is null ||
            ArchiveFolderListBox.SelectedItem
                is not LocalMailArchiveFolderInfo selectedFolder)
        {
            return;
        }

        _archiveMessagesView.Filter =
            item =>
                item is LocalMailArchiveMessageInfo message &&
                string.Equals(
                    message.ArchiveFolderId,
                    selectedFolder.ArchiveFolderId,
                    StringComparison.Ordinal);

        _archiveMessagesView.Refresh();

        ArchiveMessageListBox.UnselectAll();

        ArchiveMessageHeaderText.Text =
            $"Archivierte Nachrichten – {selectedFolder.DisplayName}";

        UpdateRestoreButtonState();
    }

    private void ArchiveMessageListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateRestoreButtonState();
    }

    private void UpdateRestoreButtonState()
    {
        var selectedMessages =
            GetSelectedArchiveMessages();

        RestoreArchiveMessageButton.IsEnabled =
            !_isLoading &&
            !_isRestoringArchiveMessage &&
            selectedMessages.Count > 0 &&
            selectedMessages.All(
                IsMessageEligibleForRestore);
    }

    private static bool IsMessageEligibleForRestore(
        LocalMailArchiveMessageInfo message)
    {
        return
            message.LocalFileState ==
                LocalMailArchiveFileState.Available &&
            message.ServerDeleted &&
            string.Equals(
                message.OperationStatus,
                "Completed",
                StringComparison.Ordinal);
    }

    private IReadOnlyList<LocalMailArchiveMessageInfo>
        GetSelectedArchiveMessages()
    {
        return ArchiveMessageListBox
            .SelectedItems
            .OfType<LocalMailArchiveMessageInfo>()
            .ToArray();
    }

    private void ShowAllMessagesButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ShowAllMessages();
    }

    private void ShowAllMessages()
    {
        if (_archiveMessagesView is null)
        {
            return;
        }

        ArchiveFolderListBox.SelectedItem =
            null;

        ArchiveMessageListBox.UnselectAll();

        _archiveMessagesView.Filter =
            null;

        _archiveMessagesView.Refresh();

        ArchiveMessageHeaderText.Text =
            "Archivierte Nachrichten";

        UpdateRestoreButtonState();
    }

    private async void RestoreArchiveMessageButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isRestoringArchiveMessage)
        {
            return;
        }

        var messagesToRestore =
            GetSelectedArchiveMessages();

        if (messagesToRestore.Count == 0)
        {
            return;
        }

        var invalidMessages =
            messagesToRestore
                .Where(
                    message =>
                        !IsMessageEligibleForRestore(
                            message))
                .ToArray();

        if (invalidMessages.Length > 0)
        {
            MessageBox.Show(
                this,
                invalidMessages.Length == 1
                    ? "Eine ausgewählte Archivnachricht kann derzeit nicht sicher wiederhergestellt werden."
                    : $"{invalidMessages.Length} ausgewählte Archivnachrichten können derzeit nicht sicher wiederhergestellt werden.",
                "Lokales Mailarchiv",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var confirmation =
            MessageBox.Show(
                this,
                CreateRestoreConfirmationText(
                    messagesToRestore),
                messagesToRestore.Count == 1
                    ? "Nachricht wiederherstellen"
                    : "Nachrichten wiederherstellen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        var preferredArchiveFolderId =
            (ArchiveFolderListBox.SelectedItem
                as LocalMailArchiveFolderInfo)?
                .ArchiveFolderId;

        _isRestoringArchiveMessage =
            true;

        UpdateRestoreButtonState();

        Mouse.OverrideCursor =
            Cursors.Wait;

        var successfullyRestoredCount =
            0;

        var restoreResults =
            new List<ArchiveRestoreResult>();

        ArchiveRestoreBatchFailure?
            failure =
                null;

        try
        {
            /*
             * Genau wie beim Archivieren werden mehrere
             * Nachrichten bewusst nacheinander verarbeitet.
             *
             * Jede Nachricht durchläuft vollständig den
             * bereits getesteten Restore-Sicherheitsablauf,
             * bevor die nächste Nachricht beginnt.
             */
            foreach (var archiveMessage in
                     messagesToRestore)
            {
                try
                {
                    var result =
                        await _archiveRestoreService
                            .RestoreAsync(
                                archiveMessage
                                    .ArchiveMessageId);

                    restoreResults.Add(
                        result);

                    successfullyRestoredCount++;
                }
                catch (MailArchiveRestoreException exception)
                {
                    failure =
                        new ArchiveRestoreBatchFailure(
                            Subject:
                                archiveMessage.Subject,

                            Title:
                                "Wiederherstellung nicht abgeschlossen",

                            Message:
                                CreateRestoreExceptionText(
                                    exception),

                            Image:
                                MessageBoxImage.Warning);

                    break;
                }
                catch (Exception exception)
                {
                    failure =
                        new ArchiveRestoreBatchFailure(
                            Subject:
                                archiveMessage.Subject,

                            Title:
                                "Wiederherstellung fehlgeschlagen",

                            Message:
                                "Die Nachricht konnte nicht vollständig wiederhergestellt werden.\n\n" +
                                "Die lokale Archivkopie wird nur dann entfernt, wenn die Serverkopie zuvor vollständig und bytegenau verifiziert wurde.\n\n" +
                                "Fehler:\n" +
                                exception.Message,

                            Image:
                                MessageBoxImage.Error);

                    break;
                }
            }
        }
        finally
        {
            /*
             * Die Archivansicht wird nach dem gesamten Batch
             * genau einmal neu geladen.
             *
             * Bereits erfolgreich wiederhergestellte
             * Nachrichten verschwinden damit aus dem Archiv.
             */
            try
            {
                await LoadArchiveAsync(
                    preferredArchiveFolderId);
            }
            catch
            {
            }

            Mouse.OverrideCursor =
                null;

            _isRestoringArchiveMessage =
                false;

            UpdateRestoreButtonState();
        }

        if (failure is not null)
        {
            ShowRestoreBatchFailure(
                messagesToRestore.Count,
                successfullyRestoredCount,
                failure);

            return;
        }

        ShowRestoreBatchSuccess(
            messagesToRestore,
            restoreResults);
    }

    private static string CreateRestoreExceptionText(
        MailArchiveRestoreException exception)
    {
        var message =
            exception.Message;

        if (exception.ServerCopyVerified)
        {
            message +=
                "\n\nDie Serverkopie wurde bereits vollständig verifiziert.";
        }

        if (exception.RetryIsSafe)
        {
            message +=
                "\n\nEin erneuter Wiederherstellungsversuch ist zulässig. " +
                "Telenec Mail prüft dabei zuerst, ob bereits eine Serverkopie existiert.";
        }
        else if (exception.RecoveryStateUnresolved)
        {
            message +=
                "\n\nEs wird aus Sicherheitsgründen kein automatischer Neu-Upload durchgeführt.";
        }

        return message;
    }

    private static string CreateRestoreConfirmationText(
        IReadOnlyList<LocalMailArchiveMessageInfo> messages)
    {
        if (messages.Count == 1)
        {
            var message =
                messages[0];

            var subject =
                NormalizeRestorePreviewSubject(
                    message.Subject);

            return
                "Soll diese Nachricht wieder auf den Mailserver zurückverschoben werden?\n\n" +
                $"Betreff:\n{subject}\n\n" +
                $"Zielordner:\n{message.SourceFolderId}\n\n" +
                "Die lokale Archivkopie wird erst entfernt, nachdem die neue Serverkopie vollständig hochgeladen " +
                "und erneut anhand von Dateigröße und SHA-256 geprüft wurde.";
        }

        var preview =
            messages
                .Take(
                    MaximumRestorePreviewMessageCount)
                .Select(
                    message =>
                        "• " +
                        NormalizeRestorePreviewSubject(
                            message.Subject) +
                        "\n  → " +
                        message.SourceFolderId)
                .ToList();

        if (messages.Count >
            MaximumRestorePreviewMessageCount)
        {
            preview.Add(
                $"… und {messages.Count - MaximumRestorePreviewMessageCount} weitere");
        }

        return
            $"{messages.Count} ausgewählte Archivnachrichten werden wieder auf den Mailserver zurückverschoben.\n\n" +
            "Jede Nachricht wird in ihren jeweils ursprünglichen Mailordner wiederhergestellt:\n\n" +
            string.Join(
                Environment.NewLine,
                preview) +
            "\n\nJede Nachricht wird einzeln hochgeladen und anschließend erneut anhand von Dateigröße und SHA-256 geprüft. " +
            "Erst danach wird ihre jeweilige lokale Archivkopie entfernt.\n\n" +
            "Falls bei einer Nachricht ein Fehler auftritt, wird der Vorgang an dieser Stelle gestoppt. " +
            "Bereits erfolgreich wiederhergestellte Nachrichten bleiben auf dem Mailserver.\n\n" +
            $"Möchten Sie die {messages.Count} Nachrichten jetzt wiederherstellen?";
    }

    private static string NormalizeRestorePreviewSubject(
        string? subject)
    {
        var normalized =
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

        if (normalized.Length <=
            MaximumRestorePreviewSubjectLength)
        {
            return normalized;
        }

        return
            normalized[
                ..MaximumRestorePreviewSubjectLength]
                .TrimEnd() +
            "…";
    }

    private void ShowRestoreBatchSuccess(
        IReadOnlyList<LocalMailArchiveMessageInfo> messages,
        IReadOnlyList<ArchiveRestoreResult> restoreResults)
    {
        if (messages.Count == 1)
        {
            var result =
                restoreResults[0];

            var successMessage =
                "Die Nachricht wurde erfolgreich wiederhergestellt.\n\n" +
                $"Zielordner: {result.SourceFolderId}\n" +
                $"Neue Server-UID: {result.RestoredUniqueId}";

            if (result.RecoveredInterruptedRestore)
            {
                successMessage +=
                    "\n\nEin zuvor unterbrochener Wiederherstellungsversuch wurde dabei sicher erkannt und abgeschlossen.";
            }

            if (!string.IsNullOrWhiteSpace(
                    result.Warning))
            {
                successMessage +=
                    "\n\nHinweis:\n" +
                    result.Warning;

                MessageBox.Show(
                    this,
                    successMessage,
                    "Nachricht wiederhergestellt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            MessageBox.Show(
                this,
                successMessage,
                "Nachricht wiederhergestellt",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var warnings =
            restoreResults
                .Where(
                    result =>
                        !string.IsNullOrWhiteSpace(
                            result.Warning))
                .Select(
                    result =>
                        result.Warning!)
                .ToArray();

        var recoveredCount =
            restoreResults.Count(
                result =>
                    result.RecoveredInterruptedRestore);

        var message =
            $"{messages.Count} Nachrichten wurden erfolgreich wiederhergestellt.\n\n" +
            "Alle ausgewählten Nachrichten wurden vollständig auf den Mailserver übertragen und anschließend bytegenau verifiziert.";

        if (recoveredCount > 0)
        {
            message +=
                recoveredCount == 1
                    ? "\n\n1 zuvor unterbrochener Wiederherstellungsversuch wurde dabei sicher erkannt und abgeschlossen."
                    : $"\n\n{recoveredCount} zuvor unterbrochene Wiederherstellungsversuche wurden dabei sicher erkannt und abgeschlossen.";
        }

        if (warnings.Length > 0)
        {
            message +=
                "\n\nHinweise:\n" +
                string.Join(
                    Environment.NewLine,
                    warnings);

            MessageBox.Show(
                this,
                message,
                "Nachrichten wiederhergestellt",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        MessageBox.Show(
            this,
            message,
            "Nachrichten wiederhergestellt",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowRestoreBatchFailure(
        int totalMessageCount,
        int successfullyRestoredCount,
        ArchiveRestoreBatchFailure failure)
    {
        var progressText =
            successfullyRestoredCount == 0
                ? "Vor dem Fehler wurde noch keine Nachricht dieses Vorgangs vollständig wiederhergestellt."
                : successfullyRestoredCount == 1
                    ? "1 Nachricht wurde vor dem Fehler bereits vollständig wiederhergestellt."
                    : $"{successfullyRestoredCount} Nachrichten wurden vor dem Fehler bereits vollständig wiederhergestellt.";

        var remainingCount =
            totalMessageCount -
            successfullyRestoredCount -
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
            "\n\nBereits vollständig wiederhergestellte Nachrichten bleiben auf dem Mailserver.",
            failure.Title,
            MessageBoxButton.OK,
            failure.Image);
    }

    private async void ArchiveMessageListBox_OnMouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isOpeningArchiveMessage)
        {
            return;
        }

        var listBoxItem =
            FindAncestor<ListBoxItem>(
                e.OriginalSource
                    as DependencyObject);

        if (listBoxItem is null ||
            ItemsControl.ItemsControlFromItemContainer(
                listBoxItem)
            != ArchiveMessageListBox ||
            listBoxItem.DataContext
                is not LocalMailArchiveMessageInfo archiveMessage)
        {
            return;
        }

        e.Handled =
            true;

        if (archiveMessage.LocalFileState !=
            LocalMailArchiveFileState.Available)
        {
            MessageBox.Show(
                this,
                "Diese Archivnachricht kann derzeit nicht geöffnet werden.\n\n" +
                "Die zugehörige lokale Archivdatei ist nicht in einem gültigen Zustand.",
                "Lokales Mailarchiv",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        _isOpeningArchiveMessage =
            true;

        Mouse.OverrideCursor =
            Cursors.Wait;

        try
        {
            var messageData =
                await _archiveMessageLoader
                    .LoadAsync(
                        archiveMessage);

            var messageViewModel =
                MailMessageItemViewModelFactory
                    .Create(
                        messageData);

            var messageWindow =
                new MailMessageWindow(
                    messageViewModel)
                {
                    Owner =
                        this
                };

            messageWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Die Archivnachricht konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Lokales Mailarchiv",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor =
                null;

            _isOpeningArchiveMessage =
                false;
        }
    }

    private static T?
        FindAncestor<T>(
            DependencyObject? element)
        where T : DependencyObject
    {
        var current =
            element;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current =
                GetParent(
                    current);
        }

        return null;
    }

    private static DependencyObject?
        GetParent(
            DependencyObject element)
    {
        if (element is Visual)
        {
            return VisualTreeHelper
                .GetParent(
                    element);
        }

        if (element is ContentElement contentElement)
        {
            var contentParent =
                ContentOperations
                    .GetParent(
                        contentElement);

            if (contentParent is not null)
            {
                return contentParent;
            }

            if (contentElement
                is FrameworkContentElement frameworkContentElement)
            {
                return frameworkContentElement
                    .Parent;
            }
        }

        return null;
    }

    private void OpenArchiveFolderButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var accountDirectory =
            _archiveSnapshot?
                .Location
                .AccountDirectory;

        if (string.IsNullOrWhiteSpace(
                accountDirectory) ||
            !Directory.Exists(
                accountDirectory))
        {
            MessageBox.Show(
                this,
                "Der lokale Archivordner konnte nicht gefunden werden.",
                "Lokales Mailarchiv",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        accountDirectory,

                    UseShellExecute =
                        true
                });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Der lokale Archivordner konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Lokales Mailarchiv",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private sealed record ArchiveRestoreBatchFailure(
        string Subject,
        string Title,
        string Message,
        MessageBoxImage Image);
}