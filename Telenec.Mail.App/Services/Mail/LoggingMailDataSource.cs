using MailKit.Security;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Sockets;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class LoggingMailDataSource :
    IMailDataSource
{
    private readonly ImapMailDataSource
        _inner;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly IMailReadReceiptStore
        _mailReadReceiptStore;

    private readonly IMailMessageCacheStore
        _mailMessageCacheStore;

    private readonly IMailFolderCacheStore
        _mailFolderCacheStore;

    private readonly ILogger<LoggingMailDataSource>
        _logger;

    public LoggingMailDataSource(
        ImapMailDataSource inner,
        IMailAccountStore mailAccountStore,
        IMailReadReceiptStore mailReadReceiptStore,
        AppDataPaths appDataPaths,
        ILogger<LoggingMailDataSource> logger)
    {
        ArgumentNullException.ThrowIfNull(
            inner);

        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            mailReadReceiptStore);

        ArgumentNullException.ThrowIfNull(
            appDataPaths);

        ArgumentNullException.ThrowIfNull(
            logger);

        _inner =
            inner;

        _mailAccountStore =
            mailAccountStore;

        _mailReadReceiptStore =
            mailReadReceiptStore;

        _mailMessageCacheStore =
            new SqliteMailMessageCacheStore(
                appDataPaths);

        _mailFolderCacheStore =
            new SqliteMailFolderCacheStore(
                appDataPaths);

        _logger =
            logger;
    }

    public async Task<IReadOnlyList<MailFolderData>>
        GetFoldersAsync(
            CancellationToken cancellationToken = default)
    {
        try
        {
            var folders =
                await _inner
                    .GetFoldersAsync(
                        cancellationToken);

            MailboxConnectivityState
                .MarkOnline();

            await PersistFolderCacheAsync(
                folders,
                cancellationToken);

            return folders;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (CanUseOfflineCache(
                exception,
                cancellationToken))
        {
            var cachedFolders =
                await TryLoadCachedFoldersAsync(
                    cancellationToken);

            if (cachedFolders is null)
            {
                _logger.LogWarning(
                    "Mailbox folder list could not use offline cache because no cached folder snapshot is available.");

                throw;
            }

            MailboxConnectivityState
                .MarkOfflineCacheActive();

            var exceptionType =
                exception.GetType().FullName
                ?? exception.GetType().Name;

            _logger.LogWarning(
                "Mailbox folder list loaded from offline cache because the mail server is unavailable. FolderCount={FolderCount}, ExceptionType={ExceptionType}.",
                cachedFolders.Count,
                exceptionType);

            return cachedFolders;
        }
    }

    public async Task<IReadOnlyList<MailMessageData>>
        GetMessagesAsync(
            string folderId,
            int maximumMessageCount = 20,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var messages =
                await _inner
                    .GetMessagesAsync(
                        folderId,
                        maximumMessageCount,
                        cancellationToken);

            await PersistReadReceiptsAsync(
                messages,
                cancellationToken);

            var enrichedMessages =
                await AttachPersistedReadReceiptsAsync(
                    messages,
                    cancellationToken);

            if (!MailSortState.HasTemporaryOverride)
            {
                await PersistMailCacheAsync(
                    folderId,
                    enrichedMessages,
                    cancellationToken);
            }

            return enrichedMessages;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (
                !MailSortState.HasTemporaryOverride &&
                CanUseOfflineCache(
                    exception,
                    cancellationToken))
        {
            var cachedMessages =
                await TryLoadCachedMessagePageAsync(
                    folderId,
                    skipMessageCount:
                        0,
                    maximumMessageCount:
                        maximumMessageCount,
                    cancellationToken:
                        cancellationToken);

            if (cachedMessages is null)
            {
                _logger.LogWarning(
                    "Mailbox messages could not use offline cache because no cached message snapshot is available.");

                throw;
            }

            var enrichedCachedMessages =
                await AttachPersistedReadReceiptsAsync(
                    cachedMessages,
                    cancellationToken);

            MailboxConnectivityState
                .MarkOfflineCacheActive();

            var exceptionType =
                exception.GetType().FullName
                ?? exception.GetType().Name;

            _logger.LogWarning(
                "Mailbox messages loaded from offline cache because the mail server is unavailable. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                enrichedCachedMessages.Count,
                exceptionType);

            return enrichedCachedMessages;
        }
    }

    public async Task<IReadOnlyList<MailMessageData>>
        GetMessagePageAsync(
            string folderId,
            int skipMessageCount,
            int maximumMessageCount,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var messages =
                await _inner
                    .GetMessagePageAsync(
                        folderId,
                        skipMessageCount,
                        maximumMessageCount,
                        cancellationToken);

            await PersistReadReceiptsAsync(
                messages,
                cancellationToken);

            return await AttachPersistedReadReceiptsAsync(
                messages,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (
                !MailSortState.HasTemporaryOverride &&
                CanUseOfflineCache(
                    exception,
                    cancellationToken))
        {
            var cachedMessages =
                await TryLoadCachedMessagePageAsync(
                    folderId,
                    skipMessageCount,
                    maximumMessageCount,
                    cancellationToken);

            if (cachedMessages is null)
            {
                _logger.LogWarning(
                    "Mailbox message page could not use offline cache because no cached message snapshot is available.");

                throw;
            }

            var enrichedCachedMessages =
                await AttachPersistedReadReceiptsAsync(
                    cachedMessages,
                    cancellationToken);

            MailboxConnectivityState
                .MarkOfflineCacheActive();

            var exceptionType =
                exception.GetType().FullName
                ?? exception.GetType().Name;

            _logger.LogWarning(
                "Mailbox message page loaded from offline cache because the mail server is unavailable. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                enrichedCachedMessages.Count,
                exceptionType);

            return enrichedCachedMessages;
        }
    }

    public Task DownloadAttachmentAsync(
        string folderId,
        uint uniqueId,
        string partSpecifier,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        return _inner
            .DownloadAttachmentAsync(
                folderId,
                uniqueId,
                partSpecifier,
                destination,
                cancellationToken);
    }

    public Task MarkAsReadAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        return _inner
            .MarkAsReadAsync(
                folderId,
                uniqueId,
                cancellationToken);
    }

    public Task MarkAsUnreadAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        return _inner
            .MarkAsUnreadAsync(
                folderId,
                uniqueId,
                cancellationToken);
    }

    public Task SetKeywordAsync(
        string folderId,
        uint uniqueId,
        string keyword,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMutationAsync(
            operation:
                "Message keyword update",

            messageCount:
                1,

            action:
                () =>
                    _inner.SetKeywordAsync(
                        folderId,
                        uniqueId,
                        keyword,
                        isEnabled,
                        cancellationToken),

            cancellationToken:
                cancellationToken);
    }

    public Task<MailMoveResult> MoveToTrashAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMoveAsync(
            operation:
                "Move to trash",

            messageCount:
                1,

            action:
                () =>
                    _inner.MoveToTrashAsync(
                        folderId,
                        uniqueId,
                        cancellationToken),

            cancellationToken:
                cancellationToken);
    }

    public Task<MailMoveResult> MoveToTrashAsync(
        string folderId,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            uniqueIds);

        return ExecuteMoveAsync(
            operation:
                "Move to trash",

            messageCount:
                uniqueIds.Count,

            action:
                () =>
                    _inner.MoveToTrashAsync(
                        folderId,
                        uniqueIds,
                        cancellationToken),

            cancellationToken:
                cancellationToken);
    }

    public Task<MailMoveResult> MoveMessagesAsync(
        string sourceFolderId,
        string targetFolderId,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            uniqueIds);

        return ExecuteMoveAsync(
            operation:
                "Message move",

            messageCount:
                uniqueIds.Count,

            action:
                () =>
                    _inner.MoveMessagesAsync(
                        sourceFolderId,
                        targetFolderId,
                        uniqueIds,
                        cancellationToken),

            cancellationToken:
                cancellationToken);
    }

    private async Task<IReadOnlyList<MailFolderData>?>
        TryLoadCachedFoldersAsync(
            CancellationToken cancellationToken)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null ||
            account.AccountId == Guid.Empty)
        {
            return null;
        }

        var folders =
            await _mailFolderCacheStore
                .GetFoldersAsync(
                    account.AccountId,
                    cancellationToken);

        /*
         * Ein echtes IMAP-Postfach besitzt mindestens den
         * Posteingang.
         *
         * Eine komplett leere lokale Ordnerliste wird daher
         * nicht als belastbarer Offline-Snapshot behandelt.
         */
        return folders.Count == 0
            ? null
            : folders;
    }

    private async Task<IReadOnlyList<MailMessageData>?>
        TryLoadCachedMessagePageAsync(
            string folderId,
            int skipMessageCount,
            int maximumMessageCount,
            CancellationToken cancellationToken)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null ||
            account.AccountId == Guid.Empty)
        {
            return null;
        }

        /*
         * Der Folder-State unterscheidet zuverlässig zwischen
         *
         * - "dieser Ordner wurde gecacht und ist wirklich leer"
         * - "für diesen Ordner existiert überhaupt kein Cache".
         *
         * Eine leere Nachrichtenliste allein könnte diese
         * beiden Fälle nicht unterscheiden.
         */
        var folderState =
            await _mailMessageCacheStore
                .GetFolderStateAsync(
                    account.AccountId,
                    folderId,
                    cancellationToken);

        if (folderState is null)
        {
            return null;
        }

        if (maximumMessageCount <= 0)
        {
            return Array.Empty<MailMessageData>();
        }

        return await _mailMessageCacheStore
            .GetMessagePageAsync(
                account.AccountId,
                folderId,
                skipMessageCount,
                maximumMessageCount,
                cancellationToken);
    }

    private async Task PersistFolderCacheAsync(
        IReadOnlyList<MailFolderData> folders,
        CancellationToken cancellationToken)
    {
        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync(
                        cancellationToken);

            if (account is null ||
                account.AccountId == Guid.Empty)
            {
                return;
            }

            await _mailFolderCacheStore
                .SaveFoldersAsync(
                    account.AccountId,
                    folders,
                    cancellationToken);

            _logger.LogInformation(
                "Offline mail folder cache snapshot updated successfully. FolderCount={FolderCount}.",
                folders.Count);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var exceptionType =
                exception.GetType().FullName
                ?? exception.GetType().Name;

            /*
             * Ein lokaler Cache-Fehler darf den erfolgreichen
             * Online-Abruf der Ordner niemals blockieren.
             *
             * Ordnernamen werden bewusst nicht protokolliert.
             */
            _logger.LogWarning(
                "Offline mail folder cache snapshot could not be persisted. FolderCount={FolderCount}, ExceptionType={ExceptionType}.",
                folders.Count,
                exceptionType);
        }
    }

    private async Task PersistMailCacheAsync(
        string folderId,
        IReadOnlyList<MailMessageData> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync(
                        cancellationToken);

            if (account is null ||
                account.AccountId == Guid.Empty)
            {
                return;
            }

            if (!MailFolderIdentityState.TryGet(
                    account.AccountId,
                    folderId,
                    out var uidValidity))
            {
                return;
            }

            await _mailMessageCacheStore
                .SaveMessagesAsync(
                    account.AccountId,
                    folderId,
                    uidValidity,
                    messages,
                    cancellationToken);

            _logger.LogInformation(
                "Offline mail cache snapshot updated successfully. MessageCount={MessageCount}.",
                messages.Count);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var exceptionType =
                exception.GetType().FullName
                ?? exception.GetType().Name;

            _logger.LogWarning(
                "Offline mail cache snapshot could not be persisted. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                messages.Count,
                exceptionType);
        }
    }

    private async Task PersistReadReceiptsAsync(
        IReadOnlyList<MailMessageData> messages,
        CancellationToken cancellationToken)
    {
        var readReceipts =
            messages
                .Select(
                    message =>
                        message.ReadReceipt)
                .Where(
                    readReceipt =>
                        readReceipt is not null)
                .Cast<MailReadReceiptData>()
                .Where(
                    CanPersistReadReceipt)
                .ToList();

        if (readReceipts.Count == 0)
        {
            return;
        }

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync(
                        cancellationToken);

            if (account is null ||
                account.AccountId == Guid.Empty)
            {
                _logger.LogWarning(
                    "Detected read receipts could not be persisted because no active mail account was available. ReceiptCount={ReceiptCount}.",
                    readReceipts.Count);

                return;
            }

            foreach (var readReceipt in
                     readReceipts)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                try
                {
                    await _mailReadReceiptStore
                        .SaveAsync(
                            account.AccountId,
                            readReceipt,
                            cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    var exceptionType =
                        exception.GetType().FullName
                        ?? exception.GetType().Name;

                    _logger.LogWarning(
                        "A detected read receipt could not be persisted. ExceptionType={ExceptionType}.",
                        exceptionType);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var exceptionType =
                exception.GetType().FullName
                ?? exception.GetType().Name;

            _logger.LogWarning(
                "Detected read receipts could not be persisted. ReceiptCount={ReceiptCount}, ExceptionType={ExceptionType}.",
                readReceipts.Count,
                exceptionType);
        }
    }

    private async Task<IReadOnlyList<MailMessageData>>
        AttachPersistedReadReceiptsAsync(
            IReadOnlyList<MailMessageData> messages,
            CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return messages;
        }

        var messageIds =
            messages
                .Select(
                    message =>
                        NormalizeMessageId(
                            message.MessageId))
                .Where(
                    messageId =>
                        !string.IsNullOrWhiteSpace(
                            messageId))
                .Select(
                    messageId =>
                        messageId!)
                .Distinct(
                    StringComparer.Ordinal)
                .ToArray();

        if (messageIds.Length == 0)
        {
            return messages;
        }

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync(
                        cancellationToken);

            if (account is null ||
                account.AccountId == Guid.Empty)
            {
                return messages;
            }

            var readReceiptsByMessageId =
                await _mailReadReceiptStore
                    .GetByOriginalMessageIdsAsync(
                        account.AccountId,
                        messageIds,
                        cancellationToken);

            if (readReceiptsByMessageId.Count == 0)
            {
                return messages;
            }

            var enrichedMessages =
                new MailMessageData[
                    messages.Count];

            var hasChanges =
                false;

            for (var index = 0;
                 index < messages.Count;
                 index++)
            {
                var message =
                    messages[index];

                var normalizedMessageId =
                    NormalizeMessageId(
                        message.MessageId);

                if (normalizedMessageId is null ||
                    !readReceiptsByMessageId.TryGetValue(
                        normalizedMessageId,
                        out var readReceipts) ||
                    readReceipts.Count == 0)
                {
                    enrichedMessages[index] =
                        message;

                    continue;
                }

                enrichedMessages[index] =
                    message with
                    {
                        ReceivedReadReceipts =
                            readReceipts
                    };

                hasChanges =
                    true;
            }

            return hasChanges
                ? enrichedMessages
                : messages;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var exceptionType =
                exception.GetType().FullName
                ?? exception.GetType().Name;

            _logger.LogWarning(
                "Stored read receipt state could not be attached to loaded messages. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                messages.Count,
                exceptionType);

            return messages;
        }
    }

    private static string?
        NormalizeMessageId(
            string? messageId)
    {
        if (string.IsNullOrWhiteSpace(
                messageId))
        {
            return null;
        }

        var normalized =
            messageId.Trim();

        if (normalized.Length >= 2 &&
            normalized[0] == '<' &&
            normalized[^1] == '>')
        {
            normalized =
                normalized[
                    1..^1]
                    .Trim();
        }

        return string.IsNullOrWhiteSpace(
                normalized)
            ? null
            : normalized;
    }

    private static bool CanPersistReadReceipt(
        MailReadReceiptData readReceipt)
    {
        return
            !string.IsNullOrWhiteSpace(
                readReceipt.OriginalMessageId) &&
            !string.IsNullOrWhiteSpace(
                readReceipt.SenderAddress) &&
            readReceipt.ReceiptDate.HasValue &&
            !string.IsNullOrWhiteSpace(
                readReceipt.Disposition);
    }

    private static bool CanUseOfflineCache(
        Exception exception,
        CancellationToken cancellationToken)
    {
        /*
         * Weder falsche Zugangsdaten noch TLS-/Zertifikats-
         * probleme dürfen vom Offline-Cache verdeckt werden.
         */
        if (exception is AuthenticationException ||
            exception is SslHandshakeException)
        {
            return false;
        }

        if (exception is OperationCanceledException)
        {
            return
                !cancellationToken
                    .IsCancellationRequested;
        }

        return
            exception is SocketException ||
            exception is IOException ||
            exception is TimeoutException;
    }

    private async Task ExecuteMutationAsync(
        string operation,
        int messageCount,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "{Operation} started. MessageCount={MessageCount}.",
            operation,
            messageCount);

        try
        {
            await action();

            _logger.LogInformation(
                "{Operation} completed successfully. MessageCount={MessageCount}.",
                operation,
                messageCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "{Operation} was cancelled. MessageCount={MessageCount}.",
                operation,
                messageCount);

            throw;
        }
        catch (Exception exception)
        {
            LogMutationFailure(
                operation,
                messageCount,
                exception);

            throw;
        }
    }

    private async Task<MailMoveResult>
        ExecuteMoveAsync(
            string operation,
            int messageCount,
            Func<Task<MailMoveResult>> action,
            CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "{Operation} started. MessageCount={MessageCount}.",
            operation,
            messageCount);

        try
        {
            var result =
                await action();

            _logger.LogInformation(
                "{Operation} completed successfully. MessageCount={MessageCount}, UndoAvailable={UndoAvailable}.",
                operation,
                messageCount,
                result.CanUndo);

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "{Operation} was cancelled. MessageCount={MessageCount}.",
                operation,
                messageCount);

            throw;
        }
        catch (Exception exception)
        {
            LogMutationFailure(
                operation,
                messageCount,
                exception);

            throw;
        }
    }

    private void LogMutationFailure(
        string operation,
        int messageCount,
        Exception exception)
    {
        var exceptionType =
            exception.GetType().FullName
            ?? exception.GetType().Name;

        switch (exception)
        {
            case MailKit.Security.AuthenticationException:
                _logger.LogWarning(
                    "{Operation} failed because authentication is required. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    operation,
                    messageCount,
                    exceptionType);
                break;

            case SslHandshakeException:
                _logger.LogError(
                    "{Operation} failed because the TLS handshake could not be completed. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    operation,
                    messageCount,
                    exceptionType);
                break;

            case SocketException:
            case IOException:
                _logger.LogWarning(
                    "{Operation} failed because the mail server or network is unavailable. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    operation,
                    messageCount,
                    exceptionType);
                break;

            case OperationCanceledException:
                _logger.LogWarning(
                    "{Operation} failed because the operation timed out. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    operation,
                    messageCount,
                    exceptionType);
                break;

            default:
                _logger.LogError(
                    "{Operation} failed unexpectedly. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    operation,
                    messageCount,
                    exceptionType);
                break;
        }
    }
}