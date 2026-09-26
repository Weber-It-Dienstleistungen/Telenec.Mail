using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Sockets;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class ImapMailMessageStateSource
    : IMailMessageStateSource
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly IMailMessageCacheStore
        _mailMessageCacheStore;

    private readonly ILogger<ImapMailMessageStateSource>
        _logger;

    public ImapMailMessageStateSource(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        AppDataPaths appDataPaths,
        ILogger<ImapMailMessageStateSource> logger)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        ArgumentNullException.ThrowIfNull(
            appDataPaths);

        ArgumentNullException.ThrowIfNull(
            logger);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _mailMessageCacheStore =
            new SqliteMailMessageCacheStore(
                appDataPaths);

        _logger =
            logger;
    }

    public async Task<MailFolderMessageStateSnapshot>
        GetMessageStatesAsync(
            string folderId,
            int maximumMessageCount = 20,
            CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId);

        if (maximumMessageCount <= 0)
        {
            return new MailFolderMessageStateSnapshot(
                FolderId:
                    folderId,

                UidValidity:
                    0,

                Messages:
                    Array.Empty<MailMessageStateData>());
        }

        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            throw new InvalidOperationException(
                "Es ist kein aktives Mailkonto eingerichtet.");
        }

        try
        {
            return await GetOnlineMessageStatesAsync(
                account,
                folderId,
                maximumMessageCount,
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
            /*
             * Nur reguläre sichtbare UI-Abrufe dürfen auf den
             * lokalen Snapshot zurückfallen.
             *
             * Hintergrunddienste wie der New-Mail-Monitor
             * verwenden einen temporären Sortierkontext.
             * Für diese wäre der sichtbare Nachrichten-Cache
             * keine belastbare Quelle, da er beispielsweise
             * nach Absender sortiert sein kann.
             */
            var cachedFolderState =
                await _mailMessageCacheStore
                    .GetFolderStateAsync(
                        account.AccountId,
                        folderId,
                        cancellationToken);

            if (cachedFolderState is null)
            {
                _logger.LogWarning(
                    "Mail message state could not use offline cache because no cached folder identity is available.");

                throw;
            }

            var cachedMessages =
                await _mailMessageCacheStore
                    .GetMessagePageAsync(
                        account.AccountId,
                        folderId,
                        skipMessageCount:
                            0,
                        maximumMessageCount:
                            maximumMessageCount,
                        cancellationToken:
                            cancellationToken);

            var cachedStates =
                cachedMessages
                    .Select(
                        message =>
                            new MailMessageStateData(
                                UniqueId:
                                    message.UniqueId,

                                IsUnread:
                                    message.IsUnread))
                    .ToArray();

            var exceptionType =
                exception.GetType().FullName
                ?? exception.GetType().Name;

            _logger.LogWarning(
                "Mail message state loaded from offline cache because the mail server is unavailable. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                cachedStates.Length,
                exceptionType);

            return new MailFolderMessageStateSnapshot(
                FolderId:
                    cachedFolderState.FolderId,

                UidValidity:
                    cachedFolderState.UidValidity,

                Messages:
                    cachedStates);
        }
    }

    private async Task<MailFolderMessageStateSnapshot>
        GetOnlineMessageStatesAsync(
            MailAccount account,
            string folderId,
            int maximumMessageCount,
            CancellationToken cancellationToken)
    {
        using var client =
            await CreateAuthenticatedClientAsync(
                account,
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    folderId,
                    cancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            var uidValidity =
                folder.UidValidity;

            /*
             * Die Ordneridentität wird ausschließlich aus
             * einem erfolgreich geöffneten IMAP-Ordner
             * übernommen.
             *
             * Ein Offline-Snapshot darf diese Online-Identität
             * ausdrücklich nicht überschreiben.
             */
            MailFolderIdentityState.Set(
                account.AccountId,
                folder.FullName,
                uidValidity);

            if (folder.Count == 0)
            {
                return new MailFolderMessageStateSnapshot(
                    FolderId:
                        folder.FullName,

                    UidValidity:
                        uidValidity,

                    Messages:
                        Array.Empty<MailMessageStateData>());
            }

            var uniqueIds =
                await GetSortedMessageUniqueIdsAsync(
                    folder,
                    maximumMessageCount,
                    cancellationToken);

            if (uniqueIds.Count == 0)
            {
                return new MailFolderMessageStateSnapshot(
                    FolderId:
                        folder.FullName,

                    UidValidity:
                        uidValidity,

                    Messages:
                        Array.Empty<MailMessageStateData>());
            }

            /*
             * Envelope bleibt hier enthalten.
             *
             * Beim Server-SORT benötigen wir es zwar nicht
             * zwingend zur Reihenfolge, der lokale Fallback
             * verwendet jedoch dieselben Headerdaten.
             *
             * Bodies, Attachments und MIME-Inhalte werden
             * weiterhin ausdrücklich nicht geladen.
             */
            var summaries =
                await folder.FetchAsync(
                    uniqueIds,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Flags |
                    MessageSummaryItems.Envelope,
                    cancellationToken);

            /*
             * MailKit garantiert bei einem Fetch über eine
             * UID-Liste nicht, dass die Ergebnisse in exakt
             * derselben Reihenfolge zurückkommen.
             *
             * Deshalb stellen wir die Reihenfolge wieder her,
             * die zuvor durch IMAP SORT bzw. den lokalen
             * Fallback ermittelt wurde.
             */
            var orderedSummaries =
                MailSortOrder
                    .RestoreRequestedUniqueIdOrder(
                        summaries,
                        uniqueIds);

            var states =
                orderedSummaries
                    .Select(
                        summary =>
                            new MailMessageStateData(
                                UniqueId:
                                    summary.UniqueId.Id,

                                IsUnread:
                                    !summary.Flags.HasValue ||
                                    !summary.Flags.Value.HasFlag(
                                        MessageFlags.Seen)))
                    .ToList();

            return new MailFolderMessageStateSnapshot(
                FolderId:
                    folder.FullName,

                UidValidity:
                    uidValidity,

                Messages:
                    states);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static async Task<IList<UniqueId>>
        GetSortedMessageUniqueIdsAsync(
            IMailFolder folder,
            int maximumMessageCount,
            CancellationToken cancellationToken)
    {
        var sort =
            MailSortState.Effective;

        try
        {
            /*
             * IMAP SORT sortiert den vollständigen Ordner
             * serverseitig.
             *
             * Erst DANACH werden die benötigten UIDs für die
             * aktuell sichtbare Tiefe ausgewählt.
             */
            var sortedUniqueIds =
                await folder.SortAsync(
                    SearchQuery.All,
                    MailSortOrder
                        .CreateServerOrder(
                            sort),
                    cancellationToken);

            return sortedUniqueIds
                .Take(
                    maximumMessageCount)
                .ToList();
        }
        catch (NotSupportedException)
        {
            /*
             * Falls der Server SORT oder das gewünschte
             * Sortierkriterium nicht unterstützt, laden wir
             * ausschließlich die leichten Headerdaten des
             * Ordners und sortieren diese lokal.
             *
             * Bodies werden hierbei nicht geladen.
             */
            var lightweightSummaries =
                await folder.FetchAsync(
                    0,
                    -1,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope,
                    cancellationToken);

            return MailSortOrder
                .SortSummaries(
                    lightweightSummaries,
                    sort)
                .Take(
                    maximumMessageCount)
                .Select(
                    summary =>
                        summary.UniqueId)
                .ToList();
        }
    }

    private async Task<ImapClient>
        CreateAuthenticatedClientAsync(
            MailAccount account,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            account);

        var credential =
            await _credentialStore
                .ReadAsync(
                    account.AccountId,
                    cancellationToken);

        if (credential is null ||
            string.IsNullOrEmpty(
                credential.Password))
        {
            throw new InvalidOperationException(
                "Für das Mailkonto sind keine Zugangsdaten gespeichert.");
        }

        var client =
            new ImapClient();

        try
        {
            await client.ConnectAsync(
                ImapHost,
                ImapPort,
                SecureSocketOptions.SslOnConnect,
                cancellationToken);

            await client.AuthenticateAsync(
                account.EmailAddress,
                credential.Password,
                cancellationToken);

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    private static bool CanUseOfflineCache(
        Exception exception,
        CancellationToken cancellationToken)
    {
        /*
         * Sicherheits- oder Authentifizierungsprobleme dürfen
         * niemals durch lokale Cache-Daten verdeckt werden.
         *
         * Ein ungültiges Zertifikat oder falsches Passwort
         * ist fachlich etwas völlig anderes als "kein Netz".
         */
        if (exception is AuthenticationException ||
            exception is SslHandshakeException)
        {
            return false;
        }

        /*
         * Ein vom Aufrufer gewünschter Abbruch ist ebenfalls
         * kein Offline-Zustand.
         *
         * Ein interner Netzwerk-Timeout kann dagegen sinnvoll
         * auf den letzten lokalen Snapshot zurückfallen.
         */
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

    private static void ValidateFolderId(
        string folderId)
    {
        if (string.IsNullOrWhiteSpace(
                folderId))
        {
            throw new ArgumentException(
                "Der Ordner darf nicht leer sein.",
                nameof(folderId));
        }
    }

    private static async Task DisconnectSafelyAsync(
        ImapClient client)
    {
        if (!client.IsConnected)
        {
            return;
        }

        try
        {
            await client.DisconnectAsync(
                true,
                CancellationToken.None);
        }
        catch
        {
        }
    }
}