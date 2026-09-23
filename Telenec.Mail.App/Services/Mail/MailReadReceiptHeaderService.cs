using MimeKit;

namespace Telenec.Mail.App.Services.Mail;

internal static class MailReadReceiptHeaderService
{
    public static void Apply(
        MimeMessage message,
        MailboxAddress sender,
        bool requestReadReceipt)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        ArgumentNullException.ThrowIfNull(
            sender);

        if (!requestReadReceipt)
        {
            return;
        }

        /*
         * Standardisierte MDN-Lesebestätigung.
         *
         * Der Header fordert den Empfänger-Mailclient auf,
         * eine Message Disposition Notification an die
         * Absenderadresse zu senden.
         *
         * Wichtig:
         *
         * Das ist ausdrücklich nur eine Anforderung.
         * Der Empfänger oder dessen Mailprogramm kann die
         * Bestätigung ablehnen, ignorieren oder nicht
         * unterstützen.
         */
        message.Headers[
            HeaderId.DispositionNotificationTo] =
                sender.ToString(
                    encode:
                        true);
    }
}