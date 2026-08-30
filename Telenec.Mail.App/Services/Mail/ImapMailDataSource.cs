using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using System.Net;
using System.Text.RegularExpressions;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class ImapMailDataSource : IMailDataSource
{
    private const string ImapHost = "mail.necnet.de";
    private const int ImapPort = 993;

    private readonly IMailAccountStore _mailAccountStore;
    private readonly ICredentialStore _credentialStore;

    public ImapMailDataSource(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        _mailAccountStore = mailAccountStore;
        _credentialStore = credentialStore;
    }

    public async Task<IReadOnlyList<MailFolderData>> GetFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folders =
                new List<IMailFolder>
                {
                    client.Inbox
                };

            if (client.PersonalNamespaces.Count > 0)
            {
                var serverFolders =
                    await client.GetFoldersAsync(
                        client.PersonalNamespaces[0],
                        StatusItems.Count | StatusItems.Unread,
                        false,
                        cancellationToken);

                folders.AddRange(serverFolders);
            }

            var uniqueFolders =
                folders
                    .Where(folder =>
                        !folder.Attributes.HasFlag(
                            FolderAttributes.NoSelect))
                    .GroupBy(
                        folder => folder.FullName,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(folder =>
                        GetFolderSortOrder(
                            folder,
                            client.Inbox.FullName))
                    .ThenBy(
                        folder => GetDisplayName(
                            folder,
                            client.Inbox.FullName),
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            var result =
                uniqueFolders
                    .Select(folder =>
                    {
                        var unreadCount =
                            Math.Max(
                                folder.Unread,
                                0);

                        var messageCount =
                            Math.Max(
                                folder.Count,
                                0);

                        var subtitle =
                            unreadCount > 0
                                ? $"{unreadCount} ungelesene Nachrichten"
                                : $"{messageCount} Nachrichten";

                        return new MailFolderData(
                            FolderId: folder.FullName,
                            DisplayName: GetDisplayName(
                                folder,
                                client.Inbox.FullName),
                            HeaderSubtitle: subtitle,
                            UnreadCount: unreadCount);
                    })
                    .ToList();

            return result;
        }
        finally
        {
            await DisconnectSafelyAsync(client);
        }
    }

    public async Task<IReadOnlyList<MailMessageData>> GetMessagesAsync(
        string folderId,
        int maximumMessageCount = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new ArgumentException(
                "Der Ordner darf nicht leer sein.",
                nameof(folderId));
        }

        if (maximumMessageCount <= 0)
        {
            return Array.Empty<MailMessageData>();
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

            if (folder.Count == 0)
            {
                return Array.Empty<MailMessageData>();
            }

            var minimumIndex =
                Math.Max(
                    0,
                    folder.Count - maximumMessageCount);

            var maximumIndex =
                folder.Count - 1;

            var summaries =
                await folder.FetchAsync(
                    minimumIndex,
                    maximumIndex,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope |
                    MessageSummaryItems.Flags |
                    MessageSummaryItems.BodyStructure,
                    cancellationToken);

            var messages =
                new List<MailMessageData>();

            foreach (var summary in
                     summaries.OrderByDescending(
                         item => item.Index))
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var body =
                    await GetBodyTextAsync(
                        folder,
                        summary,
                        cancellationToken);

                messages.Add(
                    CreateMessageData(
                        summary,
                        body));
            }

            return messages;
        }
        finally
        {
            await DisconnectSafelyAsync(client);
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

    private static async Task<string> GetBodyTextAsync(
        IMailFolder folder,
        IMessageSummary summary,
        CancellationToken cancellationToken)
    {
        BodyPartText? bodyPart =
            summary.TextBody;

        var isHtml =
            false;

        if (bodyPart is null)
        {
            bodyPart =
                summary.HtmlBody;

            isHtml =
                bodyPart is not null;
        }

        if (bodyPart is null)
        {
            return string.Empty;
        }

        var entity =
            await folder.GetBodyPartAsync(
                summary.UniqueId,
                bodyPart,
                cancellationToken);

        if (entity is not TextPart textPart)
        {
            return string.Empty;
        }

        var text =
            textPart.Text ?? string.Empty;

        if (isHtml)
        {
            text =
                ConvertHtmlToPlainText(
                    text);
        }

        return NormalizeBodyText(text);
    }

    private static MailMessageData CreateMessageData(
        IMessageSummary summary,
        string body)
    {
        var senderMailbox =
            summary.Envelope?
                .From?
                .Mailboxes
                .FirstOrDefault();

        var recipientMailbox =
            summary.Envelope?
                .To?
                .Mailboxes
                .FirstOrDefault();

        var senderAddress =
            senderMailbox?.Address
            ?? string.Empty;

        var senderName =
            !string.IsNullOrWhiteSpace(
                senderMailbox?.Name)
                ? senderMailbox.Name
                : senderAddress;

        if (string.IsNullOrWhiteSpace(
                senderName))
        {
            senderName =
                "Unbekannter Absender";
        }

        var recipientAddress =
            recipientMailbox?.Address
            ?? string.Empty;

        var subject =
            summary.Envelope?.Subject;

        if (string.IsNullOrWhiteSpace(
                subject))
        {
            subject =
                "(Kein Betreff)";
        }

        var date =
            summary.Envelope?.Date
            ?? DateTimeOffset.Now;

        var isUnread =
            !summary.Flags.HasValue ||
            !summary.Flags.Value.HasFlag(
                MessageFlags.Seen);

        var preview =
            CreatePreview(body);

        var senderInitial =
            senderName
                .Trim()
                .FirstOrDefault();

        return new MailMessageData(
            Sender: senderName,
            SenderAddress: senderAddress,
            RecipientAddress: recipientAddress,
            Subject: subject,
            Preview: preview,
            DisplayTime: FormatDisplayTime(date),
            DisplayDateTime: FormatDisplayDateTime(date),
            SenderInitial:
                senderInitial == default
                    ? "?"
                    : senderInitial
                        .ToString()
                        .ToUpperInvariant(),
            Greeting: string.Empty,
            Body:
                string.IsNullOrWhiteSpace(body)
                    ? "(Für diese Nachricht ist kein darstellbarer Textinhalt verfügbar.)"
                    : body,
            Closing: string.Empty,
            Signature: string.Empty,
            IsUnread: isUnread,
            EmphasizeSender: isUnread);
    }

    private static string GetDisplayName(
        IMailFolder folder,
        string inboxFullName)
    {
        if (string.Equals(
                folder.FullName,
                inboxFullName,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Posteingang";
        }

        var name =
            folder.Name.Trim();

        return name.ToLowerInvariant() switch
        {
            "sent" => "Gesendet",
            "sent items" => "Gesendet",
            "sent messages" => "Gesendet",
            "gesendet" => "Gesendet",

            "drafts" => "Entwürfe",
            "draft" => "Entwürfe",
            "entwürfe" => "Entwürfe",

            "trash" => "Papierkorb",
            "deleted items" => "Papierkorb",
            "papierkorb" => "Papierkorb",

            "junk" => "Junk",
            "spam" => "Spam",

            "archive" => "Archiv",
            "archives" => "Archiv",

            _ => name
        };
    }

    private static int GetFolderSortOrder(
        IMailFolder folder,
        string inboxFullName)
    {
        var displayName =
            GetDisplayName(
                folder,
                inboxFullName);

        return displayName switch
        {
            "Posteingang" => 0,
            "Gesendet" => 1,
            "Entwürfe" => 2,
            "Archiv" => 3,
            "Junk" => 4,
            "Spam" => 4,
            "Papierkorb" => 5,
            _ => 100
        };
    }

    private static string CreatePreview(
        string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Kein Nachrichtentext verfügbar.";
        }

        var preview =
            Regex.Replace(
                body,
                @"\s+",
                " ")
            .Trim();

        const int maximumLength = 140;

        if (preview.Length <= maximumLength)
        {
            return preview;
        }

        return preview[..maximumLength]
            .TrimEnd()
            + "…";
    }

    private static string ConvertHtmlToPlainText(
        string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text =
            Regex.Replace(
                html,
                @"<script\b[^>]*>.*?</script>",
                " ",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline);

        text =
            Regex.Replace(
                text,
                @"<style\b[^>]*>.*?</style>",
                " ",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline);

        text =
            Regex.Replace(
                text,
                @"<br\s*/?>",
                "\n",
                RegexOptions.IgnoreCase);

        text =
            Regex.Replace(
                text,
                @"</p\s*>",
                "\n\n",
                RegexOptions.IgnoreCase);

        text =
            Regex.Replace(
                text,
                @"<[^>]+>",
                " ");

        text =
            WebUtility.HtmlDecode(text);

        return NormalizeBodyText(text);
    }

    private static string NormalizeBodyText(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized =
            text.Replace(
                    "\r\n",
                    "\n")
                .Replace(
                    '\r',
                    '\n');

        normalized =
            Regex.Replace(
                normalized,
                @"[ \t]+",
                " ");

        normalized =
            Regex.Replace(
                normalized,
                @" *\n *",
                "\n");

        normalized =
            Regex.Replace(
                normalized,
                @"\n{3,}",
                "\n\n");

        return normalized.Trim();
    }

    private static string FormatDisplayTime(
        DateTimeOffset value)
    {
        var local =
            value.LocalDateTime;

        var today =
            DateTime.Today;

        if (local.Date == today)
        {
            return local.ToString(
                "HH:mm");
        }

        if (local.Date ==
            today.AddDays(-1))
        {
            return "Gestern";
        }

        if (local.Year ==
            today.Year)
        {
            return local.ToString(
                "dd.MM.");
        }

        return local.ToString(
            "dd.MM.yyyy");
    }

    private static string FormatDisplayDateTime(
        DateTimeOffset value)
    {
        var local =
            value.LocalDateTime;

        var today =
            DateTime.Today;

        if (local.Date == today)
        {
            return $"Heute, {local:HH:mm}";
        }

        if (local.Date ==
            today.AddDays(-1))
        {
            return $"Gestern, {local:HH:mm}";
        }

        return local.ToString(
            "dd.MM.yyyy, HH:mm");
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
            // Ein Disconnect-Fehler darf das geladene
            // Ergebnis nicht nachträglich zerstören.
        }
    }
}