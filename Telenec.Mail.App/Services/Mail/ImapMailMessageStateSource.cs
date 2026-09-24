using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
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

    public ImapMailMessageStateSource(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;
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

        using var client =
            await CreateAuthenticatedClientAsync(
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
            CancellationToken cancellationToken)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            throw new InvalidOperationException(
                "Es ist kein aktives Mailkonto eingerichtet.");
        }

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