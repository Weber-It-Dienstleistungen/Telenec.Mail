using MimeKit;
using MimeKit.Tnef;
using MimeKit.Utils;
using System.IO;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

internal static class MailReadReceiptDetectionService
{
    private const string DisplayedDisposition =
        "displayed";

    private const string LegacyOutlookReadReceiptMessageClass =
        "IPM.Microsoft Mail.Read Receipt";

    private const string LegacyOutlookV3Prefix =
        "Microsoft Mail v3.0 ";

    public static MailReadReceiptData? Detect(
        MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        /*
         * Zuerst prüfen wir den standardisierten MIME-Weg.
         *
         * Clients wie Thunderbird oder andere RFC-konforme
         * Programme können eine Lesebestätigung als
         *
         * message/disposition-notification
         *
         * innerhalb eines multipart/report liefern.
         */
        var standardReceipt =
            TryDetectStandardMdn(
                message);

        if (standardReceipt is not null)
        {
            return standardReceipt;
        }

        /*
         * Outlook / Exchange kann Lesebestätigungen
         * stattdessen als TNEF/MAPI-Bericht senden.
         *
         * Genau diesen Fall haben wir in der realen
         * Testnachricht aus unserer Umgebung gesehen.
         */
        return TryDetectOutlookTnefReceipt(
            message);
    }

    private static MailReadReceiptData?
        TryDetectStandardMdn(
            MimeMessage message)
    {
        foreach (var node in
                 EnumerateMimeTree(
                     message))
        {
            if (node.Entity
                is not MessageDispositionNotification
                    notification)
            {
                continue;
            }

            string? disposition;
            string? originalMessageId;

            try
            {
                disposition =
                    notification.Fields[
                        "Disposition"];

                originalMessageId =
                    notification.Fields[
                        "Original-Message-ID"];
            }
            catch (FormatException)
            {
                /*
                 * Eine kaputte MDN darf niemals das
                 * Öffnen der eigentlichen Nachricht
                 * verhindern.
                 */
                continue;
            }

            if (!IsDisplayedDisposition(
                    disposition))
            {
                continue;
            }

            var normalizedOriginalMessageId =
                NormalizeMessageId(
                    originalMessageId)
                ?? NormalizeMessageId(
                    node.OwnerMessage.InReplyTo)
                ?? NormalizeMessageId(
                    message.InReplyTo);

            return CreateResult(
                message,
                normalizedOriginalMessageId);
        }

        return null;
    }

    private static MailReadReceiptData?
        TryDetectOutlookTnefReceipt(
            MimeMessage message)
    {
        foreach (var node in
                 EnumerateMimeTree(
                     message))
        {
            if (node.Entity
                is not MimePart mimePart)
            {
                continue;
            }

            if (!IsTnefPart(
                    mimePart))
            {
                continue;
            }

            var metadata =
                TryReadTnefMetadata(
                    mimePart);

            if (metadata is null ||
                !IsReadReceiptMessageClass(
                    metadata.MessageClass))
            {
                continue;
            }

            /*
             * Outlook schreibt die Referenz auf die
             * Ursprungsnachricht in unserer echten
             * Testmail sowohl in die MIME-Header als
             * auch in die TNEF/MAPI-Daten.
             *
             * Die TNEF-Information hat Vorrang.
             * Danach folgen die Header der eingebetteten
             * Nachricht und schließlich der äußeren Mail.
             */
            var originalMessageId =
                NormalizeMessageId(
                    metadata.InReplyToId)
                ?? NormalizeMessageId(
                    node.OwnerMessage.InReplyTo)
                ?? NormalizeMessageId(
                    message.InReplyTo);

            return CreateResult(
                message,
                originalMessageId);
        }

        return null;
    }

    private static MailReadReceiptData CreateResult(
        MimeMessage message,
        string? originalMessageId)
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

        var sender =
            string.IsNullOrWhiteSpace(
                senderName)
                ? senderAddress
                : senderName;

        DateTimeOffset? receiptDate =
            message.Date ==
            DateTimeOffset.MinValue
                ? null
                : message.Date;

