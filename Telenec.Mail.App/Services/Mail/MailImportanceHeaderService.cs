using MimeKit;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

internal static class MailImportanceHeaderService
{
    public static void Apply(
        MimeMessage message,
        MailImportanceLevel importance)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        switch (importance)
        {
            case MailImportanceLevel.High:
                ApplyHighImportance(
                    message);

                break;

            case MailImportanceLevel.Low:
                ApplyLowImportance(
                    message);

                break;

            case MailImportanceLevel.Normal:
            default:
                /*
                 * Normal ist der bisherige Standardzustand.
                 *
                 * Dafür schreiben wir absichtlich keine
                 * zusätzlichen Priority-Header.
                 *
                 * So bleiben normale Nachrichten exakt so
                 * kompatibel wie vor Einführung der
                 * Wichtigkeitsfunktion.
                 */
                break;
        }
    }

    private static void ApplyHighImportance(
        MimeMessage message)
    {
        /*
         * Verschiedene Mailclients werten historisch
         * unterschiedliche Header aus.
         *
         * Deshalb schreiben wir sowohl die üblichen
         * standardnahen Header als auch die verbreiteten
         * Microsoft-/X-Priority-Varianten.
         */
        message.Headers.Add(
            "Importance",
            "high");

        message.Headers.Add(
            "Priority",
            "urgent");

        message.Headers.Add(
            "X-Priority",
            "1");

        message.Headers.Add(
            "X-MSMail-Priority",
            "High");
    }

    private static void ApplyLowImportance(
        MimeMessage message)
    {
        message.Headers.Add(
            "Importance",
            "low");

        message.Headers.Add(
            "Priority",
            "non-urgent");

        message.Headers.Add(
            "X-Priority",
            "5");

        message.Headers.Add(
            "X-MSMail-Priority",
            "Low");
    }
}