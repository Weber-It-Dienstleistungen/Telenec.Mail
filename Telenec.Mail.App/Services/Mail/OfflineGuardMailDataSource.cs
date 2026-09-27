using System.IO;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public sealed class OfflineGuardMailDataSource :
    IMailDataSource
{
    private readonly LoggingMailDataSource
        _inner;

    public OfflineGuardMailDataSource(
        LoggingMailDataSource inner)
    {
        ArgumentNullException.ThrowIfNull(
            inner);

        _inner =
            inner;
    }

    /*
     * Reine Leseoperationen bleiben auch im Offline-Modus
     * erlaubt.
     *
     * LoggingMailDataSource entscheidet selbst, ob die Daten
     * online vom IMAP-Server oder aus dem lokalen Cache
     * geliefert werden.
     */
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

    /*
     * Attachment-Daten selbst werden derzeit nicht lokal
     * gecacht.
     *
     * Ein Download benötigt deshalb zwingend eine aktive
     * Serververbindung.
     */
    public Task DownloadAttachmentAsync(
        string folderId,
        uint uniqueId,
        string partSpecifier,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineOperationAllowed();

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
        EnsureOnlineOperationAllowed();

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
        EnsureOnlineOperationAllowed();

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
        EnsureOnlineOperationAllowed();

        return _inner
            .SetKeywordAsync(
                folderId,
                uniqueId,
                keyword,
                isEnabled,
                cancellationToken);
    }

    public Task<MailMoveResult> MoveToTrashAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineOperationAllowed();

        return _inner
            .MoveToTrashAsync(
                folderId,
                uniqueId,
                cancellationToken);
    }

    public Task<MailMoveResult> MoveToTrashAsync(
        string folderId,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            uniqueIds);

        EnsureOnlineOperationAllowed();

        return _inner
            .MoveToTrashAsync(
                folderId,
                uniqueIds,
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

        EnsureOnlineOperationAllowed();

        return _inner
            .MoveMessagesAsync(
                sourceFolderId,
                targetFolderId,
                uniqueIds,
                cancellationToken);
    }

    private static void EnsureOnlineOperationAllowed()
    {
        if (!MailboxConnectivityState
            .IsOfflineCacheActive)
        {
            return;
        }

        /*
         * Während lokale Cache-Daten angezeigt werden, dürfen
         * wir keine serververändernde Aktion simulieren.
         *
         * Auch ein stiller erneuter Netzwerkversuch wird hier
         * bewusst vermieden.
         *
         * Erst wenn ein normaler Lese-/Recovery-Vorgang den
         * Server wieder erfolgreich erreicht hat, setzt
         * MailboxConnectivityState den Zustand zurück auf
         * Online und Mutationen sind wieder erlaubt.
         */
        throw new InvalidOperationException(
            "Diese Aktion benötigt eine aktive Verbindung zum Mailserver.");
    }
}