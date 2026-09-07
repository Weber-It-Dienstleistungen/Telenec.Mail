using MailKit.Security;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Sockets;

namespace Telenec.Mail.App.Services.Mail;

public sealed class LoggingPermanentDeleteService :
    IMailPermanentDeleteService
{
    private readonly MailKitPermanentDeleteService
        _inner;

    private readonly ILogger<LoggingPermanentDeleteService>
        _logger;

    public LoggingPermanentDeleteService(
        MailKitPermanentDeleteService inner,
        ILogger<LoggingPermanentDeleteService> logger)
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

    public async Task DeletePermanentlyAsync(
        string folderId,
        uint expectedUidValidity,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            uniqueIds);

        var messageCount =
            uniqueIds.Count;

        _logger.LogWarning(
            "Permanent delete started. MessageCount={MessageCount}.",
            messageCount);

        try
        {
            await _inner
                .DeletePermanentlyAsync(
                    folderId,
                    expectedUidValidity,
                    uniqueIds,
                    cancellationToken);

            _logger.LogWarning(
                "Permanent delete completed and was confirmed by the server. MessageCount={MessageCount}.",
                messageCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Permanent delete was cancelled. The final server state must be synchronized. MessageCount={MessageCount}.",
                messageCount);

            throw;
        }
        catch (Exception exception)
        {
            LogPermanentDeleteFailure(
                messageCount,
                exception);

            throw;
        }
    }

    public async Task<int> EmptyTrashAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        /*
         * Zu diesem Zeitpunkt kennen wir die tatsächliche
         * serverseitige Nachrichtenanzahl bewusst noch nicht.
         *
         * Sie wird erst im inneren Service direkt aus dem
         * Papierkorb ermittelt.
         */
        _logger.LogWarning(
            "Empty trash started.");

        try
        {
            var snapshotMessageCount =
                await _inner
                    .EmptyTrashAsync(
                        folderId,
                        cancellationToken);

            /*
             * Dieser Eintrag bedeutet:
             *
             * Sämtliche UIDs des serverseitigen Snapshots
             * wurden nach UID EXPUNGE als nicht mehr vorhanden
             * bestätigt.
             *
             * Neu während der Operation hinzugekommene
             * Nachrichten gehören bewusst nicht zu diesem
             * Snapshot.
             */
            _logger.LogWarning(
                "Empty trash completed and the server snapshot was confirmed deleted. SnapshotMessageCount={SnapshotMessageCount}.",
                snapshotMessageCount);

            return snapshotMessageCount;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            /*
             * Wie beim normalen Permanent Delete kann ein
             * Abbruch theoretisch nach dem verändernden
             * Serverkommando erfolgt sein.
             */
            _logger.LogWarning(
                "Empty trash was cancelled. The final server state must be synchronized.");

            throw;
        }
        catch (Exception exception)
        {
            LogEmptyTrashFailure(
                exception);

            throw;
        }
    }

    /*
     * Bei Permanent Delete übergeben wir bewusst nicht das
     * komplette Exception-Objekt an den Logger.
     *
     * Für die Diagnose reichen:
     *
     * - Anzahl der Nachrichten,
     * - technische Fehlerklasse,
     * - Aussage darüber, ob die Operation sicher nicht
     *   begonnen hat oder ob der endgültige Serverzustand
     *   erneut synchronisiert werden muss.
     *
     * Ordnernamen, UIDs und Nachrichteninhalte werden nicht
     * protokolliert.
     */
    private void LogPermanentDeleteFailure(
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
                    "Permanent delete did not begin because authentication failed. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case SslHandshakeException:
                _logger.LogError(
                    "Permanent delete did not begin because the TLS handshake failed. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case NotSupportedException:
                _logger.LogWarning(
                    "Permanent delete did not begin because the server does not support the required safe delete capability. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case ArgumentException:
                _logger.LogWarning(
                    "Permanent delete did not begin because the request was invalid. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case SocketException:
            case IOException:
                _logger.LogWarning(
                    "Permanent delete result could not be confirmed because the mail server or network became unavailable. The final server state must be synchronized. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case OperationCanceledException:
                _logger.LogWarning(
                    "Permanent delete result could not be confirmed because the operation timed out. The final server state must be synchronized. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case InvalidOperationException:
                _logger.LogWarning(
                    "Permanent delete was stopped by a server-state safety or confirmation check. The final server state must be synchronized. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            default:
                _logger.LogError(
                    "Permanent delete result is unknown because an unexpected error occurred. The final server state must be synchronized. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;
        }
    }

    private void LogEmptyTrashFailure(
        Exception exception)
    {
        var exceptionType =
            exception.GetType().FullName
            ?? exception.GetType().Name;

        switch (exception)
        {
            case MailKit.Security.AuthenticationException:
                _logger.LogWarning(
                    "Empty trash did not begin because authentication failed. ExceptionType={ExceptionType}.",
                    exceptionType);
                break;

            case SslHandshakeException:
                _logger.LogError(
                    "Empty trash did not begin because the TLS handshake failed. ExceptionType={ExceptionType}.",
                    exceptionType);
                break;

            case NotSupportedException:
                _logger.LogWarning(
                    "Empty trash did not begin because the server does not support the required safe delete capability. ExceptionType={ExceptionType}.",
                    exceptionType);
                break;

            case ArgumentException:
                _logger.LogWarning(
                    "Empty trash did not begin because the request was invalid. ExceptionType={ExceptionType}.",
                    exceptionType);
                break;

            case SocketException:
            case IOException:
                /*
                 * Der Verbindungsabbruch könnte nach dem
                 * UID EXPUNGE erfolgt sein.
                 *
                 * Deshalb niemals behaupten, dass das Leeren
                 * fehlgeschlagen ist.
                 */
                _logger.LogWarning(
                    "Empty trash result could not be confirmed because the mail server or network became unavailable. The final server state must be synchronized. ExceptionType={ExceptionType}.",
                    exceptionType);
                break;

            case OperationCanceledException:
                _logger.LogWarning(
                    "Empty trash result could not be confirmed because the operation timed out. The final server state must be synchronized. ExceptionType={ExceptionType}.",
                    exceptionType);
                break;

            case InvalidOperationException:
                _logger.LogWarning(
                    "Empty trash was stopped by a server-state safety or confirmation check. The final server state must be synchronized. ExceptionType={ExceptionType}.",
                    exceptionType);
                break;

            default:
                _logger.LogError(
                    "Empty trash result is unknown because an unexpected error occurred. The final server state must be synchronized. ExceptionType={ExceptionType}.",
                    exceptionType);
                break;
        }
    }
}