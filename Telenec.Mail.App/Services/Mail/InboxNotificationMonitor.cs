using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public sealed record InboxNotificationBatch(
    int NewMessageCount,
    MailMessageData? LatestMessage);

public sealed class InboxNotificationMonitor
{
    private const string InboxFolderId =
        "INBOX";

    /*
     * Für die reine UID-Prüfung benötigen wir keine Bodies.
     *
     * 100 Zustände sind ausreichend großzügig, ohne die
     * regelmäßige Abfrage unnötig groß zu machen.
     */
    private const int StateSnapshotLimit =
        100;

    /*
     * Vollständige Nachrichtendaten werden ausschließlich
     * dann geladen, wenn tatsächlich neue ungelesene UIDs
     * erkannt wurden.
     */
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
         * unsere Baseline.
         *
         * Bereits vorhandene Nachrichten dürfen beim
         * Programmstart niemals als "neu eingegangen"
         * gemeldet werden.
         */
        if (!_baselineEstablished)
        {
            ApplyBaseline(
                snapshot);

            return null;
        }

        /*
         * Ändert sich UIDVALIDITY, besitzen die bisherigen
         * UIDs keinerlei Bedeutung mehr.
         *
         * Wir beginnen deshalb defensiv mit einer neuen
         * Baseline und erzeugen keine Benachrichtigung.
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

        /*
         * Keine neue UID:
         * Flag-Änderungen wie gelesen/ungelesen sind für
         * diesen Monitor ausdrücklich keine neue Mail.
         */
        if (newStates.Length == 0)
        {
            _highestKnownUid =
                Math.Max(
                    _highestKnownUid,
                    highestUidInSnapshot);

            return null;
        }

        /*
         * Neue, aber bereits gelesene Nachrichten werden
         * ebenfalls in die Baseline aufgenommen.
         *
         * Dadurch werden sie nicht bei einem späteren
         * Durchlauf doch noch gemeldet.
         */
        var newUnreadStates =
            newStates
                .Where(
                    message =>
                        message.IsUnread)
                .ToArray();

        _highestKnownUid =
            Math.Max(
                _highestKnownUid,
                highestUidInSnapshot);

        if (newUnreadStates.Length == 0)
        {
            return null;
        }

        MailMessageData?
            latestMessage =
                null;

        try
        {
            /*
             * Die sichtbare Mailansicht kann nach Absender
             * oder Betreff sortiert sein.
             *
             * Für Benachrichtigungen benötigen wir dagegen
             * immer die zeitlich neuesten Nachrichten.
             *
             * Der temporäre Sortier-Override gilt nur für
             * diesen asynchronen Aufrufpfad und verändert
             * nicht die sichtbare Sortierung des Benutzers.
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
             * Die UID-Prüfung war bereits erfolgreich.
             *
             * Können die vollständigen Maildaten gerade
             * nicht geladen werden, zeigen wir lieber einen
             * generischen Hinweis statt denselben Eingang
             * beim nächsten Durchlauf nochmals zu melden.
             */
        }

        return new InboxNotificationBatch(
            NewMessageCount:
                newUnreadStates.Length,

            LatestMessage:
                latestMessage);
    }

    private async Task<MailFolderMessageStateSnapshot>
        GetInboxStateAsync(
            CancellationToken cancellationToken)
    {
        /*
         * Auch die leichte UID-Prüfung muss unabhängig von
         * der sichtbaren Sortierung immer die neuesten
         * Nachrichten betrachten.
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