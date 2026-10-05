using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Archive;

public sealed class LocalMailArchiveStorage
{
    private const int CurrentSchemaVersion =
        1;

    private const string ArchiveDirectoryName =
        "Telenec Mail Archiv";

    private const string ArchiveDatabaseFileName =
        "archive.db";

    private readonly IMailAccountStore
        _mailAccountStore;

    public LocalMailArchiveStorage(
        IMailAccountStore mailAccountStore)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        _mailAccountStore =
            mailAccountStore;
    }

    public async Task<LocalMailArchiveLocation>
        EnsureArchiveForActiveAccountAsync(
            CancellationToken cancellationToken = default)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null ||
            string.IsNullOrWhiteSpace(
                account.EmailAddress))
        {
            throw new InvalidOperationException(
                "Es ist kein aktives Telenec-Mail-Konto vorhanden.");
        }

        var emailAddress =
            account.EmailAddress.Trim();

        var accountKey =
            NormalizeAccountKey(
                emailAddress);

        var documentsDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder
                    .MyDocuments);

        if (string.IsNullOrWhiteSpace(
                documentsDirectory))
        {
            throw new InvalidOperationException(
                "Der Dokumente-Ordner dieses Windows-Benutzers konnte nicht ermittelt werden.");
        }

        var archiveRootDirectory =
            Path.Combine(
                documentsDirectory,
                ArchiveDirectoryName);

        var databasePath =
            Path.Combine(
                archiveRootDirectory,
                ArchiveDatabaseFileName);

        var accountDirectoryName =
            CreateSafeAccountDirectoryName(
                emailAddress);

        var accountDirectory =
            Path.Combine(
                archiveRootDirectory,
                accountDirectoryName);

        Directory.CreateDirectory(
            archiveRootDirectory);

        Directory.CreateDirectory(
            accountDirectory);

        await InitializeDatabaseAsync(
            databasePath,
            cancellationToken);

        await UpsertArchiveAccountAsync(
            databasePath,
            accountKey,
            emailAddress,
            accountDirectoryName,
            cancellationToken);

        return new LocalMailArchiveLocation(
            AccountKey:
                accountKey,

            EmailAddress:
                emailAddress,

            ArchiveRootDirectory:
                archiveRootDirectory,

            DatabasePath:
                databasePath,

            AccountDirectory:
                accountDirectory,

            AccountDirectoryName:
                accountDirectoryName);
    }

    private static async Task
        InitializeDatabaseAsync(
            string databasePath,
            CancellationToken cancellationToken)
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    databasePath,

                Mode =
                    SqliteOpenMode
                        .ReadWriteCreate
            }
            .ToString();

        await using var connection =
            new SqliteConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await EnableForeignKeysAsync(
            connection,
            cancellationToken);

        var schemaVersion =
            await GetSchemaVersionAsync(
                connection,
                cancellationToken);

        if (schemaVersion >
            CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                "Das lokale Mailarchiv verwendet eine neuere " +
                "Datenbankversion und kann mit dieser Version " +
                "von Telenec Mail nicht geöffnet werden.");
        }

        if (schemaVersion == 0)
        {
            await CreateSchemaVersion1Async(
                connection,
                cancellationToken);

            schemaVersion =
                1;
        }

        if (schemaVersion !=
            CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                "Das lokale Mailarchiv konnte nicht auf eine " +
                "unterstützte Datenbankversion aktualisiert werden.");
        }
    }

    private static async Task
        EnableForeignKeysAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA foreign_keys = ON;";

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task<int>
        GetSchemaVersionAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA user_version;";

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Convert.ToInt32(
            result,
            CultureInfo.InvariantCulture);
    }

    private static async Task
        CreateSchemaVersion1Async(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection
                .BeginTransactionAsync(
                    cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            (SqliteTransaction)transaction;

        command.CommandText =
            """
            CREATE TABLE ArchiveAccounts
            (
                AccountKey TEXT NOT NULL
                    COLLATE NOCASE
                    PRIMARY KEY,

                EmailAddress TEXT NOT NULL,

                AccountDirectoryName TEXT NOT NULL,

                CreatedAtUtc TEXT NOT NULL,

                LastUsedAtUtc TEXT NOT NULL
            );

            CREATE TABLE ArchiveFolders
            (
                ArchiveFolderId TEXT NOT NULL
                    PRIMARY KEY,

                AccountKey TEXT NOT NULL
                    COLLATE NOCASE,

                SourceFolderId TEXT NOT NULL,

                RelativeFolderPath TEXT NOT NULL,

                DisplayName TEXT NOT NULL,

                CreatedAtUtc TEXT NOT NULL,

                FOREIGN KEY
                (
                    AccountKey
                )
                REFERENCES ArchiveAccounts
                (
                    AccountKey
                )
                ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX
                UX_ArchiveFolders_Path
                ON ArchiveFolders
                (
                    AccountKey,
                    RelativeFolderPath COLLATE NOCASE
                );

            CREATE INDEX
                IX_ArchiveFolders_Source
                ON ArchiveFolders
                (
                    AccountKey,
                    SourceFolderId
                );

            CREATE TABLE ArchiveOperations
            (
                OperationId TEXT NOT NULL
                    PRIMARY KEY,

                AccountKey TEXT NOT NULL
                    COLLATE NOCASE,

                SourceFolderId TEXT NOT NULL,

                StartedAtUtc TEXT NOT NULL,

                LocalStorageCompletedAtUtc TEXT NULL,

                ServerDeletionCompletedAtUtc TEXT NULL,

                Status TEXT NOT NULL
                    CHECK
                    (
                        Status IN
                        (
                            'Preparing',
                            'LocalStored',
                            'Completed',
                            'Failed'
                        )
                    ),

                MessageCount INTEGER NOT NULL
                    CHECK
                    (
                        MessageCount >= 0
                    ),

                ErrorMessage TEXT NULL,

                FOREIGN KEY
                (
                    AccountKey
                )
                REFERENCES ArchiveAccounts
                (
                    AccountKey
                )
                ON DELETE CASCADE
            );

            CREATE INDEX
                IX_ArchiveOperations_Account
                ON ArchiveOperations
                (
                    AccountKey,
                    StartedAtUtc
                );

            CREATE TABLE ArchiveMessages
            (
                ArchiveMessageId TEXT NOT NULL
                    PRIMARY KEY,

                OperationId TEXT NOT NULL,

                AccountKey TEXT NOT NULL
                    COLLATE NOCASE,

                ArchiveFolderId TEXT NOT NULL,

                SourceFolderId TEXT NOT NULL,

                SourceUidValidity INTEGER NOT NULL
                    CHECK
                    (
                        SourceUidValidity > 0
                    ),

                SourceUniqueId INTEGER NOT NULL
                    CHECK
                    (
                        SourceUniqueId > 0
                    ),

                MessageId TEXT NULL,

                Subject TEXT NOT NULL,

                SenderAddress TEXT NULL,

                SenderName TEXT NULL,

                MessageDateUtc TEXT NULL,

                ArchivedAtUtc TEXT NOT NULL,

                RelativeFilePath TEXT NOT NULL,

                Sha256 TEXT NOT NULL,

                FileSizeBytes INTEGER NOT NULL
                    CHECK
                    (
                        FileSizeBytes >= 0
                    ),

                ServerDeleted INTEGER NOT NULL
                    DEFAULT 0
                    CHECK
                    (
                        ServerDeleted IN
                        (
                            0,
                            1
                        )
                    ),

                ServerDeletedAtUtc TEXT NULL,

                FOREIGN KEY
                (
                    OperationId
                )
                REFERENCES ArchiveOperations
                (
                    OperationId
                )
                ON DELETE CASCADE,

                FOREIGN KEY
                (
                    AccountKey
                )
                REFERENCES ArchiveAccounts
                (
                    AccountKey
                )
                ON DELETE CASCADE,

                FOREIGN KEY
                (
                    ArchiveFolderId
                )
                REFERENCES ArchiveFolders
                (
                    ArchiveFolderId
                )
                ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX
                UX_ArchiveMessages_SourceIdentity
                ON ArchiveMessages
                (
                    AccountKey,
                    SourceFolderId,
                    SourceUidValidity,
                    SourceUniqueId
                );

            CREATE UNIQUE INDEX
                UX_ArchiveMessages_FilePath
                ON ArchiveMessages
                (
                    AccountKey,
                    RelativeFilePath COLLATE NOCASE
                );

            CREATE INDEX
                IX_ArchiveMessages_MessageId
                ON ArchiveMessages
                (
                    AccountKey,
                    MessageId
                );

            CREATE INDEX
                IX_ArchiveMessages_ArchivedAt
                ON ArchiveMessages
                (
                    AccountKey,
                    ArchivedAtUtc
                );

            PRAGMA user_version = 1;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task
        UpsertArchiveAccountAsync(
            string databasePath,
            string accountKey,
            string emailAddress,
            string accountDirectoryName,
            CancellationToken cancellationToken)
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    databasePath,

                Mode =
                    SqliteOpenMode
                        .ReadWriteCreate
            }
            .ToString();

        await using var connection =
            new SqliteConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await EnableForeignKeysAsync(
            connection,
            cancellationToken);

        var nowUtc =
            DateTimeOffset
                .UtcNow
                .ToString(
                    "O",
                    CultureInfo.InvariantCulture);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO ArchiveAccounts
            (
                AccountKey,
                EmailAddress,
                AccountDirectoryName,
                CreatedAtUtc,
                LastUsedAtUtc
            )
            VALUES
            (
                $accountKey,
                $emailAddress,
                $accountDirectoryName,
                $createdAtUtc,
                $lastUsedAtUtc
            )
            ON CONFLICT(AccountKey)
            DO UPDATE SET
                EmailAddress =
                    excluded.EmailAddress,

                AccountDirectoryName =
                    excluded.AccountDirectoryName,

                LastUsedAtUtc =
                    excluded.LastUsedAtUtc;
            """;

        command.Parameters.AddWithValue(
            "$accountKey",
            accountKey);

        command.Parameters.AddWithValue(
            "$emailAddress",
            emailAddress);

        command.Parameters.AddWithValue(
            "$accountDirectoryName",
            accountDirectoryName);

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            nowUtc);

        command.Parameters.AddWithValue(
            "$lastUsedAtUtc",
            nowUtc);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static string NormalizeAccountKey(
        string emailAddress)
    {
        return emailAddress
            .Trim()
            .ToLowerInvariant();
    }

    private static string
        CreateSafeAccountDirectoryName(
            string emailAddress)
    {
        var invalidCharacters =
            Path.GetInvalidFileNameChars()
                .ToHashSet();

        var builder =
            new StringBuilder();

        foreach (var character in
                 emailAddress.Trim())
        {
            builder.Append(
                invalidCharacters.Contains(
                    character)
                    ? '_'
                    : character);
        }

        var directoryName =
            builder
                .ToString()
                .Trim()
                .TrimEnd(
                    '.');

        if (string.IsNullOrWhiteSpace(
                directoryName))
        {
            directoryName =
                "Mailkonto";
        }

        const int maximumVisibleLength =
            100;

        if (directoryName.Length <=
            maximumVisibleLength)
        {
            return directoryName;
        }

        var normalizedEmailAddress =
            NormalizeAccountKey(
                emailAddress);

        var hashBytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    normalizedEmailAddress));

        var shortHash =
            Convert.ToHexString(
                    hashBytes)
                [..12]
                .ToLowerInvariant();

        return
            directoryName[..80] +
            "-" +
            shortHash;
    }
}

public sealed record LocalMailArchiveLocation(
    string AccountKey,
    string EmailAddress,
    string ArchiveRootDirectory,
    string DatabasePath,
    string AccountDirectory,
    string AccountDirectoryName);