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

            /*
             * MailKitPermanentDeleteService kehrt nur dann
             * erfolgreich zurück, wenn:
             *
             * - UIDPLUS verfügbar war,
             * - der echte Papierkorb bestätigt wurde,
             * - UIDVALIDITY gepasst hat,
             * - alle angeforderten UIDs vor der Operation
             *   vorhanden waren,
             * - UID EXPUNGE ausgeführt wurde,
             * - anschließend keine dieser UIDs mehr vorhanden
             *   war und
             * - UIDVALIDITY weiterhin unverändert war.
             *
             * Erst deshalb dürfen wir hier von einem
             * bestätigten Permanent Delete sprechen.
             */
            _logger.LogWarning(
                "Permanent delete completed and was confirmed by the server. MessageCount={MessageCount}.",
                messageCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            /*
             * Auch ein vom Aufrufer ausgelöster Abbruch wird
             * bei einer irreversiblen Operation nicht als
             * gewöhnliches "cancelled" behandelt.
             *
             * Der Abbruch könnte theoretisch nach dem ersten
             * verändernden Serverkommando erfolgt sein.
             *
             * Deshalb muss anschließend der echte
             * Serverzustand maßgeblich sein.
             */
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
                /*
                 * Die Authentifizierung findet im inneren
                 * Service vor jeder verändernden
                 * Serveroperation statt.
                 */
                _logger.LogWarning(
                    "Permanent delete did not begin because authentication failed. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case SslHandshakeException:
                /*
                 * Auch der TLS-Handshake liegt vor jeder
                 * verändernden Serveroperation.
                 */
                _logger.LogError(
                    "Permanent delete did not begin because the TLS handshake failed. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case NotSupportedException:
                /*
                 * Der produktive Service prüft UIDPLUS vor
                 * dem ersten verändernden Kommando.
                 */
                _logger.LogWarning(
                    "Permanent delete did not begin because the server does not support the required safe delete capability. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case ArgumentException:
                /*
                 * Eingabevalidierung erfolgt ebenfalls vor
                 * dem Verbindungsaufbau bzw. vor einer
                 * Servermutation.
                 */
                _logger.LogWarning(
                    "Permanent delete did not begin because the request was invalid. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case SocketException:
            case IOException:
                /*
                 * Ein Verbindungsabbruch kann sowohl vor als
                 * auch nach dem UID EXPUNGE auftreten.
                 *
                 * Deshalb darf hier nicht behauptet werden,
                 * dass die Löschung fehlgeschlagen ist.
                 */
                _logger.LogWarning(
                    "Permanent delete result could not be confirmed because the mail server or network became unavailable. The final server state must be synchronized. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case OperationCanceledException:
                /*
                 * Dieser Zweig betrifft insbesondere den
                 * internen Timeout des Permanent-Delete-
                 * Services.
                 */
                _logger.LogWarning(
                    "Permanent delete result could not be confirmed because the operation timed out. The final server state must be synchronized. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            case InvalidOperationException:
                /*
                 * InvalidOperationException wird im inneren
                 * Service sowohl für Sicherheitsprüfungen vor
                 * dem Löschen als auch für eine nicht
                 * eindeutige Bestätigung nach dem Löschen
                 * verwendet.
                 *
                 * Die Logging-Hülle darf deshalb keinen
                 * konkreteren Zustand behaupten.
                 */
                _logger.LogWarning(
                    "Permanent delete was stopped by a server-state safety or confirmation check. The final server state must be synchronized. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;

            default:
                /*
                 * Bei einem unbekannten Fehler einer
                 * irreversiblen Operation ist eine
                 * konservative Aussage zwingend.
                 */
                _logger.LogError(
                    "Permanent delete result is unknown because an unexpected error occurred. The final server state must be synchronized. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                    messageCount,
                    exceptionType);
                break;
        }
    }
}