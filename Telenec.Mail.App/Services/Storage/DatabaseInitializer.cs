using Microsoft.Data.Sqlite;
using System.IO;

namespace Telenec.Mail.App.Services.Storage;

public sealed class DatabaseInitializer
{
    private const int CurrentSchemaVersion = 7;

    private readonly AppDataPaths _paths;

    public DatabaseInitializer(
        AppDataPaths paths)
    {
        _paths =
            paths;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(
            _paths.RootDirectory);

        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _paths.DatabasePath,

                Mode =
                    SqliteOpenMode.ReadWriteCreate
            }.ToString();

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
                $"Die lokale Datenbank verwendet Schema-Version " +
                $"{schemaVersion}, diese Anwendung unterstützt jedoch " +
                $"maximal Version {CurrentSchemaVersion}.");
        }

        if (schemaVersion == 0)
        {
            await CreateSchemaVersion1Async(
                connection,
                cancellationToken);

            schemaVersion =
                1;
        }

        if (schemaVersion == 1)
        {
            await UpgradeToSchemaVersion2Async(
                connection,
                cancellationToken);

            schemaVersion =
                2;
        }

        if (schemaVersion == 2)
        {
            await UpgradeToSchemaVersion3Async(
                connection,
                cancellationToken);

            schemaVersion =
                3;
        }

        if (schemaVersion == 3)
        {
            await UpgradeToSchemaVersion4Async(
                connection,
                cancellationToken);

            schemaVersion =
                4;
        }

        if (schemaVersion == 4)
        {
            await UpgradeToSchemaVersion5Async(
                connection,
                cancellationToken);

            schemaVersion =
                5;
        }

        if (schemaVersion == 5)
        {
            await UpgradeToSchemaVersion6Async(
                connection,
                cancellationToken);

            schemaVersion =
                6;
        }

        if (schemaVersion == 6)
        {
            await UpgradeToSchemaVersion7Async(
                connection,
                cancellationToken);

            schemaVersion =
                7;
        }

        if (schemaVersion !=
            CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Die lokale Datenbank konnte nicht auf Schema-Version " +
                $"{CurrentSchemaVersion} aktualisiert werden.");
        }
    }

    private static async Task EnableForeignKeysAsync(
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

    private static async Task<int> GetSchemaVersionAsync(
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
            result);
    }

    private static async Task CreateSchemaVersion1Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            (SqliteTransaction)transaction;

        command.CommandText =
            """
            CREATE TABLE Accounts
            (
                AccountId TEXT NOT NULL PRIMARY KEY,
                EmailAddress TEXT NOT NULL COLLATE NOCASE,
                DisplayName TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 0
                    CHECK (IsActive IN (0, 1)),
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX UX_Accounts_EmailAddress
                ON Accounts(EmailAddress);

            CREATE UNIQUE INDEX UX_Accounts_Active
                ON Accounts(IsActive)
                WHERE IsActive = 1;

            PRAGMA user_version = 1;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task UpgradeToSchemaVersion2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            (SqliteTransaction)transaction;

        command.CommandText =
            """
            CREATE TABLE ExternalImagePermissions
            (
                AccountId TEXT NOT NULL,
                MessageKey TEXT NOT NULL,
                AllowedAtUtc TEXT NOT NULL,

                PRIMARY KEY
                (
                    AccountId,
                    MessageKey
                )
            );

            PRAGMA user_version = 2;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task UpgradeToSchemaVersion3Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            (SqliteTransaction)transaction;

        command.CommandText =
            """
            CREATE TABLE ReadReceipts
            (
                AccountId TEXT NOT NULL,

                OriginalMessageId TEXT NOT NULL,

                ReceiptSenderAddress TEXT NOT NULL
                    COLLATE NOCASE,

                ReceiptSenderName TEXT NULL,

                ReceiptAtUtc TEXT NOT NULL,

                Disposition TEXT NOT NULL,

                RecordedAtUtc TEXT NOT NULL,

                PRIMARY KEY
                (
                    AccountId,
                    OriginalMessageId,
                    ReceiptSenderAddress
                ),

                FOREIGN KEY
                (
                    AccountId
                )
                REFERENCES Accounts(AccountId)
                ON DELETE CASCADE
            );

            CREATE INDEX IX_ReadReceipts_OriginalMessage
                ON ReadReceipts
                (
                    AccountId,
                    OriginalMessageId
                );

            PRAGMA user_version = 3;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task UpgradeToSchemaVersion4Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            (SqliteTransaction)transaction;

        command.CommandText =
            """
            CREATE TABLE ApplicationSettings
            (
                SettingKey TEXT NOT NULL PRIMARY KEY,
                SettingValue TEXT NOT NULL
            );

            CREATE TABLE AccountSettings
            (
                AccountId TEXT NOT NULL,
                SettingKey TEXT NOT NULL,
                SettingValue TEXT NOT NULL,

                PRIMARY KEY
                (
                    AccountId,
                    SettingKey
                ),

                FOREIGN KEY
                (
                    AccountId
                )
                REFERENCES Accounts(AccountId)
                ON DELETE CASCADE
            );

            PRAGMA user_version = 4;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task UpgradeToSchemaVersion5Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            (SqliteTransaction)transaction;

        command.CommandText =
            """
            CREATE TABLE CachedMailFolders
            (
                AccountId TEXT NOT NULL,
                FolderId TEXT NOT NULL,
                UidValidity INTEGER NOT NULL
                    CHECK (UidValidity > 0),
                UpdatedAtUtc TEXT NOT NULL,

                PRIMARY KEY
                (
                    AccountId,
                    FolderId
                ),

                FOREIGN KEY
                (
                    AccountId
                )
                REFERENCES Accounts(AccountId)
                ON DELETE CASCADE
            );

            CREATE TABLE CachedMailMessages
            (
                AccountId TEXT NOT NULL,
                FolderId TEXT NOT NULL,
                UidValidity INTEGER NOT NULL
                    CHECK (UidValidity > 0),
                UniqueId INTEGER NOT NULL
                    CHECK (UniqueId > 0),
                CacheFormatVersion INTEGER NOT NULL
                    CHECK (CacheFormatVersion > 0),
                MessageJson TEXT NOT NULL,
                CachedAtUtc TEXT NOT NULL,

                PRIMARY KEY
                (
                    AccountId,
                    FolderId,
                    UniqueId
                ),

                FOREIGN KEY
                (
                    AccountId,
                    FolderId
                )
                REFERENCES CachedMailFolders
                (
                    AccountId,
                    FolderId
                )
                ON DELETE CASCADE
            );

            CREATE INDEX IX_CachedMailMessages_Page
                ON CachedMailMessages
                (
                    AccountId,
                    FolderId,
                    UniqueId DESC
                );

            PRAGMA user_version = 5;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task UpgradeToSchemaVersion6Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            (SqliteTransaction)transaction;

        command.CommandText =
            """
            ALTER TABLE CachedMailMessages
                ADD COLUMN SortPosition INTEGER NULL
                    CHECK
                    (
                        SortPosition IS NULL
                        OR SortPosition >= 0
                    );

            CREATE INDEX IX_CachedMailMessages_SortPosition
                ON CachedMailMessages
                (
                    AccountId,
                    FolderId,
                    SortPosition
                );

            PRAGMA user_version = 6;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task UpgradeToSchemaVersion7Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            (SqliteTransaction)transaction;

        /*
         * Diese Tabelle bildet bewusst die komplette zuletzt
         * erfolgreich vom Server gelieferte Ordnerliste ab.
         *
         * Sie ist getrennt von CachedMailFolders.
         *
         * CachedMailFolders beschreibt die IMAP-Identität
         * eines Ordners mit UIDVALIDITY für gecachte
         * Nachrichten.
         *
         * CachedMailboxFolders dagegen beschreibt ausschließlich
         * die sichtbare Ordnerstruktur des Postfachs.
         */
        command.CommandText =
            """
            CREATE TABLE CachedMailboxFolders
            (
                AccountId TEXT NOT NULL,
                FolderId TEXT NOT NULL,
                SortPosition INTEGER NOT NULL
                    CHECK (SortPosition >= 0),
                CacheFormatVersion INTEGER NOT NULL
                    CHECK (CacheFormatVersion > 0),
                FolderJson TEXT NOT NULL,
                CachedAtUtc TEXT NOT NULL,

                PRIMARY KEY
                (
                    AccountId,
                    FolderId
                ),

                FOREIGN KEY
                (
                    AccountId
                )
                REFERENCES Accounts(AccountId)
                ON DELETE CASCADE
            );

            CREATE INDEX IX_CachedMailboxFolders_Order
                ON CachedMailboxFolders
                (
                    AccountId,
                    SortPosition
                );

            PRAGMA user_version = 7;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }
}