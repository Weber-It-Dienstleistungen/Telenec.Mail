using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;
using System.IO;
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

        /*
         * Die komplette MIME-Nachricht wird weiterhin vor
         * dem SMTP-Verbindungsaufbau erstellt.
         *
         * Das gilt jetzt auch für Originalanhänge einer
         * Weiterleitung:
         *
         * Erst wenn alle benötigten Server-Anhänge sicher
         * geladen und geprüft wurden, beginnt SMTP.
         */
        using var message =
            await CreateMessageAsync(
                sender,
                recipients,
                ccRecipients,
                request.Subject,
                request.Body,
                request.Attachments,
                account.EmailAddress,
                credential.Password,
                cancellationToken);

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

            return Array.Empty<
                MailboxAddress>();
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

    private static async Task<MimeMessage>
        CreateMessageAsync(
            MailboxAddress sender,
            IReadOnlyList<MailboxAddress> recipients,
            IReadOnlyList<MailboxAddress> ccRecipients,
            string? subject,
            string? body,
            IReadOnlyList<MailSendAttachmentData>? attachments,
            string userName,
            string password,
            CancellationToken cancellationToken)
    {
        var message =
            new MimeMessage();

        ImapClient? sourceImapClient =
            null;

        var verifiedSourceMessages =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        try
        {
            message.From.Add(
                sender);

            foreach (var recipient in recipients)
            {
                message.To.Add(
                    recipient);
            }

            foreach (var ccRecipient in
                     ccRecipients)
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

            var textBody =
                new TextPart(
                    "plain")
                {
                    Text =
                        body
                        ?? string.Empty
                };

            if (attachments is null ||
                attachments.Count == 0)
            {
                message.Body =
                    textBody;

                return message;
            }

            /*
             * Wichtig:
             *
             * Multipart sofort an MimeMessage hängen.
             * Falls beim dritten oder vierten Anhang etwas
             * scheitert, räumt message.Dispose() dadurch
             * auch bereits geöffnete lokale Dateistreams auf.
             */
            var mixedBody =
                new Multipart(
                    "mixed");

            mixedBody.Add(
                textBody);

            message.Body =
                mixedBody;

            foreach (var attachment in
                     attachments)
            {
                MimeEntity attachmentPart;

                if (attachment.IsServerAttachment)
                {
                    sourceImapClient ??=
                        await CreateAuthenticatedSourceImapClientAsync(
                            userName,
                            password,
                            cancellationToken);

                    attachmentPart =
                        await CreateServerAttachmentPartAsync(
                            sourceImapClient,
                            attachment,
                            verifiedSourceMessages,
                            cancellationToken);
                }
                else if (attachment.IsLocalFile)
                {
                    attachmentPart =
                        CreateLocalAttachmentPart(
                            attachment);
                }
                else
                {
                    throw new MailSendAttachmentException(
                        $"Der Anhang „{attachment.FileName}“ besitzt keine gültige Quelle.");
                }

                try
                {
                    mixedBody.Add(
                        attachmentPart);
                }
                catch
                {
                    attachmentPart.Dispose();

                    throw;
                }
            }

            return message;
        }
        catch
        {
            message.Dispose();

            throw;
        }
        finally
        {
            if (sourceImapClient is not null)
            {
                await DisconnectImapSafelyAsync(
                    sourceImapClient);

                sourceImapClient.Dispose();
            }
        }
    }

    private static MimePart CreateLocalAttachmentPart(
        MailSendAttachmentData attachment)
    {
        ArgumentNullException.ThrowIfNull(
            attachment);

        string fullPath;
        string safeFileName;

        try
        {
            if (string.IsNullOrWhiteSpace(
                    attachment.FilePath))
            {
                throw new ArgumentException(
                    "Der Dateipfad ist leer.");
            }

            fullPath =
                Path.GetFullPath(
                    attachment.FilePath);

            safeFileName =
                Path.GetFileName(
                    attachment.FileName);

            if (string.IsNullOrWhiteSpace(
                    safeFileName))
            {
                safeFileName =
                    Path.GetFileName(
                        fullPath);
            }

            if (string.IsNullOrWhiteSpace(
                    safeFileName))
            {
                throw new ArgumentException(
                    "Der Dateiname ist ungültig.");
            }
        }
        catch (Exception ex)
            when (ex is
                  ArgumentException or
                  NotSupportedException)
        {
            throw new MailSendAttachmentException(
                "Ein ausgewählter Anhang besitzt einen ungültigen Dateipfad.",
                ex);
        }

        FileStream? contentStream =
            null;

        try
        {
            contentStream =
                new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    options:
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan);

            var mimeType =
                MimeTypes.GetMimeType(
                    safeFileName);

            var contentType =
                ContentType.Parse(
                    mimeType);

            var mimePart =
                new MimePart(
                    contentType)
                {
                    ContentDisposition =
                        new ContentDisposition(
                            ContentDisposition.Attachment),

                    ContentTransferEncoding =
                        ContentEncoding.Base64,

                    FileName =
                        safeFileName
                };

            mimePart.Content =
                new MimeContent(
                    contentStream,
                    ContentEncoding.Default);

            /*
             * Eigentum am Stream liegt ab hier bei
             * MimeContent/MimeMessage.
             */
            contentStream =
                null;

            return mimePart;
        }
        catch (Exception ex)
            when (ex is
                  FileNotFoundException or
                  DirectoryNotFoundException or
                  UnauthorizedAccessException or
                  IOException)
        {
            contentStream?.Dispose();

            throw new MailSendAttachmentException(
                $"Der Anhang „{safeFileName}“ kann nicht gelesen werden.",
                ex);
        }
        catch
        {
            contentStream?.Dispose();

            throw;
        }
    }

    private static async Task<MimeEntity>
        CreateServerAttachmentPartAsync(
            ImapClient client,
            MailSendAttachmentData attachment,
            ISet<string> verifiedSourceMessages,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            client);

        ArgumentNullException.ThrowIfNull(
            attachment);

        ArgumentNullException.ThrowIfNull(
            verifiedSourceMessages);

        var safeFileName =
            GetSafeServerAttachmentFileName(
                attachment.FileName);

        MimeEntity? entity =
            null;

        try
        {
            var sourceFolderId =
                attachment.SourceFolderId
                ?? throw new MailSendAttachmentException(
                    $"Der Originalanhang „{safeFileName}“ besitzt keinen Quellordner.");

            var partSpecifier =
                attachment.SourcePartSpecifier
                ?? throw new MailSendAttachmentException(
                    $"Der Originalanhang „{safeFileName}“ besitzt keinen MIME-Part.");

            if (attachment.SourceUniqueId == 0)
            {
                throw new MailSendAttachmentException(
                    $"Der Originalanhang „{safeFileName}“ besitzt keine gültige Server-ID.");
            }

            var folder =
                await client.GetFolderAsync(
                    sourceFolderId,
                    cancellationToken);

            if (folder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new MailSendAttachmentException(
                    $"Der Quellordner für „{safeFileName}“ kann nicht geöffnet werden.");
            }

            if (!folder.IsOpen)
            {
                await folder.OpenAsync(
                    FolderAccess.ReadOnly,
                    cancellationToken);
            }

            await EnsureServerAttachmentSourceIsCurrentAsync(
                folder,
                attachment,
                verifiedSourceMessages,
                cancellationToken);

            if (folder is not IImapFolder imapFolder)
            {
                throw new MailSendAttachmentException(
                    $"Der Mailserver unterstützt den gezielten Abruf von „{safeFileName}“ nicht.");
            }

            entity =
                await imapFolder
                    .GetBodyPartAsync(
                        new UniqueId(
                            attachment.SourceUniqueId),
                        partSpecifier,
                        cancellationToken);

            /*
             * Der ursprüngliche MIME-Inhalt bleibt erhalten.
             *
             * Wir normalisieren lediglich die Darstellung als
             * echter Anhang und setzen einen bereinigten
             * Dateinamen.
             */
            entity.ContentDisposition =
                new ContentDisposition(
                    ContentDisposition.Attachment)
                {
                    FileName =
                        safeFileName
                };

            entity.ContentType.Name =
                safeFileName;

            var result =
                entity;

            entity =
                null;

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            entity?.Dispose();

            throw;
        }
        catch (MailSendAttachmentException)
        {
            entity?.Dispose();

            throw;
        }
        catch (Exception ex)
        {
            entity?.Dispose();

            throw new MailSendAttachmentException(
                $"Der Originalanhang „{safeFileName}“ konnte nicht vom Mailserver geladen werden.",
                ex);
        }
    }

    private static async Task
        EnsureServerAttachmentSourceIsCurrentAsync(
            IMailFolder folder,
            MailSendAttachmentData attachment,
            ISet<string> verifiedSourceMessages,
            CancellationToken cancellationToken)
    {
        var sourceMessageId =
            string.IsNullOrWhiteSpace(
                attachment.SourceMessageId)
                ? string.Empty
                : attachment.SourceMessageId.Trim();

        var verificationKey =
            $"{folder.FullName}\u001F" +
            $"{attachment.SourceUniqueId}\u001F" +
            sourceMessageId;

        if (verifiedSourceMessages.Contains(
                verificationKey))
        {
            return;
        }

        var uniqueId =
            new UniqueId(
                attachment.SourceUniqueId);

        var summaries =
            await folder.FetchAsync(
                new[]
                {
                    uniqueId
                },
                MessageSummaryItems.UniqueId |
                MessageSummaryItems.Envelope,
                cancellationToken);

        var summary =
            summaries.FirstOrDefault();

        if (summary is null ||
            !summary.UniqueId.IsValid)
        {
            throw new MailSendAttachmentException(
                $"Die Ursprungsnachricht für „{attachment.FileName}“ ist auf dem Mailserver nicht mehr vorhanden.");
        }

        /*
         * Die IMAP-UID allein reicht langfristig nicht als
         * globale Identität.
         *
         * Wenn uns die ursprüngliche Message-ID bekannt ist,
         * vergleichen wir sie deshalb zusätzlich mit der
         * Nachricht, die aktuell unter dieser UID liegt.
         */
        if (!string.IsNullOrWhiteSpace(
                sourceMessageId))
        {
            var currentMessageId =
                summary.Envelope?
                    .MessageId?
                    .Trim();

            if (!string.Equals(
                    currentMessageId,
                    sourceMessageId,
                    StringComparison.Ordinal))
            {
                throw new MailSendAttachmentException(
                    $"Die Ursprungsnachricht für „{attachment.FileName}“ hat sich auf dem Mailserver verändert.\n\n" +
                    "Der Originalanhang wird deshalb nicht automatisch weitergeleitet.");
            }
        }

        verifiedSourceMessages.Add(
            verificationKey);
    }

    private static string
        GetSafeServerAttachmentFileName(
            string? fileName)
    {
        try
        {
            var safeFileName =
                Path.GetFileName(
                    fileName?.Trim());

            if (!string.IsNullOrWhiteSpace(
                    safeFileName))
            {
                return safeFileName;
            }
        }
        catch
        {
        }

        return "Anhang";
    }

    private static async Task<ImapClient>
        CreateAuthenticatedSourceImapClientAsync(
            string userName,
            string password,
            CancellationToken cancellationToken)
    {
        var client =
            new ImapClient();

        try
        {
            using (var connectionTimeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                connectionTimeoutSource.CancelAfter(
                    ConnectionTimeout);

                await client.ConnectAsync(
                    ImapHost,
                    ImapPort,
                    SecureSocketOptions.SslOnConnect,
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

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
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
            foreach (var reference in
                     parentReferences)
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