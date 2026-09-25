using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public sealed class SqliteMailFolderCacheStore :
    IMailFolderCacheStore
{
    private const int CurrentCacheFormatVersion =
        1;

    private const int MaximumFolderIdLength =
        1024;

    private static readonly JsonSerializerOptions
        SerializerOptions =
            new()
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,

                PropertyNameCaseInsensitive =
                    true,

                WriteIndented =
                    false
            };

    private readonly AppDataPaths _paths;

    public SqliteMailFolderCacheStore(
        AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(
            paths);

        _paths =
            paths;
    }

    public async Task SaveFoldersAsync(
        Guid accountId,
        IReadOnlyList<MailFolderData> folders,
        CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        ArgumentNullException.ThrowIfNull(
            folders);

        ValidateFolders(
            folders);

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
             * Die Ordnerliste ist immer ein vollständiger
             * Snapshot des erfolgreichen Online-Abrufs.
             *
             * Deshalb werden alte Einträge des Accounts
             * vollständig entfernt.
             *
             * Dadurch verschwinden auch serverseitig
             * gelöschte Ordner zuverlässig aus dem Cache.
             */
            await DeleteFoldersAsync(
                connection,
                (SqliteTransaction)transaction,
                accountId,
                cancellationToken);

            for (var sortPosition = 0;
                 sortPosition < folders.Count;
                 sortPosition++)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var folder =
                    folders[
                        sortPosition];

                var serializedFolder =
                    JsonSerializer.Serialize(
                        folder,
                        SerializerOptions);

                await InsertFolderAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    accountId,
                    folder.FolderId.Trim(),
                    sortPosition,
                    serializedFolder,
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

    public async Task<IReadOnlyList<MailFolderData>>
        GetFoldersAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                FolderId,
                FolderJson
            FROM CachedMailboxFolders
            WHERE AccountId = $accountId
              AND CacheFormatVersion =
                    $cacheFormatVersion
            ORDER BY
                SortPosition ASC;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$cacheFormatVersion",
            CurrentCacheFormatVersion);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var folders =
            new List<MailFolderData>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var storedFolderId =
                reader.GetString(0);

            var serializedFolder =
                reader.GetString(1);

            MailFolderData?
                folder;

            try
            {
                folder =
                    JsonSerializer.Deserialize<
                        MailFolderData>(
                        serializedFolder,
                        SerializerOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Ein lokal zwischengespeicherter Mailordner ist beschädigt oder besitzt ein unbekanntes Format.",
                    exception);
            }

            if (folder is null)
            {
                throw new InvalidDataException(
                    "Ein lokal zwischengespeicherter Mailordner konnte nicht gelesen werden.");
            }

            if (string.IsNullOrWhiteSpace(
                    folder.FolderId))
            {
                throw new InvalidDataException(
                    "Ein lokal zwischengespeicherter Mailordner besitzt keine gültige Ordner-ID.");
            }

            /*
             * Die separat gespeicherte FolderId ist Teil des
             * relationalen Schlüssels.
             *
             * Sie muss deshalb exakt zum serialisierten
             * Datenobjekt passen.
             */
            if (!string.Equals(
                    storedFolderId,
                    folder.FolderId.Trim(),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Ein lokal zwischengespeicherter Mailordner besitzt eine widersprüchliche Ordner-ID.");
            }

            folders.Add(
                folder);
        }

        return folders;
    }

    private static async Task DeleteFoldersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            DELETE FROM CachedMailboxFolders
            WHERE AccountId = $accountId;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertFolderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid accountId,
        string folderId,
        int sortPosition,
        string serializedFolder,
        DateTimeOffset cachedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            INSERT INTO CachedMailboxFolders
            (
                AccountId,
                FolderId,
                SortPosition,
                CacheFormatVersion,
                FolderJson,
                CachedAtUtc
            )
            VALUES
            (
                $accountId,
                $folderId,
                $sortPosition,
                $cacheFormatVersion,
                $folderJson,
                $cachedAtUtc
            );
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$folderId",
            folderId);

        command.Parameters.AddWithValue(
            "$sortPosition",
            sortPosition);

        command.Parameters.AddWithValue(
            "$cacheFormatVersion",
            CurrentCacheFormatVersion);

        command.Parameters.AddWithValue(
            "$folderJson",
            serializedFolder);

        command.Parameters.AddWithValue(
            "$cachedAtUtc",
            cachedAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture));

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

                ForeignKeys =
                    true
            }.ToString();

        return new SqliteConnection(
            connectionString);
    }

    private static void ValidateFolders(
        IReadOnlyList<MailFolderData> folders)
    {
        var knownFolderIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var folder in folders)
        {
            ArgumentNullException.ThrowIfNull(
                folder);

            if (string.IsNullOrWhiteSpace(
                    folder.FolderId))
            {
                throw new ArgumentException(
                    "Ein zu cachender Mailordner besitzt keine gültige Ordner-ID.",
                    nameof(folders));
            }

            var normalizedFolderId =
                folder.FolderId.Trim();

            if (normalizedFolderId.Length >
                MaximumFolderIdLength)
            {
                throw new ArgumentException(
                    $"Die Ordner-ID darf maximal {MaximumFolderIdLength} Zeichen lang sein.",
                    nameof(folders));
            }

            if (!knownFolderIds.Add(
                    normalizedFolderId))
            {
                throw new ArgumentException(
                    $"Die Ordner-ID „{normalizedFolderId}“ ist innerhalb des Ordner-Snapshots mehrfach vorhanden.",
                    nameof(folders));
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
}