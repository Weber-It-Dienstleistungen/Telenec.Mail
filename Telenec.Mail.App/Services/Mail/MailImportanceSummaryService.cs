using MailKit;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

internal static class MailImportanceSummaryService
{
    /*
     * Nur diese Header werden für die Wichtigkeits-
     * erkennung benötigt.
     *
     * Wir laden bewusst nicht sämtliche Mailheader,
     * damit die normale Ordneransicht schlank bleibt.
     */
    public static IReadOnlyList<string>
        HeaderFields
    { get; } =
        new[]
        {
            "Importance",
            "Priority",
            "X-Priority",
            "X-MSMail-Priority"
        };

    public static MailImportanceLevel Read(
        IMessageSummary summary)
    {
        ArgumentNullException.ThrowIfNull(
            summary);

        /*
         * Headers ist nur gesetzt, wenn die entsprechenden
         * Felder beim IMAP-FETCH tatsächlich angefordert
         * wurden.
         *
         * Fehlen sie, bleibt Normal der sichere Standard.
         */
        if (summary.Headers is null)
        {
            return MailImportanceLevel.Normal;
        }

        return MailImportanceHeaderService.Read(
            summary.Headers);
    }
}