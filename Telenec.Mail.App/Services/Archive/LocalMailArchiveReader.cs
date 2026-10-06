using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;

namespace Telenec.Mail.App.Services.Archive;

public sealed class LocalMailArchiveReader
{
    private readonly LocalMailArchiveStorage
        _archiveStorage;

    public LocalMailArchiveReader(
        LocalMailArchiveStorage archiveStorage)
    {
        ArgumentNullException.ThrowIfNull(
            archiveStorage);

        _archiveStorage =
            archiveStorage;
    }

    public async Task<LocalMailArchiveSnapshot>
        LoadSnapshotAsync(
            CancellationToken cancellationToken = default)
    {
        /*
         * EnsureArchiveForActiveAccountAsync übernimmt
         * ausschließlich den bestehenden Archiv-Unterbau:
         *
         * - aktives Konto bestimmen
         * - Archivwurzel sicherstellen
         * - archive.db initialisieren
         * - Konto in archive.db registrieren
         *
         * Der Reader selbst verändert keine Archivnachrichten.
         */
        var archiveLocation =
            await _archiveStorage
                .EnsureArchiveForActiveAccountAsync(
                    cancellationToken);

        await using var connection =
            await OpenReadOnlyConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        var folders =
            await LoadFoldersAsync(
                connection,
                archiveLocation,
                cancellationToken);

        var messages =
            await LoadMessagesAsync(
                connection,
                archiveLocation,
                cancellationToken);

        return new LocalMailArchiveSnapshot(
            Location:
                archiveLocation,

            Folders:
                folders,

            Messages:
                messages);
    }

    private static async Task<
        IReadOnlyList<LocalMailArchiveFolderInfo>>
        LoadFoldersAsync(
            SqliteConnection connection,
            LocalMailArchiveLocation archiveLocation,
            CancellationToken cancellationToken)
    {
        var folders =
            new List<
                LocalMailArchiveFolderInfo>();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                f.ArchiveFolderId,
                f.SourceFolderId,
                f.RelativeFolderPath,
                f.DisplayName,
                COUNT(m.ArchiveMessageId)
            FROM ArchiveFolders AS f
            LEFT JOIN ArchiveMessages AS m
                ON m.ArchiveFolderId =
                    f.ArchiveFolderId
               AND m.AccountKey =
                    f.AccountKey
            WHERE f.AccountKey =
                    $accountKey
            GROUP BY
                f.ArchiveFolderId,
                f.SourceFolderId,
                f.RelativeFolderPath,
                f.DisplayName
            ORDER BY
                f.RelativeFolderPath COLLATE NOCASE;
            """;

        command.Parameters.AddWithValue(
            "$accountKey",
            archiveLocation.AccountKey);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            folders.Add(
                new LocalMailArchiveFolderInfo(
                    ArchiveFolderId:
                        reader.GetString(0),

                    SourceFolderId:
                        reader.GetString(1),

                    RelativeFolderPath:
                        reader.GetString(2),

                    DisplayName:
                        reader.GetString(3),

                    DirectMessageCount:
                        checked(
                            (int)reader.GetInt64(4))));
        }

        return folders;
    }

    private static async Task<
        IReadOnlyList<LocalMailArchiveMessageInfo>>
        LoadMessagesAsync(
            SqliteConnection connection,
            LocalMailArchiveLocation archiveLocation,
            CancellationToken cancellationToken)
    {
        var messages =
            new List<
                LocalMailArchiveMessageInfo>();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                m.ArchiveMessageId,
                m.ArchiveFolderId,
                m.SourceFolderId,
                m.SourceUidValidity,
                m.SourceUniqueId,
                m.MessageId,
                m.Subject,
                m.SenderAddress,
                m.SenderName,
                m.MessageDateUtc,
                m.ArchivedAtUtc,
                m.RelativeFilePath,
                m.Sha256,
                m.FileSizeBytes,
                m.ServerDeleted,
                m.ServerDeletedAtUtc,
                o.Status
            FROM ArchiveMessages AS m
            INNER JOIN ArchiveOperations AS o
                ON o.OperationId =
                    m.OperationId
               AND o.AccountKey =
                    m.AccountKey
            WHERE m.AccountKey =
                    $accountKey
            ORDER BY
                COALESCE(
                    m.MessageDateUtc,
                    m.ArchivedAtUtc
                ) DESC,
                m.ArchivedAtUtc DESC;
            """;

        command.Parameters.AddWithValue(
            "$accountKey",
            archiveLocation.AccountKey);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var relativeFilePath =
                reader.GetString(11);

            var expectedFileSize =
                reader.GetInt64(13);

            var localFile =
                InspectLocalArchiveFile(
                    archiveLocation.AccountDirectory,
                    relativeFilePath,
                    expectedFileSize);

            messages.Add(
                new LocalMailArchiveMessageInfo(
                    ArchiveMessageId:
                        reader.GetString(0),

                    ArchiveFolderId:
                        reader.GetString(1),

                    SourceFolderId:
                        reader.GetString(2),

                    SourceUidValidity:
                        checked(
                            (uint)reader.GetInt64(3)),

                    SourceUniqueId:
                        checked(
                            (uint)reader.GetInt64(4)),

                    MessageId:
                        GetNullableString(
                            reader,
                            5),

                    Subject:
                        reader.GetString(6),

                    SenderAddress:
                        GetNullableString(
                            reader,
                            7),

                    SenderName:
                        GetNullableString(
                            reader,
                            8),

                    MessageDateUtc:
                        ParseNullableDateTimeOffset(
                            reader,
                            9),

                    ArchivedAtUtc:
                        ParseRequiredDateTimeOffset(
                            reader.GetString(10),
                            "ArchivedAtUtc"),

                    RelativeFilePath:
                        relativeFilePath,

                    LocalFilePath:
                        localFile.FilePath,

                    LocalFileState:
                        localFile.State,

                    Sha256:
                        reader.GetString(12),

                    FileSizeBytes:
                        expectedFileSize,

                    ServerDeleted:
                        reader.GetInt64(14) != 0,

                    ServerDeletedAtUtc:
                        ParseNullableDateTimeOffset(
                            reader,
                            15),

                    OperationStatus:
                        reader.GetString(16)));
        }

