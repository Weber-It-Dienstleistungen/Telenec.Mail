using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class MailKitSendService :
    IMailSendService
{
    private const string SmtpHost =
        "mail.necnet.de";

    private const int SmtpPort =
        587;

    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan AuthenticationTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SendTimeout =
        TimeSpan.FromSeconds(60);

    private static readonly TimeSpan SentCopyTimeout =
        TimeSpan.FromSeconds(30);

    private readonly IMailAccountStore _mailAccountStore;
    private readonly ICredentialStore _credentialStore;

    public MailKitSendService(
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

    public async Task<MailSendResult> SendAsync(
        MailSendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        var recipients =
            ParseRecipientList(
                request.RecipientAddress,
                nameof(request.RecipientAddress),
                "Empfänger",
                required: true);

        var ccRecipients =
            ParseRecipientList(
                request.CcAddress,
                nameof(request.CcAddress),
                "Cc-Adresse",
                required: false);

        ccRecipients =
            RemoveDuplicateCcRecipients(
                recipients,
                ccRecipients);

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
            string.IsNullOrWhiteSpace(
                credential.Password))
        {
            throw new InvalidOperationException(
                "Für das Mailkonto sind keine Zugangsdaten gespeichert.");
        }

        var sender =
            CreateSenderAddress(
                account.EmailAddress,
                account.DisplayName);

        var message =
            CreateMessage(
                sender,
                recipients,
                ccRecipients,
                request.Subject,
                request.Body);

        ApplyReplyThreading(
            message,
            request.ParentMessageId,
            request.ParentReferences);

        await SendViaSmtpAsync(
            account.EmailAddress,
            credential.Password,
            message,
            cancellationToken);

        var sentCopySaved =
            await TrySaveSentCopyAsync(
                account.EmailAddress,
                credential.Password,
                message);

        return new MailSendResult(
            WasSent:
                true,

            SentCopySaved:
                sentCopySaved);
    }

    private static IReadOnlyList<MailboxAddress>
        ParseRecipientList(
            string? value,
            string parameterName,
            string displayName,
            bool required)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            if (required)
            {
                throw new ArgumentException(
                    "Es wurde kein Empfänger angegeben.",
                    parameterName);
            }

            return Array.Empty<MailboxAddress>();
        }

        var result =
            new List<MailboxAddress>();

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var entries =
            value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            if (!MailboxAddress.TryParse(
                    entry,
                    out var recipient) ||
                recipient is null)
            {
                throw new ArgumentException(
                    $"Die {displayName} „{entry}“ ist ungültig.",
                    parameterName);
            }

            if (!seen.Add(
                    recipient.Address))
            {
                continue;
            }

            result.Add(
                recipient);
        }

        if (required &&
            result.Count == 0)
        {
            throw new ArgumentException(
                "Es wurde kein Empfänger angegeben.",
                parameterName);
        }

        return result;
    }

    private static IReadOnlyList<MailboxAddress>
        RemoveDuplicateCcRecipients(
            IReadOnlyList<MailboxAddress> recipients,
            IReadOnlyList<MailboxAddress> ccRecipients)
    {
        if (ccRecipients.Count == 0)
        {
            return ccRecipients;
        }

        var existingAddresses =
            recipients
                .Select(
                    recipient =>
                        recipient.Address)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        return ccRecipients
            .Where(
                recipient =>
                    existingAddresses.Add(
                        recipient.Address))
            .ToList();
    }

    private static MailboxAddress CreateSenderAddress(
        string emailAddress,
        string? displayName)
    {
        if (string.IsNullOrWhiteSpace(
                emailAddress))
        {
            throw new InvalidOperationException(
                "Die Absenderadresse des aktiven Kontos ist ungültig.");
        }

        if (!MailboxAddress.TryParse(
                emailAddress.Trim(),
                out var parsedAddress) ||
            parsedAddress is null)
        {
            throw new InvalidOperationException(
                "Die Absenderadresse des aktiven Kontos ist ungültig.");
        }

        var senderName =
            !string.IsNullOrWhiteSpace(
                displayName)
                ? displayName.Trim()
                : parsedAddress.Address;

        return new MailboxAddress(
            senderName,
            parsedAddress.Address);
    }

    private static MimeMessage CreateMessage(
        MailboxAddress sender,
        IReadOnlyList<MailboxAddress> recipients,
        IReadOnlyList<MailboxAddress> ccRecipients,
        string? subject,
        string? body)
    {
        var message =
            new MimeMessage();

        message.From.Add(
            sender);

        foreach (var recipient in recipients)
        {
            message.To.Add(
                recipient);
        }

        foreach (var ccRecipient in ccRecipients)
        {
            message.Cc.Add(
                ccRecipient);
        }

        message.Subject =
            subject?.Trim()
            ?? string.Empty;

        message.Date =
            DateTimeOffset.Now;

        message.MessageId =
            MimeUtils.GenerateMessageId();

        message.Body =
            new TextPart(
                "plain")
            {
                Text =
                    body
                    ?? string.Empty
            };

        return message;
    }

    private static void ApplyReplyThreading(
        MimeMessage message,
        string? parentMessageId,
        IReadOnlyList<string>? parentReferences)
    {
        if (string.IsNullOrWhiteSpace(
                parentMessageId))
        {
            return;
        }

        var normalizedParentMessageId =
            parentMessageId.Trim();

        try
        {
            message.InReplyTo =
                normalizedParentMessageId;
        }
        catch (ArgumentException)
        {
            return;
        }

        if (parentReferences is not null)
        {
            foreach (var reference in parentReferences)
            {
                if (string.IsNullOrWhiteSpace(
                        reference))
                {
                    continue;
                }

                var normalizedReference =
                    reference.Trim();

                if (message.References.Any(
                        existingReference =>
                            string.Equals(
                                existingReference,
                                normalizedReference,
                                StringComparison.Ordinal)))
                {
                    continue;
                }

                try
                {
                    message.References.Add(
                        normalizedReference);
                }
                catch (ArgumentException)
                {
                }
            }
        }

        if (!message.References.Any(
                existingReference =>
                    string.Equals(
                        existingReference,
                        normalizedParentMessageId,
                        StringComparison.Ordinal)))
        {
            try
            {
                message.References.Add(
                    normalizedParentMessageId);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private static async Task SendViaSmtpAsync(
        string userName,
        string password,
        MimeMessage message,
        CancellationToken cancellationToken)
    {
        using var client =
            new SmtpClient();

        try
        {
            using (var connectionTimeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                connectionTimeoutSource.CancelAfter(
                    ConnectionTimeout);

                await client.ConnectAsync(
                    SmtpHost,
                    SmtpPort,
                    SecureSocketOptions.StartTls,
                    connectionTimeoutSource.Token);
            }

            using (var authenticationTimeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                authenticationTimeoutSource.CancelAfter(
                    AuthenticationTimeout);

                await client.AuthenticateAsync(
                    userName,
                    password,
                    authenticationTimeoutSource.Token);
            }

            using (var sendTimeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                sendTimeoutSource.CancelAfter(
                    SendTimeout);

                await client.SendAsync(
                    message,
                    sendTimeoutSource.Token);
            }
        }
        finally
        {
            await DisconnectSmtpSafelyAsync(
                client);
        }
    }

    private static async Task<bool> TrySaveSentCopyAsync(
        string userName,
        string password,
        MimeMessage message)
    {
        using var client =
            new ImapClient();

        try
        {
            using var timeoutSource =
                new CancellationTokenSource(
                    SentCopyTimeout);

            var cancellationToken =
                timeoutSource.Token;

            await client.ConnectAsync(
                ImapHost,
                ImapPort,
                SecureSocketOptions.SslOnConnect,
                cancellationToken);

            await client.AuthenticateAsync(
                userName,
                password,
                cancellationToken);

            var sentFolder =
                await GetSentFolderAsync(
                    client,
                    cancellationToken);

            if (sentFolder is null)
            {
                return false;
            }

            await sentFolder.AppendAsync(
                message,
                MessageFlags.Seen,
                cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            await DisconnectImapSafelyAsync(
                client);
        }
    }

    private static async Task<IMailFolder?>
        GetSentFolderAsync(
            ImapClient client,
            CancellationToken cancellationToken)
    {
        var specialUseSent =
            client.GetFolder(
                SpecialFolder.Sent);

        if (specialUseSent is not null &&
            !specialUseSent.Attributes.HasFlag(
                FolderAttributes.NoSelect))
        {
            return specialUseSent;
        }

        if (client.PersonalNamespaces.Count == 0)
        {
            return null;
        }

        var folders =
            await client.GetFoldersAsync(
                client.PersonalNamespaces[0],
                StatusItems.None,
                false,
                cancellationToken);

        return folders
            .FirstOrDefault(
                folder =>
                    !folder.Attributes.HasFlag(
                        FolderAttributes.NoSelect) &&
                    IsSentFolderName(
                        folder.Name));
    }

    private static bool IsSentFolderName(
        string folderName)
    {
        if (string.IsNullOrWhiteSpace(
                folderName))
        {
            return false;
        }

        return folderName
            .Trim()
            .ToLowerInvariant() switch
        {
            "sent" => true,
            "sent items" => true,
            "sent messages" => true,
            "gesendet" => true,
            _ => false
        };
    }

    private static async Task DisconnectSmtpSafelyAsync(
        SmtpClient client)
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

    private static async Task DisconnectImapSafelyAsync(
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