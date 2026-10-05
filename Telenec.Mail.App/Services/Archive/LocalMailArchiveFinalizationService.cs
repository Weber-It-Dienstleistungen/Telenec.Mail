using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Telenec.Mail.App.Services.Archive;

public sealed class LocalMailArchiveFinalizationService
{
    private const int CopyBufferSize =
        128 * 1024;

    private const int MaximumSubjectLength =
        80;

    private readonly LocalMailArchiveStorage
        _archiveStorage;

    public LocalMailArchiveFinalizationService(
        LocalMailArchiveStorage archiveStorage)
    {
        ArgumentNullException.ThrowIfNull(
            archiveStorage);

        _archiveStorage =
            archiveStorage;
    }

    public async Task<FinalizedLocalArchiveMessage>
        FinalizeMessageAsync(
            StagedArchiveMessage stagedMessage,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            stagedMessage);

        ValidateStagedMessage(
            stagedMessage);

        var archiveLocation =
            await _archiveStorage
                .EnsureArchiveForActiveAccountAsync(
                    cancellationToken);

        if (!string.Equals(
                archiveLocation.AccountKey,
                stagedMessage.AccountKey,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die vorbereitete Nachricht gehört nicht zum aktuell aktiven Mailkonto.");
        }

        /*
         * Recovery vor der Dublettenprüfung.
         *
         * Falls archive.db noch einen LocalStored-Datensatz
         * kennt, die endgültige lokale .eml aber nicht mehr
         * existiert und ServerDeleted noch false ist, wird
         * dieser unvollständige lokale Zustand bereinigt.
         *
         * Ein Datensatz, dessen Servermail bereits als gelöscht
         * markiert ist, wird dagegen niemals automatisch
         * entfernt.
         */
        await _archiveStorage
            .RecoverMissingLocalArchiveMessageAsync(
                archiveLocation,
                stagedMessage.SourceFolderId,
                stagedMessage.SourceUidValidity,
                stagedMessage.SourceUniqueId,
                cancellationToken);

        var alreadyArchived =
            await _archiveStorage
                .IsMessageArchivedAsync(
                    archiveLocation,
                    stagedMessage.SourceFolderId,
                    stagedMessage.SourceUidValidity,
                    stagedMessage.SourceUniqueId,
                    cancellationToken);

        if (alreadyArchived)
        {
            throw new InvalidOperationException(
                "Diese Servernachricht wurde bereits im lokalen Mailarchiv gespeichert.");
        }

        var archiveFolder =
            await _archiveStorage
                .EnsureArchiveFolderAsync(
                    archiveLocation,
                    stagedMessage.SourceFolderId,
                    stagedMessage
                        .SourceFolderDirectorySeparator,
                    cancellationToken);

        var operationId =
            await _archiveStorage
                .BeginArchiveOperationAsync(
                    archiveLocation,
                    stagedMessage.SourceFolderId,
                    messageCount:
                        1,
                    cancellationToken);

        var archiveMessageId =
            Guid.NewGuid()
                .ToString("N");

        var fileName =
            CreateArchiveFileName(
                stagedMessage,
                archiveMessageId);

        var finalFilePath =
            Path.Combine(
                archiveFolder.DirectoryPath,
                fileName);

        var partialFilePath =
            finalFilePath +
            ".part-" +
            Guid.NewGuid()
                .ToString("N");

        var relativeFilePath =
            Path.Combine(
                archiveFolder.RelativeFolderPath,
                fileName);

        var finalFileVerified =
            false;

        try
        {
            await CopyToFinalLocationAsync(
                stagedMessage,
                partialFilePath,
                cancellationToken);

            File.Move(
                partialFilePath,
                finalFilePath,
                overwrite:
                    false);

            await VerifyFinalFileAsync(
                stagedMessage,
                finalFilePath,
                cancellationToken);

            finalFileVerified =
                true;

            await _archiveStorage
                .CompleteLocalArchiveMessageAsync(
                    archiveLocation,
                    archiveFolder,
                    operationId,
                    archiveMessageId,
                    stagedMessage,
                    relativeFilePath,
                    cancellationToken);

            /*
             * Erst jetzt gilt die lokale Archivierung als
             * vollständig:
             *
             * - finale Datei vorhanden
             * - finale Datei erneut verifiziert
             * - ArchiveMessages geschrieben
             * - Operation auf LocalStored gesetzt
             *
             * Die temporäre Stagingdatei wird deshalb auch
             * wirklich erst an dieser Stelle entfernt.
             */
            TryDeleteFile(
                stagedMessage.StagingFilePath);

            return new FinalizedLocalArchiveMessage(
                OperationId:
                    operationId,

                ArchiveMessageId:
                    archiveMessageId,

                SourceFolderId:
                    stagedMessage.SourceFolderId,

                SourceUidValidity:
                    stagedMessage.SourceUidValidity,

                SourceUniqueId:
                    stagedMessage.SourceUniqueId,

                RelativeFolderPath:
                    archiveFolder.RelativeFolderPath,

                RelativeFilePath:
                    relativeFilePath,

                FinalFilePath:
                    finalFilePath,

                Sha256:
                    stagedMessage.Sha256,

                FileSizeBytes:
                    stagedMessage.FileSizeBytes);
        }
        catch (Exception exception)
        {
            TryDeleteFile(
                partialFilePath);

            /*
             * Eine bereits erfolgreich verifizierte endgültige
             * Datei bleibt auch bei einem nachfolgenden
             * DB-Fehler erhalten.
             *
             * Eine zusätzliche lokale Kopie ist sicherer als
             * ein automatisches Entfernen möglicherweise
             * wertvoller Archivdaten.
             */
            if (!finalFileVerified)
            {
                TryDeleteFile(
                    finalFilePath);
            }

            try
            {
                await _archiveStorage
                    .MarkArchiveOperationFailedAsync(
                        archiveLocation,
                        operationId,
                        exception.Message,
                        CancellationToken.None);
            }
            catch
            {
            }

            throw;
        }
    }

