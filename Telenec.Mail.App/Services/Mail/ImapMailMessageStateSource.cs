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
                await GetNewestMessageUniqueIdsAsync(
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
             * Envelope wird hier zusätzlich benötigt, damit die
             * leichte State-Liste exakt dieselbe deterministische
             * Sortierreihenfolge verwenden kann wie
             * ImapMailDataSource.
             *
             * Es werden weiterhin ausdrücklich keine Bodies,
             * Attachments oder MIME-Inhalte geladen.
             */
            var summaries =
                await folder.FetchAsync(
                    uniqueIds,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Flags |
                    MessageSummaryItems.Envelope,
                    cancellationToken);

            /*
             * Wichtig für Paging:
             *
             * MailKit garantiert bei einem Fetch über eine
             * UID-Liste nicht, dass die zurückgegebenen
             * IMessageSummary-Objekte in derselben Reihenfolge
             * wie die angeforderten UIDs stehen.
             *
             * Die eigentliche Nachrichten-Datenquelle sortiert
             * ihre Summaries ebenfalls nach Datum und danach
             * nach Index.
             *
             * Der State-Source muss dieselbe Reihenfolge
             * verwenden, weil das ViewModel damit prüft, ob die
             * bereits sichtbaren Nachrichten noch exakt den
             * aktuellen Anfang des Serverordners bilden.
             */
            var orderedSummaries =
                summaries
                    .Where(
                        summary =>
                            summary.UniqueId.IsValid)
                    .OrderByDescending(
                        GetMessageSortDate)
                    .ThenByDescending(
                        summary =>
                            summary.Index)
                    .ToList();

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
        GetNewestMessageUniqueIdsAsync(
            IMailFolder folder,
            int maximumMessageCount,
            CancellationToken cancellationToken)
    {
        try
        {
            var sortedUniqueIds =
                await folder.SortAsync(
                    SearchQuery.All,
                    new[]
                    {
                        OrderBy.ReverseDate
                    },
                    cancellationToken);

            return sortedUniqueIds
                .Take(
                    maximumMessageCount)
                .ToList();
        }
        catch (NotSupportedException)
        {
            var lightweightSummaries =
                await folder.FetchAsync(
                    0,
                    -1,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope,
                    cancellationToken);

            return lightweightSummaries
                .OrderByDescending(
                    GetMessageSortDate)
                .ThenByDescending(
                    summary =>
                        summary.Index)
                .Take(
                    maximumMessageCount)
                .Select(
                    summary =>
                        summary.UniqueId)
                .ToList();
        }
    }

    private static DateTimeOffset GetMessageSortDate(
        IMessageSummary summary)
    {
        return summary.Envelope?.Date
            ?? DateTimeOffset.MinValue;
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