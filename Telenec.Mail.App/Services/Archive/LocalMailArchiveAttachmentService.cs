using MimeKit;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Archive;

public sealed class LocalMailArchiveAttachmentService
{
    private const string ArchivePartPrefix =
        "local-archive:";

    private static readonly Regex CidReferenceRegex =
        new(
            @"\bcid:(?<contentId>[^""'\s<>)]+)",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<MailAttachmentData>>
        LoadAttachmentsAsync(
            LocalMailArchiveMessageInfo archiveMessage,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveMessage);

        await using var stream =
            await OpenVerifiedArchiveFileAsync(
                archiveMessage,
                cancellationToken);

        using var message =
            await MimeMessage.LoadAsync(
                stream,
                cancellationToken);

        var candidates =
            CreateAttachmentCandidates(
                message);

        if (candidates.Count == 0)
        {
            return Array.Empty<
                MailAttachmentData>();
        }

        var attachments =
            new List<MailAttachmentData>(
                candidates.Count);

        foreach (var candidate in
                 candidates)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var size =
                await GetAttachmentSizeAsync(
                    candidate.Entity,
                    cancellationToken);

            attachments.Add(
                new MailAttachmentData(
                    PartSpecifier:
                        candidate.PartSpecifier,

                    FileName:
                        candidate.FileName,

                    ContentType:
                        candidate.ContentType,

                    EncodedSizeBytes:
                        size));
        }

