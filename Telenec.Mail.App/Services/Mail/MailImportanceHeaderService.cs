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

    public static MailImportanceLevel Read(
        MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        /*
         * Unterschiedliche Mailprogramme verwenden
         * unterschiedliche Header.
         *
         * Wir prüfen zuerst "Importance", danach die
         * verbreiteten Microsoft-/X-Priority-Varianten.
         *
         * Sobald ein Header einen eindeutig erkennbaren
         * Wert liefert, verwenden wir ihn.
         */
        var importance =
            ParseTextImportance(
                GetHeaderValue(
                    message,
                    "Importance"));

        if (importance.HasValue)
        {
            return importance.Value;
        }

        importance =
            ParseTextImportance(
                GetHeaderValue(
                    message,
                    "X-MSMail-Priority"));

        if (importance.HasValue)
        {
            return importance.Value;
        }

        importance =
            ParseNumericPriority(
                GetHeaderValue(
                    message,
                    "X-Priority"));

        if (importance.HasValue)
        {
            return importance.Value;
        }

        importance =
            ParsePriority(
                GetHeaderValue(
                    message,
                    "Priority"));

        return importance
            ?? MailImportanceLevel.Normal;
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

    private static string? GetHeaderValue(
        MimeMessage message,
        string fieldName)
    {
        return message
            .Headers
            .FirstOrDefault(
                header =>
                    string.Equals(
                        header.Field,
                        fieldName,
                        StringComparison.OrdinalIgnoreCase))?
            .Value;
    }

    private static MailImportanceLevel?
        ParseTextImportance(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        return value
            .Trim()
            .ToLowerInvariant() switch
        {
            "high" =>
                MailImportanceLevel.High,

            "highest" =>
                MailImportanceLevel.High,

            "low" =>
                MailImportanceLevel.Low,

            "lowest" =>
                MailImportanceLevel.Low,

            "normal" =>
                MailImportanceLevel.Normal,

            _ =>
                null
        };
    }

    private static MailImportanceLevel?
        ParsePriority(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        return value
            .Trim()
            .ToLowerInvariant() switch
        {
            "urgent" =>
                MailImportanceLevel.High,

            "non-urgent" =>
                MailImportanceLevel.Low,

            "normal" =>
                MailImportanceLevel.Normal,

            _ =>
                null
        };
    }

    private static MailImportanceLevel?
        ParseNumericPriority(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        /*
         * X-Priority tritt häufig sowohl als reine Zahl
         *
         *   1
         *
         * als auch mit Beschreibung auf:
         *
         *   1 (Highest)
         *   5 (Lowest)
         *
         * Deshalb genügt für die bekannte Prioritätsklasse
         * das erste nicht-leere Zeichen.
         */
        var normalized =
            value.Trim();

        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized[0] switch
        {
            '1' or '2' =>
                MailImportanceLevel.High,

            '3' =>
                MailImportanceLevel.Normal,

            '4' or '5' =>
                MailImportanceLevel.Low,

            _ =>
                null
        };
    }
}