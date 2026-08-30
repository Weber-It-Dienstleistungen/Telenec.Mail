using Microsoft.Data.Sqlite;
using System.IO;

namespace Telenec.Mail.App.Services.Storage;

public sealed class DatabaseInitializer
{
    private const int CurrentSchemaVersion = 1;

    private readonly AppDataPaths _paths;

    public DatabaseInitializer(AppDataPaths paths)
    {
        _paths = paths;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.RootDirectory);

        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = _paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

        await using var connection =
            new SqliteConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        await EnableForeignKeysAsync(
            connection,
            cancellationToken);

        var schemaVersion =
            await GetSchemaVersionAsync(
                connection,
                cancellationToken);

        if (schemaVersion > CurrentSchemaVersion)
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

        return Convert.ToInt32(result);
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
}