    private static void ValidateStagedMessage(
        StagedArchiveMessage stagedMessage)
    {
        if (string.IsNullOrWhiteSpace(
                stagedMessage.StagingFilePath))
        {
            throw new InvalidOperationException(
                "Für die vorbereitete Nachricht existiert kein Staging-Dateipfad.");
        }

        var fileInfo =
            new FileInfo(
                stagedMessage.StagingFilePath);

        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(
                "Die vorbereitete Archivdatei ist nicht mehr vorhanden.",
                stagedMessage.StagingFilePath);
        }

        if (fileInfo.Length !=
            stagedMessage.FileSizeBytes)
        {
            throw new InvalidOperationException(
                "Die vorbereitete Archivdatei besitzt nicht mehr die erwartete Dateigröße.");
        }

        if (stagedMessage.FileSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                "Die vorbereitete Archivdatei ist leer.");
        }

        if (string.IsNullOrWhiteSpace(
                stagedMessage.AccountKey))
        {
            throw new InvalidOperationException(
                "Die vorbereitete Nachricht besitzt keine gültige Kontoidentität.");
        }

        if (string.IsNullOrWhiteSpace(
                stagedMessage.SourceFolderId))
        {
            throw new InvalidOperationException(
                "Die vorbereitete Nachricht besitzt keinen gültigen Quellordner.");
        }

        if (stagedMessage.SourceUidValidity == 0 ||
            stagedMessage.SourceUniqueId == 0)
        {
            throw new InvalidOperationException(
                "Die vorbereitete Nachricht besitzt keine gültige IMAP-Identität.");
        }

        if (string.IsNullOrWhiteSpace(
                stagedMessage.Sha256))
        {
            throw new InvalidOperationException(
                "Für die vorbereitete Nachricht liegt keine SHA-256-Prüfsumme vor.");
        }
    }

    private static async Task CopyToFinalLocationAsync(
        StagedArchiveMessage stagedMessage,
        string partialFilePath,
        CancellationToken cancellationToken)
    {
        await using var sourceStream =
            new FileStream(
                stagedMessage.StagingFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        await using var destinationStream =
            new FileStream(
                partialFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

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
                await sourceStream.ReadAsync(
                    buffer.AsMemory(
                        0,
                        buffer.Length),
                    cancellationToken);

            if (read <= 0)
            {
                break;
            }

            await destinationStream.WriteAsync(
                buffer.AsMemory(
                    0,
                    read),
                cancellationToken);

            hash.AppendData(
                buffer,
                0,
                read);

            totalBytes +=
                read;
        }

        await destinationStream.FlushAsync(
            cancellationToken);

        destinationStream.Flush(
            flushToDisk:
                true);

        if (totalBytes !=
            stagedMessage.FileSizeBytes)
        {
            throw new InvalidOperationException(
                "Die endgültige Archivkopie besitzt nicht die erwartete Dateigröße.");
        }

        var copiedSha256 =
            Convert
                .ToHexString(
                    hash.GetHashAndReset())
                .ToLowerInvariant();

        if (!string.Equals(
                copiedSha256,
                stagedMessage.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die Prüfsumme der kopierten Archivdatei stimmt nicht mit der verifizierten Stagingdatei überein.");
        }
    }

    private static async Task VerifyFinalFileAsync(
        StagedArchiveMessage stagedMessage,
        string finalFilePath,
        CancellationToken cancellationToken)
    {
        var fileInfo =
            new FileInfo(
                finalFilePath);

        if (!fileInfo.Exists)
        {
            throw new InvalidOperationException(
                "Die endgültige Archivdatei wurde nicht angelegt.");
        }

        if (fileInfo.Length !=
            stagedMessage.FileSizeBytes)
        {
            throw new InvalidOperationException(
                "Die endgültige Archivdatei besitzt nach dem Verschieben eine unerwartete Dateigröße.");
        }

        var finalSha256 =
            await ComputeFileSha256Async(
                finalFilePath,
                cancellationToken);

        if (!string.Equals(
                finalSha256,
                stagedMessage.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die endgültige Archivdatei hat die abschließende SHA-256-Prüfung nicht bestanden.");
        }
    }

    private static async Task<string>
        ComputeFileSha256Async(
            string filePath,
            CancellationToken cancellationToken)
    {
        await using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        var buffer =
            new byte[
                CopyBufferSize];

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
        }

        return Convert
            .ToHexString(
                hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    private static string CreateArchiveFileName(
        StagedArchiveMessage stagedMessage,
        string archiveMessageId)
    {
        var datePart =
            stagedMessage.MessageDateUtc
                .HasValue
                ? stagedMessage
                    .MessageDateUtc
                    .Value
                    .ToUniversalTime()
                    .ToString(
                        "yyyyMMdd-HHmmss")
                : "ohne-datum";

        var subjectPart =
            CreateSafeSubjectPart(
                stagedMessage.Subject);

        var sourceIdentityPart =
            $"{stagedMessage.SourceUidValidity}-" +
            $"{stagedMessage.SourceUniqueId}";

        var shortArchiveId =
            archiveMessageId.Length > 12
                ? archiveMessageId[..12]
                : archiveMessageId;

        return
            $"{datePart} - " +
            $"{subjectPart} - " +
            $"{sourceIdentityPart} - " +
            $"{shortArchiveId}.eml";
    }

    private static string CreateSafeSubjectPart(
        string? subject)
    {
        var original =
            string.IsNullOrWhiteSpace(
                subject)
                ? "Ohne Betreff"
                : subject.Trim();

        var invalidCharacters =
            Path.GetInvalidFileNameChars()
                .ToHashSet();

        var builder =
            new StringBuilder();

        var previousWasWhitespace =
            false;

        foreach (var character in original)
        {
            if (invalidCharacters.Contains(
                    character) ||
                char.IsControl(
                    character))
            {
                builder.Append(
                    '_');

                previousWasWhitespace =
                    false;

                continue;
            }

            if (char.IsWhiteSpace(
                    character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(
                        ' ');
                }

                previousWasWhitespace =
                    true;

                continue;
            }

            builder.Append(
                character);

            previousWasWhitespace =
                false;
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
                "Ohne Betreff";
        }

        if (result.Length >
            MaximumSubjectLength)
        {
            result =
                result[
                    ..MaximumSubjectLength]
                .TrimEnd(
                    ' ',
                    '.');
        }

        return result;
    }

    private static void TryDeleteFile(
        string filePath)
    {
        try
        {
            if (File.Exists(
                    filePath))
            {
                File.Delete(
                    filePath);
            }
        }
        catch
        {
        }
    }
}

public sealed record FinalizedLocalArchiveMessage(
    string OperationId,
    string ArchiveMessageId,
    string SourceFolderId,
    uint SourceUidValidity,
    uint SourceUniqueId,
    string RelativeFolderPath,
    string RelativeFilePath,
    string FinalFilePath,
    string Sha256,
    long FileSizeBytes);