using MailKit.Security;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private const int MessagePageSize =
        20;

    private readonly IMailDataSource _mailDataSource;

    private readonly IMailMessageStateSource
        _mailMessageStateSource;

    private readonly IMailPermanentDeleteService
        _mailPermanentDeleteService;

    private readonly ILogger<MainViewModel>
        _logger;

    private readonly Dictionary<string, uint>
        _uidValidityByFolder =
            new(
                StringComparer.OrdinalIgnoreCase);

    private int _mailMoveOperationState;
    private int _mailSynchronizationOperationState;
    private int _messagePageLoadOperationState;

    private int _loadedMessageLimit =
        MessagePageSize;

    private MailFolderItemViewModel? _selectedFolder;
    private MailMessageItemViewModel? _selectedMessage;

    private MailMoveResult? _lastMoveOperation;

    private CancellationTokenSource?
        _folderLoadCancellationSource;

    private bool _isInitialized;
    private bool _isLoading;
    private bool _hasLoadError;
    private bool _isEmptyFolder;
    private bool _hasMoreMessages;

    private bool _suppressAutomaticReadMarking;

    private string _loadingMessage =
        "Postfach wird geladen …";

    private string _loadErrorMessage =
        string.Empty;

    private string _connectionStatusText =
        "Verbindung wird hergestellt …";

    private MailConnectionState _connectionState =
        MailConnectionState.Connecting;

    public MainViewModel(
        IMailDataSource mailDataSource,
        IMailMessageStateSource mailMessageStateSource,
        IMailPermanentDeleteService mailPermanentDeleteService,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(
            mailDataSource);

        ArgumentNullException.ThrowIfNull(
            mailMessageStateSource);

        ArgumentNullException.ThrowIfNull(
            mailPermanentDeleteService);

        ArgumentNullException.ThrowIfNull(
            logger);

        _mailDataSource =
            mailDataSource;

        _mailMessageStateSource =
            mailMessageStateSource;

        _mailPermanentDeleteService =
            mailPermanentDeleteService;

        _logger =
            logger;

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
        !IsLoadingMoreMessages &&
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

    /*
     * Diese Eigenschaft wird später direkt für den sichtbaren
     * "Weitere 20 Nachrichten laden"-Button verwendet.
     */
    public bool HasMoreMessages
    {
        get =>
            _hasMoreMessages;

        private set
        {
            if (_hasMoreMessages ==
                value)
            {
                return;
            }

            _hasMoreMessages =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanLoadMoreMessages));
        }
    }

    /*
     * Load-More verwendet bewusst einen eigenen Ladezustand.
     *
     * Der normale IsLoading-Zustand steuert die große
     * Ordner-Ladeoberfläche. Beim Nachladen älterer Nachrichten
     * soll die bereits sichtbare Liste dagegen stehen bleiben.
     */
    public bool IsLoadingMoreMessages =>
        Volatile.Read(
            ref _messagePageLoadOperationState) != 0;

    public bool CanLoadMoreMessages =>
        HasMoreMessages &&
        !IsLoading &&
        !IsLoadingMoreMessages &&
        !IsMailSynchronizationRunning &&
        !IsMailMoveOperationRunning;

    private bool IsMailMoveOperationRunning =>
        Volatile.Read(
            ref _mailMoveOperationState) != 0;

    private bool IsMailSynchronizationRunning =>
        Volatile.Read(
            ref _mailSynchronizationOperationState) != 0;

    public MailFolderItemViewModel? SelectedFolder
    {
        get =>
            _selectedFolder;

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

            /*
             * Ein Ordnerwechsel beginnt bewusst wieder bei
             * der ersten Seite.
             *
             * Dadurch halten wir nicht unnötig hunderte
             * vollständige Nachrichten verschiedener Ordner
             * gleichzeitig im Speicher.
             */
            ResetMessagePagingState();

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
        get =>
            _selectedMessage;

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
                !_suppressAutomaticReadMarking &&
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
        get =>
            _isLoading;

        private set
        {
            if (_isLoading ==
                value)
            {
                return;
            }

            _isLoading =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanUndoLastMove));

            OnPropertyChanged(
                nameof(CanLoadMoreMessages));
        }
    }

    public string LoadingMessage
    {
        get =>
            _loadingMessage;

        private set
        {
            if (_loadingMessage ==
                value)
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
        get =>
            _hasLoadError;

        private set
        {
            if (_hasLoadError ==
                value)
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
        get =>
            _loadErrorMessage;

        private set
        {
            if (_loadErrorMessage ==
                value)
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
        get =>
            _isEmptyFolder;

        private set
        {
            if (_isEmptyFolder ==
                value)
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
        get =>
            _connectionState;

        private set
        {
            if (_connectionState ==
                value)
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
        get =>
            _connectionStatusText;

        private set
        {
            if (_connectionStatusText ==
                value)
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
            preferredMessageUniqueId: null,
            preferredMessageId: null,
            cancellationToken);
    }

    public async Task ReloadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            var preferredFolderId =
                SelectedFolder?.FolderId;

            var preferredMessageUniqueId =
                SelectedMessage?.UniqueId;

            var preferredMessageId =
                SelectedMessage?.MessageId;

            CancelCurrentFolderLoad();

            await InitializeCoreAsync(
                preferredFolderId,
                preferredMessageUniqueId,
                preferredMessageId,
                cancellationToken);

            return;
        }

        await SynchronizeCoreAsync(
            showUserFeedback: true,
            cancellationToken);
    }

    public Task SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        return SynchronizeCoreAsync(
            showUserFeedback: false,
            cancellationToken);
    }

    /*
     * Lädt exakt die nächste Nachrichtenseite.
     *
     * Bestehende Bodies werden dabei nicht erneut über
     * GetMessagePageAsync heruntergeladen.
     *
     * Vor und nach dem eigentlichen Seitenabruf prüfen wir
     * jedoch den leichten UID-/Flag-Zustand der bereits
     * sichtbaren Nachrichten.
     *
     * Dadurch erkennen wir, ob beispielsweise zwischenzeitlich
     * eine neue Nachricht am Anfang des Ordners angekommen ist
     * und das Offset dadurch nicht mehr zu unserer sichtbaren
     * Liste passt.
     */
    public async Task<bool> LoadMoreMessagesAsync(
        CancellationToken cancellationToken = default)
    {
        var folder =
            _selectedFolder;

        if (folder is null ||
            IsLoading ||
            !HasMoreMessages ||
            Messages.Count == 0 ||
            IsMailSynchronizationRunning ||
            IsMailMoveOperationRunning ||
            !TryBeginMessagePageLoad())
        {
            return false;
        }

        var synchronizeAfterOperation =
            false;

        var pageLoaded =
            false;

        try
        {
            if (!_uidValidityByFolder.TryGetValue(
                    folder.FolderId,
                    out var expectedUidValidity) ||
                expectedUidValidity == 0)
            {
                synchronizeAfterOperation =
                    true;

                return false;
            }

            var currentlyLoadedCount =
                Messages.Count;

            /*
             * Vorprüfung:
             *
             * Sind die aktuell sichtbaren Nachrichten noch
             * exakt der aktuelle Anfang des Serverordners?
             *
             * Bei einer neu eingegangenen oder von einem
             * anderen Client gelöschten Mail wäre das nicht
             * mehr der Fall.
             */
            var stateBeforePage =
                await _mailMessageStateSource
                    .GetMessageStatesAsync(
                        folder.FolderId,
                        maximumMessageCount:
                            currentlyLoadedCount,
                        cancellationToken:
                            cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (!ReferenceEquals(
                    _selectedFolder,
                    folder))
            {
                return false;
            }

            ValidateStateSnapshot(
                folder,
                stateBeforePage);

            if (stateBeforePage.UidValidity !=
                    expectedUidValidity ||
                !DoesStateSnapshotMatchLoadedPrefix(
                    stateBeforePage))
            {
                synchronizeAfterOperation =
                    true;

                return false;
            }

            /*
             * Erst nachdem der aktuelle Präfix bestätigt wurde,
             * werden exakt die nächsten 20 vollständigen
             * Nachrichten geladen.
             */
            var page =
                await _mailDataSource
                    .GetMessagePageAsync(
                        folder.FolderId,
                        skipMessageCount:
                            currentlyLoadedCount,
                        maximumMessageCount:
                            MessagePageSize,
                        cancellationToken:
                            cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (!ReferenceEquals(
                    _selectedFolder,
                    folder))
            {
                return false;
            }

            /*
             * Nachprüfung:
             *
             * Während des Abrufs der nächsten Bodies könnte
             * sich der Ordner erneut verändert haben.
             *
             * In diesem Fall wird die geladene Seite verworfen
             * und zuerst der echte Serverzustand synchronisiert.
             */
            var stateAfterPage =
                await _mailMessageStateSource
                    .GetMessageStatesAsync(
                        folder.FolderId,
                        maximumMessageCount:
                            currentlyLoadedCount,
                        cancellationToken:
                            cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (!ReferenceEquals(
                    _selectedFolder,
                    folder))
            {
                return false;
            }

            ValidateStateSnapshot(
                folder,
                stateAfterPage);

            if (stateAfterPage.UidValidity !=
                    expectedUidValidity ||
                !DoesStateSnapshotMatchLoadedPrefix(
                    stateAfterPage))
            {
                synchronizeAfterOperation =
                    true;

                return false;
            }

            var existingUniqueIds =
                Messages
                    .Select(
                        message =>
                            message.UniqueId)
                    .ToHashSet();

            /*
             * Eine Überschneidung sollte nach den beiden
             * Präfixprüfungen nicht vorkommen.
             *
             * Falls der Serverzustand genau zwischen den
             * Prüfungen verschoben wurde, behandeln wir eine
             * Überschneidung trotzdem defensiv als Hinweis
             * auf einen veralteten Paging-Zustand.
             */
            if (page.Any(
                    message =>
                        existingUniqueIds.Contains(
                            message.UniqueId)))
            {
                synchronizeAfterOperation =
                    true;

                return false;
            }

            foreach (var message in page)
            {
                if (message.UniqueId == 0 ||
                    existingUniqueIds.Contains(
                        message.UniqueId))
                {
                    continue;
                }

                Messages.Add(
                    CreateMessageViewModel(
                        message));

                existingUniqueIds.Add(
                    message.UniqueId);
            }

            /*
             * Die gewünschte sichtbare Tiefe steigt immer um
             * genau eine Seite.
             *
             * Diese Tiefe wird bei einer späteren normalen
             * Synchronisation beibehalten.
             */
            _loadedMessageLimit =
                checked(
                    _loadedMessageLimit +
                    MessagePageSize);

            IsEmptyFolder =
                Messages.Count == 0;

            if (page.Count <
                MessagePageSize)
            {
                /*
                 * Eine unvollständige Seite bedeutet:
                 * Zum Zeitpunkt des Serverabrufs war das Ende
                 * des Ordners erreicht.
                 */
                HasMoreMessages =
                    false;
            }
            else
            {
                UpdateHasMoreMessages();
            }

            SetConnected();

            pageLoaded =
                page.Count > 0;

            return pageLoaded;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OverflowException)
        {
            /*
             * Praktisch nicht erreichbar, verhindert aber,
             * dass ein theoretischer Integerüberlauf einen
             * falschen Paging-Zustand erzeugt.
             */
            HasMoreMessages =
                false;

            return false;
        }
        catch (Exception ex)
        {
            /*
             * Ein Fehler beim Nachladen darf die bereits
             * sichtbaren Nachrichten nicht durch eine große
             * Load-Error-Ansicht ersetzen.
             */
            SetSynchronizationErrorState(
                ex);

            return false;
        }
        finally
        {
            EndMessagePageLoad();

            if (synchronizeAfterOperation &&
                ReferenceEquals(
                    _selectedFolder,
                    folder) &&
                !cancellationToken.IsCancellationRequested)
            {
                /*
                 * Die eigentliche Paging-Operation ist bereits
                 * beendet, bevor SynchronizeCoreAsync startet.
                 *
                 * Dadurch blockieren sich die beiden
                 * Operationsguards nicht gegenseitig.
                 */
                await SynchronizeCoreAsync(
                    showUserFeedback: false,
                    cancellationToken);
            }
        }
    }

    private async Task SynchronizeCoreAsync(
        bool showUserFeedback,
        CancellationToken cancellationToken)
    {
        if (IsLoading ||
            IsLoadingMoreMessages ||
            !TryBeginSynchronization())
        {
            return;
        }

        var isConnectionRecovery =
            ConnectionState ==
            MailConnectionState.Offline;

        _logger.LogInformation(
            "Mailbox synchronization started. UserInitiated={UserInitiated}, VisibleMessages={VisibleMessageCount}.",
            showUserFeedback,
            Messages.Count);

        if (showUserFeedback)
        {
            HasLoadError =
                false;

            LoadErrorMessage =
                string.Empty;

            ConnectionState =
                MailConnectionState.Connecting;

            ConnectionStatusText =
                isConnectionRecovery
                    ? "Verbindung wird wiederhergestellt …"
                    : "Synchronisieren …";
        }

        try
        {
            var serverFolders =
                await _mailDataSource
                    .GetFoldersAsync(
                        cancellationToken);

            _logger.LogInformation(
                "Mailbox synchronization retrieved {FolderCount} folders.",
                serverFolders.Count);

            cancellationToken
                .ThrowIfCancellationRequested();

            SynchronizeFolderCollection(
                serverFolders);

            if (IsLoading)
            {
                return;
            }

            var folderToSynchronize =
                _selectedFolder;

            if (folderToSynchronize is null)
            {
                SetSelectedMessageWithoutReadMarking(
                    null);

                Messages.Clear();

                HasMoreMessages =
                    false;

                IsEmptyFolder =
                    true;

                SetConnected();

                return;
            }

            /*
             * Anders als bisher synchronisieren wir nicht
             * pauschal nur die ersten 20 Nachrichten.
             *
             * Hat der Benutzer beispielsweise bereits 60
             * Nachrichten sichtbar gemacht, werden auch diese
             * 60 UID-/Flag-Zustände geprüft.
             *
             * Bodies werden nur dann erneut geladen, wenn sich
             * der sichtbare Serverzustand tatsächlich geändert
             * hat.
             */
            var synchronizationMessageLimit =
                Math.Max(
                    _loadedMessageLimit,
                    MessagePageSize);

            var stateSnapshot =
                await _mailMessageStateSource
                    .GetMessageStatesAsync(
                        folderToSynchronize.FolderId,
                        maximumMessageCount:
                            synchronizationMessageLimit,
                        cancellationToken:
                            cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (!ReferenceEquals(
                    _selectedFolder,
                    folderToSynchronize))
            {
                return;
            }

            ValidateStateSnapshot(
                folderToSynchronize,
                stateSnapshot);

            var uidValidityChanged =
                HasUidValidityChanged(
                    folderToSynchronize.FolderId,
                    stateSnapshot.UidValidity);

            if (uidValidityChanged)
            {
                /*
                 * Wenn UIDVALIDITY wechselt, sind alle bisher
                 * bekannten UIDs dieses Ordners ungültig.
                 *
                 * Alte Nachrichtenobjekte dürfen daher nicht
                 * weiterverwendet werden.
                 */
                SetSelectedMessageWithoutReadMarking(
                    null);

                Messages.Clear();

                /*
                 * Eine neue Ordneridentität beginnt bewusst
                 * wieder mit der ersten Seite.
                 */
                _loadedMessageLimit =
                    MessagePageSize;

                HasMoreMessages =
                    false;

                InvalidateLastMoveForFolderIdentityChange(
                    folderToSynchronize.FolderId);
            }

            var requiresFullMessageReload =
                uidValidityChanged ||
                RequiresFullMessageReload(
                    folderToSynchronize.FolderId,
                    stateSnapshot);

            _logger.LogInformation(
                "Mailbox synchronization evaluated server state. FullReload={FullReload}, UidValidityChanged={UidValidityChanged}.",
                requiresFullMessageReload,
                uidValidityChanged);

            if (requiresFullMessageReload)
            {
                var serverMessages =
                    await _mailDataSource
                        .GetMessagesAsync(
                            folderToSynchronize.FolderId,
                            maximumMessageCount:
                                _loadedMessageLimit,
                            cancellationToken:
                                cancellationToken);

                cancellationToken
                    .ThrowIfCancellationRequested();

                if (!ReferenceEquals(
                        _selectedFolder,
                        folderToSynchronize))
                {
                    return;
                }

                SynchronizeMessageCollection(
                    serverMessages);
            }
            else
            {
                SynchronizeMessageReadStates(
                    stateSnapshot.Messages);
            }

            _uidValidityByFolder[
                folderToSynchronize.FolderId] =
                    stateSnapshot.UidValidity;

            IsEmptyFolder =
                Messages.Count == 0;

            UpdateHasMoreMessages();

            SetConnected();

            _logger.LogInformation(
                "Mailbox synchronization completed successfully. VisibleMessages={VisibleMessageCount}.",
                Messages.Count);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Mailbox synchronization was cancelled.");
        }
        catch (Exception ex)
        {
            LogMailOperationFailure(
                "Mailbox synchronization",
                ex);

            SetSynchronizationErrorState(
                ex);
        }
        finally
        {
            EndSynchronization();

            _logger.LogInformation(
                "Mailbox synchronization ended. ConnectionState={ConnectionState}.",
                ConnectionState);
        }
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
            IsLoadingMoreMessages ||
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

    public Task<bool> DeleteMessagePermanentlyAsync(
        MailMessageItemViewModel message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        return DeleteMessagesPermanentlyAsync(
            new[] { message },
            cancellationToken);
    }

    public async Task<bool> DeleteMessagesPermanentlyAsync(
        IReadOnlyList<MailMessageItemViewModel> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            messages);

        var trashFolder =
            _selectedFolder;

        /*
         * Permanentes Löschen darf ausschließlich aus dem
         * aktuell ausgewählten Papierkorb ausgelöst werden.
         *
         * Außerdem führen wir während eines Ordnerlade-,
         * Paging- oder Synchronisierungsvorgangs keine
         * irreversible Operation aus.
         */
        if (trashFolder is null ||
            !IsTrashFolderSelected ||
            IsLoading ||
            IsLoadingMoreMessages ||
            IsMailSynchronizationRunning ||
            messages.Count == 0)
        {
            return false;
        }

        /*
         * Für jede irreversible Operation muss die zum
         * aktuell geladenen Ordner gehörende UIDVALIDITY
         * bekannt sein.
         *
         * Ohne diese Information wird nicht gelöscht.
         */
        if (!_uidValidityByFolder.TryGetValue(
                trashFolder.FolderId,
                out var expectedUidValidity) ||
            expectedUidValidity == 0)
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

        /*
         * Wir verwenden denselben Operationsschutz wie für
         * Move/Undo/Restore.
         *
         * Dadurch kann innerhalb dieses ViewModels nicht
         * gleichzeitig eine zweite Nachrichtenmutation
         * gestartet werden.
         */
        if (!TryBeginMailMoveOperation())
        {
            return false;
        }

        try
        {
            await _mailPermanentDeleteService
                .DeletePermanentlyAsync(
                    trashFolder.FolderId,
                    expectedUidValidity,
                    uniqueIds,
                    cancellationToken);

            /*
             * Nach erfolgreichem Permanent Delete existiert
             * bewusst kein Undo.
             *
             * Auch ein eventuell älterer Move-Zustand wird
             * verworfen, damit kein Undo mehr auf inzwischen
             * endgültig entfernte UIDs zeigen kann.
             */
            SetLastMoveOperation(
                null);

            await ReloadAsync(
                cancellationToken);

            return true;
        }
        catch
        {
            /*
             * Bei einem Verbindungsabbruch kann eine
             * irreversible Serveroperation bereits erfolgreich
             * gewesen sein, obwohl die Clientseite keine
             * eindeutige Bestätigung mehr erhalten hat.
             *
             * Deshalb behalten wir in diesem Fall ebenfalls
             * keinen alten Undo-Zustand.
             */
            SetLastMoveOperation(
                null);

            /*
             * Best effort: Nach einem Fehler versuchen wir,
             * den tatsächlichen Serverzustand neu einzulesen.
             *
             * Wir wiederholen die Löschoperation ausdrücklich
             * NICHT automatisch.
             */
            try
            {
                await ReloadAsync(
                    CancellationToken.None);
            }
            catch
            {
                /*
                 * Der ursprüngliche Fehler der
                 * Permanent-Delete-Operation bleibt maßgeblich.
                 */
            }

            throw;
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
            IsLoadingMoreMessages ||
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
            IsLoading ||
            IsLoadingMoreMessages)
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
            IsLoadingMoreMessages ||
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
        if (IsLoadingMoreMessages)
        {
            return false;
        }

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

        OnPropertyChanged(
            nameof(CanLoadMoreMessages));

        return true;
    }

    private void EndMailMoveOperation()
    {
        Interlocked.Exchange(
            ref _mailMoveOperationState,
            0);

        OnPropertyChanged(
            nameof(CanUndoLastMove));

        OnPropertyChanged(
            nameof(CanLoadMoreMessages));
    }

    private bool TryBeginSynchronization()
    {
        if (IsLoadingMoreMessages)
        {
            return false;
        }

        var previousState =
            Interlocked.CompareExchange(
                ref _mailSynchronizationOperationState,
                1,
                0);

        if (previousState == 0)
        {
            OnPropertyChanged(
                nameof(CanLoadMoreMessages));

            return true;
        }

        return false;
    }

    private void EndSynchronization()
    {
        Interlocked.Exchange(
            ref _mailSynchronizationOperationState,
            0);

        OnPropertyChanged(
            nameof(CanLoadMoreMessages));
    }

    private bool TryBeginMessagePageLoad()
    {
        if (IsMailSynchronizationRunning ||
            IsMailMoveOperationRunning)
        {
            return false;
        }

        var previousState =
            Interlocked.CompareExchange(
                ref _messagePageLoadOperationState,
                1,
                0);

        if (previousState != 0)
        {
            return false;
        }

        OnPropertyChanged(
            nameof(IsLoadingMoreMessages));

        OnPropertyChanged(
            nameof(CanLoadMoreMessages));

        OnPropertyChanged(
            nameof(CanUndoLastMove));

        return true;
    }

    private void EndMessagePageLoad()
    {
        Interlocked.Exchange(
            ref _messagePageLoadOperationState,
            0);

        OnPropertyChanged(
            nameof(IsLoadingMoreMessages));

        OnPropertyChanged(
            nameof(CanLoadMoreMessages));

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

    private void InvalidateLastMoveForFolderIdentityChange(
        string folderId)
    {
        var operation =
            _lastMoveOperation;

        if (operation is null)
        {
            return;
        }

        var sourceFolderChanged =
            string.Equals(
                operation.SourceFolderId,
                folderId,
                StringComparison.OrdinalIgnoreCase);

        var targetFolderChanged =
            string.Equals(
                operation.TargetFolderId,
                folderId,
                StringComparison.OrdinalIgnoreCase);

        if (!sourceFolderChanged &&
            !targetFolderChanged)
        {
            return;
        }

        /*
         * Die gespeicherten Undo-UIDs sind nach einem
         * UIDVALIDITY-Wechsel nicht mehr vertrauenswürdig.
         */
        SetLastMoveOperation(
            null);
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
        uint? preferredMessageUniqueId,
        string? preferredMessageId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Mailbox initialization started.");

        var isConnectionRecovery =
            ConnectionState ==
            MailConnectionState.Offline;

        BeginLoading(
            "Postfach wird geladen …",
            isConnectionRecovery
                ? "Verbindung wird wiederhergestellt …"
                : "Verbindung wird hergestellt …");

        MailFolders.Clear();
        Messages.Clear();

        _uidValidityByFolder.Clear();

        ResetMessagePagingState();

        _selectedFolder =
            null;

        OnPropertyChanged(
            nameof(SelectedFolder));

        NotifySelectedFolderActionStateChanged();

        SetSelectedMessageWithoutReadMarking(
            null);

        try
        {
            var folders =
                await _mailDataSource
                    .GetFoldersAsync(
                        cancellationToken);

            _logger.LogInformation(
                "Mailbox initialization retrieved {FolderCount} folders.",
                folders.Count);

            foreach (var folder in folders)
            {
                MailFolders.Add(
                    CreateFolderViewModel(
                        folder));
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

            ResetMessagePagingState();

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

                HasMoreMessages =
                    false;

                IsEmptyFolder =
                    true;

                return;
            }

            await LoadFolderMessagesAsync(
                _selectedFolder,
                preferredMessageUniqueId,
                preferredMessageId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Mailbox initialization was cancelled.");

            IsLoading =
                false;
        }
        catch (Exception ex)
        {
            LogMailOperationFailure(
                "Mailbox initialization",
                ex);

            _isInitialized =
                false;

            IsLoading =
                false;

            HasMoreMessages =
                false;

            SetErrorState(
                ex);
        }
        finally
        {
            _logger.LogInformation(
                "Mailbox initialization ended. ConnectionState={ConnectionState}.",
                ConnectionState);
        }
    }

    private async Task LoadFolderMessagesAsync(
        MailFolderItemViewModel folder,
        uint? preferredMessageUniqueId = null,
        string? preferredMessageId = null,
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

        /*
         * Jeder vollständige Ordnerwechsel beginnt wieder mit
         * der ersten Seite.
         */
        ResetMessagePagingState();

        BeginLoading(
            $"E-Mails aus „{folder.DisplayName}“ werden geladen …",
            "Synchronisieren …");

        Messages.Clear();

        SetSelectedMessageWithoutReadMarking(
            null);

        try
        {
            /*
             * UIDVALIDITY wird bewusst bereits beim normalen
             * Laden des Ordners erfasst.
             *
             * Für spätere irreversible Aktionen dürfen wir
             * niemals mit UIDs arbeiten, deren UIDVALIDITY
             * unbekannt ist.
             */
            var stateSnapshot =
                await _mailMessageStateSource
                    .GetMessageStatesAsync(
                        folder.FolderId,
                        maximumMessageCount:
                            MessagePageSize,
                        cancellationToken:
                            token);

            token.ThrowIfCancellationRequested();

            ValidateStateSnapshot(
                folder,
                stateSnapshot);

            var uidValidityChanged =
                HasUidValidityChanged(
                    folder.FolderId,
                    stateSnapshot.UidValidity);

            if (uidValidityChanged)
            {
                InvalidateLastMoveForFolderIdentityChange(
                    folder.FolderId);
            }

            _uidValidityByFolder[
                folder.FolderId] =
                    stateSnapshot.UidValidity;

            var messages =
                await _mailDataSource
                    .GetMessagesAsync(
                        folder.FolderId,
                        maximumMessageCount:
                            MessagePageSize,
                        cancellationToken:
                            token);

            token.ThrowIfCancellationRequested();

            foreach (var message in messages)
            {
                Messages.Add(
                    CreateMessageViewModel(
                        message));
            }

            MailMessageItemViewModel?
                preferredMessage = null;

            if (preferredMessageUniqueId.HasValue &&
                !uidValidityChanged)
            {
                preferredMessage =
                    Messages.FirstOrDefault(
                        message =>
                            IsSameMessageIdentity(
                                message,
                                preferredMessageUniqueId.Value,
                                preferredMessageId));
            }

            /*
             * Nach einem UIDVALIDITY-Wechsel wird absichtlich
             * keine zuvor ausgewählte Nachricht rekonstruiert.
             *
             * Eine numerisch gleiche UID könnte jetzt eine
             * andere Nachricht bezeichnen.
             */
            SetSelectedMessageWithoutReadMarking(
                preferredMessage);

            IsEmptyFolder =
                Messages.Count == 0;

            UpdateHasMoreMessages();

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
                LogMailOperationFailure(
                    "Folder message loading",
                    ex);

                HasMoreMessages =
                    false;

                SetErrorState(
                    ex);
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

    private static void ValidateStateSnapshot(
        MailFolderItemViewModel folder,
        MailFolderMessageStateSnapshot snapshot)
    {
        if (!string.Equals(
                folder.FolderId,
                snapshot.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Der serverseitige Ordnerzustand konnte nicht eindeutig zugeordnet werden.");
        }

        if (snapshot.UidValidity == 0)
        {
            throw new InvalidOperationException(
                "Der Mailserver hat keine gültige UIDVALIDITY für den Ordner geliefert.");
        }
    }

    private bool HasUidValidityChanged(
        string folderId,
        uint currentUidValidity)
    {
        return _uidValidityByFolder.TryGetValue(
                   folderId,
                   out var previousUidValidity) &&
               previousUidValidity !=
                   currentUidValidity;
    }

    /*
     * Prüft nicht nur die Menge, sondern bewusst auch die
     * Reihenfolge.
     *
     * Paging mit Offset ist nur dann sinnvoll, wenn die
     * sichtbaren Nachrichten noch exakt den Anfang der
     * aktuellen serverseitigen Sortierreihenfolge bilden.
     */
    private bool DoesStateSnapshotMatchLoadedPrefix(
        MailFolderMessageStateSnapshot snapshot)
    {
        if (snapshot.Messages.Count !=
            Messages.Count)
        {
            return false;
        }

        for (var index = 0;
             index < Messages.Count;
             index++)
        {
            if (snapshot.Messages[index].UniqueId !=
                Messages[index].UniqueId)
            {
                return false;
            }
        }

        return true;
    }

    private bool RequiresFullMessageReload(
        string folderId,
        MailFolderMessageStateSnapshot snapshot)
    {
        if (!string.Equals(
                folderId,
                snapshot.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_uidValidityByFolder.TryGetValue(
                folderId,
                out var previousUidValidity) &&
            previousUidValidity !=
                snapshot.UidValidity)
        {
            return true;
        }

        if (Messages.Count !=
            snapshot.Messages.Count)
        {
            return true;
        }

        var statesByUniqueId =
            snapshot
                .Messages
                .GroupBy(
                    state =>
                        state.UniqueId)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.First());

        if (statesByUniqueId.Count !=
            snapshot.Messages.Count)
        {
            return true;
        }

        foreach (var message in Messages)
        {
            if (!statesByUniqueId.ContainsKey(
                    message.UniqueId))
            {
                return true;
            }
        }

        return false;
    }

    private void SynchronizeMessageReadStates(
        IReadOnlyList<MailMessageStateData> states)
    {
        var statesByUniqueId =
            states
                .GroupBy(
                    state =>
                        state.UniqueId)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.First());

        foreach (var message in Messages)
        {
            if (!statesByUniqueId.TryGetValue(
                    message.UniqueId,
                    out var state))
            {
                continue;
            }

            if (message.IsUnread ==
                state.IsUnread)
            {
                continue;
            }

            if (state.IsUnread)
            {
                message.MarkAsUnread();
            }
            else
            {
                message.MarkAsRead();
            }
        }
    }

    private void SynchronizeFolderCollection(
        IReadOnlyList<MailFolderData> serverFolders)
    {
        var selectedFolderId =
            _selectedFolder?.FolderId;

        var serverFolderIds =
            serverFolders
                .Select(
                    folder =>
                        folder.FolderId)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var removedUidValidityEntries =
            _uidValidityByFolder
                .Keys
                .Where(
                    folderId =>
                        !serverFolderIds.Contains(
                            folderId))
                .ToList();

        foreach (var folderId in
                 removedUidValidityEntries)
        {
            _uidValidityByFolder.Remove(
                folderId);
        }

        for (var index = MailFolders.Count - 1;
             index >= 0;
             index--)
        {
            if (!serverFolderIds.Contains(
                    MailFolders[index].FolderId))
            {
                MailFolders.RemoveAt(
                    index);
            }
        }

        for (var targetIndex = 0;
             targetIndex < serverFolders.Count;
             targetIndex++)
        {
            var serverFolder =
                serverFolders[targetIndex];

            var existingFolder =
                MailFolders.FirstOrDefault(
                    folder =>
                        string.Equals(
                            folder.FolderId,
                            serverFolder.FolderId,
                            StringComparison.OrdinalIgnoreCase));

            if (existingFolder is null)
            {
                existingFolder =
                    CreateFolderViewModel(
                        serverFolder);

                MailFolders.Insert(
                    Math.Min(
                        targetIndex,
                        MailFolders.Count),
                    existingFolder);
            }
            else
            {
                existingFolder.UpdateState(
                    serverFolder.HeaderSubtitle,
                    serverFolder.UnreadCount,
                    serverFolder.MessageCount);

                var currentIndex =
                    MailFolders.IndexOf(
                        existingFolder);

                if (currentIndex !=
                        targetIndex &&
                    currentIndex >= 0 &&
                    targetIndex <
                        MailFolders.Count)
                {
                    MailFolders.Move(
                        currentIndex,
                        targetIndex);
                }
            }
        }

        var synchronizedSelectedFolder =
            !string.IsNullOrWhiteSpace(
                selectedFolderId)
                ? MailFolders.FirstOrDefault(
                    folder =>
                        string.Equals(
                            folder.FolderId,
                            selectedFolderId,
                            StringComparison.OrdinalIgnoreCase))
                : null;

        synchronizedSelectedFolder ??=
            MailFolders.FirstOrDefault();

        if (ReferenceEquals(
                _selectedFolder,
                synchronizedSelectedFolder))
        {
            return;
        }

        /*
         * Wenn der bisherige ausgewählte Ordner serverseitig
         * verschwunden ist und wir auf einen anderen Ordner
         * wechseln müssen, beginnt auch dessen Paging wieder
         * bei der ersten Seite.
         */
        ResetMessagePagingState();

        _selectedFolder =
            synchronizedSelectedFolder;

        OnPropertyChanged(
            nameof(SelectedFolder));

        NotifySelectedFolderActionStateChanged();
    }

    private void SynchronizeMessageCollection(
        IReadOnlyList<MailMessageData> serverMessages)
    {
        var selectedMessageUniqueId =
            _selectedMessage?.UniqueId;

        var selectedMessageId =
            _selectedMessage?.MessageId;

        for (var index = Messages.Count - 1;
             index >= 0;
             index--)
        {
            var localMessage =
                Messages[index];

            var stillExists =
                serverMessages.Any(
                    serverMessage =>
                        IsSameMessageIdentity(
                            localMessage,
                            serverMessage.UniqueId,
                            serverMessage.MessageId));

            if (!stillExists)
            {
                Messages.RemoveAt(
                    index);
            }
        }

        for (var targetIndex = 0;
             targetIndex < serverMessages.Count;
             targetIndex++)
        {
            var serverMessage =
                serverMessages[targetIndex];

            var existingMessage =
                Messages.FirstOrDefault(
                    message =>
                        IsSameMessageIdentity(
                            message,
                            serverMessage.UniqueId,
                            serverMessage.MessageId));

            if (existingMessage is null)
            {
                existingMessage =
                    CreateMessageViewModel(
                        serverMessage);

                Messages.Insert(
                    Math.Min(
                        targetIndex,
                        Messages.Count),
                    existingMessage);
            }
            else
            {
                UpdateMessageReadState(
                    existingMessage,
                    serverMessage);

                var currentIndex =
                    Messages.IndexOf(
                        existingMessage);

                if (currentIndex !=
                        targetIndex &&
                    currentIndex >= 0 &&
                    targetIndex <
                        Messages.Count)
                {
                    Messages.Move(
                        currentIndex,
                        targetIndex);
                }
            }
        }

        if (!selectedMessageUniqueId.HasValue)
        {
            if (_selectedMessage is not null &&
                !Messages.Contains(
                    _selectedMessage))
            {
                SetSelectedMessageWithoutReadMarking(
                    null);
            }

            return;
        }

        var synchronizedSelectedMessage =
            Messages.FirstOrDefault(
                message =>
                    IsSameMessageIdentity(
                        message,
                        selectedMessageUniqueId.Value,
                        selectedMessageId));

        if (ReferenceEquals(
                _selectedMessage,
                synchronizedSelectedMessage))
        {
            return;
        }

        SetSelectedMessageWithoutReadMarking(
            synchronizedSelectedMessage);
    }

    private static MailFolderItemViewModel
        CreateFolderViewModel(
            MailFolderData folder)
    {
        return new MailFolderItemViewModel(
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
                folder.MessageCount);
    }

    private static MailMessageItemViewModel
        CreateMessageViewModel(
            MailMessageData message)
    {
        return new MailMessageItemViewModel(
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
                message.References,

            toAddresses:
                message.ToAddresses,

            ccAddresses:
                message.CcAddresses,

            replyToAddresses:
                message.ReplyToAddresses);
    }

    private static void UpdateMessageReadState(
        MailMessageItemViewModel localMessage,
        MailMessageData serverMessage)
    {
        if (localMessage.IsUnread ==
            serverMessage.IsUnread)
        {
            return;
        }

        if (serverMessage.IsUnread)
        {
            localMessage.MarkAsUnread();
        }
        else
        {
            localMessage.MarkAsRead();
        }
    }

    private static bool IsSameMessageIdentity(
        MailMessageItemViewModel message,
        uint uniqueId,
        string? messageId)
    {
        if (message.UniqueId !=
            uniqueId)
        {
            return false;
        }

        var existingMessageId =
            NormalizeMessageId(
                message.MessageId);

        var incomingMessageId =
            NormalizeMessageId(
                messageId);

        if (existingMessageId is null &&
            incomingMessageId is null)
        {
            return true;
        }

        return string.Equals(
            existingMessageId,
            incomingMessageId,
            StringComparison.Ordinal);
    }

    private static string? NormalizeMessageId(
        string? messageId)
    {
        if (string.IsNullOrWhiteSpace(
                messageId))
        {
            return null;
        }

        return messageId.Trim();
    }

    private void SetSelectedMessageWithoutReadMarking(
        MailMessageItemViewModel? message)
    {
        _suppressAutomaticReadMarking =
            true;

        try
        {
            SelectedMessage =
                message;
        }
        finally
        {
            _suppressAutomaticReadMarking =
                false;
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

    private void ResetMessagePagingState()
    {
        _loadedMessageLimit =
            MessagePageSize;

        HasMoreMessages =
            false;
    }

    private void UpdateHasMoreMessages()
    {
        var folder =
            _selectedFolder;

        HasMoreMessages =
            folder is not null &&
            folder.MessageCount >
                Messages.Count;
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

    private void LogMailOperationFailure(
        string operation,
        Exception exception)
    {
        switch (exception)
        {
            case MailKit.Security.AuthenticationException:
                _logger.LogWarning(
                    exception,
                    "{Operation} failed because authentication is required.",
                    operation);
                break;

            case SslHandshakeException:
                _logger.LogError(
                    exception,
                    "{Operation} failed because the TLS handshake could not be completed.",
                    operation);
                break;

            case SocketException:
            case IOException:
                _logger.LogWarning(
                    exception,
                    "{Operation} failed because the mail server or network is unavailable.",
                    operation);
                break;

            default:
                _logger.LogError(
                    exception,
                    "{Operation} failed unexpectedly.",
                    operation);
                break;
        }
    }

    private void SetErrorState(
        Exception exception)
    {
        SetConnectionErrorState(
            exception,
            showLoadError:
                true);
    }

    private void SetSynchronizationErrorState(
        Exception exception)
    {
        SetConnectionErrorState(
            exception,
            showLoadError:
                false);
    }

    private void SetConnectionErrorState(
        Exception exception,
        bool showLoadError)
    {
        HasLoadError =
            showLoadError;

        if (showLoadError)
        {
            IsEmptyFolder =
                false;
        }

        string errorMessage;

        switch (exception)
        {
            case MailKit.Security.AuthenticationException:
                ConnectionState =
                    MailConnectionState.AuthenticationRequired;

                ConnectionStatusText =
                    "Anmeldung erforderlich";

                errorMessage =
                    "Die gespeicherten Zugangsdaten wurden vom Mailserver nicht akzeptiert. " +
                    "Bitte melden Sie das Konto ab und anschließend erneut an.";
                break;

            case SslHandshakeException:
                ConnectionState =
                    MailConnectionState.SecurityError;

                ConnectionStatusText =
                    "Sicherheitsfehler";

                errorMessage =
                    "Die sichere Verbindung zum Mailserver konnte nicht geprüft werden. " +
                    "Aus Sicherheitsgründen wurde die Verbindung abgebrochen.";
                break;

            case SocketException:
            case IOException:
                ConnectionState =
                    MailConnectionState.Offline;

                ConnectionStatusText =
                    "Offline";

                errorMessage =
                    "Der Mailserver ist momentan nicht erreichbar. " +
                    "Bitte prüfen Sie Ihre Internetverbindung.";
                break;

            default:
                ConnectionState =
                    MailConnectionState.Error;

                ConnectionStatusText =
                    "Verbindungsfehler";

                errorMessage =
                    "Die E-Mail-Daten konnten momentan nicht geladen werden. " +
                    "Bitte versuchen Sie es erneut.";
                break;
        }

        LoadErrorMessage =
            showLoadError
                ? errorMessage
                : string.Empty;
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