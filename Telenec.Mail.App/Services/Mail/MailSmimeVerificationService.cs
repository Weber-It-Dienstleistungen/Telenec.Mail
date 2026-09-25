using MimeKit;
using MimeKit.Cryptography;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public static class MailSmimeVerificationService
{
    public static MailSmimeVerificationData Verify(
        MimeMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        cancellationToken
            .ThrowIfCancellationRequested();

        if (message.Body is null)
        {
            return MailSmimeVerificationData
                .NotChecked;
        }

        try
        {
            using var context =
                new WindowsSecureMimeContext();

            if (message.Body is MultipartSigned
                multipartSigned)
            {
                var signatures =
                    multipartSigned.Verify(
                        context,
                        cancellationToken);

                return EvaluateSignatures(
                    signatures);
            }

            if (message.Body is ApplicationPkcs7Mime
                pkcs7Mime &&
                pkcs7Mime.SecureMimeType ==
                    SecureMimeType.SignedData)
            {
                var signatures =
                    pkcs7Mime.Verify(
                        context,
                        out _,
                        cancellationToken);

                return EvaluateSignatures(
                    signatures);
            }

            return MailSmimeVerificationData
                .NotChecked;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new MailSmimeVerificationData(
                Status:
                    MailSmimeVerificationStatus.Error,

                Signers:
                    Array.Empty<MailSmimeSignerData>());
        }
    }

    private static MailSmimeVerificationData
        EvaluateSignatures(
            DigitalSignatureCollection signatures)
    {
        if (signatures.Count == 0)
        {
            return new MailSmimeVerificationData(
                Status:
                    MailSmimeVerificationStatus.Error,

                Signers:
                    Array.Empty<MailSmimeSignerData>());
        }

        var signerResults =
            new List<SignerVerificationResult>();

        foreach (var signature in signatures)
        {
            signerResults.Add(
                EvaluateSignature(
                    signature));
        }

        var aggregateStatus =
            DetermineAggregateStatus(
                signerResults);

        return new MailSmimeVerificationData(
            Status:
                aggregateStatus,

            Signers:
                signerResults
                    .Select(
                        result =>
                            result.Signer)
                    .ToArray());
    }

    private static SignerVerificationResult
        EvaluateSignature(
            IDigitalSignature signature)
    {
        ArgumentNullException.ThrowIfNull(
            signature);

        bool signatureIntegrityValid;

        try
        {
            /*
             * true bedeutet:
             *
             * Nur die kryptografische Signatur prüfen.
             * Die Vertrauenskette des Zertifikats wird hier
             * bewusst noch nicht berücksichtigt.
             *
             * Dadurch können wir sauber unterscheiden zwischen:
             *
             * - Nachricht manipuliert / Signatur ungültig
             * - Signatur mathematisch korrekt, aber Zertifikat
             *   nicht vollständig vertrauenswürdig
             */
            signatureIntegrityValid =
                signature.Verify(
                    verifySignatureOnly: true);
        }
        catch
        {
            return CreateErrorResult(
                signature);
        }

        if (!signatureIntegrityValid)
        {
            return new SignerVerificationResult(
                Status:
                    MailSmimeVerificationStatus
                        .InvalidSignature,

                Signer:
                    CreateSignerData(
                        signature,
                        signatureIntegrityValid:
                            false,
                        certificateValidationSuccessful:
                            false));
        }

        var certificate =
            signature.SignerCertificate;

        if (certificate is null)
        {
            return new SignerVerificationResult(
                Status:
                    MailSmimeVerificationStatus
                        .CertificateValidationFailed,

                Signer:
                    CreateSignerData(
                        signature,
                        signatureIntegrityValid:
                            true,
                        certificateValidationSuccessful:
                            false));
        }

        var now =
            DateTime.Now;

        if (certificate.CreationDate >
            now)
        {
            return new SignerVerificationResult(
                Status:
                    MailSmimeVerificationStatus
                        .CertificateNotYetValid,

                Signer:
                    CreateSignerData(
                        signature,
                        signatureIntegrityValid:
                            true,
                        certificateValidationSuccessful:
                            false));
        }

        if (certificate.ExpirationDate <
            now)
        {
            return new SignerVerificationResult(
                Status:
                    MailSmimeVerificationStatus
                        .CertificateExpired,

                Signer:
                    CreateSignerData(
                        signature,
                        signatureIntegrityValid:
                            true,
                        certificateValidationSuccessful:
                            false));
        }

        bool certificateValidationSuccessful;

        try
        {
            /*
             * false bedeutet:
             *
             * Signatur UND Zertifikatskette validieren.
             *
             * Die WindowsSecureMimeContext-Implementierung
             * verwendet dafür den Windows-Zertifikatsspeicher.
             */
            certificateValidationSuccessful =
                signature.Verify(
                    verifySignatureOnly: false);
        }
        catch
        {
            certificateValidationSuccessful =
                false;
        }

        if (!certificateValidationSuccessful)
        {
            return new SignerVerificationResult(
                Status:
                    MailSmimeVerificationStatus
                        .CertificateValidationFailed,

                Signer:
                    CreateSignerData(
                        signature,
                        signatureIntegrityValid:
                            true,
                        certificateValidationSuccessful:
                            false));
        }

        return new SignerVerificationResult(
            Status:
                MailSmimeVerificationStatus.Valid,

            Signer:
                CreateSignerData(
                    signature,
                    signatureIntegrityValid:
                        true,
                    certificateValidationSuccessful:
                        true));
    }

    private static MailSmimeSignerData
        CreateSignerData(
            IDigitalSignature signature,
            bool signatureIntegrityValid,
            bool certificateValidationSuccessful)
    {
        var certificate =
            signature.SignerCertificate;

        if (certificate is null)
        {
            return new MailSmimeSignerData(
                Name:
                    string.Empty,

                EmailAddress:
                    string.Empty,

                Fingerprint:
                    string.Empty,

                CertificateValidFrom:
                    null,

                CertificateValidUntil:
                    null,

                SignatureIntegrityValid:
                    signatureIntegrityValid,

                CertificateValidationSuccessful:
                    certificateValidationSuccessful);
        }

        return new MailSmimeSignerData(
            Name:
                certificate.Name?
                    .Trim()
                ?? string.Empty,

            EmailAddress:
                certificate.Email?
                    .Trim()
                ?? string.Empty,

            Fingerprint:
                certificate.Fingerprint?
                    .Trim()
                ?? string.Empty,

            CertificateValidFrom:
                certificate.CreationDate,

            CertificateValidUntil:
                certificate.ExpirationDate,

            SignatureIntegrityValid:
                signatureIntegrityValid,

            CertificateValidationSuccessful:
                certificateValidationSuccessful);
    }

    private static SignerVerificationResult
        CreateErrorResult(
            IDigitalSignature signature)
    {
        return new SignerVerificationResult(
            Status:
                MailSmimeVerificationStatus.Error,

            Signer:
                CreateSignerData(
                    signature,
                    signatureIntegrityValid:
                        false,
                    certificateValidationSuccessful:
                        false));
    }

    private static MailSmimeVerificationStatus
        DetermineAggregateStatus(
            IReadOnlyList<SignerVerificationResult>
                signerResults)
    {
        /*
         * Bei mehreren Signaturen gewinnt bewusst der
         * problematischste Zustand.
         *
         * Eine Nachricht mit zwei Signaturen darf also nicht
         * pauschal als "gültig" erscheinen, wenn eine davon
         * ungültig ist.
         */

        if (signerResults.Any(
                result =>
                    result.Status ==
                    MailSmimeVerificationStatus.Error))
        {
            return MailSmimeVerificationStatus
                .Error;
        }

        if (signerResults.Any(
                result =>
                    result.Status ==
                    MailSmimeVerificationStatus
                        .InvalidSignature))
        {
            return MailSmimeVerificationStatus
                .InvalidSignature;
        }

        if (signerResults.Any(
                result =>
                    result.Status ==
                    MailSmimeVerificationStatus
                        .CertificateNotYetValid))
        {
            return MailSmimeVerificationStatus
                .CertificateNotYetValid;
        }

        if (signerResults.Any(
                result =>
                    result.Status ==
                    MailSmimeVerificationStatus
                        .CertificateExpired))
        {
            return MailSmimeVerificationStatus
                .CertificateExpired;
        }

        if (signerResults.Any(
                result =>
                    result.Status ==
                    MailSmimeVerificationStatus
                        .CertificateValidationFailed))
        {
            return MailSmimeVerificationStatus
                .CertificateValidationFailed;
        }

        return MailSmimeVerificationStatus.Valid;
    }

    private sealed record SignerVerificationResult(
        MailSmimeVerificationStatus Status,
        MailSmimeSignerData Signer);
}