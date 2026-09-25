using MimeKit;
using MimeKit.Cryptography;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public static class MailSmimeSigningService
{
    public static async Task<MailSmimeSigningCapabilityData>
        CheckCapabilityAsync(
            string emailAddress,
            string? displayName = null,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                emailAddress))
        {
            throw new ArgumentException(
                "Die Absenderadresse darf nicht leer sein.",
                nameof(emailAddress));
        }

        if (!MailboxAddress.TryParse(
                emailAddress.Trim(),
                out var parsedAddress) ||
            parsedAddress is null)
        {
            throw new ArgumentException(
                "Die Absenderadresse ist ungültig.",
                nameof(emailAddress));
        }

        var signerName =
            !string.IsNullOrWhiteSpace(
                displayName)
                ? displayName.Trim()
                : parsedAddress.Address;

        var signer =
            new MailboxAddress(
                signerName,
                parsedAddress.Address);

        try
        {
            /*
             * Der parameterlose WindowsSecureMimeContext
             * verwendet den Zertifikatsspeicher des aktuell
             * angemeldeten Windows-Benutzers.
             *
             * CanSign prüft, ob für genau diese Mailadresse
             * ein für S/MIME-Signaturen verwendbares
             * Zertifikat inklusive privatem Schlüssel
             * verfügbar ist.
             */
            using var context =
                new WindowsSecureMimeContext();

            var canSign =
                await context.CanSignAsync(
                    signer,
                    cancellationToken);

            return new MailSmimeSigningCapabilityData(
                Status:
                    canSign
                        ? MailSmimeSigningCapabilityStatus
                            .Available
                        : MailSmimeSigningCapabilityStatus
                            .NoUsableCertificate,

                SignerAddress:
                    signer.Address);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            /*
             * Ein Fehler beim Zugriff auf den
             * Zertifikatsspeicher darf später nicht den
             * normalen Mailversand blockieren.
             *
             * Deshalb wird dieser Zustand explizit
             * zurückgegeben und nicht als "kein Zertifikat"
             * fehlinterpretiert.
             */
            return new MailSmimeSigningCapabilityData(
                Status:
                    MailSmimeSigningCapabilityStatus.Error,

                SignerAddress:
                    signer.Address);
        }
    }
}