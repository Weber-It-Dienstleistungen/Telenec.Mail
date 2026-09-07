using MailKit.Security;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Sockets;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public sealed class LoggingMailDataSource :
    IMailDataSource
{
    private readonly ImapMailDataSource
        _inner;

    private readonly ILogger<LoggingMailDataSource>
        _logger;

    public LoggingMailDataSource(
        ImapMailDataSource inner,
        ILogger<LoggingMailDataSource> logger)
    {
        ArgumentNullException.ThrowIfNull(
            inner);

        ArgumentNullException.ThrowIfNull(
            logger);

        _inner =
            inner;

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

    public Task<IReadOnlyList<MailMessageData>>
        GetMessagesAsync(
            string folderId,
            int maximumMessageCount = 20,
            CancellationToken cancellationToken = default)
    {
        return _inner
            .GetMessagesAsync(
                folderId,
                maximumMessageCount,
                cancellationToken);
    }

    public Task<IReadOnlyList<MailMessageData>>
        GetMessagePageAsync(
            string folderId,
            int skipMessageCount,
            int maximumMessageCount,
            CancellationToken cancellationToken = default)
    {
        return _inner
            .GetMessagePageAsync(
                folderId,
                skipMessageCount,
                maximumMessageCount,
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