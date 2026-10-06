using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Telenec.Mail.App.Services.Archive;

namespace Telenec.Mail.App;

public partial class ArchiveWindow : Window
{
    private readonly LocalMailArchiveReader
        _archiveReader;

    private LocalMailArchiveSnapshot?
        _archiveSnapshot;

    private ICollectionView?
        _archiveMessagesView;

    private bool
        _isLoading;

    public ArchiveWindow(
        LocalMailArchiveReader archiveReader)
    {
        ArgumentNullException.ThrowIfNull(
            archiveReader);

        _archiveReader =
            archiveReader;

        InitializeComponent();
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

    private async Task LoadArchiveAsync()
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

        /*
         * Ein Archivordner zeigt bewusst nur die Nachrichten,
         * die direkt aus genau diesem ursprünglichen
         * IMAP-Ordner archiviert wurden.
         *
         * Unterordner bleiben eigenständige Archivordner.
         */
        _archiveMessagesView.Filter =
            item =>
                item is LocalMailArchiveMessageInfo message &&
                string.Equals(
                    message.ArchiveFolderId,
                    selectedFolder.ArchiveFolderId,
                    StringComparison.Ordinal);

        _archiveMessagesView.Refresh();

        ArchiveMessageHeaderText.Text =
            $"Archivierte Nachrichten – {selectedFolder.DisplayName}";
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

        _archiveMessagesView.Filter =
            null;

        _archiveMessagesView.Refresh();

        ArchiveMessageHeaderText.Text =
            "Archivierte Nachrichten";
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