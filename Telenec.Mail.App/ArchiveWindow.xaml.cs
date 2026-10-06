using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.Services.Archive;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ArchiveWindow : Window
{
    private readonly LocalMailArchiveReader
        _archiveReader;

    private readonly LocalMailArchiveMessageLoader
        _archiveMessageLoader;

    private LocalMailArchiveSnapshot?
        _archiveSnapshot;

    private ICollectionView?
        _archiveMessagesView;

    private bool
        _isLoading;

    private bool
        _isOpeningArchiveMessage;

    public ArchiveWindow(
        LocalMailArchiveReader archiveReader)
    {
        ArgumentNullException.ThrowIfNull(
            archiveReader);

        _archiveReader =
            archiveReader;

        /*
         * Der Loader ist vollständig zustandslos und besitzt
         * keine externen Abhängigkeiten.
         *
         * Deshalb kann er hier direkt erzeugt werden.
         */
        _archiveMessageLoader =
            new LocalMailArchiveMessageLoader();

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

    private async void ArchiveMessageListBox_OnMouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isOpeningArchiveMessage)
        {
            return;
        }

        /*
         * OriginalSource kann nicht nur ein klassisches
         * WPF-Visual sein.
         *
         * Insbesondere ein <Run> innerhalb eines TextBlocks
         * ist ein FrameworkContentElement und gehört zum
         * logischen Content-Baum.
         *
         * Die Hilfsfunktion unten kann deshalb sowohl durch
         * den Visual- als auch durch den Content-Baum laufen.
         */
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
            /*
             * Vor jedem Öffnen wird die lokale Datei erneut
             * anhand von Dateigröße und SHA-256 gegen den
             * Archivdatensatz geprüft.
             */
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

            /*
             * Wie bei einer normalen Mail verwenden wir
             * bewusst Show() statt ShowDialog().
             *
             * Mehrere Archivnachrichten können dadurch
             * parallel geöffnet werden.
             */
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
        /*
         * Normale WPF-Steuerelemente liegen im Visual Tree.
         */
        if (element is Visual)
        {
            return VisualTreeHelper
                .GetParent(
                    element);
        }

        /*
         * Inline-Elemente wie Run sind dagegen keine Visuals,
         * sondern ContentElements.
         *
         * VisualTreeHelper.GetParent() würde für sie eine
         * InvalidOperationException werfen.
         */
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

            /*
             * Run, Span usw. sind normalerweise
             * FrameworkContentElements. Deren logischer Parent
             * bringt uns wieder Richtung TextBlock/ListBoxItem.
             */
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