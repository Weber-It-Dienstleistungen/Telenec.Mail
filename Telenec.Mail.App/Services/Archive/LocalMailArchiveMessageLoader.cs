using MimeKit;
using MimeKit.Cryptography;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App.Services.Archive;

public sealed class LocalMailArchiveMessageLoader
{
    private const int MaximumWebViewHtmlBytes =
        2 * 1024 * 1024;

    private static readonly Regex CidReferenceRegex =
        new(
            @"\bcid:(?<contentId>[^""'\s<>)]+)",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

    public async Task<MailMessageData> LoadAsync(
        LocalMailArchiveMessageInfo archiveMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveMessage);

        if (archiveMessage.LocalFileState !=
            LocalMailArchiveFileState.Available)
        {
            throw new InvalidOperationException(
                "Die lokale Archivdatei ist nicht in einem verwendbaren Zustand.");
        }

        if (string.IsNullOrWhiteSpace(
                archiveMessage.LocalFilePath))
        {
            throw new InvalidOperationException(
                "Für die Archivnachricht ist kein lokaler Dateipfad vorhanden.");
        }

        var filePath =
            archiveMessage.LocalFilePath;

        var fileInfo =
            new FileInfo(
                filePath);

        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(
                "Die lokale Archivdatei wurde nicht gefunden.",
                filePath);
        }

        if (fileInfo.Length !=
            archiveMessage.FileSizeBytes)
        {
            throw new InvalidOperationException(
                "Die lokale Archivdatei besitzt nicht mehr die erwartete Dateigröße.");
        }

