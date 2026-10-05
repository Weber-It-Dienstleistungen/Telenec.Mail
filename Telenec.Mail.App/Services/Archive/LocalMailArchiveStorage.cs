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

    private const int MaximumFolderSegmentLength =
        90;

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

    public async Task<bool>
        IsMessageArchivedAsync(
            LocalMailArchiveLocation archiveLocation,
            string sourceFolderId,
            uint sourceUidValidity,
            uint sourceUniqueId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveLocation);

        ValidateSourceIdentity(
            sourceFolderId,
            sourceUidValidity,
            sourceUniqueId);

        await using var connection =
            await OpenConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT 1
            FROM ArchiveMessages
            WHERE AccountKey = $accountKey
              AND SourceFolderId = $sourceFolderId
              AND SourceUidValidity = $sourceUidValidity
              AND SourceUniqueId = $sourceUniqueId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$accountKey",
            archiveLocation.AccountKey);

        command.Parameters.AddWithValue(
            "$sourceFolderId",
            sourceFolderId);

        command.Parameters.AddWithValue(
            "$sourceUidValidity",
            (long)sourceUidValidity);

        command.Parameters.AddWithValue(
            "$sourceUniqueId",
            (long)sourceUniqueId);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is not null;
    }

    public async Task
        RecoverMissingLocalArchiveMessageAsync(
            LocalMailArchiveLocation archiveLocation,
            string sourceFolderId,
            uint sourceUidValidity,
            uint sourceUniqueId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveLocation);

        ValidateSourceIdentity(
            sourceFolderId,
            sourceUidValidity,
            sourceUniqueId);

        await using var connection =
            await OpenConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        await using var transactionBase =
            await connection
                .BeginTransactionAsync(
                    cancellationToken);

        var transaction =
            (SqliteTransaction)transactionBase;

        RecoverableArchiveMessageRecord?
            archiveRecord =
                null;

        await using (var selectCommand =
                     connection.CreateCommand())
        {
            selectCommand.Transaction =
                transaction;

            selectCommand.CommandText =
                """
                SELECT
                    m.ArchiveMessageId,
                    m.OperationId,
                    m.RelativeFilePath,
                    m.ServerDeleted,
                    o.Status
                FROM ArchiveMessages AS m
                INNER JOIN ArchiveOperations AS o
                    ON o.OperationId =
                        m.OperationId
                WHERE m.AccountKey =
                        $accountKey
                  AND m.SourceFolderId =
                        $sourceFolderId
                  AND m.SourceUidValidity =
                        $sourceUidValidity
                  AND m.SourceUniqueId =
                        $sourceUniqueId
                LIMIT 1;
                """;

            selectCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            selectCommand.Parameters.AddWithValue(
                "$sourceFolderId",
                sourceFolderId);

            selectCommand.Parameters.AddWithValue(
                "$sourceUidValidity",
                (long)sourceUidValidity);

            selectCommand.Parameters.AddWithValue(
                "$sourceUniqueId",
                (long)sourceUniqueId);

            await using var reader =
                await selectCommand
                    .ExecuteReaderAsync(
                        cancellationToken);

            if (await reader.ReadAsync(
                    cancellationToken))
            {
                archiveRecord =
                    new RecoverableArchiveMessageRecord(
                        ArchiveMessageId:
                            reader.GetString(0),

                        OperationId:
                            reader.GetString(1),

                        RelativeFilePath:
                            reader.GetString(2),

                        ServerDeleted:
                            reader.GetInt64(3) != 0,

                        OperationStatus:
                            reader.GetString(4));
            }
        }

        /*
         * Es existiert keinerlei alter Archivdatensatz.
         *
         * Damit ist nichts zu reparieren.
         */
        if (archiveRecord is null)
        {
            await transaction.CommitAsync(
                cancellationToken);

            return;
        }

        var localFilePath =
            ResolveArchiveFilePath(
                archiveLocation.AccountDirectory,
                archiveRecord.RelativeFilePath);

        /*
         * Ist die endgültige Archivdatei noch vorhanden,
         * bleibt der vorhandene DB-Datensatz selbstverständlich
         * unangetastet.
         */
        if (File.Exists(
                localFilePath))
        {
            await transaction.CommitAsync(
                cancellationToken);

            return;
        }

        /*
         * Kritischer Zustand:
         *
         * Laut Datenbank wurde die Servermail bereits endgültig
         * gelöscht, die lokale Archivdatei fehlt aber.
         *
         * Dieser Datensatz darf niemals automatisch entfernt
         * werden, da damit die letzte nachvollziehbare
         * Information über die archivierte Nachricht verloren
         * gehen könnte.
         */
        if (archiveRecord.ServerDeleted)
        {
            throw new InvalidOperationException(
                "Die lokale Archivdatei fehlt, obwohl die Servermail bereits als gelöscht vermerkt ist. " +
                "Der Archivdatensatz wird aus Sicherheitsgründen nicht automatisch entfernt.");
        }

        /*
         * Eine ArchiveMessage wird im normalen Workflow nur
         * zusammen mit dem Wechsel der Operation auf
         * LocalStored angelegt.
         *
         * Jeder andere Zustand wäre deshalb unerwartet und
         * wird nicht automatisch repariert.
         */
        if (!string.Equals(
                archiveRecord.OperationStatus,
                "LocalStored",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Für die fehlende lokale Archivdatei existiert ein Archivdatensatz in einem unerwarteten Zustand. " +
                "Der Datensatz wird nicht automatisch verändert.");
        }

        /*
         * Sicher reparierbarer Fall:
         *
         * - DB behauptet LocalStored
         * - endgültige .eml fehlt
         * - ServerDeleted ist noch false
         *
         * Der lokale Archivversuch ist damit unvollständig.
         *
         * Wir entfernen ausschließlich diesen lokalen
         * Archivnachrichten-Datensatz.
         */
        await using (var deleteMessageCommand =
                     connection.CreateCommand())
        {
            deleteMessageCommand.Transaction =
                transaction;

            deleteMessageCommand.CommandText =
                """
                DELETE FROM ArchiveMessages
                WHERE ArchiveMessageId =
                        $archiveMessageId
                  AND AccountKey =
                        $accountKey
                  AND ServerDeleted =
                        0;
                """;

            deleteMessageCommand.Parameters.AddWithValue(
                "$archiveMessageId",
                archiveRecord.ArchiveMessageId);

            deleteMessageCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            var deletedRows =
                await deleteMessageCommand
                    .ExecuteNonQueryAsync(
                        cancellationToken);

            if (deletedRows != 1)
            {
                throw new InvalidOperationException(
                    "Der unvollständige lokale Archivdatensatz konnte nicht eindeutig bereinigt werden.");
            }
        }

        long remainingMessages;

        await using (var countCommand =
                     connection.CreateCommand())
        {
            countCommand.Transaction =
                transaction;

            countCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM ArchiveMessages
                WHERE AccountKey =
                        $accountKey
                  AND OperationId =
                        $operationId;
                """;

            countCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            countCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

            var result =
                await countCommand
                    .ExecuteScalarAsync(
                        cancellationToken);

            remainingMessages =
                Convert.ToInt64(
                    result,
                    CultureInfo.InvariantCulture);
        }

        if (remainingMessages == 0)
        {
            /*
             * Beim aktuellen Einzelmail-Workflow bleibt nach
             * dem Entfernen des defekten Message-Datensatzes
             * keine Nachricht mehr in der Operation.
             *
             * Dann entfernen wir auch diese verwaiste
             * LocalStored-Operation.
             */
            await using var deleteOperationCommand =
                connection.CreateCommand();

            deleteOperationCommand.Transaction =
                transaction;

            deleteOperationCommand.CommandText =
                """
                DELETE FROM ArchiveOperations
                WHERE OperationId =
                        $operationId
                  AND AccountKey =
                        $accountKey
                  AND Status =
                        'LocalStored';
                """;

            deleteOperationCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

            deleteOperationCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            var deletedOperations =
                await deleteOperationCommand
                    .ExecuteNonQueryAsync(
                        cancellationToken);

            if (deletedOperations != 1)
            {
                throw new InvalidOperationException(
                    "Die verwaiste lokale Archivoperation konnte nicht eindeutig bereinigt werden.");
            }
        }
        else
        {
            /*
             * Für spätere Mehrfach-Archivoperationen halten wir
             * MessageCount konsistent, falls nur eine einzelne
             * lokale Archivdatei verloren gegangen sein sollte.
             */
            await using var updateOperationCommand =
                connection.CreateCommand();

            updateOperationCommand.Transaction =
                transaction;

            updateOperationCommand.CommandText =
                """
                UPDATE ArchiveOperations
                SET MessageCount =
                        $messageCount
                WHERE OperationId =
                        $operationId
                  AND AccountKey =
                        $accountKey
                  AND Status =
                        'LocalStored';
                """;

            updateOperationCommand.Parameters.AddWithValue(
                "$messageCount",
                remainingMessages);

            updateOperationCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

            updateOperationCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            await updateOperationCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task<LocalMailArchiveFolderLocation>
        EnsureArchiveFolderAsync(
            LocalMailArchiveLocation archiveLocation,
            string sourceFolderId,
            char sourceDirectorySeparator,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveLocation);

        if (string.IsNullOrWhiteSpace(
                sourceFolderId))
        {
            throw new ArgumentException(
                "Der Quellordner darf nicht leer sein.",
                nameof(sourceFolderId));
        }

        var sourceSegments =
            SplitSourceFolderPath(
                sourceFolderId,
                sourceDirectorySeparator);

        await using var connection =
            await OpenConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        await using var transactionBase =
            await connection.BeginTransactionAsync(
                cancellationToken);

        var transaction =
            (SqliteTransaction)transactionBase;

        string? parentRelativePath =
            null;

        LocalMailArchiveFolderRecord?
            currentFolder =
                null;

        for (var index = 0;
             index < sourceSegments.Count;
             index++)
        {
            var sourcePrefix =
                CreateSourceFolderPrefix(
                    sourceSegments,
                    index,
                    sourceDirectorySeparator);

            var existingFolder =
                await GetArchiveFolderBySourceAsync(
                    connection,
                    transaction,
                    archiveLocation.AccountKey,
                    sourcePrefix,
                    cancellationToken);

            if (existingFolder is not null)
            {
                currentFolder =
                    existingFolder;

                parentRelativePath =
                    existingFolder
                        .RelativeFolderPath;

                continue;
            }

            var displayName =
                sourceSegments[index];

            var safeSegment =
                CreateSafeFolderPathSegment(
                    displayName,
                    sourcePrefix);

            var relativeFolderPath =
                string.IsNullOrWhiteSpace(
                    parentRelativePath)
                    ? safeSegment
                    : Path.Combine(
                        parentRelativePath,
                        safeSegment);

            relativeFolderPath =
                await ResolveFolderPathCollisionAsync(
                    connection,
                    transaction,
                    archiveLocation.AccountKey,
                    sourcePrefix,
                    parentRelativePath,
                    safeSegment,
                    relativeFolderPath,
                    cancellationToken);

            var archiveFolderId =
                Guid.NewGuid()
                    .ToString("N");

            var createdAtUtc =
                DateTimeOffset
                    .UtcNow
                    .ToString(
                        "O",
                        CultureInfo.InvariantCulture);

            await using var insertCommand =
                connection.CreateCommand();

            insertCommand.Transaction =
                transaction;

            insertCommand.CommandText =
                """
                INSERT INTO ArchiveFolders
                (
                    ArchiveFolderId,
                    AccountKey,
                    SourceFolderId,
                    RelativeFolderPath,
                    DisplayName,
                    CreatedAtUtc
                )
                VALUES
                (
                    $archiveFolderId,
                    $accountKey,
                    $sourceFolderId,
                    $relativeFolderPath,
                    $displayName,
                    $createdAtUtc
                );
                """;

            insertCommand.Parameters.AddWithValue(
                "$archiveFolderId",
                archiveFolderId);

            insertCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            insertCommand.Parameters.AddWithValue(
                "$sourceFolderId",
                sourcePrefix);

            insertCommand.Parameters.AddWithValue(
                "$relativeFolderPath",
                relativeFolderPath);

            insertCommand.Parameters.AddWithValue(
                "$displayName",
                displayName);

            insertCommand.Parameters.AddWithValue(
                "$createdAtUtc",
                createdAtUtc);

            await insertCommand.ExecuteNonQueryAsync(
                cancellationToken);

            currentFolder =
                new LocalMailArchiveFolderRecord(
                    ArchiveFolderId:
                        archiveFolderId,

                    SourceFolderId:
                        sourcePrefix,

                    RelativeFolderPath:
                        relativeFolderPath,

                    DisplayName:
                        displayName);

            parentRelativePath =
                relativeFolderPath;
        }

        if (currentFolder is null)
        {
            throw new InvalidOperationException(
                "Der lokale Archivordner konnte nicht ermittelt werden.");
        }

        await transaction.CommitAsync(
            cancellationToken);

        var absoluteDirectoryPath =
            CreateSafeAbsoluteArchiveDirectoryPath(
                archiveLocation.AccountDirectory,
                currentFolder.RelativeFolderPath);

        Directory.CreateDirectory(
            absoluteDirectoryPath);

        return new LocalMailArchiveFolderLocation(
            ArchiveFolderId:
                currentFolder.ArchiveFolderId,

            SourceFolderId:
                currentFolder.SourceFolderId,

            RelativeFolderPath:
                currentFolder.RelativeFolderPath,

            DisplayName:
                currentFolder.DisplayName,

            DirectoryPath:
                absoluteDirectoryPath);
    }

    public async Task<string>
        BeginArchiveOperationAsync(
            LocalMailArchiveLocation archiveLocation,
            string sourceFolderId,
            int messageCount,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveLocation);

        if (string.IsNullOrWhiteSpace(
                sourceFolderId))
        {
            throw new ArgumentException(
                "Der Quellordner darf nicht leer sein.",
                nameof(sourceFolderId));
        }

        if (messageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(messageCount),
                "Eine Archivoperation muss mindestens eine Nachricht enthalten.");
        }

        var operationId =
            Guid.NewGuid()
                .ToString("N");

        var startedAtUtc =
            DateTimeOffset
                .UtcNow
                .ToString(
                    "O",
                    CultureInfo.InvariantCulture);

        await using var connection =
            await OpenConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO ArchiveOperations
            (
                OperationId,
                AccountKey,
                SourceFolderId,
                StartedAtUtc,
                LocalStorageCompletedAtUtc,
                ServerDeletionCompletedAtUtc,
                Status,
                MessageCount,
                ErrorMessage
            )
            VALUES
            (
                $operationId,
                $accountKey,
                $sourceFolderId,
                $startedAtUtc,
                NULL,
                NULL,
                'Preparing',
                $messageCount,
                NULL
            );
            """;

        command.Parameters.AddWithValue(
            "$operationId",
            operationId);

        command.Parameters.AddWithValue(
            "$accountKey",
            archiveLocation.AccountKey);

        command.Parameters.AddWithValue(
            "$sourceFolderId",
            sourceFolderId);

        command.Parameters.AddWithValue(
            "$startedAtUtc",
            startedAtUtc);

        command.Parameters.AddWithValue(
            "$messageCount",
            messageCount);

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        return operationId;
    }

    public async Task CompleteLocalArchiveMessageAsync(
        LocalMailArchiveLocation archiveLocation,
        LocalMailArchiveFolderLocation archiveFolder,
        string operationId,
        string archiveMessageId,
        StagedArchiveMessage stagedMessage,
        string relativeFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveLocation);

        ArgumentNullException.ThrowIfNull(
            archiveFolder);

        ArgumentNullException.ThrowIfNull(
            stagedMessage);

        if (string.IsNullOrWhiteSpace(
                operationId))
        {
            throw new ArgumentException(
                "Die Archivoperation darf nicht leer sein.",
                nameof(operationId));
        }

        if (string.IsNullOrWhiteSpace(
                archiveMessageId))
        {
            throw new ArgumentException(
                "Die Archivnachrichten-ID darf nicht leer sein.",
                nameof(archiveMessageId));
        }

        if (string.IsNullOrWhiteSpace(
                relativeFilePath))
        {
            throw new ArgumentException(
                "Der relative Archivdateipfad darf nicht leer sein.",
                nameof(relativeFilePath));
        }

        if (!string.Equals(
                archiveLocation.AccountKey,
                stagedMessage.AccountKey,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die vorbereitete Nachricht gehört nicht zum aktiven lokalen Mailarchiv.");
        }

        if (!string.Equals(
                archiveFolder.SourceFolderId,
                stagedMessage.SourceFolderId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Der lokale Archivordner passt nicht zum Quellordner der Nachricht.");
        }

        await using var connection =
            await OpenConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        await using var transactionBase =
            await connection.BeginTransactionAsync(
                cancellationToken);

        var transaction =
            (SqliteTransaction)transactionBase;

        var archivedAtUtc =
            DateTimeOffset
                .UtcNow
                .ToString(
                    "O",
                    CultureInfo.InvariantCulture);

        await using (var insertMessageCommand =
                     connection.CreateCommand())
        {
            insertMessageCommand.Transaction =
                transaction;

            insertMessageCommand.CommandText =
                """
                INSERT INTO ArchiveMessages
                (
                    ArchiveMessageId,
                    OperationId,
                    AccountKey,
                    ArchiveFolderId,
                    SourceFolderId,
                    SourceUidValidity,
                    SourceUniqueId,
                    MessageId,
                    Subject,
                    SenderAddress,
                    SenderName,
                    MessageDateUtc,
                    ArchivedAtUtc,
                    RelativeFilePath,
                    Sha256,
                    FileSizeBytes,
                    ServerDeleted,
                    ServerDeletedAtUtc
                )
                VALUES
                (
                    $archiveMessageId,
                    $operationId,
                    $accountKey,
                    $archiveFolderId,
                    $sourceFolderId,
                    $sourceUidValidity,
                    $sourceUniqueId,
                    $messageId,
                    $subject,
                    $senderAddress,
                    $senderName,
                    $messageDateUtc,
                    $archivedAtUtc,
                    $relativeFilePath,
                    $sha256,
                    $fileSizeBytes,
                    0,
                    NULL
                );
                """;

            insertMessageCommand.Parameters.AddWithValue(
                "$archiveMessageId",
                archiveMessageId);

            insertMessageCommand.Parameters.AddWithValue(
                "$operationId",
                operationId);

            insertMessageCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            insertMessageCommand.Parameters.AddWithValue(
                "$archiveFolderId",
                archiveFolder.ArchiveFolderId);

            insertMessageCommand.Parameters.AddWithValue(
                "$sourceFolderId",
                stagedMessage.SourceFolderId);

            insertMessageCommand.Parameters.AddWithValue(
                "$sourceUidValidity",
                (long)stagedMessage.SourceUidValidity);

            insertMessageCommand.Parameters.AddWithValue(
                "$sourceUniqueId",
                (long)stagedMessage.SourceUniqueId);

            insertMessageCommand.Parameters.AddWithValue(
                "$messageId",
                stagedMessage.MessageId
                    is null
                    ? DBNull.Value
                    : stagedMessage.MessageId);

            insertMessageCommand.Parameters.AddWithValue(
                "$subject",
                stagedMessage.Subject);

            insertMessageCommand.Parameters.AddWithValue(
                "$senderAddress",
                stagedMessage.SenderAddress
                    is null
                    ? DBNull.Value
                    : stagedMessage.SenderAddress);

            insertMessageCommand.Parameters.AddWithValue(
                "$senderName",
                stagedMessage.SenderName
                    is null
                    ? DBNull.Value
                    : stagedMessage.SenderName);

            insertMessageCommand.Parameters.AddWithValue(
                "$messageDateUtc",
                stagedMessage.MessageDateUtc
                    .HasValue
                    ? stagedMessage
                        .MessageDateUtc
                        .Value
                        .ToString(
                            "O",
                            CultureInfo.InvariantCulture)
                    : DBNull.Value);

            insertMessageCommand.Parameters.AddWithValue(
                "$archivedAtUtc",
                archivedAtUtc);

            insertMessageCommand.Parameters.AddWithValue(
                "$relativeFilePath",
                relativeFilePath);

            insertMessageCommand.Parameters.AddWithValue(
                "$sha256",
                stagedMessage.Sha256);

            insertMessageCommand.Parameters.AddWithValue(
                "$fileSizeBytes",
                stagedMessage.FileSizeBytes);

            await insertMessageCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }

        await using (var updateOperationCommand =
                     connection.CreateCommand())
        {
            updateOperationCommand.Transaction =
                transaction;

            updateOperationCommand.CommandText =
                """
                UPDATE ArchiveOperations
                SET
                    LocalStorageCompletedAtUtc =
                        $completedAtUtc,

                    Status =
                        'LocalStored',

                    ErrorMessage =
                        NULL
                WHERE OperationId =
                        $operationId
                  AND AccountKey =
                        $accountKey
                  AND SourceFolderId =
                        $sourceFolderId
                  AND Status =
                        'Preparing';
                """;

            updateOperationCommand.Parameters.AddWithValue(
                "$completedAtUtc",
                archivedAtUtc);

            updateOperationCommand.Parameters.AddWithValue(
                "$operationId",
                operationId);

            updateOperationCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            updateOperationCommand.Parameters.AddWithValue(
                "$sourceFolderId",
                stagedMessage.SourceFolderId);

            var updatedRows =
                await updateOperationCommand
                    .ExecuteNonQueryAsync(
                        cancellationToken);

            if (updatedRows != 1)
            {
                throw new InvalidOperationException(
                    "Die lokale Archivoperation befindet sich nicht im erwarteten Zustand.");
            }
        }

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task MarkArchiveOperationFailedAsync(
        LocalMailArchiveLocation archiveLocation,
        string operationId,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            archiveLocation);

        if (string.IsNullOrWhiteSpace(
                operationId))
        {
            return;
        }

        var normalizedErrorMessage =
            NormalizeErrorMessage(
                errorMessage);

        await using var connection =
            await OpenConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            UPDATE ArchiveOperations
            SET
                Status =
                    'Failed',

                ErrorMessage =
                    $errorMessage
            WHERE OperationId =
                    $operationId
              AND AccountKey =
                    $accountKey
              AND Status =
                    'Preparing';
            """;

        command.Parameters.AddWithValue(
            "$errorMessage",
            normalizedErrorMessage
                is null
                ? DBNull.Value
                : normalizedErrorMessage);

        command.Parameters.AddWithValue(
            "$operationId",
            operationId);

        command.Parameters.AddWithValue(
            "$accountKey",
            archiveLocation.AccountKey);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
        InitializeDatabaseAsync(
            string databasePath,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenConnectionAsync(
                databasePath,
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

    private static async Task<SqliteConnection>
        OpenConnectionAsync(
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

        var connection =
            new SqliteConnection(
                connectionString);

        try
        {
            await connection.OpenAsync(
                cancellationToken);

            await EnableForeignKeysAsync(
                connection,
                cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
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
        await using var connection =
            await OpenConnectionAsync(
                databasePath,
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

    private static async Task<
        LocalMailArchiveFolderRecord?>
        GetArchiveFolderBySourceAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string accountKey,
            string sourceFolderId,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            SELECT
                ArchiveFolderId,
                SourceFolderId,
                RelativeFolderPath,
                DisplayName
            FROM ArchiveFolders
            WHERE AccountKey = $accountKey
              AND SourceFolderId = $sourceFolderId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$accountKey",
            accountKey);

        command.Parameters.AddWithValue(
            "$sourceFolderId",
            sourceFolderId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new LocalMailArchiveFolderRecord(
            ArchiveFolderId:
                reader.GetString(0),

            SourceFolderId:
                reader.GetString(1),

            RelativeFolderPath:
                reader.GetString(2),

            DisplayName:
                reader.GetString(3));
    }

    private static async Task<string>
        ResolveFolderPathCollisionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string accountKey,
            string sourceFolderId,
            string? parentRelativePath,
            string baseSegment,
            string initialRelativePath,
            CancellationToken cancellationToken)
    {
        if (!await IsRelativeFolderPathInUseAsync(
                connection,
                transaction,
                accountKey,
                sourceFolderId,
                initialRelativePath,
                cancellationToken))
        {
            return initialRelativePath;
        }

        var stableHash =
            CreateShortHash(
                sourceFolderId,
                12);

        for (var attempt = 0;
             attempt < 100;
             attempt++)
        {
            var suffix =
                attempt == 0
                    ? $"~{stableHash}"
                    : $"~{stableHash}-{attempt + 1}";

            var collisionSafeSegment =
                AppendSuffix(
                    baseSegment,
                    suffix,
                    MaximumFolderSegmentLength);

            var relativePath =
                string.IsNullOrWhiteSpace(
                    parentRelativePath)
                    ? collisionSafeSegment
                    : Path.Combine(
                        parentRelativePath,
                        collisionSafeSegment);

            if (!await IsRelativeFolderPathInUseAsync(
                    connection,
                    transaction,
                    accountKey,
                    sourceFolderId,
                    relativePath,
                    cancellationToken))
            {
                return relativePath;
            }
        }

        throw new InvalidOperationException(
            "Für den lokalen Archivordner konnte kein eindeutiger Dateisystempfad erzeugt werden.");
    }

    private static async Task<bool>
        IsRelativeFolderPathInUseAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string accountKey,
            string sourceFolderId,
            string relativeFolderPath,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            SELECT SourceFolderId
            FROM ArchiveFolders
            WHERE AccountKey = $accountKey
              AND RelativeFolderPath = $relativeFolderPath
                    COLLATE NOCASE
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$accountKey",
            accountKey);

        command.Parameters.AddWithValue(
            "$relativeFolderPath",
            relativeFolderPath);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (result is null)
        {
            return false;
        }

        var existingSourceFolderId =
            Convert.ToString(
                result,
                CultureInfo.InvariantCulture);

        return !string.Equals(
            existingSourceFolderId,
            sourceFolderId,
            StringComparison.Ordinal);
    }

    private static IReadOnlyList<string>
        SplitSourceFolderPath(
            string sourceFolderId,
            char sourceDirectorySeparator)
    {
        if (sourceDirectorySeparator ==
            '\0')
        {
            return new[]
            {
                sourceFolderId
            };
        }

        var segments =
            sourceFolderId.Split(
                sourceDirectorySeparator,
                StringSplitOptions.None);

        if (segments.Length == 0 ||
            segments.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "Die IMAP-Ordnerstruktur enthält einen ungültigen leeren Pfadabschnitt.");
        }

        return segments;
    }

    private static string
        CreateSourceFolderPrefix(
            IReadOnlyList<string> segments,
            int inclusiveIndex,
            char sourceDirectorySeparator)
    {
        if (sourceDirectorySeparator ==
            '\0')
        {
            return segments[0];
        }

        return string.Join(
            sourceDirectorySeparator.ToString(),
            segments.Take(
                inclusiveIndex + 1));
    }

    private static string
        CreateSafeFolderPathSegment(
            string sourceSegment,
            string stableIdentity)
    {
        var safeSegment =
            SanitizeWindowsPathSegment(
                sourceSegment);

        var changed =
            !string.Equals(
                safeSegment,
                sourceSegment,
                StringComparison.Ordinal);

        if (changed)
        {
            safeSegment =
                AppendSuffix(
                    safeSegment,
                    "~" +
                    CreateShortHash(
                        stableIdentity,
                        8),
                    MaximumFolderSegmentLength);
        }

        if (safeSegment.Length >
            MaximumFolderSegmentLength)
        {
            safeSegment =
                AppendSuffix(
                    safeSegment[
                        ..Math.Min(
                            safeSegment.Length,
                            MaximumFolderSegmentLength)],
                    "~" +
                    CreateShortHash(
                        stableIdentity,
                        8),
                    MaximumFolderSegmentLength);
        }

        return safeSegment;
    }

    private static string
        SanitizeWindowsPathSegment(
            string value)
    {
        var invalidCharacters =
            Path.GetInvalidFileNameChars()
                .ToHashSet();

        var builder =
            new StringBuilder();

        foreach (var character in value)
        {
            if (invalidCharacters.Contains(
                    character) ||
                char.IsControl(
                    character))
            {
                builder.Append(
                    '_');
            }
            else
            {
                builder.Append(
                    character);
            }
        }

        var result =
            builder
                .ToString()
                .Trim()
                .TrimEnd(
                    '.');

        if (string.IsNullOrWhiteSpace(
                result))
        {
            result =
                "Ordner";
        }

        if (IsWindowsReservedName(
                result))
        {
            result =
                "_" + result;
        }

        return result;
    }

    private static bool
        IsWindowsReservedName(
            string value)
    {
        var baseName =
            value
                .Split(
                    '.',
                    2)[0];

        return baseName
            .ToUpperInvariant()
            switch
        {
            "CON" => true,
            "PRN" => true,
            "AUX" => true,
            "NUL" => true,
            "COM1" => true,
            "COM2" => true,
            "COM3" => true,
            "COM4" => true,
            "COM5" => true,
            "COM6" => true,
            "COM7" => true,
            "COM8" => true,
            "COM9" => true,
            "LPT1" => true,
            "LPT2" => true,
            "LPT3" => true,
            "LPT4" => true,
            "LPT5" => true,
            "LPT6" => true,
            "LPT7" => true,
            "LPT8" => true,
            "LPT9" => true,
            _ => false
        };
    }

    private static string AppendSuffix(
        string value,
        string suffix,
        int maximumLength)
    {
        var maximumBaseLength =
            Math.Max(
                maximumLength -
                suffix.Length,
                1);

        var baseValue =
            value.Length >
            maximumBaseLength
                ? value[
                    ..maximumBaseLength]
                : value;

        baseValue =
            baseValue
                .TrimEnd(
                    ' ',
                    '.');

        if (string.IsNullOrWhiteSpace(
                baseValue))
        {
            baseValue =
                "Ordner";
        }

        return baseValue +
               suffix;
    }

    private static string
        CreateSafeAbsoluteArchiveDirectoryPath(
            string accountDirectory,
            string relativeFolderPath)
    {
        var fullAccountDirectory =
            Path.GetFullPath(
                accountDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var fullDirectoryPath =
            Path.GetFullPath(
                Path.Combine(
                    fullAccountDirectory,
                    relativeFolderPath));

        var requiredPrefix =
            fullAccountDirectory +
            Path.DirectorySeparatorChar;

        if (!fullDirectoryPath.StartsWith(
                requiredPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Der berechnete Archivordner liegt außerhalb des vorgesehenen lokalen Mailarchivs.");
        }

        return fullDirectoryPath;
    }

    private static string ResolveArchiveFilePath(
        string accountDirectory,
        string relativeFilePath)
    {
        if (string.IsNullOrWhiteSpace(
                relativeFilePath))
        {
            throw new InvalidOperationException(
                "Der Archivdatensatz besitzt keinen gültigen Dateipfad.");
        }

        var fullAccountDirectory =
            Path.GetFullPath(
                    accountDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var fullFilePath =
            Path.GetFullPath(
                Path.Combine(
                    fullAccountDirectory,
                    relativeFilePath));

        var requiredPrefix =
            fullAccountDirectory +
            Path.DirectorySeparatorChar;

        if (!fullFilePath.StartsWith(
                requiredPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Der Archivdatensatz verweist auf eine Datei außerhalb des vorgesehenen lokalen Mailarchivs.");
        }

        return fullFilePath;
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

    private static string CreateShortHash(
        string value,
        int length)
    {
        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    value));

        var hex =
            Convert
                .ToHexString(
                    hash)
                .ToLowerInvariant();

        return hex[
            ..Math.Min(
                length,
                hex.Length)];
    }

    private static string?
        NormalizeErrorMessage(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var normalized =
            value.Trim();

        const int maximumLength =
            2000;

        return normalized.Length <=
               maximumLength
            ? normalized
            : normalized[
                ..maximumLength];
    }

    private static void
        ValidateSourceIdentity(
            string sourceFolderId,
            uint sourceUidValidity,
            uint sourceUniqueId)
    {
        if (string.IsNullOrWhiteSpace(
                sourceFolderId))
        {
            throw new ArgumentException(
                "Der Quellordner darf nicht leer sein.",
                nameof(sourceFolderId));
        }

        if (sourceUidValidity == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceUidValidity));
        }

        if (sourceUniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceUniqueId));
        }
    }

    private sealed record
        LocalMailArchiveFolderRecord(
            string ArchiveFolderId,
            string SourceFolderId,
            string RelativeFolderPath,
            string DisplayName);

    private sealed record
        RecoverableArchiveMessageRecord(
            string ArchiveMessageId,
            string OperationId,
            string RelativeFilePath,
            bool ServerDeleted,
            string OperationStatus);
}

public sealed record LocalMailArchiveLocation(
    string AccountKey,
    string EmailAddress,
    string ArchiveRootDirectory,
    string DatabasePath,
    string AccountDirectory,
    string AccountDirectoryName);

public sealed record LocalMailArchiveFolderLocation(
    string ArchiveFolderId,
    string SourceFolderId,
    string RelativeFolderPath,
    string DisplayName,
    string DirectoryPath);