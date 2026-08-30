using Microsoft.Data.Sqlite;
using System.Globalization;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public sealed class SqliteMailAccountStore : IMailAccountStore
{
    private readonly AppDataPaths _paths;

    public SqliteMailAccountStore(AppDataPaths paths)
    {
        _paths = paths;
    }

    public async Task<MailAccount?> GetActiveAccountAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                AccountId,
                EmailAddress,
                DisplayName,
                IsActive,
                CreatedAtUtc
            FROM Accounts
            WHERE IsActive = 1
            LIMIT 1;
            """;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var accountIdText =
            reader.GetString(0);

        var emailAddress =
            reader.GetString(1);

        var displayName =
            reader.IsDBNull(2)
                ? null
                : reader.GetString(2);

        var isActive =
            reader.GetInt32(3) == 1;

        var createdAtUtcText =
            reader.GetString(4);

        if (!Guid.TryParse(
                accountIdText,
                out var accountId))
        {
            throw new InvalidOperationException(
                "Die gespeicherte Account-ID ist ungültig.");
        }

        if (!DateTime.TryParse(
                createdAtUtcText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAtUtc))
        {
            throw new InvalidOperationException(
                "Das gespeicherte Erstellungsdatum des Accounts ist ungültig.");
        }

        return new MailAccount
        {
            AccountId = accountId,
            EmailAddress = emailAddress,
            DisplayName = displayName,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc
        };
    }

    public async Task SaveAsync(
        MailAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.AccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(account));
        }

        if (string.IsNullOrWhiteSpace(
                account.EmailAddress))
        {
            throw new ArgumentException(
                "Die E-Mail-Adresse darf nicht leer sein.",
                nameof(account));
        }

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        if (account.IsActive)
        {
            await using var deactivateCommand =
                connection.CreateCommand();

            deactivateCommand.Transaction =
                (SqliteTransaction)transaction;

            deactivateCommand.CommandText =
                """
                UPDATE Accounts
                SET IsActive = 0
                WHERE AccountId <> $accountId;
                """;

            deactivateCommand.Parameters.AddWithValue(
                "$accountId",
                account.AccountId.ToString("D"));

            await deactivateCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        await using var saveCommand =
            connection.CreateCommand();

        saveCommand.Transaction =
            (SqliteTransaction)transaction;

        saveCommand.CommandText =
            """
            INSERT INTO Accounts
            (
                AccountId,
                EmailAddress,
                DisplayName,
                IsActive,
                CreatedAtUtc
            )
            VALUES
            (
                $accountId,
                $emailAddress,
                $displayName,
                $isActive,
                $createdAtUtc
            )
            ON CONFLICT(AccountId)
            DO UPDATE SET
                EmailAddress = excluded.EmailAddress,
                DisplayName = excluded.DisplayName,
                IsActive = excluded.IsActive;
            """;

        saveCommand.Parameters.AddWithValue(
            "$accountId",
            account.AccountId.ToString("D"));

        saveCommand.Parameters.AddWithValue(
            "$emailAddress",
            account.EmailAddress.Trim());

        saveCommand.Parameters.AddWithValue(
            "$displayName",
            string.IsNullOrWhiteSpace(account.DisplayName)
                ? DBNull.Value
                : account.DisplayName.Trim());

        saveCommand.Parameters.AddWithValue(
            "$isActive",
            account.IsActive ? 1 : 0);

        saveCommand.Parameters.AddWithValue(
            "$createdAtUtc",
            account.CreatedAtUtc
                .ToUniversalTime()
                .ToString(
                    "O",
                    CultureInfo.InvariantCulture));

        await saveCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task DeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(accountId));
        }

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            DELETE FROM Accounts
            WHERE AccountId = $accountId;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = _paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWrite
            }.ToString();

        return new SqliteConnection(
            connectionString);
    }
}