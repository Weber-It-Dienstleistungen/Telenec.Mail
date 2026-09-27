namespace Telenec.Mail.App.Services.Mail;

public sealed class OfflineGuardPermanentDeleteService :
    IMailPermanentDeleteService
{
    private readonly LoggingPermanentDeleteService
        _inner;

    public OfflineGuardPermanentDeleteService(
        LoggingPermanentDeleteService inner)
    {
        ArgumentNullException.ThrowIfNull(
            inner);

        _inner =
            inner;
    }

    public Task DeletePermanentlyAsync(
        string folderId,
        uint expectedUidValidity,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            uniqueIds);

        EnsureOnlineOperationAllowed();

        return _inner
            .DeletePermanentlyAsync(
                folderId,
                expectedUidValidity,
                uniqueIds,
                cancellationToken);
    }

    public Task<int> EmptyTrashAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineOperationAllowed();

        return _inner
            .EmptyTrashAsync(
                folderId,
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
         * Permanente Löschoperationen dürfen niemals auf Basis
         * eines lokalen Offline-Snapshots gestartet werden.
         *
         * Insbesondere die gecachte UIDVALIDITY und die darin
         * enthaltenen UIDs könnten gegenüber dem aktuellen
         * Serverzustand bereits veraltet sein.
         */
        throw new InvalidOperationException(
            "Diese Aktion benötigt eine aktive Verbindung zum Mailserver.");
    }
}