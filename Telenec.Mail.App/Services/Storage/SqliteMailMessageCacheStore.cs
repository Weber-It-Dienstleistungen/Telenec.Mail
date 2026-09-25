using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public sealed class SqliteMailMessageCacheStore :
    IMailMessageCacheStore
{
    private const int CurrentCacheFormatVersion =
        1;

    private const int MaximumFolderIdLength =
        1024;

    private const int MaximumPageSize =
        500;

    private static readonly JsonSerializerOptions
        SerializerOptions =
            CreateSerializerOptions();

    private readonly AppDataPaths _paths;

    public SqliteMailMessageCacheStore(
        AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(
            paths);

        _paths =
            paths;
    }

    public async Task SaveMessagesAsync(
        Guid accountId,
        string folderId,
        uint uidValidity,
        IReadOnlyList<MailMessageData> messages,
        CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        var normalizedFolderId =
            NormalizeFolderId(
                folderId);

        ValidateUidValidity(
            uidValidity);

        ArgumentNullException.ThrowIfNull(
            messages);

        ValidateMessages(
            messages);

        var cachedAtUtc =
            DateTimeOffset.UtcNow;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            /*
             * SaveMessagesAsync repräsentiert bewusst einen
             * vollständigen sichtbaren Ordner-Snapshot.
             *
             * Deshalb werden die bisher gespeicherten
             * Nachrichten dieses Ordners zuerst vollständig
             * entfernt.
             *
             * Dadurch bleiben weder gelöschte noch verschobene
             * Nachrichten als Cache-Leichen zurück.
             *
             * Gleichzeitig kann sich die sichtbare
             * Sortierreihenfolge vollständig ändern.
             */
            await DeleteCachedMessagesAsync(
                connection,
                (SqliteTransaction)transaction,
                accountId,
                normalizedFolderId,
                cancellationToken);

            await UpsertFolderStateAsync(
                connection,
                (SqliteTransaction)transaction,
                accountId,
                normalizedFolderId,
                uidValidity,
                cachedAtUtc,
                cancellationToken);

            for (var sortPosition = 0;
                 sortPosition < messages.Count;
                 sortPosition++)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var message =
                    messages[
                        sortPosition];

                var serializedMessage =
                    JsonSerializer.Serialize(
                        message,
                        SerializerOptions);

                await UpsertMessageAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    accountId,
                    normalizedFolderId,
                    uidValidity,
                    message.UniqueId,
                    sortPosition,
                    serializedMessage,
                    cachedAtUtc,
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    public async Task<IReadOnlyList<MailMessageData>>
        GetMessagePageAsync(
            Guid accountId,
            string folderId,
            int skipMessageCount,
            int maximumMessageCount,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        var normalizedFolderId =
            NormalizeFolderId(
                folderId);

        ValidatePaging(
            skipMessageCount,
            maximumMessageCount);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        /*
         * Der JOIN auf die aktuell gespeicherte UIDVALIDITY
         * ist absichtlich Bestandteil der Abfrage.
         *
         * Alte Nachrichtengenerationen dürfen niemals
         * ausgeliefert werden.
         *
         * Die Reihenfolge stammt ausdrücklich aus
         * SortPosition und damit aus dem letzten erfolgreichen
         * sichtbaren Online-Abruf.
         *
         * Dadurch funktioniert der Cache unabhängig davon,
         * ob der Benutzer nach Datum, Absender oder Betreff
         * sortiert hatte.
         */
        command.CommandText =
            """
            SELECT
                message.MessageJson
            FROM CachedMailMessages AS message
            INNER JOIN CachedMailFolders AS folder
                ON folder.AccountId =
                    message.AccountId
               AND folder.FolderId =
                    message.FolderId
               AND folder.UidValidity =
                    message.UidValidity
            WHERE message.AccountId = $accountId
              AND message.FolderId = $folderId
              AND message.CacheFormatVersion =
                    $cacheFormatVersion
              AND message.SortPosition IS NOT NULL
            ORDER BY
                message.SortPosition ASC
            LIMIT $maximumMessageCount
            OFFSET $skipMessageCount;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$folderId",
            normalizedFolderId);

        command.Parameters.AddWithValue(
            "$cacheFormatVersion",
            CurrentCacheFormatVersion);

        command.Parameters.AddWithValue(
            "$maximumMessageCount",
            maximumMessageCount);

        command.Parameters.AddWithValue(
            "$skipMessageCount",
            skipMessageCount);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var messages =
            new List<MailMessageData>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var serializedMessage =
                reader.GetString(0);

            MailMessageData?
                message;

            try
            {
                message =
                    JsonSerializer.Deserialize<
                        MailMessageData>(
                        serializedMessage,
                        SerializerOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Eine lokal zwischengespeicherte E-Mail ist beschädigt oder besitzt ein unbekanntes Format.",
                    exception);
            }

            if (message is null)
            {
                throw new InvalidDataException(
                    "Eine lokal zwischengespeicherte E-Mail konnte nicht gelesen werden.");
            }

            if (message.UniqueId == 0)
            {
                throw new InvalidDataException(
                    "Eine lokal zwischengespeicherte E-Mail besitzt keine gültige IMAP-UID.");
            }

            messages.Add(
                message);
        }

        return messages;
    }

    public async Task<MailMessageCacheFolderState?>
        GetFolderStateAsync(
            Guid accountId,
            string folderId,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        var normalizedFolderId =
            NormalizeFolderId(
                folderId);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                UidValidity,
                UpdatedAtUtc
            FROM CachedMailFolders
            WHERE AccountId = $accountId
              AND FolderId = $folderId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$folderId",
            normalizedFolderId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        var uidValidityValue =
            reader.GetInt64(0);

        if (uidValidityValue <= 0 ||
            uidValidityValue >
                uint.MaxValue)
        {
            throw new InvalidDataException(
                "Die lokal gespeicherte UIDVALIDITY ist ungültig.");
        }

        var updatedAtUtcText =
            reader.GetString(1);

        if (!DateTimeOffset.TryParse(
                updatedAtUtcText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var updatedAtUtc))
        {
            throw new InvalidDataException(
                "Der Zeitstempel des lokalen Mail-Caches ist ungültig.");
        }

        return new MailMessageCacheFolderState(
            FolderId:
                normalizedFolderId,

            UidValidity:
                (uint)uidValidityValue,

            UpdatedAtUtc:
                updatedAtUtc);
    }

    public async Task ClearFolderAsync(
        Guid accountId,
        string folderId,
        CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        var normalizedFolderId =
            NormalizeFolderId(
                folderId);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        /*
         * CachedMailMessages besitzt ON DELETE CASCADE.
         *
         * Das Entfernen des Folder-State löscht deshalb
         * automatisch die komplette zugehörige
         * Nachrichtengeneration.
         */
        command.CommandText =
            """
            DELETE FROM CachedMailFolders
            WHERE AccountId = $accountId
              AND FolderId = $folderId;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$folderId",
            normalizedFolderId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
        DeleteCachedMessagesAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Guid accountId,
            string folderId,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            DELETE FROM CachedMailMessages
            WHERE AccountId = $accountId
              AND FolderId = $folderId;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$folderId",
            folderId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
        UpsertFolderStateAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Guid accountId,
            string folderId,
            uint uidValidity,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            INSERT INTO CachedMailFolders
            (
                AccountId,
                FolderId,
                UidValidity,
                UpdatedAtUtc
            )
            VALUES
            (
                $accountId,
                $folderId,
                $uidValidity,
                $updatedAtUtc
            )
            ON CONFLICT
            (
                AccountId,
                FolderId
            )
            DO UPDATE SET
                UidValidity =
                    excluded.UidValidity,

                UpdatedAtUtc =
                    excluded.UpdatedAtUtc;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$folderId",
            folderId);

        command.Parameters.AddWithValue(
            "$uidValidity",
            (long)uidValidity);

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            updatedAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
        UpsertMessageAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Guid accountId,
            string folderId,
            uint uidValidity,
            uint uniqueId,
            int sortPosition,
            string serializedMessage,
            DateTimeOffset cachedAtUtc,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            INSERT INTO CachedMailMessages
            (
                AccountId,
                FolderId,
                UidValidity,
                UniqueId,
                CacheFormatVersion,
                MessageJson,
                CachedAtUtc,
                SortPosition
            )
            VALUES
            (
                $accountId,
                $folderId,
                $uidValidity,
                $uniqueId,
                $cacheFormatVersion,
                $messageJson,
                $cachedAtUtc,
                $sortPosition
            )
            ON CONFLICT
            (
                AccountId,
                FolderId,
                UniqueId
            )
            DO UPDATE SET
                UidValidity =
                    excluded.UidValidity,

                CacheFormatVersion =
                    excluded.CacheFormatVersion,

                MessageJson =
                    excluded.MessageJson,

                CachedAtUtc =
                    excluded.CachedAtUtc,

                SortPosition =
                    excluded.SortPosition;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$folderId",
            folderId);

        command.Parameters.AddWithValue(
            "$uidValidity",
            (long)uidValidity);

        command.Parameters.AddWithValue(
            "$uniqueId",
            (long)uniqueId);

        command.Parameters.AddWithValue(
            "$cacheFormatVersion",
            CurrentCacheFormatVersion);

        command.Parameters.AddWithValue(
            "$messageJson",
            serializedMessage);

        command.Parameters.AddWithValue(
            "$cachedAtUtc",
            cachedAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture));

        command.Parameters.AddWithValue(
            "$sortPosition",
            sortPosition);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
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
                 * CachedMailMessages hängt per Foreign Key
                 * an CachedMailFolders und das wiederum am
                 * Mailkonto.
                 *
                 * SQLite aktiviert Foreign Keys
                 * verbindungsbezogen.
                 */
                ForeignKeys =
                    true
            }.ToString();

        return new SqliteConnection(
            connectionString);
    }

    private static void ValidateMessages(
        IReadOnlyList<MailMessageData> messages)
    {
        var knownUniqueIds =
            new HashSet<uint>();

        foreach (var message in messages)
        {
            ArgumentNullException.ThrowIfNull(
                message);

            if (message.UniqueId == 0)
            {
                throw new ArgumentException(
                    "Eine zu cachende Nachricht besitzt keine gültige IMAP-UID.",
                    nameof(messages));
            }

            if (!knownUniqueIds.Add(
                    message.UniqueId))
            {
                throw new ArgumentException(
                    $"Die IMAP-UID {message.UniqueId} ist innerhalb der Cache-Aktualisierung mehrfach vorhanden.",
                    nameof(messages));
            }
        }
    }

    private static void ValidateAccountId(
        Guid accountId)
    {
        if (accountId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(accountId));
        }
    }

    private static string NormalizeFolderId(
        string folderId)
    {
        if (string.IsNullOrWhiteSpace(
                folderId))
        {
            throw new ArgumentException(
                "Die Ordner-ID darf nicht leer sein.",
                nameof(folderId));
        }

        var normalized =
            folderId.Trim();

        if (normalized.Length >
            MaximumFolderIdLength)
        {
            throw new ArgumentException(
                $"Die Ordner-ID darf maximal {MaximumFolderIdLength} Zeichen lang sein.",
                nameof(folderId));
        }

        return normalized;
    }

    private static void ValidateUidValidity(
        uint uidValidity)
    {
        if (uidValidity == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uidValidity),
                "UIDVALIDITY muss größer als 0 sein.");
        }
    }

    private static void ValidatePaging(
        int skipMessageCount,
        int maximumMessageCount)
    {
        if (skipMessageCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skipMessageCount),
                "Die Anzahl übersprungener Nachrichten darf nicht negativ sein.");
        }

        if (maximumMessageCount <= 0 ||
            maximumMessageCount >
                MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMessageCount),
                $"Pro Cache-Seite sind zwischen 1 und {MaximumPageSize} Nachrichten zulässig.");
        }
    }

    private static JsonSerializerOptions
        CreateSerializerOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,

                PropertyNameCaseInsensitive =
                    true,

                WriteIndented =
                    false
            };

        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase));

        return options;
    }
}