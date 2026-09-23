using Microsoft.Data.Sqlite;
using System.Globalization;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public sealed class SqliteMailReadReceiptStore
    : IMailReadReceiptStore
{
    private readonly AppDataPaths _paths;

    public SqliteMailReadReceiptStore(
        AppDataPaths paths)
    {
        _paths =
            paths;
    }

    public async Task SaveAsync(
        Guid accountId,
        MailReadReceiptData readReceipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            readReceipt);

        ValidateAccountId(
            accountId);

        var originalMessageId =
            NormalizeRequiredMessageId(
                readReceipt.OriginalMessageId,
                nameof(
                    readReceipt.OriginalMessageId));

        var senderAddress =
            NormalizeRequiredText(
                readReceipt.SenderAddress,
                nameof(
                    readReceipt.SenderAddress));

        var disposition =
            NormalizeRequiredText(
                readReceipt.Disposition,
                nameof(
                    readReceipt.Disposition));

        if (!readReceipt.ReceiptDate.HasValue)
        {
            throw new ArgumentException(
                "Die Lesebestätigung enthält keinen gültigen Empfangszeitpunkt.",
                nameof(readReceipt));
        }

        var senderName =
            string.IsNullOrWhiteSpace(
                readReceipt.Sender)
                ? null
                : readReceipt.Sender.Trim();

        var receiptAtUtc =
            readReceipt
                .ReceiptDate
                .Value
                .ToUniversalTime();

        var recordedAtUtc =
            DateTimeOffset.UtcNow;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO ReadReceipts
            (
                AccountId,
                OriginalMessageId,
                ReceiptSenderAddress,
                ReceiptSenderName,
                ReceiptAtUtc,
                Disposition,
                RecordedAtUtc
            )
            VALUES
            (
                $accountId,
                $originalMessageId,
                $senderAddress,
                $senderName,
                $receiptAtUtc,
                $disposition,
                $recordedAtUtc
            )
            ON CONFLICT
            (
                AccountId,
                OriginalMessageId,
                ReceiptSenderAddress
            )
            DO UPDATE SET
                ReceiptSenderName =
                    excluded.ReceiptSenderName,

                ReceiptAtUtc =
                    excluded.ReceiptAtUtc,

                Disposition =
                    excluded.Disposition,

                RecordedAtUtc =
                    excluded.RecordedAtUtc;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$originalMessageId",
            originalMessageId);

        command.Parameters.AddWithValue(
            "$senderAddress",
            senderAddress);

        command.Parameters.AddWithValue(
            "$senderName",
            senderName is null
                ? DBNull.Value
                : senderName);

        command.Parameters.AddWithValue(
            "$receiptAtUtc",
            receiptAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture));

        command.Parameters.AddWithValue(
            "$disposition",
            disposition);

        command.Parameters.AddWithValue(
            "$recordedAtUtc",
            recordedAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<MailReadReceiptData>>
        GetByOriginalMessageIdAsync(
            Guid accountId,
            string originalMessageId,
            CancellationToken cancellationToken = default)
    {
        var results =
            await GetByOriginalMessageIdsAsync(
                accountId,
                new[]
                {
                    originalMessageId
                },
                cancellationToken);

        var normalizedMessageId =
            NormalizeRequiredMessageId(
                originalMessageId,
                nameof(originalMessageId));

        if (results.TryGetValue(
                normalizedMessageId,
                out var readReceipts))
        {
            return readReceipts;
        }

        return Array.Empty<MailReadReceiptData>();
    }

    public async Task<IReadOnlyDictionary<
        string,
        IReadOnlyList<MailReadReceiptData>>>
        GetByOriginalMessageIdsAsync(
            Guid accountId,
            IReadOnlyCollection<string> originalMessageIds,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        ArgumentNullException.ThrowIfNull(
            originalMessageIds);

        var normalizedMessageIds =
            originalMessageIds
                .Where(
                    messageId =>
                        !string.IsNullOrWhiteSpace(
                            messageId))
                .Select(
                    NormalizeMessageId)
                .Where(
                    messageId =>
                        !string.IsNullOrWhiteSpace(
                            messageId))
                .Select(
                    messageId =>
                        messageId!)
                .Distinct(
                    StringComparer.Ordinal)
                .ToArray();

        if (normalizedMessageIds.Length == 0)
        {
            return new Dictionary<
                string,
                IReadOnlyList<MailReadReceiptData>>(
                    StringComparer.Ordinal);
        }

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        var parameterNames =
            new string[
                normalizedMessageIds.Length];

        for (var index = 0;
             index < normalizedMessageIds.Length;
             index++)
        {
            var parameterName =
                $"$messageId{index}";

            parameterNames[index] =
                parameterName;

            command.Parameters.AddWithValue(
                parameterName,
                normalizedMessageIds[index]);
        }

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.CommandText =
            $"""
            SELECT
                OriginalMessageId,
                ReceiptSenderAddress,
                ReceiptSenderName,
                ReceiptAtUtc,
                Disposition
            FROM ReadReceipts
            WHERE AccountId = $accountId
              AND OriginalMessageId IN
              (
                  {string.Join(
                      ", ",
                      parameterNames)}
              )
            ORDER BY
                OriginalMessageId ASC,
                ReceiptAtUtc ASC;
            """;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var mutableResults =
            new Dictionary<
                string,
                List<MailReadReceiptData>>(
                    StringComparer.Ordinal);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var storedOriginalMessageId =
                reader.GetString(0);

            var senderAddress =
                reader.GetString(1);

            var senderName =
                reader.IsDBNull(2)
                    ? string.Empty
                    : reader.GetString(2);

            var receiptAtUtcText =
                reader.GetString(3);

            var disposition =
                reader.GetString(4);

            if (!DateTimeOffset.TryParse(
                    receiptAtUtcText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var receiptAtUtc))
            {
                throw new InvalidOperationException(
                    "Der gespeicherte Zeitpunkt einer Lesebestätigung ist ungültig.");
            }

            var normalizedStoredMessageId =
                NormalizeRequiredMessageId(
                    storedOriginalMessageId,
                    nameof(storedOriginalMessageId));

            if (!mutableResults.TryGetValue(
                    normalizedStoredMessageId,
                    out var messageReadReceipts))
            {
                messageReadReceipts =
                    new List<MailReadReceiptData>();

                mutableResults.Add(
                    normalizedStoredMessageId,
                    messageReadReceipts);
            }

            messageReadReceipts.Add(
                new MailReadReceiptData(
                    OriginalMessageId:
                        normalizedStoredMessageId,

                    Sender:
                        string.IsNullOrWhiteSpace(
                            senderName)
                            ? senderAddress
                            : senderName,

                    SenderAddress:
                        senderAddress,

                    ReceiptDate:
                        receiptAtUtc,

                    Disposition:
                        disposition));
        }

        var results =
            new Dictionary<
                string,
                IReadOnlyList<MailReadReceiptData>>(
                    StringComparer.Ordinal);

        foreach (var pair in
                 mutableResults)
        {
            results.Add(
                pair.Key,
                pair.Value.ToArray());
        }

        return results;
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _paths.DatabasePath,

                Mode =
                    SqliteOpenMode.ReadWrite,

                /*
                 * SQLite aktiviert Foreign-Key-Prüfungen
                 * verbindungsbezogen.
                 *
                 * Dadurch werden insbesondere das
                 * ReadReceipts-FK und ON DELETE CASCADE
                 * tatsächlich wirksam.
                 */
                ForeignKeys =
                    true
            }.ToString();

        return new SqliteConnection(
            connectionString);
    }

    private static void ValidateAccountId(
        Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(accountId));
        }
    }

    private static string NormalizeRequiredText(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Der Wert darf nicht leer sein.",
                parameterName);
        }

        return value.Trim();
    }

    private static string
        NormalizeRequiredMessageId(
            string? value,
            string parameterName)
    {
        var normalized =
            NormalizeMessageId(
                value);

        if (string.IsNullOrWhiteSpace(
                normalized))
        {
            throw new ArgumentException(
                "Die Message-ID darf nicht leer sein.",
                parameterName);
        }

        return normalized;
    }

    private static string?
        NormalizeMessageId(
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
         * Empfangene MDNs enthalten Original-Message-ID
         * teilweise mit spitzen Klammern, während MailKit
         * Envelope.MessageId üblicherweise bereits nur den
         * eigentlichen Message-ID-Wert liefert.
         *
         * Für die lokale Zuordnung bringen wir beide
         * Varianten deshalb auf dieselbe Form.
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
}