using System.Windows;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    internal void OpenMessageWindowFromUi(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        var sourceFolderId =
            _viewModel
                .SelectedFolder?
                .FolderId;

        if (string.IsNullOrWhiteSpace(
                sourceFolderId))
        {
            return;
        }

        var sourceFolderDisplayName =
            _viewModel
                .SelectedFolder?
                .DisplayName
            ?? sourceFolderId;

        var sourceFolderWasTrash =
            _viewModel
                .IsTrashFolderSelected;

        var actions =
            new MailMessageWindowActions(
                ReplyAsync:
                    () =>
                        ExecuteOpenedMessageActionAsync(
                            message,
                            sourceFolderId,
                            sourceFolderDisplayName,
                            ReplyToMessageFromUiAsync),

                ReplyAllAsync:
                    () =>
                        ExecuteOpenedMessageActionAsync(
                            message,
                            sourceFolderId,
                            sourceFolderDisplayName,
                            ReplyAllToMessageFromUiAsync),

                ForwardAsync:
                    () =>
                        ExecuteOpenedMessageActionAsync(
                            message,
                            sourceFolderId,
                            sourceFolderDisplayName,
                            ForwardMessageFromUiAsync),

                PrimaryActionText:
                    sourceFolderWasTrash
                        ? "Wiederherstellen"
                        : "Löschen",

                PrimaryActionToolTip:
                    sourceFolderWasTrash
                        ? "Diese Nachricht aus dem Papierkorb wiederherstellen"
                        : "Diese Nachricht in den Papierkorb verschieben",

                PrimaryActionAsync:
                    () =>
                        ExecuteOpenedMessagePrimaryActionAsync(
                            message,
                            sourceFolderId,
                            sourceFolderDisplayName,
                            sourceFolderWasTrash),

                PermanentDeleteAsync:
                    sourceFolderWasTrash
                        ? () =>
                            ExecuteOpenedMessagePermanentDeleteAsync(
                                message,
                                sourceFolderId,
                                sourceFolderDisplayName)
                        : null);

        var messageWindow =
            new MailMessageWindow(
                message,
                actions)
            {
                Owner =
                    this
            };

        messageWindow.Show();
    }

    private async Task ExecuteOpenedMessageActionAsync(
        MailMessageItemViewModel originalMessage,
        string sourceFolderId,
        string sourceFolderDisplayName,
        Func<MailMessageItemViewModel, Task> action)
    {
        var currentMessage =
            ResolveOpenedMessage(
                originalMessage,
                sourceFolderId);

        if (currentMessage is null)
        {
            ShowOpenedMessageContextWarning(
                sourceFolderDisplayName);

            return;
        }

        await action(
            currentMessage);
    }

    private async Task<bool>
        ExecuteOpenedMessagePrimaryActionAsync(
            MailMessageItemViewModel originalMessage,
            string sourceFolderId,
            string sourceFolderDisplayName,
            bool sourceFolderWasTrash)
    {
        var currentMessage =
            ResolveOpenedMessage(
                originalMessage,
                sourceFolderId);

        if (currentMessage is null)
        {
            ShowOpenedMessageContextWarning(
                sourceFolderDisplayName);

            return false;
        }

        if (sourceFolderWasTrash)
        {
            if (!_viewModel
                    .IsTrashFolderSelected)
            {
                ShowOpenedMessageContextWarning(
                    sourceFolderDisplayName);

                return false;
            }

            await RestoreMessagesFromUiAsync(
                new[]
                {
                    currentMessage
                });
        }
        else
        {
            await DeleteMessagesFromUiAsync(
                new[]
                {
                    currentMessage
                });
        }

        /*
         * Ist die konkrete UID danach nicht mehr in diesem
         * Ordner vorhanden, war die Aktion erfolgreich genug,
         * um das geöffnete Nachrichtenfenster zu schließen.
         */
        return ResolveOpenedMessage(
                   originalMessage,
                   sourceFolderId)
               is null;
    }

    private async Task<bool>
        ExecuteOpenedMessagePermanentDeleteAsync(
            MailMessageItemViewModel originalMessage,
            string sourceFolderId,
            string sourceFolderDisplayName)
    {
        var currentMessage =
            ResolveOpenedMessage(
                originalMessage,
                sourceFolderId);

        if (currentMessage is null)
        {
            ShowOpenedMessageContextWarning(
                sourceFolderDisplayName);

            return false;
        }

        if (!_viewModel
                .IsTrashFolderSelected)
        {
            ShowOpenedMessageContextWarning(
                sourceFolderDisplayName);

            return false;
        }

        await DeleteMessagesPermanentlyFromUiAsync(
            new[]
            {
                currentMessage
            });

        return ResolveOpenedMessage(
                   originalMessage,
                   sourceFolderId)
               is null;
    }

    private MailMessageItemViewModel?
        ResolveOpenedMessage(
            MailMessageItemViewModel originalMessage,
            string sourceFolderId)
    {
        if (_viewModel.IsLoading ||
            !string.Equals(
                _viewModel
                    .SelectedFolder?
                    .FolderId,
                sourceFolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (originalMessage.UniqueId == 0)
        {
            return null;
        }

        return _viewModel
            .Messages
            .FirstOrDefault(
                candidate =>
                    candidate.UniqueId ==
                    originalMessage.UniqueId);
    }

    private void ShowOpenedMessageContextWarning(
        string sourceFolderDisplayName)
    {
        MessageBox.Show(
            this,
            "Diese Aktion kann aus Sicherheitsgründen nur ausgeführt werden, solange im Hauptfenster derselbe Mailordner geöffnet ist.\n\n" +
            $"Bitte wechseln Sie zurück zu „{sourceFolderDisplayName}“ und versuchen Sie es erneut.",
            "Nachrichtenaktion nicht möglich",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}