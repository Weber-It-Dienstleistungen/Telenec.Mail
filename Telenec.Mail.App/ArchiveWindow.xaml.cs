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

        /*
         * Die drei benötigten Abhängigkeiten sind bereits im
         * vorhandenen DI-Container registriert.
         *
         * Deshalb ist keine Änderung an App.xaml.cs nötig.
         */
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

        ArchiveMessageListBox.SelectedItem =
            null;

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
        var selectedMessage =
            ArchiveMessageListBox.SelectedItem
                as LocalMailArchiveMessageInfo;

        RestoreArchiveMessageButton.IsEnabled =
            !_isLoading &&
            !_isRestoringArchiveMessage &&
            selectedMessage is not null &&
            selectedMessage.LocalFileState ==
                LocalMailArchiveFileState.Available &&
            selectedMessage.ServerDeleted &&
            string.Equals(
                selectedMessage.OperationStatus,
                "Completed",
                StringComparison.Ordinal);
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

        ArchiveMessageListBox.SelectedItem =
            null;

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
        if (_isRestoringArchiveMessage ||
            ArchiveMessageListBox.SelectedItem
                is not LocalMailArchiveMessageInfo archiveMessage)
        {
            return;
        }

        if (archiveMessage.LocalFileState !=
                LocalMailArchiveFileState.Available ||
            !archiveMessage.ServerDeleted)
        {
            MessageBox.Show(
                this,
                "Diese Archivnachricht kann derzeit nicht sicher wiederhergestellt werden.",
                "Lokales Mailarchiv",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var subject =
            string.IsNullOrWhiteSpace(
                archiveMessage.Subject)
                ? "(Kein Betreff)"
                : archiveMessage.Subject;

        var confirmation =
            MessageBox.Show(
                this,
                "Soll diese Nachricht wieder auf den Mailserver zurückverschoben werden?\n\n" +
                $"Betreff: {subject}\n" +
                $"Zielordner: {archiveMessage.SourceFolderId}\n\n" +
                "Die lokale Archivkopie wird erst entfernt, nachdem die neue Serverkopie vollständig hochgeladen " +
                "und erneut anhand von Dateigröße und SHA-256 geprüft wurde.",
                "Nachricht wiederherstellen",
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

        try
        {
            var result =
                await _archiveRestoreService
                    .RestoreAsync(
                        archiveMessage
                            .ArchiveMessageId);

            /*
             * Nach erfolgreichem Restore wird archive.db
             * komplett neu eingelesen.
             *
             * Die Nachricht verschwindet dadurch sofort aus
             * dem Archiv und auch die Zähler sind aktuell.
             */
            await LoadArchiveAsync(
                preferredArchiveFolderId);

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
            }
            else
            {
                MessageBox.Show(
                    this,
                    successMessage,
                    "Nachricht wiederhergestellt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (MailArchiveRestoreException exception)
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

            MessageBox.Show(
                this,
                message,
                "Wiederherstellung nicht abgeschlossen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Die Archivnachricht konnte nicht wiederhergestellt werden.\n\n" +
                exception.Message,
                "Lokales Mailarchiv",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor =
                null;

            _isRestoringArchiveMessage =
                false;

            UpdateRestoreButtonState();
        }
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
}