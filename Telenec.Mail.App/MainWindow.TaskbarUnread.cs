using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _taskbarUnreadBadgeInitialized;

    private MailFolderItemViewModel?
        _taskbarInboxFolder;

    /*
     * Zeigt die Zahl der ungelesenen Nachrichten aus dem
     * Posteingang als Overlay auf dem Windows-Taskleistensymbol.
     *
     * Bewusst wird ausschließlich INBOX verwendet.
     *
     * Ungelesene Nachrichten in Archiv-, Papierkorb-,
     * Unterordnern usw. sollen den sichtbaren
     * "Neue Nachrichten"-Zähler nicht beeinflussen.
     */
    private void InitializeTaskbarUnreadBadge()
    {
        if (_taskbarUnreadBadgeInitialized)
        {
            return;
        }

        _taskbarUnreadBadgeInitialized =
            true;

        TaskbarItemInfo ??=
            new TaskbarItemInfo();

        _viewModel
            .MailFolders
            .CollectionChanged +=
                MailFolders_OnCollectionChangedForTaskbarUnread;

        Closed +=
            MainWindow_TaskbarUnread_OnClosed;

        RebindTaskbarInboxFolder();
    }

    private void MainWindow_TaskbarUnread_OnClosed(
        object? sender,
        EventArgs e)
    {
        _viewModel
            .MailFolders
            .CollectionChanged -=
                MailFolders_OnCollectionChangedForTaskbarUnread;

        if (_taskbarInboxFolder is not null)
        {
            _taskbarInboxFolder.PropertyChanged -=
                TaskbarInboxFolder_OnPropertyChanged;

            _taskbarInboxFolder =
                null;
        }

        Closed -=
            MainWindow_TaskbarUnread_OnClosed;
    }

    private void MailFolders_OnCollectionChangedForTaskbarUnread(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    RebindTaskbarInboxFolder));

            return;
        }

        RebindTaskbarInboxFolder();
    }

    private void RebindTaskbarInboxFolder()
    {
        var inboxFolder =
            _viewModel
                .MailFolders
                .FirstOrDefault(
                    folder =>
                        string.Equals(
                            folder.FolderId,
                            "INBOX",
                            StringComparison.OrdinalIgnoreCase));

        if (ReferenceEquals(
                _taskbarInboxFolder,
                inboxFolder))
        {
            UpdateTaskbarUnreadBadge();
            return;
        }

        if (_taskbarInboxFolder is not null)
        {
            _taskbarInboxFolder.PropertyChanged -=
                TaskbarInboxFolder_OnPropertyChanged;
        }

        _taskbarInboxFolder =
            inboxFolder;

        if (_taskbarInboxFolder is not null)
        {
            _taskbarInboxFolder.PropertyChanged +=
                TaskbarInboxFolder_OnPropertyChanged;
        }

        UpdateTaskbarUnreadBadge();
    }

    private void TaskbarInboxFolder_OnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(
                e.PropertyName) &&
            !string.Equals(
                e.PropertyName,
                nameof(
                    MailFolderItemViewModel.UnreadCount),
                StringComparison.Ordinal))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    UpdateTaskbarUnreadBadge));

            return;
        }

        UpdateTaskbarUnreadBadge();
    }

    private void UpdateTaskbarUnreadBadge()
    {
        var taskbarItemInfo =
            TaskbarItemInfo;

        if (taskbarItemInfo is null)
        {
            return;
        }

        var unreadCount =
            Math.Max(
                _taskbarInboxFolder?.UnreadCount
                    ?? 0,
                0);

        if (unreadCount == 0)
        {
            taskbarItemInfo.Overlay =
                null;

            taskbarItemInfo.Description =
                "Telenec Mail";

            return;
        }

        taskbarItemInfo.Overlay =
            CreateTaskbarUnreadBadge(
                unreadCount);

        taskbarItemInfo.Description =
            unreadCount == 1
                ? "1 ungelesene Nachricht"
                : $"{unreadCount} ungelesene Nachrichten";
    }

    private ImageSource CreateTaskbarUnreadBadge(
        int unreadCount)
    {
        const int badgeSize =
            32;

        var badgeText =
            unreadCount > 99
                ? "99+"
                : unreadCount.ToString(
                    CultureInfo.InvariantCulture);

        var visual =
            new DrawingVisual();

        using (var drawingContext =
               visual.RenderOpen())
        {
            var badgeBrush =
                TryFindResource(
                    "Brand.Primary")
                    as Brush
                ?? Brushes.DodgerBlue;

            drawingContext.DrawEllipse(
                badgeBrush,
                null,
                new Point(
                    badgeSize / 2d,
                    badgeSize / 2d),
                15,
                15);

            var fontSize =
                badgeText.Length switch
                {
                    1 => 18d,
                    2 => 15d,
                    _ => 10d
                };

            var formattedText =
                new FormattedText(
                    badgeText,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily(
                            "Segoe UI"),
                        FontStyles.Normal,
                        FontWeights.SemiBold,
                        FontStretches.Normal),
                    fontSize,
                    Brushes.White,
                    1d);

            var textPosition =
                new Point(
                    (badgeSize -
                     formattedText.Width) /
                    2d,

                    (badgeSize -
                     formattedText.Height) /
                    2d);

            drawingContext.DrawText(
                formattedText,
                textPosition);
        }

        var bitmap =
            new RenderTargetBitmap(
                badgeSize,
                badgeSize,
                96,
                96,
                PixelFormats.Pbgra32);

        bitmap.Render(
            visual);

        bitmap.Freeze();

        return bitmap;
    }
}