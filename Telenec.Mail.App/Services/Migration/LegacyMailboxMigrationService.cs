using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Migration;

public sealed class LegacyMailboxMigrationService
{
    private const string LegacyImapAddress =
        "80.79.225.35";

    private const string LegacyTlsHost =
        "necnet.de";

    private const int LegacyImapPort =
        143;

    private const string TargetImapHost =
        "mail.necnet.de";

    private const int TargetImapPort =
        993;

    private const string MigrationMarkerPrefix =
        "TelenecMigration-";

    private static readonly MessageFlags TransferableFlags =
        MessageFlags.Seen |
        MessageFlags.Answered |
        MessageFlags.Flagged |
        MessageFlags.Draft;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly RawImapAppendService
        _rawImapAppendService;

    public LegacyMailboxMigrationService(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _rawImapAppendService =
            new RawImapAppendService(
                mailAccountStore,
                credentialStore);
    }

    public async Task<LegacyMailboxMigrationResult>
        RunAsync(
            LegacyMailMigrationPlan plan,
            string legacyUserName,
            string legacyPassword,
            IProgress<LegacyMailboxMigrationProgress>?
                progress = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            plan);

        if (!plan.IsReady)
        {
            return Failure(
                "Der Migrationsplan ist nicht vollständig migrationsfähig.",
                0,
                0,
                null,
                null,
                false);
        }

        if (string.IsNullOrWhiteSpace(
                legacyUserName))
        {
            return Failure(
                "Die E-Mail-Adresse des alten Postfachs fehlt.",
                0,
                0,
                null,
                null,
                false);
        }

        if (string.IsNullOrEmpty(
                legacyPassword))
        {
            return Failure(
                "Das Passwort des alten Postfachs fehlt.",
                0,
                0,
                null,
                null,
                false);
        }

        var migratedCount =
            0;

        var alreadyPresentCount =
            0;

        using var sourceClient =
            await CreateLegacyClientAsync(
                legacyUserName,
                legacyPassword,
                cancellationToken);

        using var targetClient =
            await CreateTargetClientAsync(
                cancellationToken);

