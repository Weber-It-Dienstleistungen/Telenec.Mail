using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Archive;

public sealed class MailArchiveServerDeletionService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private const int CopyBufferSize =
        128 * 1024;

    private static readonly TimeSpan
        ConnectionTimeout =
            TimeSpan.FromSeconds(15);

    private static readonly TimeSpan
        AuthenticationTimeout =
            TimeSpan.FromSeconds(30);

    private static readonly TimeSpan
        ServerVerificationTimeout =
            TimeSpan.FromMinutes(5);

    private static readonly TimeSpan
        DeleteTimeout =
            TimeSpan.FromSeconds(30);

    private static readonly TimeSpan
        RecoveryTimeout =
            TimeSpan.FromSeconds(15);

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly LocalMailArchiveStorage
        _archiveStorage;

    public MailArchiveServerDeletionService(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        LocalMailArchiveStorage archiveStorage)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        ArgumentNullException.ThrowIfNull(
            archiveStorage);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _archiveStorage =
            archiveStorage;
    }

    public async Task<ArchiveServerDeletionResult>
        DeleteArchivedServerMessageAsync(
            string archiveMessageId,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                archiveMessageId))
        {
            throw new ArgumentException(
                "Die Archivnachrichten-ID darf nicht leer sein.",
                nameof(archiveMessageId));
        }

        /*
         * Wir lesen den zu löschenden Serverdatensatz bewusst
         * aus archive.db.
         *
         * Dadurch entscheidet nicht die UI darüber, welche
         * FolderId, UIDVALIDITY oder UID gelöscht wird.
         *
         * Die lokale Archivdatenbank ist nach erfolgreicher
         * Finalisierung unsere verbindliche Referenz.
         */
        var archiveLocation =
            await _archiveStorage
                .EnsureArchiveForActiveAccountAsync(
                    cancellationToken);

        var archiveRecord =
            await LoadArchiveRecordAsync(
                archiveLocation,
                archiveMessageId,
                cancellationToken);

        /*
         * Idempotenz:
         *
         * Ist diese konkrete Archivnachricht bereits als
         * serverseitig gelöscht vermerkt, wird keinerlei neue
         * IMAP-Löschoperation ausgelöst.
         */
        if (archiveRecord.ServerDeleted)
        {
            return new ArchiveServerDeletionResult(
                ArchiveMessageId:
                    archiveRecord.ArchiveMessageId,

                OperationId:
                    archiveRecord.OperationId,

                SourceFolderId:
                    archiveRecord.SourceFolderId,

                SourceUidValidity:
                    archiveRecord.SourceUidValidity,

                SourceUniqueId:
                    archiveRecord.SourceUniqueId,

                LocalFilePath:
                    archiveRecord.LocalFilePath,

                ServerDeletionConfirmed:
                    true,

                DatabaseCompletionRecorded:
                    true,

                AlreadyCompleted:
                    true);
        }

        /*
         * Eine Serverlöschung ist ausschließlich zulässig,
         * wenn die lokale Archivoperation bereits erfolgreich
         * den Zustand LocalStored erreicht hat.
         */
        if (!string.Equals(
                archiveRecord.OperationStatus,
                "LocalStored",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Die lokale Archivierung befindet sich nicht im Zustand LocalStored. " +
                "Die Servermail wird deshalb nicht gelöscht.");
        }

        /*
         * Vor jeder Verbindung zum Mailserver prüfen wir die
         * endgültige lokale .eml erneut.
         *
         * Dadurch wird niemals aufgrund eines bloßen
         * Datenbankeintrags gelöscht, wenn die eigentliche
         * Archivdatei inzwischen fehlt oder verändert wurde.
         */
        var localFingerprint =
            await ComputeFileFingerprintAsync(
                archiveRecord.LocalFilePath,
                cancellationToken);

        if (localFingerprint.FileSizeBytes !=
            archiveRecord.FileSizeBytes)
        {
            throw new InvalidOperationException(
                "Die endgültige lokale Archivdatei besitzt nicht mehr die erwartete Dateigröße. " +
                "Die Servermail wird nicht gelöscht.");
        }

        if (!string.Equals(
                localFingerprint.Sha256,
                archiveRecord.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die endgültige lokale Archivdatei hat die SHA-256-Prüfung nicht bestanden. " +
                "Die Servermail wird nicht gelöscht.");
        }

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            /*
             * Wie beim bestehenden Permanent Delete erlauben
             * wir irreversible selektive Löschungen nur mit
             * UIDPLUS.
             *
             * Nur dann können wir UID EXPUNGE verwenden und
             * exakt diese eine UID entfernen.
             */
            if (!client.Capabilities.HasFlag(
                    ImapCapabilities.UidPlus))
            {
                throw new NotSupportedException(
                    "Der IMAP-Server unterstützt kein UIDPLUS. " +
                    "Die archivierte Nachricht kann deshalb nicht sicher gezielt vom Server entfernt werden.");
            }

            using var verificationTimeoutSource =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            verificationTimeoutSource.CancelAfter(
                ServerVerificationTimeout);

            var verificationCancellationToken =
                verificationTimeoutSource.Token;

            var folder =
                await client.GetFolderAsync(
                    archiveRecord.SourceFolderId,
                    verificationCancellationToken);

            if (folder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new InvalidOperationException(
                    "Der ursprüngliche Mailordner kann nicht geöffnet werden. " +
                    "Die Servermail wird nicht gelöscht.");
            }

            await folder.OpenAsync(
                FolderAccess.ReadWrite,
                verificationCancellationToken);

            /*
             * Die numerische UID besitzt nur zusammen mit
             * derselben UIDVALIDITY ihre ursprüngliche
             * Bedeutung.
             *
             * Hat sich UIDVALIDITY geändert, wird unter keinen
             * Umständen gelöscht.
             */
            if (folder.UidValidity !=
                archiveRecord.SourceUidValidity)
            {
                throw new InvalidOperationException(
                    "UIDVALIDITY des ursprünglichen Mailordners hat sich geändert. " +
                    "Die archivierte Servermail kann nicht mehr sicher identifiziert werden und wird nicht gelöscht.");
            }

            var serverUniqueId =
                new UniqueId(
                    archiveRecord.SourceUniqueId);

            var messageExists =
                await MessageExistsAsync(
                    folder,
                    serverUniqueId,
                    verificationCancellationToken);

            if (!messageExists)
            {
                throw new InvalidOperationException(
                    "Die archivierte Nachricht ist im ursprünglichen Serverordner nicht mehr vorhanden. " +
                    "Es wird keine Löschoperation durchgeführt.");
            }

            /*
             * Zusätzliche Sicherheitsstufe:
             *
             * Unmittelbar vor der Löschung laden wir die
             * Servermail nochmals als Rohdaten und vergleichen
             * sie mit der endgültigen lokalen Archivdatei.
             *
             * Damit reicht nicht nur
             *
             * FolderId + UIDVALIDITY + UID,
             *
             * sondern auch der tatsächliche Nachrichteninhalt
             * muss exakt unserer archivierten Kopie
             * entsprechen.
             */
            var serverFingerprint =
                await ComputeServerMessageFingerprintAsync(
                    folder,
                    serverUniqueId,
                    verificationCancellationToken);

            if (serverFingerprint.FileSizeBytes !=
                archiveRecord.FileSizeBytes)
            {
                throw new InvalidOperationException(
                    "Die Servernachricht entspricht hinsichtlich ihrer Größe nicht mehr der lokalen Archivkopie. " +
                    "Sie wird deshalb nicht gelöscht.");
            }

            if (!string.Equals(
                    serverFingerprint.Sha256,
                    archiveRecord.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Die Servernachricht entspricht hinsichtlich ihrer SHA-256-Prüfsumme nicht mehr der lokalen Archivkopie. " +
                    "Sie wird deshalb nicht gelöscht.");
            }

            /*
             * Auch zwischen Inhaltsprüfung und erster
             * verändernder Operation darf sich UIDVALIDITY
             * nicht verändert haben.
             */
            if (folder.UidValidity !=
                archiveRecord.SourceUidValidity)
            {
                throw new InvalidOperationException(
                    "Der serverseitige Ordnerzustand hat sich während der Sicherheitsprüfung geändert. " +
                    "Die Nachricht wird nicht gelöscht.");
            }

            var mutationAttempted =
                false;

            var serverDeletionConfirmed =
                false;

            try
            {
                using var deleteTimeoutSource =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken);

                deleteTimeoutSource.CancelAfter(
                    DeleteTimeout);

                var deleteCancellationToken =
                    deleteTimeoutSource.Token;

                /*
                 * Ab hier beginnt die erste serververändernde
                 * Operation.
                 *
                 * mutationAttempted wird deshalb bewusst VOR
                 * dem Await gesetzt:
                 *
                 * Auch bei einem Verbindungsabbruch während
                 * AddFlagsAsync müssen wir davon ausgehen,
                 * dass der Server das \Deleted-Flag eventuell
                 * bereits angenommen hat.
                 */
                mutationAttempted =
                    true;

                await folder.AddFlagsAsync(
                    new[]
                    {
                        serverUniqueId
                    },
                    MessageFlags.Deleted,
                    silent:
                        true,
                    deleteCancellationToken);

                /*
                 * Ausschließlich selektives UID EXPUNGE.
                 *
                 * Kein allgemeines EXPUNGE.
                 *
                 * Andere eventuell bereits mit \Deleted
                 * markierte Nachrichten bleiben dadurch
                 * unangetastet.
                 */
                await folder.ExpungeAsync(
                    new[]
                    {
                        serverUniqueId
                    },
                    deleteCancellationToken);

                /*
                 * Erfolgsbestätigung direkt vom Server.
                 *
                 * Die UID darf anschließend nicht mehr
                 * vorhanden sein.
                 */
                var stillExists =
                    await MessageExistsAsync(
                        folder,
                        serverUniqueId,
                        deleteCancellationToken);

                if (stillExists)
                {
                    try
                    {
                        await folder.RemoveFlagsAsync(
                            new[]
                            {
                                serverUniqueId
                            },
                            MessageFlags.Deleted,
                            silent:
                                true,
                            deleteCancellationToken);
                    }
                    catch
                    {
                    }

                    throw new InvalidOperationException(
                        "Der Mailserver hat die selektive Löschung nicht eindeutig bestätigt.");
                }

                if (folder.UidValidity !=
                    archiveRecord.SourceUidValidity)
                {
                    throw new InvalidOperationException(
                        "UIDVALIDITY des Mailordners hat sich während des Löschvorgangs geändert.");
                }

                serverDeletionConfirmed =
                    true;
            }
            catch (Exception deleteException)
            {
                /*
                 * Wurde noch keinerlei verändernder Befehl
                 * versucht, kann der ursprüngliche Fehler
                 * unverändert weitergegeben werden.
                 */
                if (!mutationAttempted)
                {
                    throw;
                }

                /*
                 * Nach einer begonnenen Mutation kann ein
                 * Netzwerkfehler bedeuten:
                 *
                 * - Nachricht noch vorhanden
                 * - Nachricht nur mit \Deleted markiert
                 * - UID EXPUNGE bereits erfolgreich
                 *
                 * Deshalb versuchen wir mit einem eigenen,
                 * frischen Timeout den tatsächlichen Zustand
                 * festzustellen.
                 */
                var recoveryResult =
                    await TryRecoverAfterDeleteFailureAsync(
                        folder,
                        serverUniqueId,
                        archiveRecord.SourceUidValidity);

                if (recoveryResult ==
                    DeleteRecoveryResult
                        .ServerMessageAbsent)
                {
                    /*
                     * Der ursprüngliche Befehl hat offenbar
                     * trotz Ausnahme zum gewünschten Ergebnis
                     * geführt.
                     */
                    serverDeletionConfirmed =
                        true;
                }
                else if (recoveryResult ==
                         DeleteRecoveryResult
                             .ServerMessagePresentAndRecovered)
                {
                    throw new MailArchiveServerDeletionException(
                        "Die Serverlöschung ist fehlgeschlagen. " +
                        "Die Nachricht ist weiterhin vorhanden und ein eventuell gesetztes Löschkennzeichen wurde nach Möglichkeit zurückgenommen.",
                        deleteException,
                        serverDeletionConfirmed:
                            false,
                        serverStateUncertain:
                            false);
                }
                else
                {
                    throw new MailArchiveServerDeletionException(
                        "Während der Serverlöschung ist ein Fehler aufgetreten und der endgültige Serverzustand konnte nicht sicher ermittelt werden. " +
                        "Die lokale Archivkopie bleibt erhalten. Das Postfach muss vor einem weiteren Löschversuch neu geprüft werden.",
                        deleteException,
                        serverDeletionConfirmed:
                            false,
                        serverStateUncertain:
                            true);
                }
            }

            if (!serverDeletionConfirmed)
            {
                throw new MailArchiveServerDeletionException(
                    "Die Löschung der Servermail konnte nicht eindeutig bestätigt werden.",
                    innerException:
                        null,
                    serverDeletionConfirmed:
                        false,
                    serverStateUncertain:
                        true);
            }

            /*
             * Erst nachdem der Server die Abwesenheit der
             * exakten UID bestätigt hat, aktualisieren wir
             * archive.db.
             *
             * Schlägt ausschließlich dieser DB-Schritt fehl,
             * ist die lokale .eml weiterhin vollständig und
             * verifiziert vorhanden.
             *
             * Es entsteht also kein Mailverlust, sondern
             * lediglich ein später zu bereinigender
             * Statusunterschied zwischen Archivdatei und DB.
             */
            try
            {
                await MarkServerDeletionCompletedAsync(
                    archiveLocation,
                    archiveRecord,
                    cancellationToken);
            }
            catch (Exception databaseException)
            {
                throw new MailArchiveServerDeletionException(
                    "Die Servermail wurde erfolgreich gelöscht, aber der Abschlussstatus konnte nicht in archive.db gespeichert werden. " +
                    "Die lokale Archivdatei ist weiterhin vorhanden und geprüft.",
                    databaseException,
                    serverDeletionConfirmed:
                        true,
                    serverStateUncertain:
                        false);
            }

            return new ArchiveServerDeletionResult(
                ArchiveMessageId:
                    archiveRecord.ArchiveMessageId,

                OperationId:
                    archiveRecord.OperationId,

                SourceFolderId:
                    archiveRecord.SourceFolderId,

                SourceUidValidity:
                    archiveRecord.SourceUidValidity,

                SourceUniqueId:
                    archiveRecord.SourceUniqueId,

                LocalFilePath:
                    archiveRecord.LocalFilePath,

                ServerDeletionConfirmed:
                    true,

                DatabaseCompletionRecorded:
                    true,

                AlreadyCompleted:
                    false);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static async Task<
        ArchiveServerDeletionRecord>
        LoadArchiveRecordAsync(
            LocalMailArchiveLocation archiveLocation,
            string archiveMessageId,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenDatabaseConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                m.ArchiveMessageId,
                m.OperationId,
                m.SourceFolderId,
                m.SourceUidValidity,
                m.SourceUniqueId,
                m.RelativeFilePath,
                m.Sha256,
                m.FileSizeBytes,
                m.ServerDeleted,
                o.Status
            FROM ArchiveMessages AS m
            INNER JOIN ArchiveOperations AS o
                ON o.OperationId =
                    m.OperationId
            WHERE m.AccountKey =
                    $accountKey
              AND m.ArchiveMessageId =
                    $archiveMessageId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$accountKey",
            archiveLocation.AccountKey);

        command.Parameters.AddWithValue(
            "$archiveMessageId",
            archiveMessageId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Die angegebene Nachricht wurde nicht im lokalen Mailarchiv gefunden.");
        }

        var relativeFilePath =
            reader.GetString(5);

        var localFilePath =
            ResolveArchiveFilePath(
                archiveLocation.AccountDirectory,
                relativeFilePath);

        return new ArchiveServerDeletionRecord(
            ArchiveMessageId:
                reader.GetString(0),

            OperationId:
                reader.GetString(1),

            SourceFolderId:
                reader.GetString(2),

            SourceUidValidity:
                checked(
                    (uint)reader.GetInt64(3)),

            SourceUniqueId:
                checked(
                    (uint)reader.GetInt64(4)),

            RelativeFilePath:
                relativeFilePath,

            LocalFilePath:
                localFilePath,

            Sha256:
                reader.GetString(6),

            FileSizeBytes:
                reader.GetInt64(7),

            ServerDeleted:
                reader.GetInt64(8) != 0,

            OperationStatus:
                reader.GetString(9));
    }

    private static async Task
        MarkServerDeletionCompletedAsync(
            LocalMailArchiveLocation archiveLocation,
            ArchiveServerDeletionRecord archiveRecord,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenDatabaseConnectionAsync(
                archiveLocation.DatabasePath,
                cancellationToken);

        await using var transactionBase =
            await connection.BeginTransactionAsync(
                cancellationToken);

        var transaction =
            (SqliteTransaction)transactionBase;

        var completedAtUtc =
            DateTimeOffset
                .UtcNow
                .ToString(
                    "O",
                    CultureInfo.InvariantCulture);

        /*
         * Zuerst wird ausschließlich die konkrete
         * Archivnachricht als serverseitig gelöscht markiert.
         */
        await using (var updateMessageCommand =
                     connection.CreateCommand())
        {
            updateMessageCommand.Transaction =
                transaction;

            updateMessageCommand.CommandText =
                """
                UPDATE ArchiveMessages
                SET
                    ServerDeleted =
                        1,

                    ServerDeletedAtUtc =
                        $serverDeletedAtUtc
                WHERE ArchiveMessageId =
                        $archiveMessageId
                  AND AccountKey =
                        $accountKey
                  AND OperationId =
                        $operationId
                  AND SourceFolderId =
                        $sourceFolderId
                  AND SourceUidValidity =
                        $sourceUidValidity
                  AND SourceUniqueId =
                        $sourceUniqueId
                  AND ServerDeleted =
                        0;
                """;

            updateMessageCommand.Parameters.AddWithValue(
                "$serverDeletedAtUtc",
                completedAtUtc);

            updateMessageCommand.Parameters.AddWithValue(
                "$archiveMessageId",
                archiveRecord.ArchiveMessageId);

            updateMessageCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            updateMessageCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

            updateMessageCommand.Parameters.AddWithValue(
                "$sourceFolderId",
                archiveRecord.SourceFolderId);

            updateMessageCommand.Parameters.AddWithValue(
                "$sourceUidValidity",
                (long)archiveRecord.SourceUidValidity);

            updateMessageCommand.Parameters.AddWithValue(
                "$sourceUniqueId",
                (long)archiveRecord.SourceUniqueId);

            var updatedMessages =
                await updateMessageCommand
                    .ExecuteNonQueryAsync(
                        cancellationToken);

            if (updatedMessages != 1)
            {
                throw new InvalidOperationException(
                    "Der lokale Archivdatensatz konnte nach der bestätigten Serverlöschung nicht eindeutig aktualisiert werden.");
            }
        }

        /*
         * Schon jetzt berücksichtigen wir spätere
         * Mehrfach-Archivoperationen:
         *
         * ArchiveOperations wird erst auf Completed gesetzt,
         * wenn ALLE Nachrichten dieser Operation
         * ServerDeleted = 1 besitzen.
         *
         * Beim aktuellen Einzelmail-Test ist das unmittelbar
         * der Fall.
         */
        long remainingMessages;

        await using (var remainingCommand =
                     connection.CreateCommand())
        {
            remainingCommand.Transaction =
                transaction;

            remainingCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM ArchiveMessages
                WHERE AccountKey =
                        $accountKey
                  AND OperationId =
                        $operationId
                  AND ServerDeleted =
                        0;
                """;

            remainingCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            remainingCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

            var result =
                await remainingCommand
                    .ExecuteScalarAsync(
                        cancellationToken);

            remainingMessages =
                Convert.ToInt64(
                    result,
                    CultureInfo.InvariantCulture);
        }

        if (remainingMessages == 0)
        {
            await using var updateOperationCommand =
                connection.CreateCommand();

            updateOperationCommand.Transaction =
                transaction;

            updateOperationCommand.CommandText =
                """
                UPDATE ArchiveOperations
                SET
                    ServerDeletionCompletedAtUtc =
                        $completedAtUtc,

                    Status =
                        'Completed',

                    ErrorMessage =
                        NULL
                WHERE OperationId =
                        $operationId
                  AND AccountKey =
                        $accountKey
                  AND Status =
                        'LocalStored';
                """;

            updateOperationCommand.Parameters.AddWithValue(
                "$completedAtUtc",
                completedAtUtc);

            updateOperationCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

            updateOperationCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            var updatedOperations =
                await updateOperationCommand
                    .ExecuteNonQueryAsync(
                        cancellationToken);

            if (updatedOperations != 1)
            {
                throw new InvalidOperationException(
                    "Die lokale Archivoperation konnte nach der bestätigten Serverlöschung nicht auf Completed gesetzt werden.");
            }
        }

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task<
        DeleteRecoveryResult>
        TryRecoverAfterDeleteFailureAsync(
            IMailFolder folder,
            UniqueId uniqueId,
            uint expectedUidValidity)
    {
        try
        {
            using var recoveryTimeoutSource =
                new CancellationTokenSource(
                    RecoveryTimeout);

            var cancellationToken =
                recoveryTimeoutSource.Token;

            if (!folder.IsOpen)
            {
                return DeleteRecoveryResult
                    .ServerStateUnknown;
            }

            if (folder.UidValidity !=
                expectedUidValidity)
            {
                return DeleteRecoveryResult
                    .ServerStateUnknown;
            }

            var stillExists =
                await MessageExistsAsync(
                    folder,
                    uniqueId,
                    cancellationToken);

            if (!stillExists)
            {
                return DeleteRecoveryResult
                    .ServerMessageAbsent;
            }

            /*
             * Ist die Nachricht noch vorhanden, entfernen wir
             * vorsorglich das \Deleted-Flag.
             *
             * Damit soll ein späteres EXPUNGE eines anderen
             * Clients diese Mail nicht unbeabsichtigt
             * endgültig löschen.
             */
            try
            {
                await folder.RemoveFlagsAsync(
                    new[]
                    {
                        uniqueId
                    },
                    MessageFlags.Deleted,
                    silent:
                        true,
                    cancellationToken);
            }
            catch
            {
                return DeleteRecoveryResult
                    .ServerStateUnknown;
            }

            return DeleteRecoveryResult
                .ServerMessagePresentAndRecovered;
        }
        catch
        {
            return DeleteRecoveryResult
                .ServerStateUnknown;
        }
    }

    private static async Task<bool>
        MessageExistsAsync(
            IMailFolder folder,
            UniqueId uniqueId,
            CancellationToken cancellationToken)
    {
        var summaries =
            await folder.FetchAsync(
                new[]
                {
                    uniqueId
                },
                MessageSummaryItems.UniqueId,
                cancellationToken);

        return summaries.Any(
            summary =>
                summary.UniqueId ==
                uniqueId);
    }

    private static async Task<
        ArchiveFileFingerprint>
        ComputeServerMessageFingerprintAsync(
            IMailFolder folder,
            UniqueId uniqueId,
            CancellationToken cancellationToken)
    {
        await using var stream =
            await folder.GetStreamAsync(
                uniqueId,
                cancellationToken);

        return await ComputeStreamFingerprintAsync(
            stream,
            cancellationToken);
    }

    private static async Task<
        ArchiveFileFingerprint>
        ComputeFileFingerprintAsync(
            string filePath,
            CancellationToken cancellationToken)
    {
        var fileInfo =
            new FileInfo(
                filePath);

        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(
                "Die endgültige lokale Archivdatei ist nicht mehr vorhanden.",
                filePath);
        }

        if (fileInfo.Length <= 0)
        {
            throw new InvalidOperationException(
                "Die endgültige lokale Archivdatei ist leer.");
        }

        await using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        return await ComputeStreamFingerprintAsync(
            stream,
            cancellationToken);
    }

    private static async Task<
        ArchiveFileFingerprint>
        ComputeStreamFingerprintAsync(
            Stream stream,
            CancellationToken cancellationToken)
    {
        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        var buffer =
            new byte[
                CopyBufferSize];

        long totalBytes =
            0;

        while (true)
        {
            var read =
                await stream.ReadAsync(
                    buffer.AsMemory(
                        0,
                        buffer.Length),
                    cancellationToken);

            if (read <= 0)
            {
                break;
            }

            hash.AppendData(
                buffer,
                0,
                read);

            totalBytes +=
                read;
        }

        var sha256 =
            Convert
                .ToHexString(
                    hash.GetHashAndReset())
                .ToLowerInvariant();

        return new ArchiveFileFingerprint(
            FileSizeBytes:
                totalBytes,

            Sha256:
                sha256);
    }

    private async Task<ImapClient>
        CreateAuthenticatedClientAsync(
            CancellationToken cancellationToken)
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
                "Es ist kein aktives Mailkonto eingerichtet.");
        }

        var credential =
            await _credentialStore
                .ReadAsync(
                    account.AccountId,
                    cancellationToken);

        if (credential is null ||
            string.IsNullOrWhiteSpace(
                credential.Password))
        {
            throw new InvalidOperationException(
                "Für das Mailkonto sind keine Zugangsdaten gespeichert.");
        }

        var client =
            new ImapClient();

        try
        {
            using (var connectionTimeoutSource =
                   CancellationTokenSource
                       .CreateLinkedTokenSource(
                           cancellationToken))
            {
                connectionTimeoutSource.CancelAfter(
                    ConnectionTimeout);

                await client.ConnectAsync(
                    ImapHost,
                    ImapPort,
                    SecureSocketOptions
                        .SslOnConnect,
                    connectionTimeoutSource
                        .Token);
            }

            using (var authenticationTimeoutSource =
                   CancellationTokenSource
                       .CreateLinkedTokenSource(
                           cancellationToken))
            {
                authenticationTimeoutSource.CancelAfter(
                    AuthenticationTimeout);

                await client.AuthenticateAsync(
                    account.EmailAddress,
                    credential.Password,
                    authenticationTimeoutSource
                        .Token);
            }

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    private static async Task<SqliteConnection>
        OpenDatabaseConnectionAsync(
            string databasePath,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(
                databasePath))
        {
            throw new FileNotFoundException(
                "Die lokale Archivdatenbank ist nicht vorhanden.",
                databasePath);
        }

        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    databasePath,

                Mode =
                    SqliteOpenMode
                        .ReadWrite
            }
            .ToString();

        var connection =
            new SqliteConnection(
                connectionString);

        try
        {
            await connection.OpenAsync(
                cancellationToken);

            await using var pragmaCommand =
                connection.CreateCommand();

            pragmaCommand.CommandText =
                "PRAGMA foreign_keys = ON;";

            await pragmaCommand.ExecuteNonQueryAsync(
                cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }
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

    private static async Task
        DisconnectSafelyAsync(
            ImapClient client)
    {
        if (!client.IsConnected)
        {
            return;
        }

        try
        {
            await client.DisconnectAsync(
                true,
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private enum DeleteRecoveryResult
    {
        ServerMessageAbsent,
        ServerMessagePresentAndRecovered,
        ServerStateUnknown
    }

    private sealed record ArchiveFileFingerprint(
        long FileSizeBytes,
        string Sha256);

    private sealed record ArchiveServerDeletionRecord(
        string ArchiveMessageId,
        string OperationId,
        string SourceFolderId,
        uint SourceUidValidity,
        uint SourceUniqueId,
        string RelativeFilePath,
        string LocalFilePath,
        string Sha256,
        long FileSizeBytes,
        bool ServerDeleted,
        string OperationStatus);
}

public sealed record ArchiveServerDeletionResult(
    string ArchiveMessageId,
    string OperationId,
    string SourceFolderId,
    uint SourceUidValidity,
    uint SourceUniqueId,
    string LocalFilePath,
    bool ServerDeletionConfirmed,
    bool DatabaseCompletionRecorded,
    bool AlreadyCompleted);

public sealed class MailArchiveServerDeletionException
    : Exception
{
    public MailArchiveServerDeletionException(
        string message,
        Exception? innerException,
        bool serverDeletionConfirmed,
        bool serverStateUncertain)
        : base(
            message,
            innerException)
    {
        ServerDeletionConfirmed =
            serverDeletionConfirmed;

        ServerStateUncertain =
            serverStateUncertain;
    }

    /*
     * True bedeutet:
     *
     * Die exakte Servermail wurde nachweislich entfernt.
     *
     * Ein danach auftretender Fehler betrifft beispielsweise
     * nur noch die lokale Statuspersistenz.
     */
    public bool ServerDeletionConfirmed { get; }

    /*
     * True bedeutet:
     *
     * Nach einem begonnenen serververändernden Vorgang konnte
     * wegen eines Fehlers nicht eindeutig festgestellt werden,
     * ob die Mail noch vorhanden oder bereits entfernt ist.
     *
     * In diesem Zustand darf nicht blind erneut gelöscht
     * werden.
     */
    public bool ServerStateUncertain { get; }
}