using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using System.IO;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class MailKitDraftEditService :
    IMailDraftEditService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan AuthenticationTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DraftLoadTimeout =
        TimeSpan.FromSeconds(60);

    private readonly IMailAccountStore _mailAccountStore;
    private readonly ICredentialStore _credentialStore;

    public MailKitDraftEditService(
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

    public async Task<MailDraftEditData> LoadDraftAsync(
        string folderId,
        uint uniqueId,
        string? expectedMessageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                folderId))
        {
            throw new ArgumentException(
                "Der Entwürfe-Ordner darf nicht leer sein.",
                nameof(folderId));
        }

        if (uniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uniqueId),
                "Die Nachrichten-ID muss größer als 0 sein.");
        }

        if (string.IsNullOrWhiteSpace(
                expectedMessageId))
        {
            /*
             * Ohne Message-ID würden wir uns ausschließlich
             * auf die IMAP-UID verlassen.
             *
             * Eine UID ist jedoch nur innerhalb einer
             * bestimmten UIDVALIDITY eindeutig.
             *
             * Solange die Draft-Bearbeitung die UIDVALIDITY
             * noch nicht explizit mitführt, bearbeiten wir
             * deshalb nur Entwürfe mit bekannter Message-ID.
             */
            throw new MailDraftEditException(
                "Der Entwurf besitzt keine eindeutige Message-ID und kann deshalb nicht sicher bearbeitet werden.");
        }

        var normalizedExpectedMessageId =
            expectedMessageId.Trim();

        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            throw new MailDraftEditException(
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
            throw new MailDraftEditException(
                "Für das Mailkonto sind keine Zugangsdaten gespeichert.");
        }

        using var client =
            await CreateAuthenticatedClientAsync(
                account.EmailAddress,
                credential.Password,
                cancellationToken);

        try
        {
            using var loadTimeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            loadTimeoutSource.CancelAfter(
                DraftLoadTimeout);

            var operationCancellationToken =
                loadTimeoutSource.Token;

            var folder =
                await client.GetFolderAsync(
                    folderId.Trim(),
                    operationCancellationToken);

            if (folder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new MailDraftEditException(
                    "Der ausgewählte Entwürfe-Ordner kann nicht geöffnet werden.");
            }

            if (!IsDraftFolder(
                    client,
                    folder))
            {
                throw new MailDraftEditException(
                    "Die ausgewählte Nachricht befindet sich nicht in einem Entwürfe-Ordner.");
            }

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                operationCancellationToken);

            var uniqueIdValue =
                new UniqueId(
                    uniqueId);

            var summaries =
                await folder.FetchAsync(
                    new[]
                    {
                        uniqueIdValue
                    },
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Flags |
                    MessageSummaryItems.Envelope |
                    MessageSummaryItems.BodyStructure |
                    MessageSummaryItems.References,
                    operationCancellationToken);

            var summary =
                summaries.FirstOrDefault();

            if (summary is null ||
                !summary.UniqueId.IsValid)
            {
                throw new MailDraftEditException(
                    "Der Entwurf ist auf dem Mailserver nicht mehr vorhanden.");
            }

            using var message =
                await folder.GetMessageAsync(
                    uniqueIdValue,
                    operationCancellationToken);

            var currentMessageId =
                NormalizeMessageId(
                    message.MessageId);

            if (string.IsNullOrWhiteSpace(
                    currentMessageId))
            {
                throw new MailDraftEditException(
                    "Der Entwurf besitzt auf dem Mailserver keine eindeutige Message-ID.");
            }

            /*
             * Wichtig:
             *
             * Die IMAP-UID allein darf nicht darüber
             * entscheiden, welche Nachricht bearbeitet wird.
             *
             * Der Benutzer hat ursprünglich eine bestimmte
             * Message-ID ausgewählt. Nach dem erneuten Laden
             * muss exakt dieselbe Nachricht unter der UID
             * liegen.
             */
            if (!string.Equals(
                    currentMessageId,
                    normalizedExpectedMessageId,
                    StringComparison.Ordinal))
            {
                throw new MailDraftEditException(
                    "Der ausgewählte Entwurf hat sich auf dem Mailserver verändert.\n\n" +
                    "Er wird deshalb nicht automatisch zur Bearbeitung geöffnet.");
            }

            ValidateSender(
                message,
                account.EmailAddress);

            ValidateSupportedDraftFormat(
                message);

            var attachments =
                CreateAttachmentData(
                    summary,
                    folder.FullName,
                    uniqueId,
                    currentMessageId);

            var toAddresses =
                GetMailboxAddresses(
                    message.To);

            var ccAddresses =
                GetMailboxAddresses(
                    message.Cc);

            var references =
                message.References
                    .Where(
                        reference =>
                            !string.IsNullOrWhiteSpace(
                                reference))
                    .Select(
                        reference =>
                            reference.Trim())
                    .Distinct(
                        StringComparer.Ordinal)
                    .ToArray();

            var parentMessageId =
                NormalizeMessageId(
                    message.InReplyTo);

            return new MailDraftEditData(
                SourceFolderId:
                    folder.FullName,

                SourceUniqueId:
                    uniqueId,

                SourceMessageId:
                    currentMessageId,

                ToAddresses:
                    toAddresses,

                CcAddresses:
                    ccAddresses,

                Subject:
                    message.Subject
                    ?? string.Empty,

                Body:
                    message.TextBody
                    ?? string.Empty,

                ParentMessageId:
                    parentMessageId,

                ParentReferences:
                    references,

                Attachments:
                    attachments);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new MailDraftEditException(
                "Das Laden des Entwurfs hat zu lange gedauert.",
                ex);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static void ValidateSender(
        MimeMessage message,
        string activeAccountAddress)
    {
        var senderAddress =
            message
                .From
                .Mailboxes
                .FirstOrDefault()?
                .Address?
                .Trim();

        /*
         * Ein leerer From-Header ist bei unfertigen
         * Fremdentwürfen möglich.
         *
         * In diesem Fall wird später ohnehin das aktive
         * Telenec-Konto als Absender verwendet.
         */
        if (string.IsNullOrWhiteSpace(
                senderAddress))
        {
            return;
        }

        if (string.Equals(
                senderAddress,
                activeAccountAddress.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new MailDraftEditException(
            "Der Entwurf verwendet einen anderen Absender als das aktuell angemeldete Konto.\n\n" +
            "Er wird deshalb nicht automatisch bearbeitet.");
    }

    private static void ValidateSupportedDraftFormat(
        MimeMessage message)
    {
        /*
         * Telenec Mail verfasst Nachrichten momentan als
         * Plaintext.
         *
         * Einen HTML-Entwurf aus Roundcube, Smartphone oder
         * einem anderen Client stillschweigend als Plaintext
         * neu zu speichern, würde Formatierungen und unter
         * Umständen Inline-Inhalte zerstören.
         *
         * Deshalb wird dieser Fall vorerst ausdrücklich
         * abgelehnt.
         */
        if (!string.IsNullOrWhiteSpace(
                message.HtmlBody))
        {
            throw new MailDraftEditException(
                "Dieser Entwurf enthält HTML-Formatierungen.\n\n" +
                "Telenec Mail bearbeitet HTML-Entwürfe derzeit noch nicht, damit keine Formatierungen verloren gehen.");
        }

        /*
         * Bcc wird im aktuellen Compose-Fenster noch nicht
         * unterstützt.
         *
         * Würden wir einen fremden Draft mit Bcc öffnen und
         * erneut speichern, würden diese Empfänger verloren
         * gehen.
         */
        if (message.Bcc.Mailboxes.Any())
        {
            throw new MailDraftEditException(
                "Dieser Entwurf enthält Bcc-Empfänger.\n\n" +
                "Telenec Mail bearbeitet solche Entwürfe derzeit noch nicht, damit keine Empfänger verloren gehen.");
        }
    }

    private static IReadOnlyList<string>
        GetMailboxAddresses(
            InternetAddressList addressList)
    {
        return addressList
            .Mailboxes
            .Select(
                mailbox =>
                    mailbox.Address?
                        .Trim())
            .Where(
                address =>
                    !string.IsNullOrWhiteSpace(
                        address))
            .Select(
                address =>
                    address!)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MailSendAttachmentData>
        CreateAttachmentData(
            IMessageSummary summary,
            string sourceFolderId,
            uint sourceUniqueId,
            string sourceMessageId)
    {
        var attachments =
            new List<MailSendAttachmentData>();

        var attachmentNumber =
            0;

        foreach (var attachment in
                 summary.Attachments)
        {
            if (IsSmimeSignaturePart(
                    attachment))
            {
                continue;
            }

            var partSpecifier =
                attachment.PartSpecifier;

            if (string.IsNullOrWhiteSpace(
                    partSpecifier))
            {
                /*
                 * Ein Anhang ohne eindeutigen IMAP-MIME-Part
                 * darf nicht in einen bearbeitbaren Entwurf
                 * übernommen werden.
                 */
                throw new MailDraftEditException(
                    "Mindestens ein Anhang des Entwurfs kann auf dem Mailserver nicht eindeutig identifiziert werden.");
            }

            attachmentNumber++;

            var fileName =
                GetSafeAttachmentFileName(
                    attachment,
                    attachmentNumber);

            attachments.Add(
                new MailSendAttachmentData(
                    FilePath:
                        string.Empty,

                    FileName:
                        fileName,

                    SizeBytes:
                        attachment.Octets,

                    SourceFolderId:
                        sourceFolderId,

                    SourceUniqueId:
                        sourceUniqueId,

                    SourcePartSpecifier:
                        partSpecifier,

                    SourceMessageId:
                        sourceMessageId));
        }

        return attachments;
    }

    private static bool IsSmimeSignaturePart(
        BodyPartBasic attachment)
    {
        var mimeType =
            attachment.ContentType.MimeType;

        if (string.IsNullOrWhiteSpace(
                mimeType))
        {
            return false;
        }

        return
            string.Equals(
                mimeType,
                "application/pkcs7-signature",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                mimeType,
                "application/x-pkcs7-signature",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeAttachmentFileName(
        BodyPartBasic attachment,
        int attachmentNumber)
    {
        var fileName =
            attachment.FileName?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            if (string.Equals(
                    attachment.ContentType.MimeType,
                    "message/rfc822",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                    $"Angehängte Nachricht {attachmentNumber}.eml";
            }

            return
                $"Anhang {attachmentNumber}";
        }

        try
        {
            var safeFileName =
                Path.GetFileName(
                    fileName);

            return string.IsNullOrWhiteSpace(
                    safeFileName)
                ? $"Anhang {attachmentNumber}"
                : safeFileName;
        }
        catch
        {
            return
                $"Anhang {attachmentNumber}";
        }
    }

    private static string? NormalizeMessageId(
        string? messageId)
    {
        if (string.IsNullOrWhiteSpace(
                messageId))
        {
            return null;
        }

        return messageId.Trim();
    }

    private static bool IsDraftFolder(
        ImapClient client,
        IMailFolder folder)
    {
        var specialUseDrafts =
            client.GetFolder(
                SpecialFolder.Drafts);

        if (specialUseDrafts is not null &&
            string.Equals(
                specialUseDrafts.FullName,
                folder.FullName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsDraftFolderName(
            folder.Name);
    }

    private static bool IsDraftFolderName(
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
            "drafts" => true,
            "draft" => true,
            "draft messages" => true,
            "entwürfe" => true,
            "entwurf" => true,
            _ => false
        };
    }

    private static async Task<ImapClient>
        CreateAuthenticatedClientAsync(
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