        var encounteredTargetFolders =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!sourceClient.Capabilities.HasFlag(
                    ImapCapabilities.UidPlus))
            {
                return Failure(
                    "Der alte Mailserver unterstützt kein UIDPLUS. " +
                    "Eine sicher selektive Löschung ist nicht möglich.",
                    migratedCount,
                    alreadyPresentCount,
                    null,
                    null,
                    false);
            }

            if (!targetClient.Capabilities.HasFlag(
                    ImapCapabilities.UidPlus))
            {
                return Failure(
                    "Der neue Mailserver unterstützt kein UIDPLUS. " +
                    "Die Migration wird vor Änderungen abgebrochen.",
                    migratedCount,
                    alreadyPresentCount,
                    null,
                    null,
                    false);
            }

            var totalMessages =
                plan.PlannedMessageCount;

            foreach (var folderPlan in
                     plan.Folders.Where(
                         item =>
                             item.CanMigrate))
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var sourceFolder =
                    await sourceClient.GetFolderAsync(
                        folderPlan.SourceFolderName,
                        cancellationToken);

                if (sourceFolder.Attributes.HasFlag(
                        FolderAttributes.NoSelect))
                {
                    return Failure(
                        $"Der Quellordner „{folderPlan.SourceFolderName}“ kann nicht geöffnet werden.",
                        migratedCount,
                        alreadyPresentCount,
                        folderPlan.SourceFolderName,
                        null,
                        false);
                }

                var targetFolder =
                    await ResolveTargetFolderAsync(
                        targetClient,
                        sourceFolder,
                        folderPlan,
                        cancellationToken);

                encounteredTargetFolders.Add(
                    targetFolder.FullName);

                if (!sourceFolder.IsOpen)
                {
                    await sourceFolder.OpenAsync(
                        FolderAccess.ReadWrite,
                        cancellationToken);
                }

                var sourceUids =
                    await sourceFolder.SearchAsync(
                        SearchQuery.All,
                        cancellationToken);

                if (sourceUids.Count !=
                    folderPlan.MessageCount)
                {
                    return Failure(
                        $"Der Quellordner „{folderPlan.SourceFolderName}“ hat sich seit Erstellung des Migrationsplans verändert. " +
                        $"Plan: {folderPlan.MessageCount:N0}, aktuell: {sourceUids.Count:N0} Nachrichten. " +
                        "Bitte Bestandsaufnahme und Migrationsplan erneut erstellen.",
                        migratedCount,
                        alreadyPresentCount,
                        folderPlan.SourceFolderName,
                        null,
                        false);
                }

                foreach (var sourceUid in
                         sourceUids)
                {
                    cancellationToken
                        .ThrowIfCancellationRequested();

                    var sourceSummary =
                        await GetSummaryAsync(
                            sourceFolder,
                            sourceUid,
                            cancellationToken);

                    if (!sourceSummary.InternalDate.HasValue)
                    {
                        return Failure(
                            "Die Quellmail besitzt kein internes Empfangsdatum. " +
                            "Sie wurde nicht gelöscht.",
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            null,
                            false);
                    }

                    if ((sourceSummary.Flags ??
                         MessageFlags.None)
                        .HasFlag(
                            MessageFlags.Deleted))
                    {
                        return Failure(
                            "Eine Quellmail ist bereits mit dem IMAP-Flag \\Deleted markiert. " +
                            "Die Migration wurde gestoppt, ohne diese Nachricht zu verändern.",
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            null,
                            false);
                    }

                    var sourceBytes =
                        await ReadMessageBytesAsync(
                            sourceFolder,
                            sourceUid,
                            cancellationToken);

                    var sourceHash =
                        ComputeSha256(
                            sourceBytes);

                    using var parseStream =
                        new MemoryStream(
                            sourceBytes,
                            writable: false);

                    var message =
                        await MimeMessage.LoadAsync(
                            parseStream,
                            cancellationToken);

                    var subject =
                        string.IsNullOrWhiteSpace(
                            message.Subject)
                            ? "(ohne Betreff)"
                            : message.Subject.Trim();

                    var sourceFlags =
                        (sourceSummary.Flags ??
                         MessageFlags.None) &
                        TransferableFlags;

                    var sourceKeywords =
                        sourceSummary.Keywords?
                            .ToArray()
                        ??
                        Array.Empty<string>();

                    var migrationMarker =
                        CreateMigrationMarker(
                            folderPlan.SourceFolderName,
                            sourceUid);

                    if (sourceKeywords.Contains(
                            migrationMarker,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        return Failure(
                            "Die Quellmail besitzt unerwartet bereits den internen Telenec-Migrationsmarker. " +
                            "Sie wurde nicht verändert.",
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            subject,
                            false);
                    }

                    var targetKeywordsDuringMigration =
                        sourceKeywords
                            .Append(
                                migrationMarker)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                    progress?.Report(
                        new LegacyMailboxMigrationProgress(
                            ProcessedMessageCount:
                                migratedCount +
                                alreadyPresentCount,

                            TotalMessageCount:
                                totalMessages,

                            CurrentSourceFolder:
                                folderPlan.SourceFolderName,

                            CurrentTargetFolder:
                                targetFolder.FullName,

                            CurrentSubject:
                                subject,

                            StatusText:
                                "Quellmail wird geprüft …"));

                    var markedTargetUids =
                        await FindMarkedTargetUidsAsync(
                            targetFolder,
                            migrationMarker,
                            cancellationToken);

                    if (markedTargetUids.Count > 1)
                    {
                        return Failure(
                            $"Im Ziel wurden {markedTargetUids.Count:N0} Nachrichten mit demselben internen Migrationsmarker gefunden. " +
                            "Die Quelle bleibt unverändert.",
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            subject,
                            false);
                    }

                    UniqueId targetUid;

                    var appendedNow =
                        false;

                    if (markedTargetUids.Count == 1)
                    {
                        targetUid =
                            markedTargetUids[0];

                        var resumeVerification =
                            await VerifyTargetMessageAsync(
                                targetFolder,
                                targetUid,
                                sourceBytes,
                                sourceHash,
                                sourceFlags,
                                targetKeywordsDuringMigration,
                                sourceSummary.InternalDate.Value,
                                cancellationToken);

                        if (!resumeVerification.Success)
                        {
                            return Failure(
                                "Eine markierte frühere Zielkopie wurde gefunden, konnte aber nicht vollständig bestätigt werden. " +
                                "Die Quelle wurde NICHT gelöscht.\n\n" +
                                resumeVerification.Details,
                                migratedCount,
                                alreadyPresentCount,
                                folderPlan.SourceFolderName,
                                subject,
                                false);
                        }

                        progress?.Report(
                            new LegacyMailboxMigrationProgress(
                                ProcessedMessageCount:
                                    migratedCount +
                                    alreadyPresentCount,

                                TotalMessageCount:
                                    totalMessages,

                                CurrentSourceFolder:
                                    folderPlan.SourceFolderName,

                                CurrentTargetFolder:
                                    targetFolder.FullName,

                                CurrentSubject:
                                    subject,

                                StatusText:
                                    "Bereits übertragene markierte Zielkopie wurde eindeutig wiedergefunden."));
                    }
                    else
                    {
                        var exactFormatOptions =
                            CreateExactFormatOptions(
                                sourceBytes);

                        var mailKitAppendBytes =
                            await SerializeAsMailKitAppendWouldAsync(
                                message,
                                exactFormatOptions,
                                cancellationToken);

                        var requiresRawAppend =
                            !sourceBytes.AsSpan()
                                .SequenceEqual(
                                    mailKitAppendBytes);

                        if (requiresRawAppend)
                        {
                            var normalizationArtifacts =
                                await FindNormalizationArtifactsAsync(
                                    targetFolder,
                                    mailKitAppendBytes,
                                    sourceFlags,
                                    sourceKeywords,
                                    sourceSummary.InternalDate.Value,
                                    cancellationToken);

                            if (normalizationArtifacts.Count > 0)
                            {
                                return Failure(
                                    normalizationArtifacts.Count == 1
                                        ? "Im Ziel wurde eine ältere normalisierte Kopie dieser Mail gefunden. " +
                                          "Sie entspricht exakt der von MailKit erzeugten Variante, ist aber nicht byteidentisch mit der Quelle. " +
                                          "Aus Sicherheitsgründen wird diese Zielmail nicht automatisch gelöscht. Die Quelle bleibt vollständig erhalten."
                                        : $"Im Ziel wurden {normalizationArtifacts.Count:N0} ältere normalisierte Varianten dieser Mail gefunden. " +
                                          "Sie werden nicht automatisch verändert. Die Quelle bleibt vollständig erhalten.",
                                    migratedCount,
                                    alreadyPresentCount,
                                    folderPlan.SourceFolderName,
                                    subject,
                                    false);
                            }

                            progress?.Report(
                                new LegacyMailboxMigrationProgress(
                                    ProcessedMessageCount:
                                        migratedCount +
                                        alreadyPresentCount,

                                    TotalMessageCount:
                                        totalMessages,

                                    CurrentSourceFolder:
                                        folderPlan.SourceFolderName,

                                    CurrentTargetFolder:
                                        targetFolder.FullName,

                                    CurrentSubject:
                                        subject,

                                    StatusText:
                                        "MailKit würde die Rohdaten verändern. Exakter RAW-IMAP-Fallback wird verwendet …"));

                            targetUid =
                                await _rawImapAppendService
                                    .AppendAsync(
                                        targetFolder.FullName,
                                        sourceBytes,
                                        sourceFlags,
                                        targetKeywordsDuringMigration,
                                        sourceSummary.InternalDate.Value,
                                        cancellationToken);

                            appendedNow =
                                true;

                            /*
                             * Der RAW-APPEND läuft über eine
                             * eigene IMAP-Verbindung.
                             *
                             * Die bereits geöffnete
                             * MailKit-Verbindung kennt die
                             * dadurch neu angelegte UID noch
                             * nicht zwingend.
                             *
                             * Deshalb wird der Zielordner
                             * ausdrücklich geschlossen und
                             * neu geöffnet, bevor die neue UID
                             * gelesen oder verifiziert wird.
                             */
                            await RefreshFolderAsync(
                                targetFolder,
                                FolderAccess.ReadOnly,
                                cancellationToken);
                        }
                        else
                        {
                            progress?.Report(
                                new LegacyMailboxMigrationProgress(
                                    ProcessedMessageCount:
                                        migratedCount +
                                        alreadyPresentCount,

                                    TotalMessageCount:
                                        totalMessages,

                                    CurrentSourceFolder:
                                        folderPlan.SourceFolderName,

                                    CurrentTargetFolder:
                                        targetFolder.FullName,

                                    CurrentSubject:
                                        subject,

                                    StatusText:
                                        "Mail wird auf den neuen Server übertragen …"));

                            var request =
                                new AppendRequest(
                                    message,
                                    sourceFlags,
                                    targetKeywordsDuringMigration,
                                    sourceSummary.InternalDate.Value);

                            var appendedUid =
                                await targetFolder.AppendAsync(
                                    exactFormatOptions,
                                    request,
                                    cancellationToken);

                            if (!appendedUid.HasValue ||
                                !appendedUid.Value.IsValid)
                            {
                                return Failure(
                                    "Der neue Server hat nach dem APPEND keine eindeutige Ziel-UID geliefert. " +
                                    "Die Quellmail bleibt erhalten.",
                                    migratedCount,
                                    alreadyPresentCount,
                                    folderPlan.SourceFolderName,
                                    subject,
                                    false);
                            }

                            targetUid =
                                appendedUid.Value;

                            appendedNow =
                                true;
                        }
                    }

                    progress?.Report(
                        new LegacyMailboxMigrationProgress(
                            ProcessedMessageCount:
                                migratedCount +
                                alreadyPresentCount,

                            TotalMessageCount:
                                totalMessages,

                            CurrentSourceFolder:
                                folderPlan.SourceFolderName,

                            CurrentTargetFolder:
                                targetFolder.FullName,

                            CurrentSubject:
                                subject,

                            StatusText:
                                "Zielmail wird bytegenau verifiziert …"));

                    var targetVerification =
                        await VerifyTargetMessageAsync(
                            targetFolder,
                            targetUid,
                            sourceBytes,
                            sourceHash,
                            sourceFlags,
                            targetKeywordsDuringMigration,
                            sourceSummary.InternalDate.Value,
                            cancellationToken);

                    if (!targetVerification.Success)
                    {
                        if (appendedNow)
                        {
                            await TryDeleteTargetMessageAsync(
                                targetFolder,
                                targetUid);
                        }

                        return Failure(
                            "Die Zielmail konnte nicht vollständig bestätigt werden. " +
                            "Die Quellmail wurde NICHT gelöscht.\n\n" +
                            targetVerification.Details,
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            subject,
                            false);
                    }

                    cancellationToken
                        .ThrowIfCancellationRequested();

                    var sourceBytesBeforeDelete =
                        await ReadMessageBytesAsync(
                            sourceFolder,
                            sourceUid,
                            cancellationToken);

                    if (!sourceBytes.AsSpan()
                            .SequenceEqual(
                                sourceBytesBeforeDelete))
                    {
                        return Failure(
                            "Die Quellmail hat sich während der Migration verändert. " +
                            "Sie wurde nicht gelöscht.",
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            subject,
                            false);
                    }

                    var sourceSummaryBeforeDelete =
                        await GetSummaryAsync(
                            sourceFolder,
                            sourceUid,
                            cancellationToken);

                    if (!MetadataMatches(
                            sourceSummaryBeforeDelete,
                            sourceFlags,
                            sourceKeywords,
                            sourceSummary.InternalDate.Value))
                    {
                        return Failure(
                            "Die IMAP-Metadaten der Quellmail haben sich während der Migration verändert. " +
                            "Sie wurde nicht gelöscht.",
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            subject,
                            false);
                    }

                    progress?.Report(
                        new LegacyMailboxMigrationProgress(
                            ProcessedMessageCount:
                                migratedCount +
                                alreadyPresentCount,

                            TotalMessageCount:
                                totalMessages,

                            CurrentSourceFolder:
                                folderPlan.SourceFolderName,

                            CurrentTargetFolder:
                                targetFolder.FullName,

                            CurrentSubject:
                                subject,

                            StatusText:
                                "Ziel bestätigt. Quell-UID wird jetzt selektiv gelöscht …"));

                    try
                    {
                        await sourceFolder.AddFlagsAsync(
                            sourceUid,
                            MessageFlags.Deleted,
                            silent: true,
                            CancellationToken.None);

                        await sourceFolder.ExpungeAsync(
                            new[]
                            {
                                sourceUid
                            },
                            CancellationToken.None);
                    }
                    catch
                    {
                        try
                        {
                            var stillExists =
                                await SourceUidExistsAsync(
                                    sourceFolder,
                                    sourceUid,
                                    CancellationToken.None);

                            if (stillExists)
                            {
                                await sourceFolder.RemoveFlagsAsync(
                                    sourceUid,
                                    MessageFlags.Deleted,
                                    silent: true,
                                    CancellationToken.None);
                            }
                        }
                        catch
                        {
                        }

                        throw;
                    }

                    var sourceStillExists =
                        await SourceUidExistsAsync(
                            sourceFolder,
                            sourceUid,
                            CancellationToken.None);

                    if (sourceStillExists)
                    {
                        return Failure(
                            "Die Quellmail wurde zum Löschen markiert, ist nach UID-EXPUNGE aber weiterhin vorhanden.",
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            subject,
                            true);
                    }

                    try
                    {
                        await EnsureFolderOpenAsync(
                            targetFolder,
                            FolderAccess.ReadWrite,
                            CancellationToken.None);

                        await targetFolder.RemoveFlagsAsync(
                            targetUid,
                            MessageFlags.None,
                            new HashSet<string>(
                                StringComparer.OrdinalIgnoreCase)
                            {
                                migrationMarker
                            },
                            silent: true,
                            CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        return Failure(
                            "Die Quellmail wurde sicher gelöscht und die Zielmail ist byteidentisch vorhanden, " +
                            "aber der temporäre Telenec-Migrationsmarker konnte anschließend nicht entfernt werden.\n\n" +
                            exception.Message,
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            subject,
                            true);
                    }

                    var finalTargetVerification =
                        await VerifyTargetMessageAsync(
                            targetFolder,
                            targetUid,
                            sourceBytes,
                            sourceHash,
                            sourceFlags,
                            sourceKeywords,
                            sourceSummary.InternalDate.Value,
                            CancellationToken.None);

                    if (!finalTargetVerification.Success)
                    {
                        return Failure(
                            "Die Quelle wurde erfolgreich gelöscht, aber die abschließende Zielprüfung ist fehlgeschlagen. " +
                            "Die Migration wurde sofort gestoppt; der Zielbestand muss geprüft werden.\n\n" +
                            finalTargetVerification.Details,
                            migratedCount,
                            alreadyPresentCount,
                            folderPlan.SourceFolderName,
                            subject,
                            true);
                    }

                    if (appendedNow)
                    {
                        migratedCount++;
                    }
                    else
                    {
                        alreadyPresentCount++;
                    }

                    progress?.Report(
                        new LegacyMailboxMigrationProgress(
                            ProcessedMessageCount:
                                migratedCount +
                                alreadyPresentCount,

                            TotalMessageCount:
                                totalMessages,

                            CurrentSourceFolder:
                                folderPlan.SourceFolderName,

                            CurrentTargetFolder:
                                targetFolder.FullName,

                            CurrentSubject:
                                subject,

                            StatusText:
                                "Mail vollständig verifiziert und Quelle gelöscht."));
                }
            }

            long remainingMessages =
                0;

            foreach (var folderPlan in
                     plan.Folders.Where(
                         item =>
                             item.CanMigrate))
            {
                var folder =
                    await sourceClient.GetFolderAsync(
                        folderPlan.SourceFolderName,
                        CancellationToken.None);

                await EnsureFolderOpenAsync(
                    folder,
                    FolderAccess.ReadOnly,
                    CancellationToken.None);

                var remainingUids =
                    await folder.SearchAsync(
                        SearchQuery.All,
                        CancellationToken.None);

                remainingMessages +=
                    remainingUids.Count;
            }

            if (remainingMessages > 0)
            {
                return new LegacyMailboxMigrationResult(
                    Success:
                        false,

                    Message:
                        $"Die Übertragung wurde abgeschlossen, aber auf dem alten Server befinden sich in den geplanten Ordnern noch {remainingMessages:N0} Nachrichten.",

                    MigratedMessageCount:
                        migratedCount,

                    AlreadyPresentMessageCount:
                        alreadyPresentCount,

                    RemainingSourceMessageCount:
                        remainingMessages,

                    FailedFolder:
                        null,

                    FailedSubject:
                        null,

                    SourceDeletedBeforeFailure:
                        false);
            }

            try
            {
                await CleanupOrphanMigrationMarkersAsync(
                    targetClient,
                    encounteredTargetFolders,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                return Failure(
                    "Alle geplanten Quellnachrichten wurden migriert und der Altbestand ist leer, " +
                    "aber mindestens ein interner Telenec-Migrationsmarker konnte im Ziel nicht bereinigt werden.\n\n" +
                    exception.Message,
                    migratedCount,
                    alreadyPresentCount,
                    null,
                    null,
                    true);
            }

            return new LegacyMailboxMigrationResult(
                Success:
                    true,

                Message:
                    "Alle geplanten Nachrichten wurden vollständig auf dem neuen Server bestätigt und anschließend vom alten Server entfernt.",

                MigratedMessageCount:
                    migratedCount,

                AlreadyPresentMessageCount:
                    alreadyPresentCount,

                RemainingSourceMessageCount:
                    0,

                FailedFolder:
                    null,

                FailedSubject:
                    null,

                SourceDeletedBeforeFailure:
                    false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(
                "Die Migration wurde sicher gestoppt.\n\n" +
                exception.Message,
                migratedCount,
                alreadyPresentCount,
                null,
                null,
                false);
        }
        finally
        {
            await DisconnectSafelyAsync(
                sourceClient);

            await DisconnectSafelyAsync(
                targetClient);
        }
    }

    private async Task<ImapClient>
        CreateTargetClientAsync(
            CancellationToken cancellationToken)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            throw new InvalidOperationException(
                "Es ist kein aktives Telenec-Mail-Konto eingerichtet.");
        }

        var credential =
            await _credentialStore
                .ReadAsync(
                    account.AccountId,
                    cancellationToken);

        if (credential is null ||
            string.IsNullOrEmpty(
                credential.Password))
        {
            throw new InvalidOperationException(
                "Für das neue Telenec-Mail-Konto sind keine gültigen Zugangsdaten gespeichert.");
        }

        var client =
            new ImapClient
            {
                Timeout =
                    30000
            };

        try
        {
            await client.ConnectAsync(
                TargetImapHost,
                TargetImapPort,
                SecureSocketOptions.SslOnConnect,
                cancellationToken);

            await client.AuthenticateAsync(
                account.EmailAddress,
                credential.Password,
                cancellationToken);

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    private static async Task<ImapClient>
        CreateLegacyClientAsync(
            string userName,
            string password,
            CancellationToken cancellationToken)
    {
        var client =
            new ImapClient
            {
                Timeout =
                    30000
            };

        Socket? socket =
            null;

        try
        {
            socket =
                new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);

            await socket.ConnectAsync(
                new IPEndPoint(
                    IPAddress.Parse(
                        LegacyImapAddress),
                    LegacyImapPort),
                cancellationToken);

            await client.ConnectAsync(
                socket,
                LegacyTlsHost,
                LegacyImapPort,
                SecureSocketOptions.StartTls,
                cancellationToken);

            socket =
                null;

            if (!client.IsSecure)
            {
                throw new IOException(
                    "Die STARTTLS-Verbindung zum alten Mailserver ist nicht aktiv.");
            }

            await client.AuthenticateAsync(
                userName.Trim(),
                password,
                cancellationToken);

            return client;
        }
        catch
        {
            try
            {
                socket?.Dispose();
            }
            catch
            {
            }

            client.Dispose();

            throw;
        }
    }

    private static async Task<IMailFolder>
        ResolveTargetFolderAsync(
            ImapClient targetClient,
            IMailFolder sourceFolder,
            LegacyMailMigrationFolderPlanItem planItem,
            CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(
                planItem.TargetFolderId))
        {
            return await targetClient.GetFolderAsync(
                planItem.TargetFolderId,
                cancellationToken);
        }

        if (!planItem.RequiresTargetCreation)
        {
            throw new InvalidOperationException(
                $"Für „{planItem.SourceFolderName}“ ist kein gültiger Zielordner definiert.");
        }

        if (targetClient.PersonalNamespaces.Count == 0)
        {
            throw new InvalidOperationException(
                "Der neue Mailserver stellt keinen persönlichen IMAP-Namensraum bereit.");
        }

        var separator =
            sourceFolder.DirectorySeparator;

        var segments =
            separator == '\0'
                ? new[]
                {
                    sourceFolder.FullName
                }
                : sourceFolder
                    .FullName
                    .Split(
                        separator,
                        StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new InvalidOperationException(
                $"Der Quellordner „{sourceFolder.FullName}“ besitzt keinen gültigen Ordnernamen.");
        }

        IMailFolder parent;

        var startIndex =
            0;

        if (string.Equals(
                segments[0],
                "INBOX",
                StringComparison.OrdinalIgnoreCase))
        {
            parent =
                targetClient.Inbox;

            startIndex =
                1;
        }
        else
        {
            parent =
                targetClient.GetFolder(
                    targetClient.PersonalNamespaces[0]);
        }

        for (var index =
                 startIndex;
             index < segments.Length;
             index++)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var segment =
                segments[index]
                    .Trim();

            if (string.IsNullOrWhiteSpace(
                    segment))
            {
                continue;
            }

            IMailFolder child;

            try
            {
                child =
                    await parent.GetSubfolderAsync(
                        segment,
                        cancellationToken);
            }
            catch (FolderNotFoundException)
            {
                child =
                    await parent.CreateAsync(
                        segment,
                        isMessageFolder: true,
                        cancellationToken);
            }

            parent =
                child;
        }

        if (!parent.IsSubscribed)
        {
            try
            {
                await parent.SubscribeAsync(
                    cancellationToken);
            }
            catch
            {
            }
        }

        return parent;
    }

    private static async Task<IList<UniqueId>>
        FindMarkedTargetUidsAsync(
            IMailFolder targetFolder,
            string migrationMarker,
            CancellationToken cancellationToken)
    {
        await EnsureFolderOpenAsync(
            targetFolder,
            FolderAccess.ReadOnly,
            cancellationToken);

        return await targetFolder.SearchAsync(
            SearchQuery.HasKeyword(
                migrationMarker),
            cancellationToken);
    }

    private static async Task<IReadOnlyList<UniqueId>>
        FindNormalizationArtifactsAsync(
            IMailFolder targetFolder,
            byte[] normalizedBytes,
            MessageFlags expectedFlags,
            IReadOnlyCollection<string> expectedKeywords,
            DateTimeOffset expectedInternalDate,
            CancellationToken cancellationToken)
    {
        await EnsureFolderOpenAsync(
            targetFolder,
            FolderAccess.ReadOnly,
            cancellationToken);

        var normalizedHash =
            ComputeSha256(
                normalizedBytes);

        var uids =
            await targetFolder.SearchAsync(
                SearchQuery.All,
                cancellationToken);

        var matches =
            new List<UniqueId>();

        foreach (var uid in
                 uids)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var targetBytes =
                await ReadMessageBytesAsync(
                    targetFolder,
                    uid,
                    cancellationToken);

            if (targetBytes.Length !=
                normalizedBytes.Length)
            {
                continue;
            }

            if (!string.Equals(
                    ComputeSha256(
                        targetBytes),
                    normalizedHash,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!normalizedBytes.AsSpan()
                    .SequenceEqual(
                        targetBytes))
            {
                continue;
            }

            var summary =
                await GetSummaryAsync(
                    targetFolder,
                    uid,
                    cancellationToken);

            if (!MetadataMatches(
                    summary,
                    expectedFlags,
                    expectedKeywords,
                    expectedInternalDate))
            {
                continue;
            }

            matches.Add(
                uid);
        }

        return matches;
    }

    private static async Task<TargetMessageVerificationResult>
        VerifyTargetMessageAsync(
            IMailFolder targetFolder,
            UniqueId targetUid,
            byte[] sourceBytes,
            string sourceHash,
            MessageFlags sourceFlags,
            IReadOnlyCollection<string> sourceKeywords,
            DateTimeOffset sourceInternalDate,
            CancellationToken cancellationToken)
    {
        try
        {
            var targetBytes =
                await ReadMessageBytesAsync(
                    targetFolder,
                    targetUid,
                    cancellationToken);

            var targetHash =
                ComputeSha256(
                    targetBytes);

            if (!string.Equals(
                    sourceHash,
                    targetHash,
                    StringComparison.Ordinal))
            {
                var firstDifference =
                    FindFirstDifference(
                        sourceBytes,
                        targetBytes);

                return TargetMessageVerificationResult
                    .Failed(
                        "RAW/SHA-256 stimmt nicht überein.\n" +
                        $"Quelle: {AbbreviateHash(sourceHash)} · {sourceBytes.Length:N0} Bytes\n" +
                        $"Ziel: {AbbreviateHash(targetHash)} · {targetBytes.Length:N0} Bytes\n" +
                        $"Erste Byte-Abweichung: {FormatDifferencePosition(firstDifference)}");
            }

            if (!sourceBytes.AsSpan()
                    .SequenceEqual(
                        targetBytes))
            {
                var firstDifference =
                    FindFirstDifference(
                        sourceBytes,
                        targetBytes);

                return TargetMessageVerificationResult
                    .Failed(
                        "SHA-256 stimmt zwar überein, der direkte Bytevergleich aber nicht.\n" +
                        $"Quelle: {sourceBytes.Length:N0} Bytes\n" +
                        $"Ziel: {targetBytes.Length:N0} Bytes\n" +
                        $"Erste Byte-Abweichung: {FormatDifferencePosition(firstDifference)}");
            }

            var targetSummary =
                await GetSummaryAsync(
                    targetFolder,
                    targetUid,
                    cancellationToken);

            var targetFlags =
                (targetSummary.Flags ??
                 MessageFlags.None) &
                TransferableFlags;

            if (targetFlags !=
                sourceFlags)
            {
                return TargetMessageVerificationResult
                    .Failed(
                        "RAW-Daten sind byteidentisch, aber die IMAP-Systemflags unterscheiden sich.\n" +
                        $"Quelle: {FormatFlags(sourceFlags)}\n" +
                        $"Ziel: {FormatFlags(targetFlags)}");
            }

            var targetKeywords =
                targetSummary.Keywords ??
                new HashSet<string>();

            var sourceKeywordSet =
                new HashSet<string>(
                    sourceKeywords,
                    StringComparer.OrdinalIgnoreCase);

            var targetKeywordSet =
                new HashSet<string>(
                    targetKeywords,
                    StringComparer.OrdinalIgnoreCase);

            if (!targetKeywordSet.SetEquals(
                    sourceKeywordSet))
            {
                return TargetMessageVerificationResult
                    .Failed(
                        "RAW-Daten und Systemflags sind identisch, aber die benutzerdefinierten IMAP-Keywords unterscheiden sich.\n" +
                        $"Quelle: {FormatKeywords(sourceKeywordSet)}\n" +
                        $"Ziel: {FormatKeywords(targetKeywordSet)}");
            }

            if (!targetSummary.InternalDate.HasValue)
            {
                return TargetMessageVerificationResult
                    .Failed(
                        "RAW-Daten, Flags und Keywords sind identisch, aber der Zielserver liefert kein INTERNALDATE.");
            }

            var sourceUnixSeconds =
                sourceInternalDate
                    .ToUnixTimeSeconds();

            var targetUnixSeconds =
                targetSummary
                    .InternalDate
                    .Value
                    .ToUnixTimeSeconds();

            if (sourceUnixSeconds !=
                targetUnixSeconds)
            {
                var differenceSeconds =
                    targetUnixSeconds -
                    sourceUnixSeconds;

                return TargetMessageVerificationResult
                    .Failed(
                        "RAW-Daten, Flags und Keywords sind identisch, aber INTERNALDATE unterscheidet sich.\n" +
                        $"Quelle: {sourceInternalDate:O}\n" +
                        $"Ziel: {targetSummary.InternalDate.Value:O}\n" +
                        $"Differenz: {differenceSeconds:+#;-#;0} Sekunden");
            }

            return TargetMessageVerificationResult
                .Succeeded();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return TargetMessageVerificationResult
                .Failed(
                    "Die Zielnachricht konnte während der Detailverifikation nicht vollständig gelesen werden.\n" +
                    exception.Message);
        }
    }

    private static bool
        MetadataMatches(
            IMessageSummary summary,
            MessageFlags expectedFlags,
            IReadOnlyCollection<string> expectedKeywords,
            DateTimeOffset expectedInternalDate)
    {
        if (!summary.InternalDate.HasValue)
        {
            return false;
        }

        var actualFlags =
            (summary.Flags ??
             MessageFlags.None) &
            TransferableFlags;

        if (actualFlags !=
            expectedFlags)
        {
            return false;
        }

        if (summary.InternalDate.Value
                .ToUnixTimeSeconds() !=
            expectedInternalDate
                .ToUnixTimeSeconds())
        {
            return false;
        }

        var actualKeywords =
            summary.Keywords ??
            new HashSet<string>();

        return new HashSet<string>(
                actualKeywords,
                StringComparer.OrdinalIgnoreCase)
            .SetEquals(
                expectedKeywords);
    }

    private static async Task<IMessageSummary>
        GetSummaryAsync(
            IMailFolder folder,
            UniqueId uid,
            CancellationToken cancellationToken)
    {
        var summaries =
            await folder.FetchAsync(
                new[]
                {
                    uid
                },
                MessageSummaryItems.UniqueId |
                MessageSummaryItems.Flags |
                MessageSummaryItems.InternalDate,
                cancellationToken);

        var summary =
            summaries.FirstOrDefault(
                item =>
                    item.UniqueId ==
                    uid);

        if (summary is null)
        {
            throw new InvalidOperationException(
                $"Die Nachricht mit UID {uid.Id} konnte nicht gelesen werden.");
        }

        return summary;
    }

    private static async Task<byte[]>
        ReadMessageBytesAsync(
            IMailFolder folder,
            UniqueId uid,
            CancellationToken cancellationToken)
    {
        if (!folder.IsOpen)
        {
            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);
        }

        await using var stream =
            await folder.GetStreamAsync(
                uid,
                cancellationToken);

        using var memoryStream =
            new MemoryStream();

        await stream.CopyToAsync(
            memoryStream,
            cancellationToken);

        return memoryStream.ToArray();
    }

    private static async Task
        EnsureFolderOpenAsync(
            IMailFolder folder,
            FolderAccess requiredAccess,
            CancellationToken cancellationToken)
    {
        if (folder.IsOpen &&
            folder.Access ==
            requiredAccess)
        {
            return;
        }

        if (folder.IsOpen)
        {
            await folder.CloseAsync(
                expunge: false,
                cancellationToken);
        }

        await folder.OpenAsync(
            requiredAccess,
            cancellationToken);
    }

    private static async Task
        RefreshFolderAsync(
            IMailFolder folder,
            FolderAccess requiredAccess,
            CancellationToken cancellationToken)
    {
        /*
         * Im Gegensatz zu EnsureFolderOpenAsync wird hier
         * ABSICHTLICH auch dann neu geöffnet, wenn der Ordner
         * bereits mit dem gewünschten Zugriff offen ist.
         *
         * Das ist nach einem RAW-APPEND über eine zweite
         * IMAP-Session nötig, damit MailKit den aktuellen
         * Serverzustand inklusive der neuen UID einliest.
         */
        if (folder.IsOpen)
        {
            await folder.CloseAsync(
                expunge: false,
                cancellationToken);
        }

        await folder.OpenAsync(
            requiredAccess,
            cancellationToken);
    }

    private static async Task<bool>
        SourceUidExistsAsync(
            IMailFolder folder,
            UniqueId uid,
            CancellationToken cancellationToken)
    {
        var found =
            await folder.SearchAsync(
                SearchQuery.Uids(
                    new[]
                    {
                        uid
                    }),
                cancellationToken);

        return found.Contains(
            uid);
    }

    private static async Task
        TryDeleteTargetMessageAsync(
            IMailFolder folder,
            UniqueId uid)
    {
        try
        {
            await EnsureFolderOpenAsync(
                folder,
                FolderAccess.ReadWrite,
                CancellationToken.None);

            await folder.AddFlagsAsync(
                uid,
                MessageFlags.Deleted,
                silent: true,
                CancellationToken.None);

            await folder.ExpungeAsync(
                new[]
                {
                    uid
                },
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private static async Task<byte[]>
        SerializeAsMailKitAppendWouldAsync(
            MimeMessage message,
            FormatOptions baseOptions,
            CancellationToken cancellationToken)
    {
        var options =
            baseOptions.Clone();

        options.NewLineFormat =
            NewLineFormat.Dos;

        options.EnsureNewLine =
            true;

        using var stream =
            new MemoryStream();

        await message.WriteToAsync(
            options,
            stream,
            cancellationToken);

        return stream.ToArray();
    }

    private static FormatOptions
        CreateExactFormatOptions(
            byte[] sourceBytes)
    {
        var options =
            FormatOptions.Default.Clone();

        options.EnsureNewLine =
            false;

        options.NewLineFormat =
            DetectNewLineFormat(
                sourceBytes);

        return options;
    }

    private static NewLineFormat
        DetectNewLineFormat(
            byte[] bytes)
    {
        for (var index = 0;
             index < bytes.Length;
             index++)
        {
            if (bytes[index] !=
                (byte)'\n')
            {
                continue;
            }

            if (index > 0 &&
                bytes[index - 1] ==
                (byte)'\r')
            {
                return NewLineFormat.Dos;
            }

            return NewLineFormat.Unix;
        }

        return NewLineFormat.Dos;
    }

    private static string
        CreateMigrationMarker(
            string sourceFolderName,
            UniqueId sourceUid)
    {
        var folderHash =
            Convert
                .ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            sourceFolderName)))
                .ToLowerInvariant();

        return
            MigrationMarkerPrefix +
            folderHash[..16] +
            "-" +
            sourceUid.Id;
    }

    private static async Task
        CleanupOrphanMigrationMarkersAsync(
            ImapClient targetClient,
            IEnumerable<string> targetFolderNames,
            CancellationToken cancellationToken)
    {
        foreach (var folderName in
                 targetFolderNames.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            var folder =
                await targetClient.GetFolderAsync(
                    folderName,
                    cancellationToken);

            await EnsureFolderOpenAsync(
                folder,
                FolderAccess.ReadWrite,
                cancellationToken);

            var uids =
                await folder.SearchAsync(
                    SearchQuery.All,
                    cancellationToken);

            if (uids.Count == 0)
            {
                continue;
            }

            var summaries =
                await folder.FetchAsync(
                    uids,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Flags,
                    cancellationToken);

            foreach (var summary in
                     summaries)
            {
                var markers =
                    summary.Keywords?
                        .Where(
                            keyword =>
                                keyword.StartsWith(
                                    MigrationMarkerPrefix,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToHashSet(
                            StringComparer.OrdinalIgnoreCase)
                    ??
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                if (markers.Count == 0)
                {
                    continue;
                }

                await folder.RemoveFlagsAsync(
                    summary.UniqueId,
                    MessageFlags.None,
                    markers,
                    silent: true,
                    cancellationToken);
            }

            var verificationSummaries =
                await folder.FetchAsync(
                    uids,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Flags,
                    cancellationToken);

            var remainingMarkers =
                verificationSummaries
                    .SelectMany(
                        summary =>
                            summary.Keywords ??
                            Enumerable.Empty<string>())
                    .Where(
                        keyword =>
                            keyword.StartsWith(
                                MigrationMarkerPrefix,
                                StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (remainingMarkers.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Im Zielordner „{folderName}“ konnten nicht alle internen Migrationsmarker entfernt werden.");
            }
        }
    }

    private static string
        ComputeSha256(
            byte[] bytes)
    {
        return Convert
            .ToHexString(
                SHA256.HashData(
                    bytes))
            .ToLowerInvariant();
    }

    private static int?
        FindFirstDifference(
            byte[] sourceBytes,
            byte[] targetBytes)
    {
        var commonLength =
            Math.Min(
                sourceBytes.Length,
                targetBytes.Length);

        for (var index = 0;
             index < commonLength;
             index++)
        {
            if (sourceBytes[index] !=
                targetBytes[index])
            {
                return index;
            }
        }

        if (sourceBytes.Length !=
            targetBytes.Length)
        {
            return commonLength;
        }

        return null;
    }

    private static string
        FormatDifferencePosition(
            int? position)
    {
        return position.HasValue
            ? $"Byte {position.Value:N0}"
            : "keine Abweichung gefunden";
    }

    private static string
        AbbreviateHash(
            string hash)
    {
        if (hash.Length <= 20)
        {
            return hash;
        }

        return hash[..20] +
               "…";
    }

    private static string
        FormatFlags(
            MessageFlags flags)
    {
        if (flags ==
            MessageFlags.None)
        {
            return "(keine)";
        }

        return flags.ToString();
    }

    private static string
        FormatKeywords(
            IEnumerable<string> keywords)
    {
        var values =
            keywords
                .Where(
                    keyword =>
                        !string.IsNullOrWhiteSpace(
                            keyword))
                .OrderBy(
                    keyword =>
                        keyword,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return values.Length == 0
            ? "(keine)"
            : string.Join(
                ", ",
                values);
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
                quit: true);
        }
        catch
        {
        }
    }

    private static LegacyMailboxMigrationResult
        Failure(
            string message,
            int migratedCount,
            int alreadyPresentCount,
            string? failedFolder,
            string? failedSubject,
            bool sourceDeletedBeforeFailure)
    {
        return new LegacyMailboxMigrationResult(
            Success:
                false,

            Message:
                message,

            MigratedMessageCount:
                migratedCount,

            AlreadyPresentMessageCount:
                alreadyPresentCount,

            RemainingSourceMessageCount:
                null,

            FailedFolder:
                failedFolder,

            FailedSubject:
                failedSubject,

            SourceDeletedBeforeFailure:
                sourceDeletedBeforeFailure);
    }

    private sealed record TargetMessageVerificationResult(
        bool Success,
        string Details)
    {
        public static TargetMessageVerificationResult
            Succeeded()
        {
            return new TargetMessageVerificationResult(
                Success:
                    true,

                Details:
                    "RAW-Daten, SHA-256, Flags, Keywords und INTERNALDATE stimmen überein.");
        }

        public static TargetMessageVerificationResult
            Failed(
                string details)
        {
            return new TargetMessageVerificationResult(
                Success:
                    false,

                Details:
                    details);
        }
    }
}

public sealed record LegacyMailboxMigrationProgress(
    long ProcessedMessageCount,
    long TotalMessageCount,
    string CurrentSourceFolder,
    string CurrentTargetFolder,
    string? CurrentSubject,
    string StatusText)
{
    public double Percentage =>
        TotalMessageCount <= 0
            ? 0
            : Math.Clamp(
                ProcessedMessageCount *
                100d /
                TotalMessageCount,
                0d,
                100d);
}

public sealed record LegacyMailboxMigrationResult(
    bool Success,
    string Message,
    int MigratedMessageCount,
    int AlreadyPresentMessageCount,
    long? RemainingSourceMessageCount,
    string? FailedFolder,
    string? FailedSubject,
    bool SourceDeletedBeforeFailure);