        return messages;
    }

    private static LocalMailArchiveFileInspection
        InspectLocalArchiveFile(
            string accountDirectory,
            string relativeFilePath,
            long expectedFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(
                accountDirectory) ||
            string.IsNullOrWhiteSpace(
                relativeFilePath))
        {
            return new LocalMailArchiveFileInspection(
                State:
                    LocalMailArchiveFileState
                        .InvalidPath,

                FilePath:
                    null);
        }

        try
        {
            var accountRoot =
                Path.GetFullPath(
                        accountDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            var candidatePath =
                Path.GetFullPath(
                    Path.Combine(
                        accountDirectory,
                        relativeFilePath));

            /*
             * archive.db darf niemals auf Dateien außerhalb
             * des kontobezogenen Archivordners verweisen.
             */
            if (!candidatePath.StartsWith(
                    accountRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new LocalMailArchiveFileInspection(
                    State:
                        LocalMailArchiveFileState
                            .InvalidPath,

                    FilePath:
                        null);
            }

            if (!File.Exists(
                    candidatePath))
            {
                return new LocalMailArchiveFileInspection(
                    State:
                        LocalMailArchiveFileState
                            .Missing,

                    FilePath:
                        candidatePath);
            }

            var fileInfo =
                new FileInfo(
                    candidatePath);

            if (fileInfo.Length !=
                expectedFileSizeBytes)
            {
                return new LocalMailArchiveFileInspection(
                    State:
                        LocalMailArchiveFileState
                            .SizeMismatch,

                    FilePath:
                        candidatePath);
            }

            return new LocalMailArchiveFileInspection(
                State:
                    LocalMailArchiveFileState
                        .Available,

                FilePath:
                    candidatePath);
        }
        catch
        {
            /*
             * Ein ungültiger oder manipulierter Dateipfad
             * darf die gesamte Archivübersicht nicht
             * unbrauchbar machen.
             *
             * Die betreffende Nachricht wird später in der
             * Oberfläche stattdessen sichtbar als fehlerhaft
             * gekennzeichnet.
             */
            return new LocalMailArchiveFileInspection(
                State:
                    LocalMailArchiveFileState
                        .InvalidPath,

                FilePath:
                    null);
        }
    }

    private static async Task<SqliteConnection>
        OpenReadOnlyConnectionAsync(
            string databasePath,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                databasePath))
        {
            throw new ArgumentException(
                "Der Pfad zur Archivdatenbank darf nicht leer sein.",
                nameof(databasePath));
        }

        if (!File.Exists(
                databasePath))
        {
            throw new FileNotFoundException(
                "Die lokale Archivdatenbank wurde nicht gefunden.",
                databasePath);
        }

        /*
         * WICHTIG:
         *
         * Der Archivbrowser ist ein reiner Snapshot-Reader.
         * Er benötigt keinen SQLite Shared Cache.
         *
         * Eine gepoolte ReadOnly-Verbindung mit Shared Cache
         * kann später mit einer schreibenden Restore-
         * Verbindung kollidieren und zu
         *
         * "attempt to write a readonly database"
         *
         * führen.
         *
         * Deshalb verwenden wir hier bewusst:
         *
         * - ReadOnly
         * - Private Cache
         * - kein Connection Pooling
         *
         * Nach dem Snapshot bleibt damit kein ReadOnly-
         * Datenbankhandle für spätere Schreiboperationen
         * zurück.
         */
        var connectionStringBuilder =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    databasePath,

                Mode =
                    SqliteOpenMode.ReadOnly,

                Cache =
                    SqliteCacheMode.Private,

                Pooling =
                    false
            };

        var connection =
            new SqliteConnection(
                connectionStringBuilder
                    .ToString());

        try
        {
            await connection.OpenAsync(
                cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }
    }

    private static string?
        GetNullableString(
            SqliteDataReader reader,
            int ordinal)
    {
        return reader.IsDBNull(
            ordinal)
            ? null
            : reader.GetString(
                ordinal);
    }

    private static DateTimeOffset?
        ParseNullableDateTimeOffset(
            SqliteDataReader reader,
            int ordinal)
    {
        if (reader.IsDBNull(
                ordinal))
        {
            return null;
        }

        var value =
            reader.GetString(
                ordinal);

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedValue))
        {
            return null;
        }

        return parsedValue;
    }

    private static DateTimeOffset
        ParseRequiredDateTimeOffset(
            string value,
            string columnName)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedValue))
        {
            return parsedValue;
        }

        throw new InvalidDataException(
            $"Die Archivdatenbank enthält für „{columnName}“ keinen gültigen Zeitwert.");
    }

    private sealed record
        LocalMailArchiveFileInspection(
            LocalMailArchiveFileState State,
            string? FilePath);
}

