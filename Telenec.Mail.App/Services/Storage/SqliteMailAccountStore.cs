using Microsoft.Data.Sqlite;
using System.Globalization;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public sealed class SqliteMailAccountStore : IMailAccountStore
{
    private readonly AppDataPaths _paths;

    public SqliteMailAccountStore(
        AppDataPaths paths)
    {
        _paths =
            paths;
    }

    public async Task<IReadOnlyList<MailAccount>> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

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
            ORDER BY
                CreatedAtUtc,
                EmailAddress COLLATE NOCASE;
            """;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var accounts =
            new List<MailAccount>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            accounts.Add(
                ReadAccount(reader));
        }

        return accounts;
    }

    public async Task<MailAccount?> GetAccountByEmailAddressAsync(
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                emailAddress))
        {
            throw new ArgumentException(
                "Die E-Mail-Adresse darf nicht leer sein.",
                nameof(emailAddress));
        }

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

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
            WHERE EmailAddress = $emailAddress
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$emailAddress",
            emailAddress.Trim());

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadAccount(
            reader);
    }

    public async Task<MailAccount?> GetActiveAccountAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

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

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadAccount(
            reader);
    }

    public async Task SetActiveAccountAsync(
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

        await connection.OpenAsync(
            cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var existsCommand =
            connection.CreateCommand();

        existsCommand.Transaction =
            (SqliteTransaction)transaction;

        existsCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM Accounts
            WHERE AccountId = $accountId;
            """;

        existsCommand.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        var existingAccountCount =
            Convert.ToInt32(
                await existsCommand.ExecuteScalarAsync(
                    cancellationToken));

        if (existingAccountCount != 1)
        {
            throw new InvalidOperationException(
                "Das zu aktivierende E-Mail-Konto wurde nicht gefunden.");
        }

        await using var deactivateCommand =
            connection.CreateCommand();

        deactivateCommand.Transaction =
            (SqliteTransaction)transaction;

        deactivateCommand.CommandText =
            """
            UPDATE Accounts
            SET IsActive = 0
            WHERE IsActive = 1;
            """;

        await deactivateCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await using var activateCommand =
            connection.CreateCommand();

        activateCommand.Transaction =
            (SqliteTransaction)transaction;

        activateCommand.CommandText =
            """
            UPDATE Accounts
            SET IsActive = 1
            WHERE AccountId = $accountId;
            """;

        activateCommand.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        var affectedRows =
            await activateCommand.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "Das E-Mail-Konto konnte nicht aktiviert werden.");
        }

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task SaveAsync(
        MailAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            account);

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

        await connection.OpenAsync(
            cancellationToken);

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
            string.IsNullOrWhiteSpace(
                account.DisplayName)
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

        await connection.OpenAsync(
            cancellationToken);

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

    private static MailAccount ReadAccount(
        SqliteDataReader reader)
    {
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
            AccountId =
                accountId,

            EmailAddress =
                emailAddress,

            DisplayName =
                displayName,

            IsActive =
                isActive,

            CreatedAtUtc =
                createdAtUtc
        };
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
                 * Foreign-Key-Verhalten ist bei SQLite
                 * verbindungsbezogen.
                 *
                 * Besonders wichtig ist dies beim Löschen
                 * eines Accounts, damit abhängige lokale
                 * Datensätze mit ON DELETE CASCADE ebenfalls
                 * entfernt werden.
                 */
                ForeignKeys =
                    true
            }.ToString();

        return new SqliteConnection(
            connectionString);
    }
}