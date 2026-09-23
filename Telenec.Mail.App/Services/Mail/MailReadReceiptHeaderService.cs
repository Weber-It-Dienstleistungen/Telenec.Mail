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

    public static bool Read(
        MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        /*
         * Für unseren Composer ist entscheidend, ob die
         * Nachricht überhaupt eine MDN-Anforderung enthält.
         *
         * Der konkrete Zielwert wird beim späteren erneuten
         * Speichern ohnehin wieder aus dem aktuell aktiven
         * Absenderkonto erzeugt.
         *
         * Dadurch übernehmen wir keine möglicherweise alte
         * oder fremde Bestätigungsadresse aus einem Entwurf.
         */
        var dispositionNotificationTo =
            message.Headers[
                HeaderId.DispositionNotificationTo];

        return !string.IsNullOrWhiteSpace(
            dispositionNotificationTo);
    }
}