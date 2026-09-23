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
            NormalizeRequiredText(
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
        ValidateAccountId(
            accountId);

        var normalizedMessageId =
            NormalizeRequiredText(
                originalMessageId,
                nameof(originalMessageId));

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                OriginalMessageId,
                ReceiptSenderAddress,
                ReceiptSenderName,
                ReceiptAtUtc,
                Disposition
            FROM ReadReceipts
            WHERE AccountId = $accountId
              AND OriginalMessageId = $originalMessageId
            ORDER BY ReceiptAtUtc ASC;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$originalMessageId",
            normalizedMessageId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var results =
            new List<MailReadReceiptData>();

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

            results.Add(
                new MailReadReceiptData(
                    OriginalMessageId:
                        storedOriginalMessageId,

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
}