        await using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize:
                    81920,
                options:
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);

        await VerifySha256Async(
            stream,
            archiveMessage.Sha256,
            cancellationToken);

        stream.Position =
            0;

        using var message =
            await MimeMessage.LoadAsync(
                stream,
                cancellationToken);

        return await CreateMessageDataAsync(
            message,
            archiveMessage,
            cancellationToken);
    }

    private static async Task VerifySha256Async(
        Stream stream,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                expectedSha256))
        {
            throw new InvalidOperationException(
                "Für die Archivdatei ist keine SHA-256-Prüfsumme gespeichert.");
        }

        using var sha256 =
            SHA256.Create();

        var hash =
            await sha256.ComputeHashAsync(
                stream,
                cancellationToken);

        var actualSha256 =
            Convert
                .ToHexString(
                    hash)
                .ToLowerInvariant();

        if (!string.Equals(
                actualSha256,
                expectedSha256.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die SHA-256-Prüfsumme der lokalen Archivdatei stimmt nicht mehr mit dem Archivdatensatz überein.");
        }
    }

    private static async Task<MailMessageData>
        CreateMessageDataAsync(
            MimeMessage message,
            LocalMailArchiveMessageInfo archiveMessage,
            CancellationToken cancellationToken)
    {
        var senderMailbox =
            message
                .From
                .Mailboxes
                .FirstOrDefault();

        var senderAddress =
            senderMailbox?
                .Address?
                .Trim()
            ?? string.Empty;

        var senderName =
            senderMailbox?
                .Name?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                senderName))
        {
            senderName =
                senderAddress;
        }

        if (string.IsNullOrWhiteSpace(
                senderName))
        {
            senderName =
                "Unbekannter Absender";
        }

        var toAddresses =
            GetMailboxAddresses(
                message.To);

        var ccAddresses =
            GetMailboxAddresses(
                message.Cc);

        var replyToAddresses =
            GetMailboxAddresses(
                message.ReplyTo);

        var recipientAddress =
            toAddresses
                .FirstOrDefault()
            ?? string.Empty;

        var subject =
            string.IsNullOrWhiteSpace(
                message.Subject)
                ? "(Kein Betreff)"
                : message.Subject.Trim();

        var date =
            message.Date !=
            DateTimeOffset.MinValue
                ? message.Date
                : archiveMessage.MessageDateUtc
                  ?? archiveMessage.ArchivedAtUtc;

        var htmlBody =
            message.HtmlBody;

        if (!string.IsNullOrWhiteSpace(
                htmlBody))
        {
            htmlBody =
                await ResolveCidImagesAsync(
                    message.Body,
                    htmlBody,
                    cancellationToken);
        }

        var plainText =
            NormalizeBodyText(
                message.TextBody
                ?? string.Empty);

        if (string.IsNullOrWhiteSpace(
                plainText) &&
            !string.IsNullOrWhiteSpace(
                htmlBody))
        {
            plainText =
                ConvertHtmlToPlainText(
                    htmlBody);
        }

        var senderInitial =
            senderName
                .Trim()
                .FirstOrDefault();

        var securityData =
            DetectSecurityData(
                message.Body);

        var smimeVerification =
            TryVerifySmime(
                message,
                securityData,
                cancellationToken);

        var readReceipt =
            TryDetectReadReceipt(
                message);

        var messageId =
            string.IsNullOrWhiteSpace(
                message.MessageId)
                ? archiveMessage.MessageId
                : message.MessageId.Trim();

        var references =
            message
                .References
                .Where(
                    reference =>
                        !string.IsNullOrWhiteSpace(
                            reference))
                .Select(
                    reference =>
                        reference.Trim())
                .ToArray();

        return new MailMessageData(
            Sender:
                senderName,

            SenderAddress:
                senderAddress,

            RecipientAddress:
                recipientAddress,

            Subject:
                subject,

            Preview:
                CreatePreview(
                    plainText),

            DisplayTime:
                FormatDisplayTime(
                    date),

            DisplayDateTime:
                FormatDisplayDateTime(
                    date),

            SenderInitial:
                senderInitial == default
                    ? "?"
                    : senderInitial
                        .ToString()
                        .ToUpperInvariant(),

            Greeting:
                string.Empty,

            Body:
                string.IsNullOrWhiteSpace(
                    plainText)
                    ? "(Für diese Nachricht ist kein darstellbarer Textinhalt verfügbar.)"
                    : plainText,

            Closing:
                string.Empty,

            Signature:
                string.Empty,

            /*
             * IMAP-Flags gehören nicht zur .eml-Datei.
             * Eine Archivmail wird deshalb in der lokalen
             * Anzeige nicht als ungelesen behandelt.
             */
            IsUnread:
                false,

            EmphasizeSender:
                false,

            HtmlBody:
                htmlBody,

            /*
             * Eine Archivmail besitzt bewusst keine aktive
             * Server-UID.
             *
             * Damit kann sie später nicht versehentlich wie
             * eine aktuell auf dem IMAP-Server liegende Mail
             * behandelt werden.
             */
            UniqueId:
                0,

            Attachments:
                Array.Empty<
                    MailAttachmentData>(),

            HasSmimeSignature:
                securityData
                    .HasSmimeSignature,

            MessageId:
                messageId,

            References:
                references,

            ToAddresses:
                toAddresses,

            CcAddresses:
                ccAddresses,

            ReplyToAddresses:
                replyToAddresses,

            Importance:
                MailImportanceHeaderService
                    .Read(
                        message),

            ReadReceipt:
                readReceipt,

            SecurityData:
                securityData,

            SmimeVerification:
                smimeVerification);
    }

    private static MailReadReceiptData?
        TryDetectReadReceipt(
            MimeMessage message)
    {
        try
        {
            return MailReadReceiptDetectionService
                .Detect(
                    message);
        }
        catch
        {
            return null;
        }
    }

    private static MailSmimeVerificationData
        TryVerifySmime(
            MimeMessage message,
            MailSecurityData securityData,
            CancellationToken cancellationToken)
    {
        if (!securityData
                .HasSmimeSignature)
        {
            return MailSmimeVerificationData
                .NotChecked;
        }

        try
        {
            return MailSmimeVerificationService
                .Verify(
                    message,
                    cancellationToken);
        }
        catch
        {
            return new MailSmimeVerificationData(
                Status:
                    MailSmimeVerificationStatus.Error,

                Signers:
                    Array.Empty<
                        MailSmimeSignerData>());
        }
    }

    private static MailSecurityData
        DetectSecurityData(
            MimeEntity? body)
    {
        var state =
            new LocalSmimeDetectionState();

        InspectMimeEntity(
            body,
            state);

        return new MailSecurityData(
            HasSmimeSignature:
                state.HasSignature,

            HasSmimeEncryption:
                state.HasEncryption,

            HasUnclassifiedSmimeContent:
                state.HasUnclassifiedContent,

            SmimeSignatureFormat:
                state.SignatureFormat,

            SmimeEncryptionFormat:
                state.HasEncryption
                    ? MailSmimeEncryptionFormat
                        .EnvelopedData
                    : MailSmimeEncryptionFormat
                        .None);
    }

    private static void InspectMimeEntity(
        MimeEntity? entity,
        LocalSmimeDetectionState state)
    {
        if (entity is null)
        {
            return;
        }

        /*
         * Eine angehängte Nachricht darf nicht den
         * Sicherheitsstatus der äußeren Archivmail
         * beeinflussen.
         */
        if (entity is MessagePart)
        {
            return;
        }

        if (entity is MultipartSigned)
        {
            state.HasSignature =
                true;

            if (state.SignatureFormat ==
                MailSmimeSignatureFormat.None)
            {
                state.SignatureFormat =
                    MailSmimeSignatureFormat
                        .Detached;
            }
        }

        if (entity is ApplicationPkcs7Mime
            pkcs7Mime)
        {
            switch (pkcs7Mime.SecureMimeType)
            {
                case SecureMimeType.SignedData:
                    state.HasSignature =
                        true;

                    state.SignatureFormat =
                        MailSmimeSignatureFormat
                            .Opaque;
                    break;

                case SecureMimeType.EnvelopedData:
                    state.HasEncryption =
                        true;
                    break;

                default:
                    state.HasUnclassifiedContent =
                        true;
                    break;
            }
        }

        if (entity is not Multipart multipart)
        {
            return;
        }

        foreach (var child in
                 multipart)
        {
            InspectMimeEntity(
                child,
                state);
        }
    }

    private static async Task<string>
        ResolveCidImagesAsync(
            MimeEntity? body,
            string htmlBody,
            CancellationToken cancellationToken)
    {
        if (body is null ||
            string.IsNullOrWhiteSpace(
                htmlBody))
        {
            return htmlBody;
        }

        var imageParts =
            EnumerateMimeParts(
                    body)
                .Where(
                    part =>
                        string.Equals(
                            part.ContentType.MediaType,
                            "image",
                            StringComparison.OrdinalIgnoreCase))
                .Where(
                    part =>
                        !string.IsNullOrWhiteSpace(
                            part.ContentId))
                .ToList();

        if (imageParts.Count == 0)
        {
            return htmlBody;
        }

        var resolvedHtml =
            htmlBody;

        foreach (var imagePart in
                 imageParts)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var normalizedContentId =
                NormalizeContentId(
                    imagePart.ContentId);

            if (string.IsNullOrWhiteSpace(
                    normalizedContentId) ||
                imagePart.Content is null)
            {
                continue;
            }

            try
            {
                using var buffer =
                    new MemoryStream();

                await imagePart
                    .Content
                    .DecodeToAsync(
                        buffer,
                        cancellationToken);

                if (buffer.Length == 0)
                {
                    continue;
                }

                var mimeType =
                    imagePart
                        .ContentType
                        .MimeType;

                if (string.IsNullOrWhiteSpace(
                        mimeType))
                {
                    continue;
                }

                var dataUri =
                    $"data:{mimeType};base64," +
                    Convert.ToBase64String(
                        buffer.ToArray());

                var candidateHtml =
                    CidReferenceRegex.Replace(
                        resolvedHtml,
                        match =>
                        {
                            var matchedContentId =
                                NormalizeContentId(
                                    match
                                        .Groups["contentId"]
                                        .Value);

                            return string.Equals(
                                    matchedContentId,
                                    normalizedContentId,
                                    StringComparison.OrdinalIgnoreCase)
                                ? dataUri
                                : match.Value;
                        });

                if (Encoding.UTF8.GetByteCount(
                        candidateHtml) >
                    MaximumWebViewHtmlBytes)
                {
                    continue;
                }

                resolvedHtml =
                    candidateHtml;
            }
            catch (OperationCanceledException)
                when (cancellationToken
                    .IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        return resolvedHtml;
    }

    private static IEnumerable<MimePart>
        EnumerateMimeParts(
            MimeEntity entity)
    {
        if (entity is MessagePart)
        {
            yield break;
        }

        if (entity is MimePart mimePart)
        {
            yield return mimePart;
            yield break;
        }

        if (entity is not Multipart multipart)
        {
            yield break;
        }

        foreach (var child in
                 multipart)
        {
            foreach (var part in
                     EnumerateMimeParts(
                         child))
            {
                yield return part;
            }
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

    private static string NormalizeContentId(
        string? contentId)
    {
        if (string.IsNullOrWhiteSpace(
                contentId))
        {
            return string.Empty;
        }

        var normalized =
            WebUtility
                .HtmlDecode(
                    contentId)
                .Trim();

        try
        {
            normalized =
                Uri.UnescapeDataString(
                    normalized);
        }
        catch (UriFormatException)
        {
        }

        return normalized
            .Trim()
            .Trim(
                '<',
                '>');
    }

    private static string CreatePreview(
        string body)
    {
        if (string.IsNullOrWhiteSpace(
                body))
        {
            return
                "Kein Nachrichtentext verfügbar.";
        }

        var preview =
            Regex.Replace(
                    body,
                    @"\s+",
                    " ")
                .Trim();

        const int maximumLength =
            140;

        if (preview.Length <=
            maximumLength)
        {
            return preview;
        }

        return preview[
                   ..maximumLength]
               .TrimEnd() +
               "…";
    }

    private static string ConvertHtmlToPlainText(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
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
            WebUtility.HtmlDecode(
                text);

        return NormalizeBodyText(
            text);
    }

    private static string NormalizeBodyText(
        string text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
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

        if (local.Date ==
            today)
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

        if (local.Date ==
            today)
        {
            return
                $"Heute, {local:HH:mm}";
        }

        if (local.Date ==
            today.AddDays(-1))
        {
            return
                $"Gestern, {local:HH:mm}";
        }

        return local.ToString(
            "dd.MM.yyyy, HH:mm");
    }

    private sealed class LocalSmimeDetectionState
    {
        public bool HasSignature
        { get; set; }

        public bool HasEncryption
        { get; set; }

        public bool HasUnclassifiedContent
        { get; set; }

        public MailSmimeSignatureFormat
            SignatureFormat
        { get; set; } =
            MailSmimeSignatureFormat.None;
    }
}