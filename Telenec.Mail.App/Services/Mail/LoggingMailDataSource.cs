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

    private readonly ILogger<LoggingMailDataSource>
        _logger;

    public LoggingMailDataSource(
        ImapMailDataSource inner,
        IMailAccountStore mailAccountStore,
        IMailReadReceiptStore mailReadReceiptStore,
        ILogger<LoggingMailDataSource> logger)
    {
        ArgumentNullException.ThrowIfNull(
            inner);

        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            mailReadReceiptStore);

        ArgumentNullException.ThrowIfNull(
            logger);

        _inner =
            inner;

        _mailAccountStore =
            mailAccountStore;

        _mailReadReceiptStore =
            mailReadReceiptStore;

        _logger =
            logger;
    }

    public Task<IReadOnlyList<MailFolderData>>
        GetFoldersAsync(
            CancellationToken cancellationToken = default)
    {
        return _inner
            .GetFoldersAsync(
                cancellationToken);
    }

    public async Task<IReadOnlyList<MailMessageData>>
        GetMessagesAsync(
            string folderId,
            int maximumMessageCount = 20,
            CancellationToken cancellationToken = default)
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

        return await AttachPersistedReadReceiptsAsync(
            messages,
            cancellationToken);
    }

    public async Task<IReadOnlyList<MailMessageData>>
        GetMessagePageAsync(
            string folderId,
            int skipMessageCount,
            int maximumMessageCount,
            CancellationToken cancellationToken = default)
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
                    /*
                     * Die Persistenz einer Lesebestätigung ist
                     * eine Zusatzfunktion.
                     *
                     * Ein lokaler SQLite-Fehler darf niemals
                     * verhindern, dass der Benutzer seine
                     * Nachrichten weiterhin laden und lesen
                     * kann.
                     *
                     * Personenbezogene Daten wie Mailadresse,
                     * Message-ID oder Betreff werden bewusst
                     * nicht protokolliert.
                     */
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
            /*
             * Auch ein Fehler beim Ermitteln des aktiven
             * Accounts darf den normalen Mailabruf nicht
             * beeinträchtigen.
             */
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
            /*
             * Das Anreichern mit lokal gespeicherten
             * Lesebestätigungen ist eine Zusatzfunktion.
             *
             * Ein Fehler in SQLite darf den normalen
             * Mailabruf nicht verhindern.
             *
             * Auch hier werden bewusst weder Message-IDs noch
             * Mailadressen oder Betreffzeilen protokolliert.
             */
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
        /*
         * Der SQLite-Store ist bewusst streng.
         *
         * Eine Lesebestätigung ohne sichere Zuordnung zur
         * ursprünglichen Nachricht wird zwar weiterhin als
         * empfangene Nachricht angezeigt, aber nicht als
         * dauerhafter Status einer gesendeten Mail gespeichert.
         */
        return
            !string.IsNullOrWhiteSpace(
                readReceipt.OriginalMessageId) &&
            !string.IsNullOrWhiteSpace(
                readReceipt.SenderAddress) &&
            readReceipt.ReceiptDate.HasValue &&
            !string.IsNullOrWhiteSpace(
                readReceipt.Disposition);
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

            /*
             * Dieser Eintrag erfolgt bewusst direkt nach der
             * bestätigten Serveroperation.
             *
             * Ein eventuell anschließend im ViewModel
             * ausgeführter Reload gehört nicht mehr zu dieser
             * Mutation.
             *
             * Dadurch behauptet das Log bei einem späteren
             * Synchronisierungsproblem nicht fälschlich, dass
             * das Verschieben selbst fehlgeschlagen sei.
             */
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

    /*
     * Für Mutationsfehler wird bewusst nicht das komplette
     * Exception-Objekt an den Logger übergeben.
     *
     * Die Produktionsdiagnostik benötigt hier zunächst:
     *
     * - Art der Mutation
     * - Anzahl Nachrichten
     * - technische Fehlerklasse
     *
     * Mailadressen, Betreffzeilen, Ordnernamen, UIDs und
     * sonstige Nachrichteninhalte gehören ausdrücklich nicht
     * in diese Logeinträge.
     */
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
                /*
                 * Der vom Aufrufer ausgelöste Abbruch wurde
                 * bereits separat behandelt.
                 *
                 * Ein hier ankommender OperationCanceledException
                 * deutet deshalb typischerweise auf einen internen
                 * Timeout hin.
                 */
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