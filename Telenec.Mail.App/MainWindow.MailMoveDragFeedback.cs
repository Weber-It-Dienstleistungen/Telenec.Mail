using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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