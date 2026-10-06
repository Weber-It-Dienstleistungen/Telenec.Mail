using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Data.Sqlite;
using MimeKit;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Telenec.Mail.App.Services.Migration;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Archive;

public sealed class MailArchiveRestoreService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private const int CopyBufferSize =
        128 * 1024;

    private const string RestoreMarkerPrefix =
        "TelenecMailRestore:v1|";

    private static readonly TimeSpan
        ConnectionTimeout =
            TimeSpan.FromSeconds(15);

    private static readonly TimeSpan
        AuthenticationTimeout =
            TimeSpan.FromSeconds(30);

    private static readonly TimeSpan
        ServerOperationTimeout =
            TimeSpan.FromMinutes(5);

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly LocalMailArchiveStorage
        _archiveStorage;

    private readonly RawImapAppendService
        _rawImapAppendService;

    public MailArchiveRestoreService(
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

        /*
         * Der bereits vorhandene RAW-APPEND-Dienst wird
         * bewusst wiederverwendet.
         *
         * Dadurch wird die archivierte .eml bytegenau als
         * IMAP-Literal übertragen und nicht durch MimeKit
         * neu serialisiert.
         */
        _rawImapAppendService =
            new RawImapAppendService(
                mailAccountStore,
                credentialStore);
    }

    public async Task<ArchiveRestoreResult>
        RestoreAsync(
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

        var archiveLocation =
            await _archiveStorage
                .EnsureArchiveForActiveAccountAsync(
                    cancellationToken);

        var archiveRecord =
            await LoadArchiveRecordAsync(
                archiveLocation,
                archiveMessageId,
                cancellationToken);

        ValidateRestoreRecord(
            archiveRecord);

        /*
         * Falls ein vorheriger Wiederherstellungsversuch
         * unterbrochen wurde, wird zuerst ausschließlich
         * dessen Serverzustand geprüft.
         *
         * Es wird niemals blind ein zweiter APPEND gestartet.
         */
        var existingRestoreMarker =
            TryParseRestoreMarker(
                archiveRecord
                    .OperationErrorMessage);

        if (existingRestoreMarker is not null)
        {
            if (!string.Equals(
                    existingRestoreMarker.ArchiveMessageId,
                    archiveRecord.ArchiveMessageId,
                    StringComparison.Ordinal))
            {
                throw new MailArchiveRestoreException(
                    "Für diese Archivoperation existiert bereits ein anderer nicht abgeschlossener Wiederherstellungsvorgang. " +
                    "Es wird keine weitere Serverkopie erzeugt.",
                    innerException:
                        null,
                    serverCopyVerified:
                        false,
                    recoveryStateUnresolved:
                        true,
                    retryIsSafe:
                        false);
            }

            return await RecoverInterruptedRestoreAsync(
                archiveLocation,
                archiveRecord,
                existingRestoreMarker,
                cancellationToken);
        }

        /*
         * Completed-Operationen sollten normalerweise keine
         * andere Fehlermeldung mehr besitzen.
         *
         * Wir überschreiben einen unbekannten Zustand nicht
         * einfach mit unserem Restore-Marker.
         */
        if (!string.IsNullOrWhiteSpace(
                archiveRecord.OperationErrorMessage))
        {
            throw new InvalidOperationException(
                "Die Archivoperation enthält einen unerwarteten Diagnosezustand. " +
                "Die Nachricht wird aus Sicherheitsgründen nicht wiederhergestellt.");
        }

        await EnsureLocalArchiveFileMatchesAsync(
            archiveRecord,
            cancellationToken);

        var rawMessage =
            await File.ReadAllBytesAsync(
                archiveRecord.LocalFilePath,
                cancellationToken);

        var rawFingerprint =
            ComputeByteArrayFingerprint(
                rawMessage);

        if (rawFingerprint.FileSizeBytes !=
                archiveRecord.FileSizeBytes ||
            !string.Equals(
                rawFingerprint.Sha256,
                archiveRecord.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die lokale Archivdatei hat sich während der Vorbereitung der Wiederherstellung verändert.");
        }

        var internalDate =
            await DetermineInternalDateAsync(
                rawMessage,
                archiveRecord,
                cancellationToken);

        /*
         * Vor dem APPEND prüfen wir den ursprünglichen
         * Zielordner und merken uns dessen aktuellen
         * UID-Zustand.
         *
         * BaselineHighestUid ermöglicht uns später die
         * sichere Erkennung einer eventuell bereits vom
         * unterbrochenen APPEND erzeugten Nachricht.
         */
        var targetState =
            await LoadTargetFolderStateAsync(
                archiveRecord,
                cancellationToken);

        var restoreMarker =
            new ArchiveRestoreMarker(
                ArchiveMessageId:
                    archiveRecord.ArchiveMessageId,

                TargetUidValidity:
                    targetState.UidValidity,

                BaselineHighestUid:
                    targetState.HighestUid);

        var restoreMarkerText =
            CreateRestoreMarkerText(
                restoreMarker);

        await WriteRestoreMarkerAsync(
            archiveLocation,
            archiveRecord,
            restoreMarkerText,
            cancellationToken);

        UniqueId appendedUniqueId;

        try
        {
            using var uploadTimeoutSource =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            uploadTimeoutSource.CancelAfter(
                ServerOperationTimeout);

            /*
             * Die ursprünglichen IMAP-Flags wurden beim
             * Archivieren bisher nicht separat gespeichert.
             *
             * Deshalb stellen wir ältere Archivmails bewusst
             * als gelesen wieder her.
             */
            appendedUniqueId =
                await _rawImapAppendService
                    .AppendAsync(
                        archiveRecord
                            .SourceFolderId,
                        rawMessage,
                        MessageFlags.Seen,
                        Array.Empty<string>(),
                        internalDate,
                        uploadTimeoutSource.Token);
        }
        catch (Exception appendException)
            when (appendException
                is not OperationCanceledException ||
                  !cancellationToken
                      .IsCancellationRequested)
        {
            return await RecoverAfterAppendFailureAsync(
                archiveLocation,
                archiveRecord,
                restoreMarker,
                restoreMarkerText,
                appendException,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            throw new MailArchiveRestoreException(
                "Die Wiederherstellung wurde während eines möglicherweise bereits begonnenen Serveruploads abgebrochen. " +
                "Die lokale Archivkopie bleibt vollständig erhalten. " +
                "Beim nächsten Versuch prüft Telenec Mail zuerst, ob bereits eine neue Serverkopie vorhanden ist.",
                exception,
                serverCopyVerified:
                    false,
                recoveryStateUnresolved:
                    true,
                retryIsSafe:
                    true);
        }

        /*
         * Eine erhaltene APPENDUID reicht uns noch nicht.
         *
         * Die konkrete neue UID wird erneut roh vom Server
         * gelesen und mit der lokalen Archivdatei verglichen.
         */
        try
        {
            await VerifySpecificServerCopyAsync(
                archiveRecord,
                restoreMarker,
                appendedUniqueId.Id,
                cancellationToken);
        }
        catch (Exception verificationException)
        {
            return await RecoverAfterVerificationFailureAsync(
                archiveLocation,
                archiveRecord,
                restoreMarker,
                restoreMarkerText,
                verificationException,
                cancellationToken);
        }

        return await CompleteVerifiedRestoreAsync(
            archiveLocation,
            archiveRecord,
            restoreMarker,
            restoreMarkerText,
            appendedUniqueId.Id,
            recoveredInterruptedRestore:
                false,
            warning:
                null,
            cancellationToken);
    }

    private async Task<ArchiveRestoreResult>
        RecoverInterruptedRestoreAsync(
            LocalMailArchiveLocation archiveLocation,
            ArchiveRestoreRecord archiveRecord,
            ArchiveRestoreMarker restoreMarker,
            CancellationToken cancellationToken)
    {
        await EnsureLocalArchiveFileMatchesAsync(
            archiveRecord,
            cancellationToken);

        ServerRecoveryScan recoveryScan;

        try
        {
            recoveryScan =
                await FindMatchingServerCopiesCreatedAfterBaselineAsync(
                    archiveRecord,
                    restoreMarker,
                    cancellationToken);
        }
        catch (Exception exception)
        {
            throw new MailArchiveRestoreException(
                "Ein früherer Wiederherstellungsversuch ist noch nicht eindeutig abgeschlossen. " +
                "Der aktuelle Serverzustand konnte nicht sicher geprüft werden. " +
                "Die lokale Archivkopie bleibt erhalten.",
                exception,
                serverCopyVerified:
                    false,
                recoveryStateUnresolved:
                    true,
                retryIsSafe:
                    true);
        }

        if (recoveryScan.MatchingUids.Count > 0)
        {
            var restoredUid =
                recoveryScan
                    .MatchingUids
                    .Max();

            string? warning =
                null;

            if (recoveryScan.MatchingUids.Count >
                1)
            {
                warning =
                    "Bei der Wiederaufnahme wurden mehrere neue byteidentische Serverkopien gefunden. " +
                    "Die lokale Archivkopie wurde nur einmal entfernt.";
            }

            return await CompleteVerifiedRestoreAsync(
                archiveLocation,
                archiveRecord,
                restoreMarker,
                CreateRestoreMarkerText(
                    restoreMarker),
                restoredUid,
                recoveredInterruptedRestore:
                    true,
                warning:
                    warning,
                cancellationToken);
        }

        /*
         * Seit der gespeicherten Baseline existiert keine
         * neue byteidentische Nachricht.
         *
         * Damit hat der frühere Versuch nachweislich keine
         * wiederhergestellte Serverkopie hinterlassen.
         *
         * Der Marker darf entfernt und ein neuer APPEND
         * gestartet werden.
         */
        await ClearRestoreMarkerAsync(
            archiveLocation,
            archiveRecord,
            CreateRestoreMarkerText(
                restoreMarker),
            cancellationToken);

        return await RestoreAsync(
            archiveRecord.ArchiveMessageId,
            cancellationToken);
    }

    private async Task<ArchiveRestoreResult>
        RecoverAfterAppendFailureAsync(
            LocalMailArchiveLocation archiveLocation,
            ArchiveRestoreRecord archiveRecord,
            ArchiveRestoreMarker restoreMarker,
            string restoreMarkerText,
            Exception appendException,
            CancellationToken cancellationToken)
    {
        try
        {
            var recoveryScan =
                await FindMatchingServerCopiesCreatedAfterBaselineAsync(
                    archiveRecord,
                    restoreMarker,
                    cancellationToken);

            if (recoveryScan.MatchingUids.Count >
                0)
            {
                var restoredUid =
                    recoveryScan
                        .MatchingUids
                        .Max();

                string? warning =
                    null;

                if (recoveryScan.MatchingUids.Count >
                    1)
                {
                    warning =
                        "Nach dem unterbrochenen Upload wurden mehrere neue byteidentische Serverkopien gefunden.";
                }

                return await CompleteVerifiedRestoreAsync(
                    archiveLocation,
                    archiveRecord,
                    restoreMarker,
                    restoreMarkerText,
                    restoredUid,
                    recoveredInterruptedRestore:
                        true,
                    warning:
                        warning,
                    cancellationToken);
            }

            /*
             * Keine neue byteidentische Servermail:
             *
             * Der fehlgeschlagene APPEND hat damit keine
             * nachweisbare Kopie hinterlassen.
             */
            await ClearRestoreMarkerAsync(
                archiveLocation,
                archiveRecord,
                restoreMarkerText,
                cancellationToken);

            throw new MailArchiveRestoreException(
                "Die Nachricht konnte nicht auf den Mailserver hochgeladen werden. " +
                "Eine anschließende Sicherheitsprüfung hat keine neue byteidentische Serverkopie gefunden. " +
                "Das lokale Archiv bleibt vollständig erhalten und ein erneuter Versuch ist möglich.",
                appendException,
                serverCopyVerified:
                    false,
                recoveryStateUnresolved:
                    false,
                retryIsSafe:
                    true);
        }
        catch (MailArchiveRestoreException)
        {
            throw;
        }
        catch (Exception recoveryException)
        {
            throw new MailArchiveRestoreException(
                "Beim Wiederherstellen ist ein Fehler aufgetreten und anschließend konnte nicht eindeutig festgestellt werden, " +
                "ob der Server die Nachricht bereits angenommen hat. " +
                "Die lokale Archivkopie bleibt erhalten. Beim nächsten Versuch wird zuerst der Serverzustand geprüft.",
                new AggregateException(
                    appendException,
                    recoveryException),
                serverCopyVerified:
                    false,
                recoveryStateUnresolved:
                    true,
                retryIsSafe:
                    true);
        }
    }

    private async Task<ArchiveRestoreResult>
        RecoverAfterVerificationFailureAsync(
            LocalMailArchiveLocation archiveLocation,
            ArchiveRestoreRecord archiveRecord,
            ArchiveRestoreMarker restoreMarker,
            string restoreMarkerText,
            Exception verificationException,
            CancellationToken cancellationToken)
    {
        try
        {
            var recoveryScan =
                await FindMatchingServerCopiesCreatedAfterBaselineAsync(
                    archiveRecord,
                    restoreMarker,
                    cancellationToken);

            if (recoveryScan.MatchingUids.Count >
                0)
            {
                var restoredUid =
                    recoveryScan
                        .MatchingUids
                        .Max();

                string? warning =
                    null;

                if (recoveryScan.MatchingUids.Count >
                    1)
                {
                    warning =
                        "Bei der Sicherheitsprüfung wurden mehrere neue byteidentische Serverkopien gefunden.";
                }

                return await CompleteVerifiedRestoreAsync(
                    archiveLocation,
                    archiveRecord,
                    restoreMarker,
                    restoreMarkerText,
                    restoredUid,
                    recoveredInterruptedRestore:
                        true,
                    warning:
                        warning,
                    cancellationToken);
            }
        }
        catch
        {
            /*
             * Der ursprüngliche Verifikationsfehler bleibt
             * maßgeblich.
             *
             * Der zusätzliche Recovery-Versuch darf ihn
             * nicht in einen unsicheren Neu-Upload verwandeln.
             */
        }

        /*
         * APPEND wurde bereits bestätigt.
         *
         * Deshalb bleibt der Restore-Marker bestehen.
         * Beim nächsten Klick wird ausschließlich geprüft,
         * bevor irgendein neuer APPEND möglich ist.
         */
        throw new MailArchiveRestoreException(
            "Der Mailserver hat den Upload bestätigt, aber die anschließend gelesene Serverkopie konnte nicht eindeutig " +
            "gegen die lokale Archivdatei verifiziert werden. " +
            "Die lokale Archivkopie bleibt erhalten. Beim nächsten Versuch wird zuerst der Serverzustand geprüft.",
            verificationException,
            serverCopyVerified:
                false,
            recoveryStateUnresolved:
                true,
            retryIsSafe:
                true);
    }

    private async Task<ArchiveRestoreResult>
        CompleteVerifiedRestoreAsync(
            LocalMailArchiveLocation archiveLocation,
            ArchiveRestoreRecord archiveRecord,
            ArchiveRestoreMarker restoreMarker,
            string restoreMarkerText,
            uint restoredUniqueId,
            bool recoveredInterruptedRestore,
            string? warning,
            CancellationToken cancellationToken)
    {
        /*
         * Serverkopie existiert und wurde bytegenau
         * verifiziert.
         *
         * Erst jetzt darf archive.db bereinigt werden.
         */
        try
        {
            await RemoveArchiveDatabaseRecordAsync(
                archiveLocation,
                archiveRecord,
                restoreMarkerText,
                cancellationToken);
        }
        catch (Exception databaseException)
        {
            throw new MailArchiveRestoreException(
                "Die Nachricht wurde erfolgreich und bytegenau geprüft auf dem Mailserver wiederhergestellt, " +
                "aber der lokale Archivdatensatz konnte nicht entfernt werden. " +
                "Die lokale .eml bleibt erhalten. Ein erneuter Versuch erzeugt nicht blind eine weitere Serverkopie.",
                databaseException,
                serverCopyVerified:
                    true,
                recoveryStateUnresolved:
                    true,
                retryIsSafe:
                    true);
        }

        string? cleanupWarning =
            warning;

        /*
         * archive.db ist jetzt konsistent bereinigt.
         *
         * Erst danach entfernen wir die physische .eml.
         * Ein Fehler hier kann keinen Mailverlust verursachen,
         * sondern höchstens eine verwaiste lokale Datei.
         */
        try
        {
            if (File.Exists(
                    archiveRecord.LocalFilePath))
            {
                File.Delete(
                    archiveRecord.LocalFilePath);
            }
        }
        catch (Exception fileException)
        {
            var fileWarning =
                "Die Serverwiederherstellung ist abgeschlossen, die alte lokale .eml konnte jedoch nicht gelöscht werden:\n" +
                archiveRecord.LocalFilePath +
                "\n\n" +
                fileException.Message;

            cleanupWarning =
                string.IsNullOrWhiteSpace(
                    cleanupWarning)
                    ? fileWarning
                    : cleanupWarning +
                      "\n\n" +
                      fileWarning;
        }

        return new ArchiveRestoreResult(
            ArchiveMessageId:
                archiveRecord.ArchiveMessageId,

            SourceFolderId:
                archiveRecord.SourceFolderId,

            RestoredUniqueId:
                restoredUniqueId,

            ServerCopyVerified:
                true,

            ArchiveDatabaseEntryRemoved:
                true,

            LocalFileRemoved:
                !File.Exists(
                    archiveRecord.LocalFilePath),

            RecoveredInterruptedRestore:
                recoveredInterruptedRestore,

            Warning:
                cleanupWarning);
    }

    private async Task
        EnsureLocalArchiveFileMatchesAsync(
            ArchiveRestoreRecord archiveRecord,
            CancellationToken cancellationToken)
    {
        var fingerprint =
            await ComputeFileFingerprintAsync(
                archiveRecord.LocalFilePath,
                cancellationToken);

        if (fingerprint.FileSizeBytes !=
            archiveRecord.FileSizeBytes)
        {
            throw new InvalidOperationException(
                "Die lokale Archivdatei besitzt nicht mehr die erwartete Dateigröße. " +
                "Die Nachricht wird nicht wiederhergestellt.");
        }

        if (!string.Equals(
                fingerprint.Sha256,
                archiveRecord.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die lokale Archivdatei hat die SHA-256-Prüfung nicht bestanden. " +
                "Die Nachricht wird nicht wiederhergestellt.");
        }
    }

    private async Task<TargetFolderState>
        LoadTargetFolderStateAsync(
            ArchiveRestoreRecord archiveRecord,
            CancellationToken cancellationToken)
    {
        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            if (!client.Capabilities.HasFlag(
                    ImapCapabilities.UidPlus))
            {
                throw new NotSupportedException(
                    "Der IMAP-Server unterstützt kein UIDPLUS. " +
                    "Eine sichere Wiederherstellung mit eindeutiger APPENDUID ist deshalb nicht möglich.");
            }

            using var operationTimeoutSource =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            operationTimeoutSource.CancelAfter(
                ServerOperationTimeout);

            var operationCancellationToken =
                operationTimeoutSource.Token;

            var folder =
                await GetSelectableFolderAsync(
                    client,
                    archiveRecord.SourceFolderId,
                    operationCancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                operationCancellationToken);

            if (folder.UidValidity == 0)
            {
                throw new InvalidOperationException(
                    "Der ursprüngliche Zielordner besitzt keine gültige UIDVALIDITY.");
            }

            var uniqueIds =
                await folder.SearchAsync(
                    SearchQuery.All,
                    operationCancellationToken);

            var highestUid =
                uniqueIds.Count == 0
                    ? 0u
                    : uniqueIds
                        .Max(
                            uniqueId =>
                                uniqueId.Id);

            return new TargetFolderState(
                UidValidity:
                    folder.UidValidity,

                HighestUid:
                    highestUid);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private async Task<ServerRecoveryScan>
        FindMatchingServerCopiesCreatedAfterBaselineAsync(
            ArchiveRestoreRecord archiveRecord,
            ArchiveRestoreMarker restoreMarker,
            CancellationToken cancellationToken)
    {
        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            using var operationTimeoutSource =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            operationTimeoutSource.CancelAfter(
                ServerOperationTimeout);

            var operationCancellationToken =
                operationTimeoutSource.Token;

            var folder =
                await GetSelectableFolderAsync(
                    client,
                    archiveRecord.SourceFolderId,
                    operationCancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                operationCancellationToken);

            if (folder.UidValidity !=
                restoreMarker.TargetUidValidity)
            {
                throw new InvalidOperationException(
                    "UIDVALIDITY des ursprünglichen Mailordners hat sich seit Beginn der Wiederherstellung geändert. " +
                    "Der Serverzustand kann nicht mehr eindeutig anhand der gespeicherten Baseline bewertet werden.");
            }

            IList<UniqueId> candidateUids;

            /*
             * Eine Message-ID erlaubt eine deutlich kleinere
             * Server-Suche.
             *
             * Nachrichten ohne Message-ID bleiben trotzdem
             * vollständig recoverbar: dann prüfen wir alle
             * seit der Baseline neu vergebenen UIDs.
             */
            if (!string.IsNullOrWhiteSpace(
                    archiveRecord.MessageId))
            {
                var searchMessageId =
                    archiveRecord
                        .MessageId
                        .Trim()
                        .Trim(
                            '<',
                            '>');

                candidateUids =
                    await folder.SearchAsync(
                        SearchQuery.HeaderContains(
                            "Message-Id",
                            searchMessageId),
                        operationCancellationToken);
            }
            else
            {
                candidateUids =
                    await folder.SearchAsync(
                        SearchQuery.All,
                        operationCancellationToken);
            }

            var newCandidateUids =
                candidateUids
                    .Where(
                        uniqueId =>
                            uniqueId.Id >
                            restoreMarker
                                .BaselineHighestUid)
                    .OrderBy(
                        uniqueId =>
                            uniqueId.Id)
                    .ToArray();

            var matchingUids =
                new List<uint>();

            foreach (var candidateUid in
                     newCandidateUids)
            {
                operationCancellationToken
                    .ThrowIfCancellationRequested();

                try
                {
                    var fingerprint =
                        await ComputeServerMessageFingerprintAsync(
                            folder,
                            candidateUid,
                            operationCancellationToken);

                    if (fingerprint.FileSizeBytes !=
                        archiveRecord.FileSizeBytes)
                    {
                        continue;
                    }

                    if (!string.Equals(
                            fingerprint.Sha256,
                            archiveRecord.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    matchingUids.Add(
                        candidateUid.Id);
                }
                catch (OperationCanceledException)
                    when (operationCancellationToken
                        .IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    /*
                     * Ein einzelner verschwundener Kandidat
                     * darf die Prüfung der übrigen neuen UIDs
                     * nicht verhindern.
                     */
                }
            }

            return new ServerRecoveryScan(
                MatchingUids:
                    matchingUids);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private async Task
        VerifySpecificServerCopyAsync(
            ArchiveRestoreRecord archiveRecord,
            ArchiveRestoreMarker restoreMarker,
            uint uniqueId,
            CancellationToken cancellationToken)
    {
        if (uniqueId == 0)
        {
            throw new InvalidOperationException(
                "Der Mailserver hat keine gültige UID für die wiederhergestellte Nachricht geliefert.");
        }

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            using var operationTimeoutSource =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            operationTimeoutSource.CancelAfter(
                ServerOperationTimeout);

            var operationCancellationToken =
                operationTimeoutSource.Token;

            var folder =
                await GetSelectableFolderAsync(
                    client,
                    archiveRecord.SourceFolderId,
                    operationCancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                operationCancellationToken);

            if (folder.UidValidity !=
                restoreMarker.TargetUidValidity)
            {
                throw new InvalidOperationException(
                    "UIDVALIDITY des Zielordners hat sich während der Wiederherstellung geändert.");
            }

            var restoredUniqueId =
                new UniqueId(
                    uniqueId);

            var summaries =
                await folder.FetchAsync(
                    new[]
                    {
                        restoredUniqueId
                    },
                    MessageSummaryItems.UniqueId,
                    operationCancellationToken);

            if (!summaries.Any(
                    summary =>
                        summary.UniqueId ==
                        restoredUniqueId))
            {
                throw new InvalidOperationException(
                    "Die vom Server gemeldete neue UID ist im ursprünglichen Zielordner nicht vorhanden.");
            }

            var fingerprint =
                await ComputeServerMessageFingerprintAsync(
                    folder,
                    restoredUniqueId,
                    operationCancellationToken);

            if (fingerprint.FileSizeBytes !=
                archiveRecord.FileSizeBytes)
            {
                throw new InvalidOperationException(
                    "Die wiederhergestellte Servermail besitzt nicht dieselbe Dateigröße wie die lokale Archivkopie.");
            }

            if (!string.Equals(
                    fingerprint.Sha256,
                    archiveRecord.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Die wiederhergestellte Servermail besitzt nicht dieselbe SHA-256-Prüfsumme wie die lokale Archivkopie.");
            }
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static async Task<DateTimeOffset>
        DetermineInternalDateAsync(
            byte[] rawMessage,
            ArchiveRestoreRecord archiveRecord,
            CancellationToken cancellationToken)
    {
        try
        {
            using var stream =
                new MemoryStream(
                    rawMessage,
                    writable:
                        false);

            using var message =
                await MimeMessage.LoadAsync(
                    stream,
                    cancellationToken);

            if (message.Date !=
                DateTimeOffset.MinValue)
            {
                return message.Date;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Die lokale Archivdatei konnte vor der Wiederherstellung nicht mehr als gültige E-Mail gelesen werden.",
                exception);
        }

        return archiveRecord.MessageDateUtc
            ?? archiveRecord.ArchivedAtUtc;
    }

    private static void ValidateRestoreRecord(
        ArchiveRestoreRecord archiveRecord)
    {
        if (!archiveRecord.ServerDeleted)
        {
            throw new InvalidOperationException(
                "Die ursprüngliche Servermail ist laut Archivdatenbank noch nicht als gelöscht bestätigt. " +
                "Eine Wiederherstellung würde möglicherweise eine Dublette erzeugen und wird deshalb nicht ausgeführt.");
        }

        if (!string.Equals(
                archiveRecord.OperationStatus,
                "Completed",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Die Archivoperation befindet sich im Zustand „{archiveRecord.OperationStatus}“ und kann nicht sicher wiederhergestellt werden.");
        }

        if (string.IsNullOrWhiteSpace(
                archiveRecord.SourceFolderId))
        {
            throw new InvalidOperationException(
                "Der Archivdatensatz enthält keinen ursprünglichen Mailordner.");
        }

        if (archiveRecord.FileSizeBytes <= 0 ||
            string.IsNullOrWhiteSpace(
                archiveRecord.Sha256))
        {
            throw new InvalidOperationException(
                "Der Archivdatensatz enthält keine vollständigen Integritätsdaten.");
        }
    }

    private static async Task<IMailFolder>
        GetSelectableFolderAsync(
            ImapClient client,
            string folderId,
            CancellationToken cancellationToken)
    {
        IMailFolder folder;

        try
        {
            folder =
                await client.GetFolderAsync(
                    folderId,
                    cancellationToken);

            /*
             * OpenAsync ist gleichzeitig unsere definitive
             * Existenzprüfung. Ein nicht mehr vorhandener
             * ursprünglicher Ordner wird nicht automatisch
             * neu angelegt.
             */
            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            await folder.CloseAsync(
                expunge:
                    false,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Der ursprüngliche Mailordner „{folderId}“ existiert auf dem Mailserver nicht mehr oder kann nicht geöffnet werden. " +
                "Die Archivnachricht wurde nicht verändert.",
                exception);
        }

        if (folder.Attributes.HasFlag(
                FolderAttributes.NoSelect))
        {
            throw new InvalidOperationException(
                $"Der ursprüngliche Mailordner „{folderId}“ ist auf dem Mailserver nicht auswählbar. " +
                "Die Archivnachricht wurde nicht verändert.");
        }

        return folder;
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
                "Für das aktive Mailkonto sind keine Zugangsdaten gespeichert.");
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
                    SecureSocketOptions.SslOnConnect,
                    connectionTimeoutSource.Token);
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
                    authenticationTimeoutSource.Token);
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
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

    private static async Task<ArchiveRestoreRecord>
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
                m.MessageId,
                m.MessageDateUtc,
                m.ArchivedAtUtc,
                m.RelativeFilePath,
                m.Sha256,
                m.FileSizeBytes,
                m.ServerDeleted,
                o.Status,
                o.ErrorMessage
            FROM ArchiveMessages AS m
            INNER JOIN ArchiveOperations AS o
                ON o.OperationId =
                    m.OperationId
               AND o.AccountKey =
                    m.AccountKey
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
                "Die ausgewählte Nachricht wurde nicht im lokalen Mailarchiv gefunden.");
        }

        var relativeFilePath =
            reader.GetString(6);

        return new ArchiveRestoreRecord(
            ArchiveMessageId:
                reader.GetString(0),

            OperationId:
                reader.GetString(1),

            SourceFolderId:
                reader.GetString(2),

            MessageId:
                GetNullableString(
                    reader,
                    3),

            MessageDateUtc:
                ParseNullableDateTimeOffset(
                    reader,
                    4),

            ArchivedAtUtc:
                ParseRequiredDateTimeOffset(
                    reader.GetString(5),
                    "ArchivedAtUtc"),

            RelativeFilePath:
                relativeFilePath,

            LocalFilePath:
                ResolveArchiveFilePath(
                    archiveLocation.AccountDirectory,
                    relativeFilePath),

            Sha256:
                reader.GetString(7),

            FileSizeBytes:
                reader.GetInt64(8),

            ServerDeleted:
                reader.GetInt64(9) != 0,

            OperationStatus:
                reader.GetString(10),

            OperationErrorMessage:
                GetNullableString(
                    reader,
                    11));
    }

    private static async Task WriteRestoreMarkerAsync(
        LocalMailArchiveLocation archiveLocation,
        ArchiveRestoreRecord archiveRecord,
        string restoreMarkerText,
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
            UPDATE ArchiveOperations
            SET ErrorMessage =
                    $restoreMarker
            WHERE OperationId =
                    $operationId
              AND AccountKey =
                    $accountKey
              AND Status =
                    'Completed'
              AND
                (
                    ErrorMessage IS NULL
                    OR TRIM(ErrorMessage) = ''
                );
            """;

        command.Parameters.AddWithValue(
            "$restoreMarker",
            restoreMarkerText);

        command.Parameters.AddWithValue(
            "$operationId",
            archiveRecord.OperationId);

        command.Parameters.AddWithValue(
            "$accountKey",
            archiveLocation.AccountKey);

        var updatedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (updatedRows != 1)
        {
            throw new InvalidOperationException(
                "Die Archivoperation konnte vor dem Serverupload nicht eindeutig für die Wiederherstellung reserviert werden.");
        }
    }

    private static async Task ClearRestoreMarkerAsync(
        LocalMailArchiveLocation archiveLocation,
        ArchiveRestoreRecord archiveRecord,
        string expectedRestoreMarker,
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
            UPDATE ArchiveOperations
            SET ErrorMessage =
                    NULL
            WHERE OperationId =
                    $operationId
              AND AccountKey =
                    $accountKey
              AND Status =
                    'Completed'
              AND ErrorMessage =
                    $expectedRestoreMarker;
            """;

        command.Parameters.AddWithValue(
            "$operationId",
            archiveRecord.OperationId);

        command.Parameters.AddWithValue(
            "$accountKey",
            archiveLocation.AccountKey);

        command.Parameters.AddWithValue(
            "$expectedRestoreMarker",
            expectedRestoreMarker);

        var updatedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (updatedRows != 1)
        {
            throw new InvalidOperationException(
                "Der Wiederherstellungsmarker konnte nicht eindeutig zurückgesetzt werden.");
        }
    }

    private static async Task RemoveArchiveDatabaseRecordAsync(
        LocalMailArchiveLocation archiveLocation,
        ArchiveRestoreRecord archiveRecord,
        string expectedRestoreMarker,
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

        /*
         * Vor dem Löschen prüfen wir nochmals, dass genau
         * unser Restore-Marker weiterhin auf der Operation
         * liegt.
         */
        await using (var verifyOperationCommand =
                     connection.CreateCommand())
        {
            verifyOperationCommand.Transaction =
                transaction;

            verifyOperationCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM ArchiveOperations
                WHERE OperationId =
                        $operationId
                  AND AccountKey =
                        $accountKey
                  AND Status =
                        'Completed'
                  AND ErrorMessage =
                        $expectedRestoreMarker;
                """;

            verifyOperationCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

            verifyOperationCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            verifyOperationCommand.Parameters.AddWithValue(
                "$expectedRestoreMarker",
                expectedRestoreMarker);

            var operationCount =
                Convert.ToInt64(
                    await verifyOperationCommand
                        .ExecuteScalarAsync(
                            cancellationToken),
                    CultureInfo.InvariantCulture);

            if (operationCount != 1)
            {
                throw new InvalidOperationException(
                    "Die Archivoperation befindet sich nicht mehr im erwarteten Wiederherstellungszustand.");
            }
        }

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
                  AND OperationId =
                        $operationId
                  AND AccountKey =
                        $accountKey
                  AND ServerDeleted =
                        1;
                """;

            deleteMessageCommand.Parameters.AddWithValue(
                "$archiveMessageId",
                archiveRecord.ArchiveMessageId);

            deleteMessageCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

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
                    "Der wiederhergestellte Archivdatensatz konnte nicht eindeutig entfernt werden.");
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

            remainingMessages =
                Convert.ToInt64(
                    await countCommand
                        .ExecuteScalarAsync(
                            cancellationToken),
                    CultureInfo.InvariantCulture);
        }

        if (remainingMessages == 0)
        {
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
                        'Completed'
                  AND ErrorMessage =
                        $expectedRestoreMarker;
                """;

            deleteOperationCommand.Parameters.AddWithValue(
                "$operationId",
                archiveRecord.OperationId);

            deleteOperationCommand.Parameters.AddWithValue(
                "$accountKey",
                archiveLocation.AccountKey);

            deleteOperationCommand.Parameters.AddWithValue(
                "$expectedRestoreMarker",
                expectedRestoreMarker);

            var deletedOperations =
                await deleteOperationCommand
                    .ExecuteNonQueryAsync(
                        cancellationToken);

            if (deletedOperations != 1)
            {
                throw new InvalidOperationException(
                    "Die abgeschlossene Archivoperation konnte nach dem Restore nicht eindeutig entfernt werden.");
            }
        }
        else
        {
            await using var updateOperationCommand =
                connection.CreateCommand();

            updateOperationCommand.Transaction =
                transaction;

            updateOperationCommand.CommandText =
                """
                UPDATE ArchiveOperations
                SET
                    MessageCount =
                        $messageCount,

                    ErrorMessage =
                        NULL
                WHERE OperationId =
                        $operationId
                  AND AccountKey =
                        $accountKey
                  AND Status =
                        'Completed'
                  AND ErrorMessage =
                        $expectedRestoreMarker;
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

            updateOperationCommand.Parameters.AddWithValue(
                "$expectedRestoreMarker",
                expectedRestoreMarker);

            var updatedOperations =
                await updateOperationCommand
                    .ExecuteNonQueryAsync(
                        cancellationToken);

            if (updatedOperations != 1)
            {
                throw new InvalidOperationException(
                    "Die verbleibende Archivoperation konnte nach dem Restore nicht konsistent aktualisiert werden.");
            }
        }

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static string CreateRestoreMarkerText(
        ArchiveRestoreMarker marker)
    {
        return
            RestoreMarkerPrefix +
            marker.ArchiveMessageId +
            "|" +
            marker.TargetUidValidity.ToString(
                CultureInfo.InvariantCulture) +
            "|" +
            marker.BaselineHighestUid.ToString(
                CultureInfo.InvariantCulture);
    }

    private static ArchiveRestoreMarker?
        TryParseRestoreMarker(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value) ||
            !value.StartsWith(
                RestoreMarkerPrefix,
                StringComparison.Ordinal))
        {
            return null;
        }

        var payload =
            value[
                RestoreMarkerPrefix.Length..];

        var parts =
            payload.Split(
                '|');

        if (parts.Length != 3 ||
            string.IsNullOrWhiteSpace(
                parts[0]) ||
            !uint.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var uidValidity) ||
            uidValidity == 0 ||
            !uint.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var baselineHighestUid))
        {
            return null;
        }

        return new ArchiveRestoreMarker(
            ArchiveMessageId:
                parts[0],

            TargetUidValidity:
                uidValidity,

            BaselineHighestUid:
                baselineHighestUid);
    }

    private static async Task<ArchiveFileFingerprint>
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
                "Die lokale Archivdatei ist nicht mehr vorhanden.",
                filePath);
        }

        if (fileInfo.Length <= 0)
        {
            throw new InvalidOperationException(
                "Die lokale Archivdatei ist leer.");
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

    private static ArchiveFileFingerprint
        ComputeByteArrayFingerprint(
            byte[] content)
    {
        var sha256 =
            Convert
                .ToHexString(
                    SHA256.HashData(
                        content))
                .ToLowerInvariant();

        return new ArchiveFileFingerprint(
            FileSizeBytes:
                content.LongLength,

            Sha256:
                sha256);
    }

    private static async Task<ArchiveFileFingerprint>
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

    private static async Task<ArchiveFileFingerprint>
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

        return new ArchiveFileFingerprint(
            FileSizeBytes:
                totalBytes,

            Sha256:
                Convert
                    .ToHexString(
                        hash.GetHashAndReset())
                    .ToLowerInvariant());
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
                "Die lokale Archivdatenbank wurde nicht gefunden.",
                databasePath);
        }

        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    databasePath,

                Mode =
                    SqliteOpenMode.ReadWrite,

                Cache =
                    SqliteCacheMode.Shared
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

    private static string?
        GetNullableString(
            SqliteDataReader reader,
            int ordinal)
    {
        return reader.IsDBNull(
            ordinal)
            ? null
            : reader.GetString(
                ordinal);
    }

    private static DateTimeOffset?
        ParseNullableDateTimeOffset(
            SqliteDataReader reader,
            int ordinal)
    {
        if (reader.IsDBNull(
                ordinal))
        {
            return null;
        }

        var value =
            reader.GetString(
                ordinal);

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTimeOffset
        ParseRequiredDateTimeOffset(
            string value,
            string columnName)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed;
        }

        throw new InvalidDataException(
            $"Die Archivdatenbank enthält für „{columnName}“ keinen gültigen Zeitwert.");
    }

    private sealed record ArchiveRestoreRecord(
        string ArchiveMessageId,
        string OperationId,
        string SourceFolderId,
        string? MessageId,
        DateTimeOffset? MessageDateUtc,
        DateTimeOffset ArchivedAtUtc,
        string RelativeFilePath,
        string LocalFilePath,
        string Sha256,
        long FileSizeBytes,
        bool ServerDeleted,
        string OperationStatus,
        string? OperationErrorMessage);

    private sealed record ArchiveRestoreMarker(
        string ArchiveMessageId,
        uint TargetUidValidity,
        uint BaselineHighestUid);

    private sealed record ArchiveFileFingerprint(
        long FileSizeBytes,
        string Sha256);

    private sealed record TargetFolderState(
        uint UidValidity,
        uint HighestUid);

    private sealed record ServerRecoveryScan(
        IReadOnlyList<uint> MatchingUids);
}

public sealed record ArchiveRestoreResult(
    string ArchiveMessageId,
    string SourceFolderId,
    uint RestoredUniqueId,
    bool ServerCopyVerified,
    bool ArchiveDatabaseEntryRemoved,
    bool LocalFileRemoved,
    bool RecoveredInterruptedRestore,
    string? Warning);

public sealed class MailArchiveRestoreException
    : Exception
{
    public MailArchiveRestoreException(
        string message,
        Exception? innerException,
        bool serverCopyVerified,
        bool recoveryStateUnresolved,
        bool retryIsSafe)
        : base(
            message,
            innerException)
    {
        ServerCopyVerified =
            serverCopyVerified;

        RecoveryStateUnresolved =
            recoveryStateUnresolved;

        RetryIsSafe =
            retryIsSafe;
    }

    public bool ServerCopyVerified
    { get; }

    public bool RecoveryStateUnresolved
    { get; }

    public bool RetryIsSafe
    { get; }
}