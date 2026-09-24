using Microsoft.Data.Sqlite;

namespace Telenec.Mail.App.Services.Storage;

public sealed class SqliteSettingsStore
    : ISettingsStore
{
    private readonly AppDataPaths _paths;

    public SqliteSettingsStore(
        AppDataPaths paths)
    {
        _paths =
            paths;
    }

    public async Task<string?> GetApplicationSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(
            key);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT SettingValue
            FROM ApplicationSettings
            WHERE SettingKey = $settingKey
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$settingKey",
            key.Trim());

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is null ||
               result == DBNull.Value
            ? null
            : (string)result;
    }

    public async Task SetApplicationSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(
            key);

        ArgumentNullException.ThrowIfNull(
            value);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO ApplicationSettings
            (
                SettingKey,
                SettingValue
            )
            VALUES
            (
                $settingKey,
                $settingValue
            )
            ON CONFLICT(SettingKey)
            DO UPDATE SET
                SettingValue = excluded.SettingValue;
            """;

        command.Parameters.AddWithValue(
            "$settingKey",
            key.Trim());

        command.Parameters.AddWithValue(
            "$settingValue",
            value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<string?> GetAccountSettingAsync(
        Guid accountId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        ValidateKey(
            key);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT SettingValue
            FROM AccountSettings
            WHERE AccountId = $accountId
              AND SettingKey = $settingKey
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$settingKey",
            key.Trim());

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is null ||
               result == DBNull.Value
            ? null
            : (string)result;
    }

    public async Task SetAccountSettingAsync(
        Guid accountId,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        ValidateKey(
            key);

        ArgumentNullException.ThrowIfNull(
            value);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO AccountSettings
            (
                AccountId,
                SettingKey,
                SettingValue
            )
            VALUES
            (
                $accountId,
                $settingKey,
                $settingValue
            )
            ON CONFLICT(AccountId, SettingKey)
            DO UPDATE SET
                SettingValue = excluded.SettingValue;
            """;

        command.Parameters.AddWithValue(
            "$accountId",
            accountId.ToString("D"));

        command.Parameters.AddWithValue(
            "$settingKey",
            key.Trim());

        command.Parameters.AddWithValue(
            "$settingValue",
            value);

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
                 * AccountSettings besitzt bewusst einen
                 * Foreign Key auf Accounts.
                 *
                 * SQLite aktiviert Foreign Keys
                 * verbindungsbezogen. Deshalb wird die
                 * Prüfung auch für diesen Store explizit
                 * eingeschaltet.
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

    private static void ValidateKey(
        string key)
    {
        if (string.IsNullOrWhiteSpace(
                key))
        {
            throw new ArgumentException(
                "Der Einstellungsschlüssel darf nicht leer sein.",
                nameof(key));
        }
    }
}