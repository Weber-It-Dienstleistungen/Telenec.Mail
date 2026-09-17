using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Telenec.Mail.App.Services.Storage;

public sealed class SqliteExternalImagePermissionStore
    : IExternalImagePermissionStore
{
    private readonly AppDataPaths _paths;

    public SqliteExternalImagePermissionStore(
        AppDataPaths paths)
    {
        _paths =
            paths;
    }

    public async Task<bool> IsAllowedAsync(
        Guid accountId,
        string messageKey,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(
            accountId,
            messageKey);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT 1
            FROM ExternalImagePermissions
            WHERE AccountId = $accountId
              AND MessageKey = $messageKey
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$messageKey",
            messageKey.Trim());

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is not null;
    }

    public async Task AllowAsync(
        Guid accountId,
        string messageKey,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(
            accountId,
            messageKey);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO ExternalImagePermissions
            (
                AccountId,
                MessageKey,
                AllowedAtUtc
            )
            VALUES
            (
                $accountId,
                $messageKey,
                $allowedAtUtc
            )
            ON CONFLICT(AccountId, MessageKey)
            DO UPDATE SET
                AllowedAtUtc = excluded.AllowedAtUtc;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$messageKey",
            messageKey.Trim());

        command.Parameters.AddWithValue(
            "$allowedAtUtc",
            DateTime.UtcNow.ToString(
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
                    SqliteOpenMode.ReadWrite
            }.ToString();

        return new SqliteConnection(
            connectionString);
    }

    private static void ValidateArguments(
        Guid accountId,
        string messageKey)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(
                messageKey))
        {
            throw new ArgumentException(
                "Der Nachrichtenschlüssel darf nicht leer sein.",
                nameof(messageKey));
        }
    }
}