        return new MailReadReceiptData(
            OriginalMessageId:
                originalMessageId,

            Sender:
                sender,

            SenderAddress:
                senderAddress,

            ReceiptDate:
                receiptDate,

            Disposition:
                DisplayedDisposition);
    }

    private static bool IsTnefPart(
        MimePart mimePart)
    {
        return
            mimePart
                .ContentType
                .IsMimeType(
                    "application",
                    "ms-tnef") ||
            mimePart
                .ContentType
                .IsMimeType(
                    "application",
                    "vnd.ms-tnef");
    }

    private static TnefReadReceiptMetadata?
        TryReadTnefMetadata(
            MimePart mimePart)
    {
        if (mimePart.Content is null)
        {
            return null;
        }

        try
        {
            using var contentStream =
                mimePart.Content.Open();

            /*
             * Loose entspricht bewusst dem Vorgehen von
             * MimeKits eigenem TnefPart.ConvertToMessage().
             *
             * Reale Outlook-/Exchange-Nachrichten sollen
             * nicht wegen kleiner TNEF-Abweichungen die
             * gesamte Maildarstellung sprengen.
             */
            using var reader =
                new TnefReader(
                    contentStream,
                    0,
                    TnefComplianceMode.Loose);

            string? messageClass =
                null;

            string? inReplyToId =
                null;

            while (reader.ReadNextAttribute())
            {
                if (reader.AttributeLevel !=
                    TnefAttributeLevel.Message)
                {
                    continue;
                }

                switch (reader.AttributeTag)
                {
                    case TnefAttributeTag.MessageClass:

                        var attributeMessageClass =
                            TryReadAttributeString(
                                reader);

                        if (!string.IsNullOrWhiteSpace(
                                attributeMessageClass))
                        {
                            messageClass =
                                attributeMessageClass;
                        }

                        break;

                    case TnefAttributeTag.MapiProperties:

                        ReadMapiProperties(
                            reader,
                            ref messageClass,
                            ref inReplyToId);

                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(
                    messageClass))
            {
                return null;
            }

            return new TnefReadReceiptMetadata(
                MessageClass:
                    messageClass,

                InReplyToId:
                    inReplyToId);
        }
        catch (FormatException)
        {
            /*
             * TnefException erbt von FormatException.
             *
             * Defektes oder nicht vollständig lesbares
             * TNEF ist damit einfach "nicht erkannt".
             */
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? TryReadAttributeString(
        TnefReader reader)
    {
        try
        {
            return reader
                .TnefPropertyReader
                .ReadValueAsString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void ReadMapiProperties(
        TnefReader reader,
        ref string? messageClass,
        ref string? inReplyToId)
    {
        var propertyReader =
            reader.TnefPropertyReader;

        while (propertyReader.ReadNextProperty())
        {
            switch (propertyReader.PropertyTag.Id)
            {
                case TnefPropertyId.MessageClass:

                    var currentMessageClass =
                        TryReadPropertyString(
                            propertyReader);

                    if (!string.IsNullOrWhiteSpace(
                            currentMessageClass))
                    {
                        /*
                         * PR_MESSAGE_CLASS ist die
                         * präzisere Information und darf
                         * deshalb einen zuvor gelesenen
                         * attMessageClass-Wert ersetzen.
                         */
                        messageClass =
                            currentMessageClass;
                    }

                    break;

                case TnefPropertyId.InReplyToId:

                    var currentInReplyToId =
                        TryReadPropertyString(
                            propertyReader);

                    if (!string.IsNullOrWhiteSpace(
                            currentInReplyToId))
                    {
                        inReplyToId =
                            currentInReplyToId;
                    }

                    break;
            }
        }
    }

    private static string? TryReadPropertyString(
        TnefPropertyReader propertyReader)
    {
        var propertyType =
            propertyReader
                .PropertyTag
                .ValueTnefType;

        if (propertyType !=
                TnefPropertyType.String8 &&
            propertyType !=
                TnefPropertyType.Unicode &&
            propertyType !=
                TnefPropertyType.Binary)
        {
            return null;
        }

        try
        {
            return propertyReader
                .ReadValueAsString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsReadReceiptMessageClass(
        string? messageClass)
    {
        if (string.IsNullOrWhiteSpace(
                messageClass))
        {
            return false;
        }

        var normalized =
            messageClass.Trim();

        /*
         * Alte TNEF-Varianten können den historischen
         * Microsoft-Mail-v3-Präfix enthalten.
         */
        if (normalized.StartsWith(
                LegacyOutlookV3Prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            normalized =
                normalized[
                    LegacyOutlookV3Prefix.Length..]
                    .Trim();
        }

        /*
         * Legacy-TNEF-Bezeichnung für eine positive
         * Lesebestätigung.
         */
        if (string.Equals(
                normalized,
                LegacyOutlookReadReceiptMessageClass,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        /*
         * Moderne Exchange-/Outlook-Berichte verwenden:
         *
         * REPORT.<ursprüngliche Nachrichtenklasse>.IPNRN
         *
         * IPNRN = positive Lesebestätigung.
         *
         * Wir beschränken uns bewusst auf REPORT.*.IPNRN.
         * Zustellberichte (.DR), Fehlerberichte (.NDR) und
         * Nicht-Lesebestätigungen (.IPNNRN) werden hier
         * ausdrücklich nicht als "gelesen" gewertet.
         */
        return
            normalized.StartsWith(
                "REPORT.",
                StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(
                ".IPNRN",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisplayedDisposition(
        string? disposition)
    {
        if (string.IsNullOrWhiteSpace(
                disposition))
        {
            return false;
        }

        /*
         * Beispiel:
         *
         * manual-action/MDN-sent-manually; displayed
         *
         * Hinter dem Semikolon steht der eigentliche
         * Disposition-Type. Optionale Modifier können
         * wiederum hinter einem Slash folgen.
         */
        var separatorIndex =
            disposition.LastIndexOf(
                ';');

        var dispositionType =
            separatorIndex >= 0
                ? disposition[
                    (separatorIndex + 1)..]
                : disposition;

        var modifierIndex =
            dispositionType.IndexOf(
                '/');

        if (modifierIndex >= 0)
        {
            dispositionType =
                dispositionType[
                    ..modifierIndex];
        }

        return string.Equals(
            dispositionType.Trim(),
            DisplayedDisposition,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeMessageId(
        string? messageId)
    {
        if (string.IsNullOrWhiteSpace(
                messageId))
        {
            return null;
        }

        var normalized =
            messageId.Trim();

        /*
         * MimeMessage.MessageId und MimeMessage.InReplyTo
         * liefern in MimeKit nur den addr-spec-Teil ohne
         * spitze Klammern.
         *
         * Original-Message-ID aus einer MDN enthält diese
         * Klammern dagegen häufig noch.
         *
         * MimeUtils bringt beide Varianten deshalb auf
         * dieselbe Form, sodass wir sie später zuverlässig
         * miteinander vergleichen können.
         */
        try
        {
            var parsed =
                MimeUtils.ParseMessageId(
                    normalized);

            if (!string.IsNullOrWhiteSpace(
                    parsed))
            {
                return parsed.Trim();
            }
        }
        catch (FormatException)
        {
        }

        /*
         * Defensiver Fallback für leicht nicht-konforme
         * Server-/Client-Ausgaben.
         */
        if (normalized.Length >= 2 &&
            normalized[0] == '<' &&
            normalized[^1] == '>')
        {
            normalized =
                normalized[
                    1..^1]
                    .Trim();
        }

        return string.IsNullOrWhiteSpace(
                normalized)
            ? null
            : normalized;
    }

    private static IEnumerable<MimeTreeNode>
        EnumerateMimeTree(
            MimeMessage message)
    {
        if (message.Body is null)
        {
            yield break;
        }

        foreach (var node in
                 EnumerateMimeEntity(
                     message.Body,
                     message))
        {
            yield return node;
        }
    }

    private static IEnumerable<MimeTreeNode>
        EnumerateMimeEntity(
            MimeEntity entity,
            MimeMessage ownerMessage)
    {
        yield return new MimeTreeNode(
            Entity:
                entity,

            OwnerMessage:
                ownerMessage);

        if (entity is Multipart multipart)
        {
            foreach (var child in multipart)
            {
                foreach (var node in
                         EnumerateMimeEntity(
                             child,
                             ownerMessage))
                {
                    yield return node;
                }
            }

            yield break;
        }

        if (entity is not MessagePart messagePart ||
            messagePart.Message is null)
        {
            yield break;
        }

        foreach (var node in
                 EnumerateMimeTree(
                     messagePart.Message))
        {
            yield return node;
        }
    }

    private sealed record MimeTreeNode(
        MimeEntity Entity,
        MimeMessage OwnerMessage);

    private sealed record TnefReadReceiptMetadata(
        string MessageClass,
        string? InReplyToId);
}