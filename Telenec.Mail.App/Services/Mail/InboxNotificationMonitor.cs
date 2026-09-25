using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public sealed record InboxNotificationBatch(
    int NewMessageCount,
    MailMessageData? LatestMessage)
{
    /*
     * UIDVALIDITY gehört zwingend zu den UIDs.
     *
     * Dadurch kann die nachgeschaltete Regel-Engine
     * sicherstellen, dass sich der Posteingang zwischen
     * Erkennung und Verarbeitung nicht neu aufgebaut hat.
     */
    public uint UidValidity
    { get; init; }

    /*
     * Alle seit der letzten Baseline neu erkannten UIDs.
     *
     * Dazu gehören bewusst auch Nachrichten, die bereits als
     * gelesen auf dem Server angekommen sind.
     *
     * Regeln sollen nicht von einem Seen-Flag abhängen.
     */
    public IReadOnlyList<uint> NewUniqueIds
    { get; init; } =
        Array.Empty<uint>();

    /*
     * Diese Teilmenge wird ausschließlich für
     * Desktop-/In-App-Benachrichtigungen benötigt.
     */
    public IReadOnlyList<uint> NewUnreadUniqueIds
    { get; init; } =
        Array.Empty<uint>();
}

public sealed class InboxNotificationMonitor
{
    private const string InboxFolderId =
        "INBOX";

    private const int StateSnapshotLimit =
        100;

    private const int MessageDetailLimit =
        50;

    private readonly IMailMessageStateSource
        _mailMessageStateSource;

    private readonly IMailDataSource
        _mailDataSource;

    private bool
        _baselineEstablished;

    private uint
        _uidValidity;

    private uint
        _highestKnownUid;

    public InboxNotificationMonitor(
        IMailMessageStateSource mailMessageStateSource,
        IMailDataSource mailDataSource)
    {
        ArgumentNullException.ThrowIfNull(
            mailMessageStateSource);

        ArgumentNullException.ThrowIfNull(
            mailDataSource);

        _mailMessageStateSource =
            mailMessageStateSource;

        _mailDataSource =
            mailDataSource;
    }

    public void Reset()
    {
        _baselineEstablished =
            false;

        _uidValidity =
            0;

        _highestKnownUid =
            0;
    }

    public async Task EstablishBaselineAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot =
            await GetInboxStateAsync(
                cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        ApplyBaseline(
            snapshot);
    }

    public async Task<InboxNotificationBatch?>
        CheckForNewMailAsync(
            CancellationToken cancellationToken = default)
    {
        var snapshot =
            await GetInboxStateAsync(
                cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * Der erste erfolgreiche Abruf ist ausschließlich
         * Baseline.
         *
         * Bereits vorhandene Nachrichten dürfen weder als
         * neue Nachricht gemeldet noch automatisch durch
         * Regeln verarbeitet werden.
         */
        if (!_baselineEstablished)
        {
            ApplyBaseline(
                snapshot);

            return null;
        }

        /*
         * Bei geänderter UIDVALIDITY verlieren alle alten
         * UIDs ihre Bedeutung.
         *
         * Wir beginnen daher neu mit einer Baseline.
         */
        if (snapshot.UidValidity == 0 ||
            snapshot.UidValidity !=
                _uidValidity)
        {
            ApplyBaseline(
                snapshot);

            return null;
        }

        var newStates =
            snapshot
                .Messages
                .Where(
                    message =>
                        message.UniqueId >
                        _highestKnownUid)
                .ToArray();

        var highestUidInSnapshot =
            GetHighestUid(
                snapshot);

        if (newStates.Length == 0)
        {
            _highestKnownUid =
                Math.Max(
                    _highestKnownUid,
                    highestUidInSnapshot);

            return null;
        }

        var newUnreadStates =
            newStates
                .Where(
                    message =>
                        message.IsUnread)
                .ToArray();

        /*
         * Die Baseline wird sofort fortgeschrieben.
         *
         * Eine nachgeschaltete Regel oder Benachrichtigung
         * darf bei einem späteren Durchlauf nicht dieselbe
         * neue UID erneut behandeln.
         */
        _highestKnownUid =
            Math.Max(
                _highestKnownUid,
                highestUidInSnapshot);

        MailMessageData?
            latestMessage =
                null;

        if (newUnreadStates.Length > 0)
        {
            try
            {
                /*
                 * Für die Benachrichtigung benötigen wir
                 * vollständige Daten nur dann, wenn mindestens
                 * eine neue ungelesene Nachricht vorhanden ist.
                 */
                using var sortOverride =
                    MailSortState.UseTemporarySort(
                        MailSortState.DefaultSort);

                var messages =
                    await _mailDataSource
                        .GetMessagesAsync(
                            InboxFolderId,
                            maximumMessageCount:
                                MessageDetailLimit,
                            cancellationToken:
                                cancellationToken);

                cancellationToken
                    .ThrowIfCancellationRequested();

                var newUnreadUids =
                    newUnreadStates
                        .Select(
                            message =>
                                message.UniqueId)
                        .ToHashSet();

                latestMessage =
                    messages
                        .Where(
                            message =>
                                newUnreadUids.Contains(
                                    message.UniqueId))
                        .OrderByDescending(
                            message =>
                                message.UniqueId)
                        .FirstOrDefault();
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                /*
                 * Die UID-Erkennung war bereits erfolgreich.
                 *
                 * Die Regelverarbeitung kann auch ohne
                 * vollständige Maildaten weiterarbeiten.
                 *
                 * Für Benachrichtigungen bleibt dann der
                 * generische Hinweis als Fallback.
                 */
            }
        }

        return new InboxNotificationBatch(
            NewMessageCount:
                newUnreadStates.Length,

            LatestMessage:
                latestMessage)
        {
            UidValidity =
                snapshot.UidValidity,

            NewUniqueIds =
                newStates
                    .Select(
                        message =>
                            message.UniqueId)
                    .ToArray(),

            NewUnreadUniqueIds =
                newUnreadStates
                    .Select(
                        message =>
                            message.UniqueId)
                    .ToArray()
        };
    }

    private async Task<MailFolderMessageStateSnapshot>
        GetInboxStateAsync(
            CancellationToken cancellationToken)
    {
        /*
         * Die UID-Prüfung muss immer unabhängig von der
         * sichtbaren Benutzersortierung auf den neuesten
         * Nachrichten arbeiten.
         */
        using var sortOverride =
            MailSortState.UseTemporarySort(
                MailSortState.DefaultSort);

        return await _mailMessageStateSource
            .GetMessageStatesAsync(
                InboxFolderId,
                maximumMessageCount:
                    StateSnapshotLimit,
                cancellationToken:
                    cancellationToken);
    }

    private void ApplyBaseline(
        MailFolderMessageStateSnapshot snapshot)
    {
        _uidValidity =
            snapshot.UidValidity;

        _highestKnownUid =
            GetHighestUid(
                snapshot);

        _baselineEstablished =
            snapshot.UidValidity != 0;
    }

    private static uint GetHighestUid(
        MailFolderMessageStateSnapshot snapshot)
    {
        if (snapshot.Messages.Count == 0)
        {
            return 0;
        }

        return snapshot
            .Messages
            .Max(
                message =>
                    message.UniqueId);
    }
}