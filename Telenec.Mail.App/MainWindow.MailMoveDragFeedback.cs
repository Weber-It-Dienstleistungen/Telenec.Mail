using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private static readonly bool
        MailMoveDragFeedbackClassHandlerRegistered =
            RegisterMailMoveDragFeedbackClassHandler();

    private ListBoxItem?
        _mailMoveFeedbackTargetContainer;

    private static bool
        RegisterMailMoveDragFeedbackClassHandler()
    {
        /*
         * Normales Drag-Feedback.
         */
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            DragDrop.DragOverEvent,
            new DragEventHandler(
                MainWindow_OnMailMoveDragOver),
            handledEventsToo:
                true);

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            DragDrop.DragLeaveEvent,
            new DragEventHandler(
                MainWindow_OnMailMoveDragLeave),
            handledEventsToo:
                true);

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            DragDrop.DropEvent,
            new DragEventHandler(
                MainWindow_OnMailMoveDrop),
            handledEventsToo:
                true);

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            DragDrop.QueryContinueDragEvent,
            new QueryContinueDragEventHandler(
                MainWindow_OnMailMoveQueryContinueDrag),
            handledEventsToo:
                true);

        /*
         * Der Drop auf die Ordnerliste wird bewusst bereits
         * auf ListBox-Ebene übernommen.
         *
         * Dadurch können wir die sichtbare Reaktion vom
         * vollständigen IMAP-/Synchronisationsvorgang
         * entkoppeln.
         *
         * Der vorhandene FolderListBox_OnDrop-Handler wird für
         * diesen Drop nicht zusätzlich ausgeführt, weil das
         * RoutedEvent hier als behandelt markiert wird.
         */
        EventManager.RegisterClassHandler(
            typeof(ListBox),
            DragDrop.DropEvent,
            new DragEventHandler(
                FolderListBox_OnImmediateMailMoveDrop),
            handledEventsToo:
                true);

        return true;
    }

    private static void MainWindow_OnMailMoveDragOver(
        object sender,
        DragEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        if (!TryGetDraggedMessages(
                e.Data,
                out var messages) ||
            messages.Count == 0)
        {
            window.ClearMailMoveTargetHighlight();

            return;
        }

        var targetFolder =
            window.GetFolderFromElement(
                e.OriginalSource as DependencyObject);

        var sourceFolder =
            window._viewModel.SelectedFolder;

        var targetContainer =
            window.GetFolderContainerFromElement(
                e.OriginalSource as DependencyObject);

        if (targetFolder is null ||
            targetContainer is null ||
            sourceFolder is null ||
            string.Equals(
                targetFolder.FolderId,
                sourceFolder.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            window.ClearMailMoveTargetHighlight();

            return;
        }

        window.HighlightMailMoveTarget(
            targetContainer);
    }

    private static void MainWindow_OnMailMoveDragLeave(
        object sender,
        DragEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        var position =
            e.GetPosition(
                window.FolderListBox);

        var isStillInsideFolderList =
            position.X >= 0 &&
            position.Y >= 0 &&
            position.X <=
                window.FolderListBox.ActualWidth &&
            position.Y <=
                window.FolderListBox.ActualHeight;

        if (isStillInsideFolderList)
        {
            return;
        }

        window.ClearMailMoveTargetHighlight();
    }

    private static void MainWindow_OnMailMoveDrop(
        object sender,
        DragEventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.ClearMailMoveTargetHighlight();
        }
    }

    private static void MainWindow_OnMailMoveQueryContinueDrag(
        object sender,
        QueryContinueDragEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        if (e.EscapePressed ||
            !e.KeyStates.HasFlag(
                DragDropKeyStates.LeftMouseButton))
        {
            window.ClearMailMoveTargetHighlight();
        }
    }

    private static async void
        FolderListBox_OnImmediateMailMoveDrop(
            object sender,
            DragEventArgs e)
    {
        if (sender is not ListBox folderListBox)
        {
            return;
        }

        if (Window.GetWindow(
                folderListBox)
            is not MainWindow window)
        {
            return;
        }

        /*
         * Der Klassenhandler gilt grundsätzlich für alle
         * ListBoxen.
         *
         * Tatsächlich übernehmen wir aber ausschließlich den
         * Drop auf die Ordnernavigation.
         */
        if (!ReferenceEquals(
                folderListBox,
                window.FolderListBox))
        {
            return;
        }

        e.Handled =
            true;

        e.Effects =
            DragDropEffects.None;

        window.ClearMailMoveTargetHighlight();

        if (window._viewModel.IsLoading ||
            !TryGetDraggedMessages(
                e.Data,
                out var messages) ||
            messages.Count == 0)
        {
            return;
        }

        var targetFolder =
            window.GetFolderFromElement(
                e.OriginalSource as DependencyObject);

        var sourceFolder =
            window._viewModel.SelectedFolder;

        if (targetFolder is null ||
            sourceFolder is null ||
            string.Equals(
                targetFolder.FolderId,
                sourceFolder.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Effects =
            DragDropEffects.Move;

        await window
            .MoveMessagesWithImmediateFeedbackAsync(
                messages,
                sourceFolder,
                targetFolder);
    }

    private async Task
        MoveMessagesWithImmediateFeedbackAsync(
            IReadOnlyList<MailMessageItemViewModel> messages,
            MailFolderItemViewModel sourceFolder,
            MailFolderItemViewModel targetFolder)
    {
        /*
         * Wichtig:
         *
         * MoveMessagesAsync läuft synchron bis zum ersten
         * echten await.
         *
         * Damit hat das ViewModel die zu verschiebenden
         * Nachrichten bereits validiert und als eigene Liste
         * übernommen, bevor wir sie anschließend rein visuell
         * aus der ObservableCollection entfernen.
         */
        var moveTask =
            _viewModel.MoveMessagesAsync(
                messages,
                targetFolder);

        var immediateFeedbackApplied =
            false;

        /*
         * Ist die Operation noch nicht abgeschlossen, geben wir
         * dem Benutzer sofort die sichtbare Rückmeldung.
         *
         * Ist sie bereits vollständig beendet, hat das
         * ViewModel ohnehin schon den endgültigen Zustand
         * synchronisiert.
         */
        if (!moveTask.IsCompleted)
        {
            immediateFeedbackApplied =
                ApplyImmediateMoveFeedback(
                    messages,
                    sourceFolder,
                    targetFolder);
        }

        try
        {
            var moveSucceeded =
                await moveTask;

            if (!moveSucceeded &&
                immediateFeedbackApplied)
            {
                await RestoreAuthoritativeMailboxStateAsync();
            }
        }
        catch
        {
            /*
             * Wurde die Mail nur lokal bereits ausgeblendet,
             * der serverseitige MOVE schlägt aber fehl, laden
             * wir unmittelbar wieder den autoritativen
             * Serverzustand.
             */
            if (immediateFeedbackApplied)
            {
                await RestoreAuthoritativeMailboxStateAsync();
            }

            var messageText =
                messages.Count == 1
                    ? "Die Nachricht konnte nicht verschoben werden."
                    : "Die Nachrichten konnten nicht verschoben werden.";

            MessageBox.Show(
                messageText +
                "\n\nBitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool ApplyImmediateMoveFeedback(
        IReadOnlyList<MailMessageItemViewModel> messages,
        MailFolderItemViewModel sourceFolder,
        MailFolderItemViewModel targetFolder)
    {
        /*
         * Hat der Benutzer während des laufenden Drops bereits
         * den Ordner gewechselt, dürfen wir keinesfalls die
         * inzwischen sichtbare Nachrichtenliste verändern.
         *
         * Der normale nachgeschaltete Serverabgleich übernimmt
         * in diesem Fall alles Weitere.
         */
        if (!ReferenceEquals(
                _viewModel.SelectedFolder,
                sourceFolder))
        {
            return false;
        }

        var visibleMessagesToRemove =
            messages
                .Where(
                    message =>
                        message is not null &&
                        _viewModel.Messages.Contains(
                            message))
                .Distinct()
                .ToList();

        if (visibleMessagesToRemove.Count == 0)
        {
            return false;
        }

        var movedUnreadCount =
            visibleMessagesToRemove.Count(
                message =>
                    message.IsUnread);

        var selectedMessageWasMoved =
            _viewModel.SelectedMessage is not null &&
            visibleMessagesToRemove.Contains(
                _viewModel.SelectedMessage);

        foreach (var message in visibleMessagesToRemove)
        {
            _viewModel.Messages.Remove(
                message);
        }

        if (selectedMessageWasMoved)
        {
            _viewModel.SelectedMessage =
                null;
        }

        UpdateFolderCountersAfterImmediateMove(
            sourceFolder,
            targetFolder,
            visibleMessagesToRemove.Count,
            movedUnreadCount);

        return true;
    }

    private static void
        UpdateFolderCountersAfterImmediateMove(
            MailFolderItemViewModel sourceFolder,
            MailFolderItemViewModel targetFolder,
            int movedMessageCount,
            int movedUnreadCount)
    {
        var sourceMessageCount =
            Math.Max(
                sourceFolder.MessageCount -
                movedMessageCount,
                0);

        var sourceUnreadCount =
            Math.Max(
                sourceFolder.UnreadCount -
                movedUnreadCount,
                0);

        sourceFolder.UpdateState(
            CreateFolderSubtitle(
                sourceUnreadCount,
                sourceMessageCount),
            sourceUnreadCount,
            sourceMessageCount);

        var targetMessageCount =
            Math.Max(
                targetFolder.MessageCount +
                movedMessageCount,
                0);

        var targetUnreadCount =
            Math.Max(
                targetFolder.UnreadCount +
                movedUnreadCount,
                0);

        targetFolder.UpdateState(
            CreateFolderSubtitle(
                targetUnreadCount,
                targetMessageCount),
            targetUnreadCount,
            targetMessageCount);
    }

    private static string CreateFolderSubtitle(
        int unreadCount,
        int messageCount)
    {
        return unreadCount > 0
            ? $"{unreadCount} ungelesene Nachrichten"
            : $"{messageCount} Nachrichten";
    }

    private async Task
        RestoreAuthoritativeMailboxStateAsync()
    {
        try
        {
            await _viewModel
                .ReloadAsync();
        }
        catch
        {
            /*
             * Der eigentliche Fehlerdialog wird vom aufrufenden
             * Verschiebevorgang angezeigt.
             *
             * Ein zusätzlicher Fehler beim Wiederherstellen
             * darf diesen nicht verdecken.
             */
        }
    }

    private ListBoxItem?
        GetFolderContainerFromElement(
            DependencyObject? element)
    {
        if (element is null)
        {
            return null;
        }

        return ItemsControl
            .ContainerFromElement(
                FolderListBox,
                element)
            as ListBoxItem;
    }

    private void HighlightMailMoveTarget(
        ListBoxItem targetContainer)
    {
        if (ReferenceEquals(
                _mailMoveFeedbackTargetContainer,
                targetContainer))
        {
            return;
        }

        ClearMailMoveTargetHighlight();

        _mailMoveFeedbackTargetContainer =
            targetContainer;

        targetContainer.SetResourceReference(
            Control.BackgroundProperty,
            "Brand.Primary");

        targetContainer.FontWeight =
            FontWeights.SemiBold;
    }

    private void ClearMailMoveTargetHighlight()
    {
        var targetContainer =
            _mailMoveFeedbackTargetContainer;

        _mailMoveFeedbackTargetContainer =
            null;

        if (targetContainer is null)
        {
            return;
        }

        targetContainer.ClearValue(
            Control.BackgroundProperty);

        targetContainer.ClearValue(
            Control.FontWeightProperty);
    }
}