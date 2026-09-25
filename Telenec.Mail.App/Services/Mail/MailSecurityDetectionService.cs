using MailKit;
using MimeKit;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public static class MailSecurityDetectionService
{
    private const string MultipartSignedMimeType =
        "multipart/signed";

    private const string Pkcs7SignatureMimeType =
        "application/pkcs7-signature";

    private const string XPkcs7SignatureMimeType =
        "application/x-pkcs7-signature";

    private const string Pkcs7MimeType =
        "application/pkcs7-mime";

    private const string XPkcs7MimeType =
        "application/x-pkcs7-mime";

    private const string SmimeTypeParameterName =
        "smime-type";

    private const string ProtocolParameterName =
        "protocol";

    private const string SignedDataSmimeType =
        "signed-data";

    private const string EnvelopedDataSmimeType =
        "enveloped-data";

    public static MailSecurityData Detect(
        BodyPart? body)
    {
        if (body is null)
        {
            return MailSecurityData.None;
        }

        var state =
            new DetectionState();

        InspectBodyPart(
            body,
            state);

        return state.CreateResult();
    }

    private static void InspectBodyPart(
        BodyPart bodyPart,
        DetectionState state)
    {
        /*
         * message/rfc822 wird bewusst nicht rekursiv
         * untersucht.
         *
         * Eine angehängte, signierte E-Mail macht die
         * aktuelle E-Mail selbst nicht zu einer signierten
         * Nachricht.
         */
        if (bodyPart is BodyPartMessage)
        {
            return;
        }

        if (bodyPart is BodyPartMultipart multipart)
        {
            InspectMultipart(
                multipart,
                state);

            return;
        }

        if (bodyPart is BodyPartBasic basicPart)
        {
            InspectBasicPart(
                basicPart,
                state);
        }
    }

    private static void InspectMultipart(
        BodyPartMultipart multipart,
        DetectionState state)
    {
        if (IsSmimeMultipartSigned(
                multipart))
        {
            state.HasSmimeSignature =
                true;

            state.HasDetachedSignature =
                true;
        }

        foreach (var child in
                 multipart.BodyParts)
        {
            InspectBodyPart(
                child,
                state);
        }
    }

    private static bool IsSmimeMultipartSigned(
        BodyPartMultipart multipart)
    {
        if (!MimeTypeEquals(
                multipart.ContentType?.MimeType,
                MultipartSignedMimeType))
        {
            return false;
        }

        /*
         * Regulärer S/MIME-Fall:
         *
         * multipart/signed;
         * protocol="application/pkcs7-signature"
         */
        var protocol =
            GetContentTypeParameter(
                multipart.ContentType,
                ProtocolParameterName);

        if (IsPkcs7SignatureMimeType(
                protocol))
        {
            return true;
        }

        /*
         * Manche Absender erzeugen multipart/signed ohne
         * korrekt gesetzten protocol-Parameter.
         *
         * Wenn ein direkter Kind-Part eindeutig
         * application/pkcs7-signature ist, können wir die
         * Struktur trotzdem als S/MIME-signiert erkennen.
         */
        return multipart
            .BodyParts
            .OfType<BodyPartBasic>()
            .Any(
                part =>
                    IsPkcs7SignatureMimeType(
                        part.ContentType?
                            .MimeType));
    }

    private static void InspectBasicPart(
        BodyPartBasic basicPart,
        DetectionState state)
    {
        var mimeType =
            basicPart.ContentType?
                .MimeType;

        /*
         * Ein einzelner application/pkcs7-signature-Part
         * außerhalb eines multipart/signed reicht nicht,
         * um die gesamte Nachricht als signiert einzustufen.
         *
         * Es könnte lediglich eine angehängte
         * Signaturdatei sein.
         */
        if (IsPkcs7SignatureMimeType(
                mimeType))
        {
            return;
        }

        if (!IsPkcs7MimeType(
                mimeType))
        {
            return;
        }

        var smimeType =
            GetContentTypeParameter(
                basicPart.ContentType,
                SmimeTypeParameterName);

        if (string.Equals(
                smimeType,
                SignedDataSmimeType,
                StringComparison.OrdinalIgnoreCase))
        {
            state.HasSmimeSignature =
                true;

            state.HasOpaqueSignature =
                true;

            return;
        }

        if (string.Equals(
                smimeType,
                EnvelopedDataSmimeType,
                StringComparison.OrdinalIgnoreCase))
        {
            state.HasSmimeEncryption =
                true;

            return;
        }

        /*
         * application/pkcs7-mime ohne eindeutiges
         * smime-type wird bewusst nicht geraten.
         *
         * "smime.p7m" allein sagt nicht zuverlässig,
         * ob signed-data, enveloped-data oder ein anderer
         * CMS-Inhalt enthalten ist.
         */
        state.HasUnclassifiedSmimeContent =
            true;
    }

    private static bool IsPkcs7SignatureMimeType(
        string? mimeType)
    {
        return
            MimeTypeEquals(
                mimeType,
                Pkcs7SignatureMimeType)
            ||
            MimeTypeEquals(
                mimeType,
                XPkcs7SignatureMimeType);
    }

    private static bool IsPkcs7MimeType(
        string? mimeType)
    {
        return
            MimeTypeEquals(
                mimeType,
                Pkcs7MimeType)
            ||
            MimeTypeEquals(
                mimeType,
                XPkcs7MimeType);
    }

    private static bool MimeTypeEquals(
        string? actual,
        string expected)
    {
        return string.Equals(
            actual,
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string?
        GetContentTypeParameter(
            ContentType? contentType,
            string parameterName)
    {
        if (contentType is null ||
            string.IsNullOrWhiteSpace(
                parameterName))
        {
            return null;
        }

        var parameter =
            contentType
                .Parameters
                .FirstOrDefault(
                    current =>
                        string.Equals(
                            current.Name,
                            parameterName,
                            StringComparison.OrdinalIgnoreCase));

        var value =
            parameter?
                .Value?
                .Trim();

        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value.Trim(
                '"');
    }

    private sealed class DetectionState
    {
        public bool HasSmimeSignature
        { get; set; }

        public bool HasSmimeEncryption
        { get; set; }

        public bool HasUnclassifiedSmimeContent
        { get; set; }

        public bool HasDetachedSignature
        { get; set; }

        public bool HasOpaqueSignature
        { get; set; }

        public MailSecurityData CreateResult()
        {
            var signatureFormat =
                DetermineSignatureFormat();

            return new MailSecurityData(
                HasSmimeSignature:
                    HasSmimeSignature,

                HasSmimeEncryption:
                    HasSmimeEncryption,

                HasUnclassifiedSmimeContent:
                    HasUnclassifiedSmimeContent,

                SmimeSignatureFormat:
                    signatureFormat,

                SmimeEncryptionFormat:
                    HasSmimeEncryption
                        ? MailSmimeEncryptionFormat
                            .EnvelopedData
                        : MailSmimeEncryptionFormat
                            .None);
        }

        private MailSmimeSignatureFormat
            DetermineSignatureFormat()
        {
            /*
             * Bei einer ungewöhnlichen verschachtelten
             * Struktur mit detached und opaque gewinnt
             * "Opaque" als Beschreibung des eigentlichen
             * CMS-Inhalts.
             */
            if (HasOpaqueSignature)
            {
                return MailSmimeSignatureFormat
                    .Opaque;
            }

            if (HasDetachedSignature)
            {
                return MailSmimeSignatureFormat
                    .Detached;
            }

            return MailSmimeSignatureFormat
                .None;
        }
    }
}