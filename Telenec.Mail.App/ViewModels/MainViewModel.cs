using MailKit.Security;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private readonly IMailDataSource _mailDataSource;

    private int _mailMoveOperationState;

    private MailFolderItemViewModel? _selectedFolder;
    private MailMessageItemViewModel? _selectedMessage;

    private MailMoveResult? _lastMoveOperation;

    private CancellationTokenSource?
        _folderLoadCancellationSource;

    private bool _isInitialized;
    private bool _isLoading;
    private bool _hasLoadError;
    private bool _isEmptyFolder;

    private string _loadingMessage =
        "Postfach wird geladen …";

    private string _loadErrorMessage =
        string.Empty;

    private string _connectionStatusText =
        "Verbindung wird hergestellt …";

    private MailConnectionState _connectionState =
        MailConnectionState.Connecting;

    public MainViewModel(
        IMailDataSource mailDataSource)
    {
        ArgumentNullException.ThrowIfNull(
            mailDataSource);

        _mailDataSource =
            mailDataSource;

        MailFolders =
            new ObservableCollection<
                MailFolderItemViewModel>();

        Messages =
            new ObservableCollection<
                MailMessageItemViewModel>();
    }

    public string ApplicationTitle =>
        "Telenec Mail";

    public ObservableCollection<
        MailFolderItemViewModel> MailFolders
    { get; }

    public ObservableCollection<
        MailMessageItemViewModel> Messages
    { get; }

    public bool CanUndoLastMove =>
        _lastMoveOperation?.CanUndo == true &&
        !IsLoading &&
        !IsMailMoveOperationRunning;

    public bool IsTrashFolderSelected =>
        _selectedFolder is not null &&
        string.Equals(
            _selectedFolder.DisplayName,
            "Papierkorb",
            StringComparison.OrdinalIgnoreCase);

    public string MessageActionToolTip =>
        IsTrashFolderSelected
            ? "Nachricht wiederherstellen"
            : "Nachricht löschen";

    public string MessageActionGlyph =>
        IsTrashFolderSelected
            ? "\uE72B"
            : "\uE74D";

    private bool IsMailMoveOperationRunning =>
        Volatile.Read(
            ref _mailMoveOperationState) != 0;

    public MailFolderItemViewModel? SelectedFolder
    {
        get => _selectedFolder;

        set
        {
            if (ReferenceEquals(
                    _selectedFolder,
                    value))
            {
                return;
            }

            _selectedFolder =
                value;

            OnPropertyChanged();

            NotifySelectedFolderActionStateChanged();

            if (_isInitialized &&
                value is not null)
            {
                _ =
                    LoadFolderMessagesAsync(
                        value);
            }
        }
    }

    public MailMessageItemViewModel? SelectedMessage
    {
        get => _selectedMessage;

        set
        {
            if (ReferenceEquals(
                    _selectedMessage,
                    value))
            {
                return;
            }

            _selectedMessage =
                value;

            OnPropertyChanged();

            var selectedFolder =
                _selectedFolder;

            if (_isInitialized &&
                selectedFolder is not null &&
                value is not null &&
                value.IsUnread)
            {
                _ =
                    MarkMessageAsReadAsync(
                        selectedFolder,
                        value);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;

        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanUndoLastMove));
        }
    }

    public string LoadingMessage
    {
        get => _loadingMessage;

        private set
        {
            if (_loadingMessage == value)
            {
                return;
            }

            _loadingMessage =
                value;

            OnPropertyChanged();
        }
    }

    public bool HasLoadError
    {
        get => _hasLoadError;

        private set
        {
            if (_hasLoadError == value)
            {
                return;
            }

            _hasLoadError =
                value;

            OnPropertyChanged();
        }
    }

    public string LoadErrorMessage
    {
        get => _loadErrorMessage;

        private set
        {
            if (_loadErrorMessage == value)
            {
                return;
            }

            _loadErrorMessage =
                value;

            OnPropertyChanged();
        }
    }

    public bool IsEmptyFolder
    {
        get => _isEmptyFolder;

        private set
        {
            if (_isEmptyFolder == value)
            {
                return;
            }

            _isEmptyFolder =
                value;

            OnPropertyChanged();
        }
    }

    public MailConnectionState ConnectionState
    {
        get => _connectionState;

        private set
        {
            if (_connectionState == value)
            {
                return;
            }

            _connectionState =
                value;

            OnPropertyChanged();
        }
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;

        private set
        {
            if (_connectionStatusText == value)
            {
                return;
            }

            _connectionStatusText =
                value;

            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await InitializeCoreAsync(
            preferredFolderId: null,
            cancellationToken);
    }

    public async Task ReloadAsync(
        CancellationToken cancellationToken = default)
    {
        var preferredFolderId =
            SelectedFolder?.FolderId;

        CancelCurrentFolderLoad();

        _isInitialized =
            false;

        await InitializeCoreAsync(
            preferredFolderId,
            cancellationToken);
    }

    public async Task<bool> DownloadAttachmentAsync(
        MailMessageItemViewModel message,
        MailAttachmentData attachment,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        ArgumentNullException.ThrowIfNull(
            attachment);

        ArgumentNullException.ThrowIfNull(
            destination);

        var folder =
            _selectedFolder;

        if (folder is null ||
            message.UniqueId == 0 ||
            string.IsNullOrWhiteSpace(
                attachment.PartSpecifier) ||
            !Messages.Contains(
                message))
        {
            return false;
        }

        /*
         * Der übergebene Anhang muss tatsächlich aus der
         * ausgewählten Nachricht stammen.
         *
         * Damit verhindern wir, dass veraltete UI-Referenzen
         * versehentlich mit einer anderen Nachricht kombiniert
         * werden.
         */
        var attachmentBelongsToMessage =
            message
                .Attachments
                .Any(
                    currentAttachment =>
                        ReferenceEquals(
                            currentAttachment,
                            attachment));

        if (!attachmentBelongsToMessage)
        {
            return false;
        }

        /*
         * Ordner, UID und MIME-Part werden jetzt gemeinsam
         * an die Datenquelle übergeben.
         *
         * Die Datenquelle lädt ausschließlich diesen einen
         * Attachment-Part.
         */
        await _mailDataSource
            .DownloadAttachmentAsync(
                folder.FolderId,
                message.UniqueId,
                attachment.PartSpecifier,
                destination,
                cancellationToken);

        return true;
    }

    public async Task<bool> MarkMessageAsUnreadAsync(
        MailMessageItemViewModel message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        var folder =
            _selectedFolder;

        if (folder is null ||
            message.IsUnread ||
            message.UniqueId == 0 ||
            !Messages.Contains(
                message))
        {
            return false;
        }

        await _mailDataSource
            .MarkAsUnreadAsync(
                folder.FolderId,
                message.UniqueId,
                cancellationToken);

        if (message.IsUnread)
        {
            return true;
        }

        message.MarkAsUnread();

        folder.IncrementUnreadCount();

        return true;
    }

    public Task<bool> DeleteMessageAsync(
        MailMessageItemViewModel message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        return DeleteMessagesAsync(
            new[] { message },
            cancellationToken);
    }

    public async Task<bool> DeleteMessagesAsync(
        IReadOnlyList<MailMessageItemViewModel> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            messages);

        var folder =
            _selectedFolder;

        if (folder is null ||
            IsLoading ||
            messages.Count == 0)
        {
            return false;
        }

        var messagesToDelete =
            NormalizeMessages(
                messages);

        if (messagesToDelete.Count == 0)
        {
            return false;
        }

        var uniqueIds =
            messagesToDelete
                .Select(
                    message =>
                        message.UniqueId)
                .ToList();

        if (!TryBeginMailMoveOperation())
        {
            return false;
        }

        try
        {
            var moveResult =
                await _mailDataSource
                    .MoveToTrashAsync(
                        folder.FolderId,
                        uniqueIds,
                        cancellationToken);

            SetLastMoveOperation(
                moveResult);

            await ReloadAsync(
                cancellationToken);

            return true;
        }
        finally
        {
            EndMailMoveOperation();
        }
    }

    public async Task<bool> MoveMessagesAsync(
        IReadOnlyList<MailMessageItemViewModel> messages,
        MailFolderItemViewModel targetFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            messages);

        ArgumentNullException.ThrowIfNull(
            targetFolder);

        var sourceFolder =
            _selectedFolder;

        if (sourceFolder is null ||
            IsLoading ||
            messages.Count == 0 ||
            !MailFolders.Contains(
                targetFolder))
        {
            return false;
        }

        if (string.Equals(
                sourceFolder.FolderId,
                targetFolder.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var messagesToMove =
            NormalizeMessages(
                messages);

        if (messagesToMove.Count == 0)
        {
            return false;
        }

        var uniqueIds =
            messagesToMove
                .Select(
                    message =>
                        message.UniqueId)
                .ToList();

        if (!TryBeginMailMoveOperation())
        {
            return false;
        }

        try
        {
            var moveResult =
                await _mailDataSource
                    .MoveMessagesAsync(
                        sourceFolder.FolderId,
                        targetFolder.FolderId,
                        uniqueIds,
                        cancellationToken);

            SetLastMoveOperation(
                moveResult);

            await ReloadAsync(
                cancellationToken);

            return true;
        }
        finally
        {
            EndMailMoveOperation();
        }
    }

    public async Task<bool> UndoLastMoveAsync(
        CancellationToken cancellationToken = default)
    {
        var operation =
            _lastMoveOperation;

        if (operation is null ||
            !operation.CanUndo ||
            IsLoading)
        {
            return false;
        }

        var targetUniqueIds =
            operation.TargetUniqueIds;

        if (targetUniqueIds.Count == 0)
        {
            return false;
        }

        if (!TryBeginMailMoveOperation())
        {
            return false;
        }

        try
        {
            await _mailDataSource
                .MoveMessagesAsync(
                    operation.TargetFolderId,
                    operation.SourceFolderId,
                    targetUniqueIds,
                    cancellationToken);

            SetLastMoveOperation(
                null);

            await ReloadAsync(
                cancellationToken);

            return true;
        }
        finally
        {
            EndMailMoveOperation();
        }
    }

    public async Task<bool> RestoreMessagesFromTrashAsync(
        IReadOnlyList<MailMessageItemViewModel> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            messages);

        var trashFolder =
            _selectedFolder;

        if (trashFolder is null ||
            !IsTrashFolderSelected ||
            IsLoading ||
            messages.Count == 0)
        {
            return false;
        }

        var messagesToRestore =
            NormalizeMessages(
                messages);

        if (messagesToRestore.Count == 0)
        {
            return false;
        }

        var uniqueIds =
            messagesToRestore
                .Select(
                    message =>
                        message.UniqueId)
                .ToList();

        var restoreTargetFolderId =
            GetRestoreTargetFolderId(
                trashFolder,
                uniqueIds);

        if (string.IsNullOrWhiteSpace(
                restoreTargetFolderId))
        {
            return false;
        }

        if (!TryBeginMailMoveOperation())
        {
            return false;
        }

        try
        {
            await _mailDataSource
                .MoveMessagesAsync(
                    trashFolder.FolderId,
                    restoreTargetFolderId,
                    uniqueIds,
                    cancellationToken);

            RemoveRestoredMessagesFromLastMove(
                trashFolder.FolderId,
                uniqueIds);

            await ReloadAsync(
                cancellationToken);

            return true;
        }
        finally
        {
            EndMailMoveOperation();
        }
    }

    private bool TryBeginMailMoveOperation()
    {
        var previousState =
            Interlocked.CompareExchange(
                ref _mailMoveOperationState,
                1,
                0);

        if (previousState != 0)
        {
            return false;
        }

        OnPropertyChanged(
            nameof(CanUndoLastMove));

        return true;
    }

    private void EndMailMoveOperation()
    {
        Interlocked.Exchange(
            ref _mailMoveOperationState,
            0);

        OnPropertyChanged(
            nameof(CanUndoLastMove));
    }

    private string? GetRestoreTargetFolderId(
        MailFolderItemViewModel trashFolder,
        IReadOnlyList<uint> uniqueIds)
    {
        var operation =
            _lastMoveOperation;

        if (operation is not null &&
            operation.CanUndo &&
            string.Equals(
                operation.TargetFolderId,
                trashFolder.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            var knownTargetUniqueIds =
                operation
                    .UidMappings
                    .Select(
                        mapping =>
                            mapping.TargetUniqueId)
                    .ToHashSet();

            var allMessagesHaveKnownOrigin =
                uniqueIds.All(
                    knownTargetUniqueIds.Contains);

            var originalFolderStillExists =
                MailFolders.Any(
                    folder =>
                        string.Equals(
                            folder.FolderId,
                            operation.SourceFolderId,
                            StringComparison.OrdinalIgnoreCase));

            if (allMessagesHaveKnownOrigin &&
                originalFolderStillExists)
            {
                return operation.SourceFolderId;
            }
        }

        return MailFolders
            .FirstOrDefault(
                folder =>
                    string.Equals(
                        folder.DisplayName,
                        "Posteingang",
                        StringComparison.OrdinalIgnoreCase))
            ?.FolderId;
    }

    private void RemoveRestoredMessagesFromLastMove(
        string trashFolderId,
        IReadOnlyList<uint> restoredUniqueIds)
    {
        var operation =
            _lastMoveOperation;

        if (operation is null ||
            !string.Equals(
                operation.TargetFolderId,
                trashFolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var restoredIds =
            restoredUniqueIds
                .ToHashSet();

        var remainingMappings =
            operation
                .UidMappings
                .Where(
                    mapping =>
                        !restoredIds.Contains(
                            mapping.TargetUniqueId))
                .ToList();

        if (remainingMappings.Count == 0)
        {
            SetLastMoveOperation(
                null);

            return;
        }

        SetLastMoveOperation(
            new MailMoveResult(
                SourceFolderId:
                    operation.SourceFolderId,

                TargetFolderId:
                    operation.TargetFolderId,

                UidMappings:
                    remainingMappings));
    }

    private void NotifySelectedFolderActionStateChanged()
    {
        OnPropertyChanged(
            nameof(IsTrashFolderSelected));

        OnPropertyChanged(
            nameof(MessageActionToolTip));

        OnPropertyChanged(
            nameof(MessageActionGlyph));
    }

    private List<MailMessageItemViewModel> NormalizeMessages(
        IReadOnlyList<MailMessageItemViewModel> messages)
    {
        return messages
            .Where(
                message =>
                    message is not null &&
                    message.UniqueId > 0 &&
                    Messages.Contains(
                        message))
            .GroupBy(
                message =>
                    message.UniqueId)
            .Select(
                group =>
                    group.First())
            .ToList();
    }

    private void SetLastMoveOperation(
        MailMoveResult? operation)
    {
        _lastMoveOperation =
            operation?.CanUndo == true
                ? operation
                : null;

        OnPropertyChanged(
            nameof(CanUndoLastMove));
    }

    private async Task InitializeCoreAsync(
        string? preferredFolderId,
        CancellationToken cancellationToken)
    {
        BeginLoading(
            "Postfach wird geladen …",
            "Verbindung wird hergestellt …");

        MailFolders.Clear();
        Messages.Clear();

        _selectedFolder =
            null;

        OnPropertyChanged(
            nameof(SelectedFolder));

        NotifySelectedFolderActionStateChanged();

        SelectedMessage =
            null;

        try
        {
            var folders =
                await _mailDataSource
                    .GetFoldersAsync(
                        cancellationToken);

            foreach (var folder in folders)
            {
                MailFolders.Add(
                    new MailFolderItemViewModel(
                        folderId:
                            folder.FolderId,

                        displayName:
                            folder.DisplayName,

                        headerSubtitle:
                            folder.HeaderSubtitle,

                        unreadCount:
                            folder.UnreadCount,

                        hasSeparatorAfter:
                            folder.HasSeparatorAfter,

                        messageCount:
                            folder.MessageCount));
            }

            _selectedFolder =
                !string.IsNullOrWhiteSpace(
                    preferredFolderId)
                    ? MailFolders.FirstOrDefault(
                        folder =>
                            string.Equals(
                                folder.FolderId,
                                preferredFolderId,
                                StringComparison.OrdinalIgnoreCase))
                    : null;

            _selectedFolder ??=
                MailFolders.FirstOrDefault();

            OnPropertyChanged(
                nameof(SelectedFolder));

            NotifySelectedFolderActionStateChanged();

            _isInitialized =
                true;

            if (_selectedFolder is null)
            {
                SetConnected();

                IsLoading =
                    false;

                IsEmptyFolder =
                    true;

                return;
            }

            await LoadFolderMessagesAsync(
                _selectedFolder,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            IsLoading =
                false;
        }
        catch (Exception ex)
        {
            _isInitialized =
                false;

            IsLoading =
                false;

            SetErrorState(ex);
        }
    }

    private async Task LoadFolderMessagesAsync(
        MailFolderItemViewModel folder,
        CancellationToken cancellationToken = default)
    {
        var previousSource =
            _folderLoadCancellationSource;

        previousSource?.Cancel();

        var loadSource =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        _folderLoadCancellationSource =
            loadSource;

        previousSource?.Dispose();

        var token =
            loadSource.Token;

        BeginLoading(
            $"E-Mails aus „{folder.DisplayName}“ werden geladen …",
            "Synchronisieren …");

        Messages.Clear();

        SelectedMessage =
            null;

        try
        {
            var messages =
                await _mailDataSource
                    .GetMessagesAsync(
                        folder.FolderId,
                        maximumMessageCount: 20,
                        cancellationToken: token);

            token.ThrowIfCancellationRequested();

            foreach (var message in messages)
            {
                Messages.Add(
                    new MailMessageItemViewModel(
                        sender:
                            message.Sender,

                        senderAddress:
                            message.SenderAddress,

                        recipientAddress:
                            message.RecipientAddress,

                        subject:
                            message.Subject,

                        preview:
                            message.Preview,

                        displayTime:
                            message.DisplayTime,

                        displayDateTime:
                            message.DisplayDateTime,

                        senderInitial:
                            message.SenderInitial,

                        greeting:
                            message.Greeting,

                        body:
                            message.Body,

                        closing:
                            message.Closing,

                        signature:
                            message.Signature,

                        isUnread:
                            message.IsUnread,

                        emphasizeSender:
                            message.EmphasizeSender,

                        highlightTitle:
                            message.HighlightTitle,

                        highlightText:
                            message.HighlightText,

                        htmlBody:
                            message.HtmlBody,

                        uniqueId:
                            message.UniqueId,

                        attachments:
                            message.Attachments,

                        hasSmimeSignature:
                            message.HasSmimeSignature,

                        messageId:
                            message.MessageId,

                        references:
                            message.References));
            }

            SelectedMessage =
                Messages.FirstOrDefault();

            IsEmptyFolder =
                Messages.Count == 0;

            SetConnected();
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(
                    _folderLoadCancellationSource,
                    loadSource))
            {
                SetErrorState(ex);
            }
        }
        finally
        {
            var isCurrentLoad =
                ReferenceEquals(
                    _folderLoadCancellationSource,
                    loadSource);

            if (isCurrentLoad)
            {
                _folderLoadCancellationSource =
                    null;

                IsLoading =
                    false;
            }

            loadSource.Dispose();
        }
    }

    private async Task MarkMessageAsReadAsync(
        MailFolderItemViewModel folder,
        MailMessageItemViewModel message)
    {
        if (!message.IsUnread ||
            message.UniqueId == 0)
        {
            return;
        }

        try
        {
            await _mailDataSource
                .MarkAsReadAsync(
                    folder.FolderId,
                    message.UniqueId);

            if (!message.IsUnread)
            {
                return;
            }

            message.MarkAsRead();

            folder.DecrementUnreadCount();
        }
        catch
        {
        }
    }

    private void BeginLoading(
        string loadingMessage,
        string connectionStatus)
    {
        HasLoadError =
            false;

        LoadErrorMessage =
            string.Empty;

        IsEmptyFolder =
            false;

        LoadingMessage =
            loadingMessage;

        ConnectionState =
            MailConnectionState.Connecting;

        ConnectionStatusText =
            connectionStatus;

        IsLoading =
            true;
    }

    private void SetConnected()
    {
        HasLoadError =
            false;

        LoadErrorMessage =
            string.Empty;

        ConnectionState =
            MailConnectionState.Connected;

        ConnectionStatusText =
            "Verbunden";
    }

    private void SetErrorState(
        Exception exception)
    {
        HasLoadError =
            true;

        IsEmptyFolder =
            false;

        switch (exception)
        {
            case MailKit.Security.AuthenticationException:
                ConnectionState =
                    MailConnectionState.AuthenticationRequired;

                ConnectionStatusText =
                    "Anmeldung erforderlich";

                LoadErrorMessage =
                    "Die gespeicherten Zugangsdaten wurden vom Mailserver nicht akzeptiert. " +
                    "Bitte melden Sie das Konto ab und anschließend erneut an.";
                break;

            case SslHandshakeException:
                ConnectionState =
                    MailConnectionState.SecurityError;

                ConnectionStatusText =
                    "Sicherheitsfehler";

                LoadErrorMessage =
                    "Die sichere Verbindung zum Mailserver konnte nicht geprüft werden. " +
                    "Aus Sicherheitsgründen wurde die Verbindung abgebrochen.";
                break;

            case SocketException:
            case IOException:
                ConnectionState =
                    MailConnectionState.Offline;

                ConnectionStatusText =
                    "Offline";

                LoadErrorMessage =
                    "Der Mailserver ist momentan nicht erreichbar. " +
                    "Bitte prüfen Sie Ihre Internetverbindung.";
                break;

            default:
                ConnectionState =
                    MailConnectionState.Error;

                ConnectionStatusText =
                    "Verbindungsfehler";

                LoadErrorMessage =
                    "Die E-Mail-Daten konnten momentan nicht geladen werden. " +
                    "Bitte versuchen Sie es erneut.";
                break;
        }
    }

    private void CancelCurrentFolderLoad()
    {
        var source =
            _folderLoadCancellationSource;

        _folderLoadCancellationSource =
            null;

        if (source is null)
        {
            return;
        }

        source.Cancel();
        source.Dispose();
    }
}