        return attachments;
    }

    public async Task<bool> SaveAttachmentAsync(
        LocalMailArchiveMessageInfo archiveMessage,
        MailAttachmentData attachment,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveMessage);

        ArgumentNullException.ThrowIfNull(
            attachment);

        ArgumentNullException.ThrowIfNull(
            destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "Der Zielstream ist nicht beschreibbar.",
                nameof(destination));
        }

        if (string.IsNullOrWhiteSpace(
                attachment.PartSpecifier) ||
            !attachment.PartSpecifier.StartsWith(
                ArchivePartPrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        await using var stream =
            await OpenVerifiedArchiveFileAsync(
                archiveMessage,
                cancellationToken);

        using var message =
            await MimeMessage.LoadAsync(
                stream,
                cancellationToken);

        var candidates =
            CreateAttachmentCandidates(
                message);

        var candidate =
            candidates
                .FirstOrDefault(
                    current =>
                        string.Equals(
                            current.PartSpecifier,
                            attachment.PartSpecifier,
                            StringComparison.Ordinal));

        if (candidate is null)
        {
            return false;
        }

        /*
         * Die Archivdatei wurde unmittelbar vorher erneut
         * anhand ihrer gespeicherten Größe und SHA-256-
         * Prüfsumme verifiziert.
         *
         * Zusätzlich prüfen wir hier die sichtbaren
         * Attachment-Metadaten.
         *
         * Damit kann ein veraltetes UI-Objekt nicht
         * versehentlich auf einen anderen MIME-Part zeigen.
         */
        if (!string.Equals(
                candidate.FileName,
                attachment.FileName,
                StringComparison.Ordinal) ||
            !string.Equals(
                candidate.ContentType,
                attachment.ContentType,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await WriteAttachmentContentAsync(
            candidate.Entity,
            destination,
            cancellationToken);

        await destination.FlushAsync(
            cancellationToken);

        return true;
    }

    private static async Task<FileStream>
        OpenVerifiedArchiveFileAsync(
            LocalMailArchiveMessageInfo archiveMessage,
            CancellationToken cancellationToken)
    {
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

        if (string.IsNullOrWhiteSpace(
                archiveMessage.Sha256))
        {
            throw new InvalidOperationException(
                "Für die Archivdatei ist keine SHA-256-Prüfsumme gespeichert.");
        }

        var stream =
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

        try
        {
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
                    archiveMessage.Sha256.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Die SHA-256-Prüfsumme der lokalen Archivdatei stimmt nicht mehr mit dem Archivdatensatz überein.");
            }

            stream.Position =
                0;

            return stream;
        }
        catch
        {
            await stream.DisposeAsync();

            throw;
        }
    }

    private static IReadOnlyList<ArchiveAttachmentCandidate>
        CreateAttachmentCandidates(
            MimeMessage message)
    {
        var referencedContentIds =
            GetReferencedContentIds(
                message.HtmlBody);

        var entities =
            new List<MimeEntity>();

        CollectAttachmentEntities(
            message.Body,
            referencedContentIds,
            entities);

        var result =
            new List<ArchiveAttachmentCandidate>(
                entities.Count);

        for (var index = 0;
             index < entities.Count;
             index++)
        {
            var entity =
                entities[index];

            var attachmentNumber =
                index + 1;

            var partSpecifier =
                ArchivePartPrefix +
                attachmentNumber;

            var contentType =
                entity.ContentType?.MimeType;

            if (string.IsNullOrWhiteSpace(
                    contentType))
            {
                contentType =
                    "application/octet-stream";
            }

            result.Add(
                new ArchiveAttachmentCandidate(
                    PartSpecifier:
                        partSpecifier,

                    Entity:
                        entity,

                    FileName:
                        GetSafeAttachmentFileName(
                            entity,
                            attachmentNumber),

                    ContentType:
                        contentType));
        }

        return result;
    }

    private static void CollectAttachmentEntities(
        MimeEntity? entity,
        IReadOnlySet<string> referencedContentIds,
        ICollection<MimeEntity> result)
    {
        if (entity is null)
        {
            return;
        }

        /*
         * Eine angehängte Nachricht wird als genau ein
         * Attachment behandelt.
         *
         * Ihre eventuell eigenen Anhänge werden nicht als
         * Anhänge der äußeren Mail aufgeführt.
         */
        if (entity is MessagePart)
        {
            result.Add(
                entity);

            return;
        }

        if (entity is MimePart mimePart)
        {
            if (IsSmimeSignaturePart(
                    mimePart))
            {
                return;
            }

            if (IsReferencedInlineImage(
                    mimePart,
                    referencedContentIds))
            {
                return;
            }

            if (mimePart.IsAttachment ||
                string.Equals(
                    mimePart.ContentType.MediaType,
                    "image",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    mimePart);
            }

            return;
        }

        if (entity is not Multipart multipart)
        {
            return;
        }

        foreach (var child in
                 multipart)
        {
            CollectAttachmentEntities(
                child,
                referencedContentIds,
                result);
        }
    }

    private static IReadOnlySet<string>
        GetReferencedContentIds(
            string? htmlBody)
    {
        if (string.IsNullOrWhiteSpace(
                htmlBody))
        {
            return new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        }

        return CidReferenceRegex
            .Matches(
                htmlBody)
            .Cast<Match>()
            .Select(
                match =>
                    NormalizeContentId(
                        match
                            .Groups["contentId"]
                            .Value))
            .Where(
                contentId =>
                    !string.IsNullOrWhiteSpace(
                        contentId))
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsReferencedInlineImage(
        MimePart mimePart,
        IReadOnlySet<string> referencedContentIds)
    {
        if (!string.Equals(
                mimePart.ContentType.MediaType,
                "image",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                mimePart.ContentId))
        {
            return false;
        }

        var normalizedContentId =
            NormalizeContentId(
                mimePart.ContentId);

        if (string.IsNullOrWhiteSpace(
                normalizedContentId))
        {
            return false;
        }

        return referencedContentIds.Contains(
            normalizedContentId);
    }

    private static bool IsSmimeSignaturePart(
        MimePart mimePart)
    {
        var mimeType =
            mimePart.ContentType.MimeType;

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
        MimeEntity entity,
        int attachmentNumber)
    {
        string? fileName =
            entity switch
            {
                MimePart mimePart =>
                    mimePart.FileName,

                MessagePart messagePart =>
                    messagePart
                        .ContentDisposition?
                        .FileName,

                _ =>
                    null
            };

        fileName =
            fileName?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            if (entity is MessagePart ||
                string.Equals(
                    entity.ContentType?.MimeType,
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

    private static async Task<uint>
        GetAttachmentSizeAsync(
            MimeEntity entity,
            CancellationToken cancellationToken)
    {
        if (entity is MimePart mimePart &&
            mimePart.Content?.Stream is
            { CanSeek: true } contentStream)
        {
            try
            {
                return ClampToUInt32(
                    contentStream.Length);
            }
            catch
            {
            }
        }

        using var counter =
            new CountingWriteStream();

        await WriteAttachmentContentAsync(
            entity,
            counter,
            cancellationToken);

        return ClampToUInt32(
            counter.BytesWritten);
    }

    private static async Task WriteAttachmentContentAsync(
        MimeEntity entity,
        Stream destination,
        CancellationToken cancellationToken)
    {
        if (entity is MimePart mimePart)
        {
            var content =
                mimePart.Content
                ?? throw new InvalidDataException(
                    "Der Anhang enthält keinen Dateinhalt.");

            await content.DecodeToAsync(
                destination,
                cancellationToken);

            return;
        }

        if (entity is MessagePart messagePart)
        {
            var attachedMessage =
                messagePart.Message
                ?? throw new InvalidDataException(
                    "Die angehängte E-Mail enthält keine Nachrichtendaten.");

            await attachedMessage.WriteToAsync(
                destination,
                cancellationToken);

            return;
        }

        await entity.WriteToAsync(
            destination,
            contentOnly:
                true,
            cancellationToken);
    }

    private static uint ClampToUInt32(
        long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >=
            uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)value;
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

    private sealed record ArchiveAttachmentCandidate(
        string PartSpecifier,
        MimeEntity Entity,
        string FileName,
        string ContentType);

    private sealed class CountingWriteStream :
        Stream
    {
        public long BytesWritten
        { get; private set; }

        public override bool CanRead =>
            false;

        public override bool CanSeek =>
            false;

        public override bool CanWrite =>
            true;

        public override long Length =>
            BytesWritten;

        public override long Position
        {
            get =>
                BytesWritten;

            set =>
                throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(
            long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            ArgumentNullException.ThrowIfNull(
                buffer);

            BytesWritten +=
                count;
        }

        public override void Write(
            ReadOnlySpan<byte> buffer)
        {
            BytesWritten +=
                buffer.Length;
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Write(
                buffer,
                offset,
                count);

            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            BytesWritten +=
                buffer.Length;

            return ValueTask.CompletedTask;
        }
    }
}