public sealed record LocalMailArchiveSnapshot(
    LocalMailArchiveLocation Location,
    IReadOnlyList<LocalMailArchiveFolderInfo> Folders,
    IReadOnlyList<LocalMailArchiveMessageInfo> Messages)
{
    public int FolderCount =>
        Folders.Count;

    public int MessageCount =>
        Messages.Count;

    public int MissingFileCount =>
        Messages.Count(
            message =>
                message.LocalFileState !=
                LocalMailArchiveFileState.Available);
}

public sealed record LocalMailArchiveFolderInfo(
    string ArchiveFolderId,
    string SourceFolderId,
    string RelativeFolderPath,
    string DisplayName,
    int DirectMessageCount);

public sealed record LocalMailArchiveMessageInfo(
    string ArchiveMessageId,
    string ArchiveFolderId,
    string SourceFolderId,
    uint SourceUidValidity,
    uint SourceUniqueId,
    string? MessageId,
    string Subject,
    string? SenderAddress,
    string? SenderName,
    DateTimeOffset? MessageDateUtc,
    DateTimeOffset ArchivedAtUtc,
    string RelativeFilePath,
    string? LocalFilePath,
    LocalMailArchiveFileState LocalFileState,
    string Sha256,
    long FileSizeBytes,
    bool ServerDeleted,
    DateTimeOffset? ServerDeletedAtUtc,
    string OperationStatus);

public enum LocalMailArchiveFileState
{
    Available = 0,
    Missing = 1,
    SizeMismatch = 2,
    InvalidPath = 3
}