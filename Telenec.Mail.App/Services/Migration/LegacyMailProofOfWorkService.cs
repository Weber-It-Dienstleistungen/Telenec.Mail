using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Migration;

public sealed class LegacyMailProofOfWorkService
{
    public const string TestFolderName =
        "Telenec-Migrationstest";

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

    private static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly MessageFlags TransferableFlags =
        MessageFlags.Seen |
        MessageFlags.Answered |
        MessageFlags.Flagged |
        MessageFlags.Draft;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    public LegacyMailProofOfWorkService(
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
    }

    public async Task<LegacyMailProofOfWorkResult>
        RunAsync(
            string legacyUserName,
            string legacyPassword,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                legacyUserName))
        {
            return Failure(
                "Die E-Mail-Adresse des alten Postfachs fehlt.");
        }

        if (string.IsNullOrEmpty(
                legacyPassword))
        {
            return Failure(
                "Das Passwort des alten Postfachs fehlt.");
        }

        string? sourceSha256 =
            null;

        string? targetSha256 =
            null;

        string? subject =
            null;

        var targetVerified =
            false;

        var sourceDeleted =
            false;

        var sourceDeletionVerified =
            false;

        try
        {
            using var sourceClient =
                await CreateLegacyClientAsync(
                    legacyUserName,
                    legacyPassword,
                    cancellationToken);

            using var targetClient =
                await CreateTargetClientAsync(
                    cancellationToken);

            try
            {
                /*
                 * Für selektives UID-EXPUNGE verlangen wir
                 * UIDPLUS auf BEIDEN Servern.
                 *
                 * Ohne UIDPLUS würden wir niemals ein
                 * allgemeines EXPUNGE verwenden, weil dadurch
                 * fremde bereits als \Deleted markierte Mails
                 * betroffen sein könnten.
                 */
                if (!sourceClient.Capabilities.HasFlag(
                        ImapCapabilities.UidPlus))
                {
                    return Failure(
                        "Der alte Mailserver unterstützt kein UIDPLUS. " +
                        "Eine sicher selektive Löschung der Testmail ist deshalb nicht möglich.");
                }

                if (!targetClient.Capabilities.HasFlag(
                        ImapCapabilities.UidPlus))
                {
                    return Failure(
                        "Der neue Mailserver unterstützt kein UIDPLUS. " +
                        "Der Proof-of-Work wird deshalb vor jeder Änderung abgebrochen.");
                }

                var sourceFolder =
                    await FindExistingFolderAsync(
                        sourceClient,
                        TestFolderName,
                        cancellationToken);

                if (sourceFolder is null)
                {
                    return Failure(
                        $"Der Ordner „{TestFolderName}“ wurde auf dem alten Mailserver nicht gefunden.");
                }

                if (sourceFolder.Attributes.HasFlag(
                        FolderAttributes.NoSelect))
                {
                    return Failure(
                        $"Der Ordner „{TestFolderName}“ kann auf dem alten Mailserver nicht geöffnet werden.");
                }

                await sourceFolder.OpenAsync(
                    FolderAccess.ReadOnly,
                    cancellationToken);

                var sourceUids =
                    await sourceFolder.SearchAsync(
                        SearchQuery.All,
                        cancellationToken);

                if (sourceUids.Count != 1)
                {
                    return Failure(
                        sourceUids.Count == 0
                            ? $"Der Ordner „{TestFolderName}“ enthält keine Testmail."
                            : $"Der Ordner „{TestFolderName}“ enthält {sourceUids.Count:N0} Nachrichten. " +
                              "Für den Proof-of-Work muss dort exakt eine Nachricht liegen.");
                }

                var sourceUid =
                    sourceUids[0];

                var sourceSummary =
                    await GetSummaryAsync(
                        sourceFolder,
                        sourceUid,
                        cancellationToken);

                if (!sourceSummary.InternalDate.HasValue)
                {
                    return Failure(
                        "Der alte Mailserver liefert für die Testmail kein internes Empfangsdatum. " +
                        "Die Mail wird nicht verändert.");
                }

                if ((sourceSummary.Flags ??
                     MessageFlags.None)
                    .HasFlag(
                        MessageFlags.Deleted))
                {
                    return Failure(
                        "Die Testmail ist auf dem alten Server bereits als gelöscht markiert. " +
                        "Der Proof-of-Work wird nicht gestartet.");
                }

                if (sourceSummary.Keywords is
                    { Count: > 0 })
                {
                    return Failure(
                        "Die Testmail besitzt benutzerdefinierte IMAP-Schlagwörter. " +
                        "Für den ersten Proof-of-Work verwenden wir bitte eine Testmail ohne benutzerdefinierte Flags.");
                }

                var sourceBytes =
                    await ReadMessageBytesAsync(
                        sourceFolder,
                        sourceUid,
                        cancellationToken);

                sourceSha256 =
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

                subject =
                    string.IsNullOrWhiteSpace(
                        message.Subject)
                        ? "(ohne Betreff)"
                        : message.Subject.Trim();

                /*
                 * Vor dem ersten Schreibzugriff prüfen wir,
                 * ob MimeKit die vom alten Server gelesene
                 * Nachricht mit denselben FormatOptions
                 * BYTEIDENTISCH serialisiert.
                 *
                 * Ist das nicht möglich, wird überhaupt
                 * nichts auf den Zielserver geschrieben.
                 */
                var formatOptions =
                    CreateExactFormatOptions(
                        sourceBytes);

                using var preflightStream =
                    new MemoryStream();

                await message.WriteToAsync(
                    formatOptions,
                    preflightStream,
                    cancellationToken);

                var preflightBytes =
                    preflightStream.ToArray();

                if (!sourceBytes.AsSpan()
                        .SequenceEqual(
                            preflightBytes))
                {
                    return Failure(
                        "Die Testmail kann durch MimeKit nicht byteidentisch reproduziert werden. " +
                        "Der Proof-of-Work wurde vor dem Upload sicher abgebrochen.",
                        subject,
                        sourceSha256);
                }

                var targetFolder =
                    await GetOrCreateTargetFolderAsync(
                        targetClient,
                        TestFolderName,
                        cancellationToken);

                if (targetFolder.Attributes.HasFlag(
                        FolderAttributes.NoSelect))
                {
                    return Failure(
                        $"Der Zielordner „{TestFolderName}“ kann nicht als Nachrichtenordner verwendet werden.",
                        subject,
                        sourceSha256);
                }

                /*
                 * Wiederaufnehmbarkeit:
                 *
                 * Ist im Ziel bereits genau eine identische
                 * Nachricht vorhanden, wird NICHT noch einmal
                 * APPEND ausgeführt.
                 *
                 * Damit kann auch ein vorheriger Lauf, der
                 * nach dem Upload aber vor dem Löschen der
                 * Quelle abgebrochen wurde, sicher fortgesetzt
                 * werden.
                 */
                var targetUidsBefore =
                    await GetAllUidsAsync(
                        targetFolder,
                        cancellationToken);

                UniqueId targetUid;

                var appendedNow =
                    false;

                if (targetUidsBefore.Count == 0)
                {
                    var sourceFlags =
                        sourceSummary.Flags ??
                        MessageFlags.None;

                    var appendFlags =
                        sourceFlags &
                        TransferableFlags;

                    var appendedUid =
                        await targetFolder.AppendAsync(
                            formatOptions,
                            message,
                            appendFlags,
                            sourceSummary.InternalDate.Value,
                            cancellationToken);

                    appendedNow =
                        true;

                    /*
                     * APPENDUID ist ideal, aber wir verlassen
                     * uns nicht ausschließlich darauf.
                     *
                     * Anschließend wird der Zielordner ohnehin
                     * anhand des tatsächlichen Rohinhalts
                     * geprüft.
                     */
                    var matchingUids =
                        await FindByteIdenticalMessagesAsync(
                            targetFolder,
                            sourceBytes,
                            cancellationToken);

                    if (matchingUids.Count != 1)
                    {
                        if (appendedUid.HasValue)
                        {
                            await TryDeleteTargetMessageAsync(
                                targetFolder,
                                appendedUid.Value,
                                cancellationToken);
                        }

                        return Failure(
                            matchingUids.Count == 0
                                ? "Die Testmail wurde hochgeladen, konnte im Ziel anschließend aber nicht byteidentisch wiedergefunden werden. Die Quelle wurde nicht gelöscht."
                                : "Im Ziel wurden nach dem Upload mehrere byteidentische Nachrichten gefunden. Die Quelle wurde aus Sicherheitsgründen nicht gelöscht.",
                            subject,
                            sourceSha256);
                    }

                    targetUid =
                        matchingUids[0];
                }
                else if (targetUidsBefore.Count == 1)
                {
                    targetUid =
                        targetUidsBefore[0];

                    var existingTargetBytes =
                        await ReadMessageBytesAsync(
                            targetFolder,
                            targetUid,
                            cancellationToken);

                    if (!sourceBytes.AsSpan()
                            .SequenceEqual(
                                existingTargetBytes))
                    {
                        return Failure(
                            $"Im Zielordner „{TestFolderName}“ liegt bereits eine andere Nachricht. " +
                            "Es wird nichts überschrieben und die Quelle bleibt unverändert.",
                            subject,
                            sourceSha256,
                            ComputeSha256(
                                existingTargetBytes));
                    }
                }
                else
                {
                    return Failure(
                        $"Im Zielordner „{TestFolderName}“ liegen bereits {targetUidsBefore.Count:N0} Nachrichten. " +
                        "Für den Proof-of-Work muss der Zielordner leer sein oder exakt die bereits migrierte Testmail enthalten.",
                        subject,
                        sourceSha256);
                }

                /*
                 * HARTE ZIELVERIFIKATION
                 *
                 * 1. komplette RAW-Nachricht erneut abrufen
                 * 2. SHA-256 bilden
                 * 3. zusätzlich Byte für Byte vergleichen
                 * 4. übertragbare Flags vergleichen
                 * 5. INTERNALDATE vergleichen
                 */
                var targetBytes =
                    await ReadMessageBytesAsync(
                        targetFolder,
                        targetUid,
                        cancellationToken);

                targetSha256 =
                    ComputeSha256(
                        targetBytes);

                if (!string.Equals(
                        sourceSha256,
                        targetSha256,
                        StringComparison.Ordinal) ||
                    !sourceBytes.AsSpan()
                        .SequenceEqual(
                            targetBytes))
                {
                    if (appendedNow)
                    {
                        await TryDeleteTargetMessageAsync(
                            targetFolder,
                            targetUid,
                            cancellationToken);
                    }

                    return Failure(
                        "Die Rohdaten von Quelle und Ziel sind nicht identisch. " +
                        "Die Testmail wurde auf dem Altserver NICHT gelöscht.",
                        subject,
                        sourceSha256,
                        targetSha256);
                }

                var targetSummary =
                    await GetSummaryAsync(
                        targetFolder,
                        targetUid,
                        cancellationToken);

                var sourceFlagsForComparison =
                    (sourceSummary.Flags ??
                     MessageFlags.None) &
                    TransferableFlags;

                var targetFlagsForComparison =
                    (targetSummary.Flags ??
                     MessageFlags.None) &
                    TransferableFlags;

                if (sourceFlagsForComparison !=
                    targetFlagsForComparison)
                {
                    if (appendedNow)
                    {
                        await TryDeleteTargetMessageAsync(
                            targetFolder,
                            targetUid,
                            cancellationToken);
                    }

                    return Failure(
                        "Der Rohinhalt ist identisch, aber die übertragbaren IMAP-Flags stimmen nicht überein. " +
                        "Die Quelle wurde nicht gelöscht.",
                        subject,
                        sourceSha256,
                        targetSha256);
                }

                if (!targetSummary.InternalDate.HasValue ||
                    sourceSummary.InternalDate.Value
                        .ToUnixTimeSeconds() !=
                    targetSummary.InternalDate.Value
                        .ToUnixTimeSeconds())
                {
                    if (appendedNow)
                    {
                        await TryDeleteTargetMessageAsync(
                            targetFolder,
                            targetUid,
                            cancellationToken);
                    }

                    return Failure(
                        "Der Rohinhalt ist identisch, aber das interne Empfangsdatum wurde nicht exakt übernommen. " +
                        "Die Quelle wurde nicht gelöscht.",
                        subject,
                        sourceSha256,
                        targetSha256);
                }

                targetVerified =
                    true;

                /*
                 * Unmittelbar VOR der Löschung lesen wir die
                 * Quellmail ein zweites Mal vom alten Server.
                 *
                 * Damit schließen wir aus, dass sich zwischen
                 * erster Analyse und Löschung noch etwas an
                 * genau dieser Quell-UID geändert hat.
                 */
                if (sourceFolder.IsOpen)
                {
                    await sourceFolder.CloseAsync(
                        expunge: false,
                        cancellationToken);
                }

                await sourceFolder.OpenAsync(
                    FolderAccess.ReadWrite,
                    cancellationToken);

                var sourceUidsBeforeDelete =
                    await sourceFolder.SearchAsync(
                        SearchQuery.All,
                        cancellationToken);

                if (!sourceUidsBeforeDelete.Contains(
                        sourceUid))
                {
                    return Failure(
                        "Die ursprüngliche Quell-UID ist vor der Löschung nicht mehr vorhanden. " +
                        "Der Zielbestand bleibt erhalten; es wurde keine weitere Löschoperation ausgeführt.",
                        subject,
                        sourceSha256,
                        targetSha256,
                        targetVerified: true);
                }

                var sourceBytesImmediatelyBeforeDelete =
                    await ReadMessageBytesAsync(
                        sourceFolder,
                        sourceUid,
                        cancellationToken);

                if (!sourceBytes.AsSpan()
                        .SequenceEqual(
                            sourceBytesImmediatelyBeforeDelete))
                {
                    return Failure(
                        "Die Quellmail hat sich während des Proof-of-Work verändert. " +
                        "Sie wurde deshalb nicht gelöscht.",
                        subject,
                        sourceSha256,
                        targetSha256,
                        targetVerified: true);
                }

                var sourceSummaryImmediatelyBeforeDelete =
                    await GetSummaryAsync(
                        sourceFolder,
                        sourceUid,
                        cancellationToken);

                var currentSourceFlags =
                    (sourceSummaryImmediatelyBeforeDelete.Flags ??
                     MessageFlags.None) &
                    TransferableFlags;

                if (currentSourceFlags !=
                    sourceFlagsForComparison ||
                    !sourceSummaryImmediatelyBeforeDelete
                        .InternalDate
                        .HasValue ||
                    sourceSummaryImmediatelyBeforeDelete
                        .InternalDate
                        .Value
                        .ToUnixTimeSeconds() !=
                    sourceSummary
                        .InternalDate
                        .Value
                        .ToUnixTimeSeconds())
                {
                    return Failure(
                        "Die Metadaten der Quellmail haben sich während des Proof-of-Work verändert. " +
                        "Sie wurde deshalb nicht gelöscht.",
                        subject,
                        sourceSha256,
                        targetSha256,
                        targetVerified: true);
                }

                /*
                 * ERST JETZT wird destruktiv gearbeitet.
                 *
                 * Es wird ausschließlich die zuvor mehrfach
                 * verifizierte Quell-UID markiert und per
                 * UID-EXPUNGE endgültig entfernt.
                 */
                await sourceFolder.AddFlagsAsync(
                    sourceUid,
                    MessageFlags.Deleted,
                    silent: true,
                    cancellationToken);

                await sourceFolder.ExpungeAsync(
                    new[]
                    {
                        sourceUid
                    },
                    cancellationToken);

                sourceDeleted =
                    true;

                var sourceUidsAfterDelete =
                    await sourceFolder.SearchAsync(
                        SearchQuery.All,
                        cancellationToken);

                sourceDeletionVerified =
                    !sourceUidsAfterDelete.Contains(
                        sourceUid);

                if (!sourceDeletionVerified)
                {
                    return Failure(
                        "Die Quellmail wurde als gelöscht markiert, ist aber nach dem selektiven EXPUNGE weiterhin vorhanden.",
                        subject,
                        sourceSha256,
                        targetSha256,
                        targetVerified: true,
                        sourceDeleted: true);
                }

                /*
                 * Letzte Sicherheitsprüfung NACH dem Löschen:
                 * Das Ziel muss weiterhin exakt dieselben
                 * Bytes besitzen.
                 */
                var finalTargetBytes =
                    await ReadMessageBytesAsync(
                        targetFolder,
                        targetUid,
                        cancellationToken);

                var finalTargetSha256 =
                    ComputeSha256(
                        finalTargetBytes);

                if (!string.Equals(
                        sourceSha256,
                        finalTargetSha256,
                        StringComparison.Ordinal) ||
                    !sourceBytes.AsSpan()
                        .SequenceEqual(
                            finalTargetBytes))
                {
                    return Failure(
                        "Die Quellmail wurde erfolgreich entfernt, aber die abschließende Zielprüfung konnte keine Byteidentität mehr bestätigen. " +
                        "Der Zielbestand muss manuell geprüft werden.",
                        subject,
                        sourceSha256,
                        finalTargetSha256,
                        targetVerified: true,
                        sourceDeleted: true,
                        sourceDeletionVerified: true);
                }

                targetSha256 =
                    finalTargetSha256;

                var finalTargetUids =
                    await targetFolder.SearchAsync(
                        SearchQuery.All,
                        cancellationToken);

                var finalSourceCount =
                    sourceUidsAfterDelete.Count;

                var finalTargetCount =
                    finalTargetUids.Count;

                if (finalSourceCount != 0 ||
                    finalTargetCount != 1)
                {
                    return new LegacyMailProofOfWorkResult(
                        Success:
                            false,

                        Message:
                            $"Die Quell-UID wurde sicher gelöscht und die Zielmail ist byteidentisch vorhanden. " +
                            $"Der Testordner enthält danach jedoch Quelle={finalSourceCount:N0}, Ziel={finalTargetCount:N0} Nachrichten. " +
                            "Für einen sauberen Proof-of-Work wurden Quelle=0 und Ziel=1 erwartet.",

                        Subject:
                            subject,

                        SourceSha256:
                            sourceSha256,

                        TargetSha256:
                            targetSha256,

                        TargetVerified:
                            true,

                        SourceDeleted:
                            true,

                        SourceDeletionVerified:
                            true,

                        SourceRemainingCount:
                            finalSourceCount,

                        TargetMessageCount:
                            finalTargetCount);
                }

                return new LegacyMailProofOfWorkResult(
                    Success:
                        true,

                    Message:
                        "Proof-of-Work erfolgreich: Die Testmail wurde byteidentisch auf den neuen Server übertragen, " +
                        "erneut verifiziert und erst danach selektiv vom alten Server gelöscht.",

                    Subject:
                        subject,

                    SourceSha256:
                        sourceSha256,

                    TargetSha256:
                        targetSha256,

                    TargetVerified:
                        true,

                    SourceDeleted:
                        true,

                    SourceDeletionVerified:
                        true,

                    SourceRemainingCount:
                        0,

                    TargetMessageCount:
                        1);
            }
            finally
            {
                await DisconnectSafelyAsync(
                    sourceClient);

                await DisconnectSafelyAsync(
                    targetClient);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(
                "Der Proof-of-Work konnte nicht abgeschlossen werden.\n\n" +
                exception.Message,
                subject,
                sourceSha256,
                targetSha256,
                targetVerified,
                sourceDeleted,
                sourceDeletionVerified);
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
                    (int)ConnectionTimeout
                        .TotalMilliseconds
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
                    (int)ConnectionTimeout
                        .TotalMilliseconds
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

    private static async Task<IMailFolder?>
        FindExistingFolderAsync(
            ImapClient client,
            string fullName,
            CancellationToken cancellationToken)
    {
        if (client.PersonalNamespaces.Count == 0)
        {
            return null;
        }

        var folders =
            await client.GetFoldersAsync(
                client.PersonalNamespaces[0],
                StatusItems.None,
                subscribedOnly: false,
                cancellationToken);

        return folders.FirstOrDefault(
            folder =>
                string.Equals(
                    folder.FullName,
                    fullName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IMailFolder>
        GetOrCreateTargetFolderAsync(
            ImapClient client,
            string fullName,
            CancellationToken cancellationToken)
    {
        var existing =
            await FindExistingFolderAsync(
                client,
                fullName,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        if (client.PersonalNamespaces.Count == 0)
        {
            throw new InvalidOperationException(
                "Der neue Mailserver stellt keinen persönlichen IMAP-Namensraum bereit.");
        }

        var namespaceRoot =
            client.GetFolder(
                client.PersonalNamespaces[0]);

        var createdFolder =
            await namespaceRoot.CreateAsync(
                fullName,
                isMessageFolder: true,
                cancellationToken);

        if (createdFolder is null ||
            string.IsNullOrWhiteSpace(
                createdFolder.FullName))
        {
            throw new InvalidOperationException(
                "Der neue Mailserver hat den Testordner nicht bestätigt.");
        }

        if (!createdFolder.IsSubscribed)
        {
            await createdFolder.SubscribeAsync(
                cancellationToken);
        }

        return createdFolder;
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
                    item.UniqueId == uid);

        if (summary is null)
        {
            throw new InvalidOperationException(
                $"Die Nachricht mit UID {uid.Id} konnte nicht erneut gelesen werden.");
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

        await using var sourceStream =
            await folder.GetStreamAsync(
                uid,
                cancellationToken);

        using var memoryStream =
            new MemoryStream();

        await sourceStream.CopyToAsync(
            memoryStream,
            cancellationToken);

        return memoryStream.ToArray();
    }

    private static async Task<IList<UniqueId>>
        GetAllUidsAsync(
            IMailFolder folder,
            CancellationToken cancellationToken)
    {
        if (!folder.IsOpen)
        {
            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);
        }

        return await folder.SearchAsync(
            SearchQuery.All,
            cancellationToken);
    }

    private static async Task<IList<UniqueId>>
        FindByteIdenticalMessagesAsync(
            IMailFolder folder,
            byte[] expectedBytes,
            CancellationToken cancellationToken)
    {
        var uids =
            await GetAllUidsAsync(
                folder,
                cancellationToken);

        var matches =
            new List<UniqueId>();

        foreach (var uid in uids)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var candidateBytes =
                await ReadMessageBytesAsync(
                    folder,
                    uid,
                    cancellationToken);

            if (expectedBytes.AsSpan()
                    .SequenceEqual(
                        candidateBytes))
            {
                matches.Add(
                    uid);
            }
        }

        return matches;
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

        /*
         * Reguläre RFC-Maildaten verwenden CRLF.
         * Falls überhaupt kein Zeilenumbruch vorhanden ist,
         * wird DOS/CRLF als konservativer Standard genutzt.
         */
        return NewLineFormat.Dos;
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

    private static async Task
        TryDeleteTargetMessageAsync(
            IMailFolder folder,
            UniqueId uid,
            CancellationToken cancellationToken)
    {
        try
        {
            if (folder.IsOpen &&
                folder.Access !=
                FolderAccess.ReadWrite)
            {
                await folder.CloseAsync(
                    expunge: false,
                    cancellationToken);
            }

            if (!folder.IsOpen)
            {
                await folder.OpenAsync(
                    FolderAccess.ReadWrite,
                    cancellationToken);
            }

            await folder.AddFlagsAsync(
                uid,
                MessageFlags.Deleted,
                silent: true,
                cancellationToken);

            await folder.ExpungeAsync(
                new[]
                {
                    uid
                },
                cancellationToken);
        }
        catch
        {
            /*
             * Rollback ist Best-Effort.
             *
             * Entscheidend ist: Bei jeder Unsicherheit bleibt
             * die Quellmail erhalten.
             */
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
                quit: true);
        }
        catch
        {
        }
    }

    private static LegacyMailProofOfWorkResult
        Failure(
            string message,
            string? subject = null,
            string? sourceSha256 = null,
            string? targetSha256 = null,
            bool targetVerified = false,
            bool sourceDeleted = false,
            bool sourceDeletionVerified = false)
    {
        return new LegacyMailProofOfWorkResult(
            Success:
                false,

            Message:
                message,

            Subject:
                subject,

            SourceSha256:
                sourceSha256,

            TargetSha256:
                targetSha256,

            TargetVerified:
                targetVerified,

            SourceDeleted:
                sourceDeleted,

            SourceDeletionVerified:
                sourceDeletionVerified,

            SourceRemainingCount:
                null,

            TargetMessageCount:
                null);
    }
}

public sealed record LegacyMailProofOfWorkResult(
    bool Success,
    string Message,
    string? Subject,
    string? SourceSha256,
    string? TargetSha256,
    bool TargetVerified,
    bool SourceDeleted,
    bool SourceDeletionVerified,
    int? SourceRemainingCount,
    int? TargetMessageCount);