using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using System.IO;
using System.Security.Cryptography;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Archive;

public sealed class MailArchiveMessageStagingService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private static readonly TimeSpan
        ConnectionTimeout =
            TimeSpan.FromSeconds(15);

    private static readonly TimeSpan
        AuthenticationTimeout =
            TimeSpan.FromSeconds(30);

    private static readonly TimeSpan
        MessageDownloadTimeout =
            TimeSpan.FromMinutes(5);

    private const int CopyBufferSize =
        128 * 1024;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly LocalMailArchiveStorage
        _archiveStorage;

    public MailArchiveMessageStagingService(
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

    public async Task<StagedArchiveMessage>
        StageMessageAsync(
            string folderId,
            uint uniqueId,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                folderId))
        {
            throw new ArgumentException(
                "Der Quellordner darf nicht leer sein.",
                nameof(folderId));
        }

        if (uniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uniqueId),
                "Die Nachrichten-UID muss größer als 0 sein.");
        }

        var archiveLocation =
            await _archiveStorage
                .EnsureArchiveForActiveAccountAsync(
                    cancellationToken);

        var stagingDirectory =
            Path.Combine(
                archiveLocation
                    .ArchiveRootDirectory,
                ".staging");

        Directory.CreateDirectory(
            stagingDirectory);

        var stagingFilePath =
            Path.Combine(
                stagingDirectory,
                Guid.NewGuid()
                    .ToString("N") +
                ".eml.tmp");

        try
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
                    MessageDownloadTimeout);

                var operationCancellationToken =
                    operationTimeoutSource.Token;

                var folder =
                    await client.GetFolderAsync(
                        folderId.Trim(),
                        operationCancellationToken);

                if (folder.Attributes.HasFlag(
                        FolderAttributes.NoSelect))
                {
                    throw new InvalidOperationException(
                        "Der angegebene Mailordner kann nicht geöffnet werden.");
                }

                await folder.OpenAsync(
                    FolderAccess.ReadOnly,
                    operationCancellationToken);

                var uidValidity =
                    folder.UidValidity;

                if (uidValidity == 0)
                {
                    throw new InvalidOperationException(
                        "Der Mailserver hat keine gültige UIDVALIDITY für den Ordner geliefert.");
                }

                var serverUniqueId =
                    new UniqueId(
                        uniqueId);

                var summaries =
                    await folder.FetchAsync(
                        new[]
                        {
                            serverUniqueId
                        },
                        MessageSummaryItems.UniqueId,
                        operationCancellationToken);

                var messageStillExists =
                    summaries.Any(
                        summary =>
                            summary.UniqueId ==
                            serverUniqueId);

                if (!messageStillExists)
                {
                    throw new InvalidOperationException(
                        "Die ausgewählte Nachricht ist nicht mehr im Quellordner vorhanden.");
                }

                var downloadResult =
                    await DownloadRawMessageAsync(
                        folder,
                        serverUniqueId,
                        stagingFilePath,
                        operationCancellationToken);

                var verification =
                    await VerifyStagedMessageAsync(
                        stagingFilePath,
                        operationCancellationToken);

                if (verification.FileSizeBytes !=
                    downloadResult.FileSizeBytes)
                {
                    throw new InvalidOperationException(
                        "Die lokal gespeicherte Archivdatei besitzt nach der Prüfung eine unerwartete Dateigröße.");
                }

                var verifiedSha256 =
                    await ComputeFileSha256Async(
                        stagingFilePath,
                        operationCancellationToken);

                if (!string.Equals(
                        verifiedSha256,
                        downloadResult.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Die Prüfsumme der lokal gespeicherten Archivdatei stimmt nicht mit dem Download überein.");
                }

                return new StagedArchiveMessage(
                    stagingFilePath:
                        stagingFilePath,

                    accountKey:
                        archiveLocation
                            .AccountKey,

                    accountEmailAddress:
                        archiveLocation
                            .EmailAddress,

                    sourceFolderId:
                        folder.FullName,

                    sourceFolderDirectorySeparator:
                        folder.DirectorySeparator,

                    sourceUidValidity:
                        uidValidity,

                    sourceUniqueId:
                        uniqueId,

                    sha256:
                        downloadResult
                            .Sha256,

                    fileSizeBytes:
                        downloadResult
                            .FileSizeBytes,

                    messageId:
                        verification
                            .MessageId,

                    subject:
                        verification
                            .Subject,

                    senderAddress:
                        verification
                            .SenderAddress,

                    senderName:
                        verification
                            .SenderName,

                    messageDateUtc:
                        verification
                            .MessageDateUtc);
            }
            finally
            {
                await DisconnectSafelyAsync(
                    client);
            }
        }
        catch
        {
            TryDeleteFile(
                stagingFilePath);

            throw;
        }
    }

    private static async Task<
        RawMessageDownloadResult>
        DownloadRawMessageAsync(
            IMailFolder folder,
            UniqueId uniqueId,
            string stagingFilePath,
            CancellationToken cancellationToken)
    {
        await using var sourceStream =
            await folder.GetStreamAsync(
                uniqueId,
                cancellationToken);

        await using var destinationStream =
            new FileStream(
                stagingFilePath,
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

        if (totalBytes <= 0)
        {
            throw new InvalidOperationException(
                "Der Mailserver hat eine leere Nachricht geliefert.");
        }

        var sha256 =
            Convert
                .ToHexString(
                    hash.GetHashAndReset())
                .ToLowerInvariant();

        return new RawMessageDownloadResult(
            FileSizeBytes:
                totalBytes,

            Sha256:
                sha256);
    }

    private static async Task<
        StagedMessageVerification>
        VerifyStagedMessageAsync(
            string stagingFilePath,
            CancellationToken cancellationToken)
    {
        var fileInfo =
            new FileInfo(
                stagingFilePath);

        if (!fileInfo.Exists ||
            fileInfo.Length <= 0)
        {
            throw new InvalidOperationException(
                "Die temporäre Archivdatei wurde nicht korrekt angelegt.");
        }

        await using var stream =
            new FileStream(
                stagingFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        MimeMessage message;

        try
        {
            message =
                await MimeMessage.LoadAsync(
                    stream,
                    cancellationToken);
        }
        catch (Exception exception)
            when (exception
                is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Die heruntergeladene Nachricht konnte nicht als gültige E-Mail gelesen werden.",
                exception);
        }

        var sender =
            message
                .From
                .Mailboxes
                .FirstOrDefault();

        DateTimeOffset?
            messageDateUtc =
                null;

        if (message.Date !=
            DateTimeOffset.MinValue)
        {
            messageDateUtc =
                message.Date
                    .ToUniversalTime();
        }

        return new StagedMessageVerification(
            FileSizeBytes:
                fileInfo.Length,

            MessageId:
                NormalizeOptionalText(
                    message.MessageId),

            Subject:
                message.Subject
                ?? string.Empty,

            SenderAddress:
                NormalizeOptionalText(
                    sender?.Address),

            SenderName:
                NormalizeOptionalText(
                    sender?.Name),

            MessageDateUtc:
                messageDateUtc);
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

    private async Task<ImapClient>
        CreateAuthenticatedClientAsync(
            CancellationToken cancellationToken)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
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
                connectionTimeoutSource
                    .CancelAfter(
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
                authenticationTimeoutSource
                    .CancelAfter(
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

    private static string?
        NormalizeOptionalText(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        return value.Trim();
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

    private sealed record
        RawMessageDownloadResult(
            long FileSizeBytes,
            string Sha256);

    private sealed record
        StagedMessageVerification(
            long FileSizeBytes,
            string? MessageId,
            string Subject,
            string? SenderAddress,
            string? SenderName,
            DateTimeOffset? MessageDateUtc);
}

public sealed class StagedArchiveMessage
    : IDisposable
{
    internal StagedArchiveMessage(
        string stagingFilePath,
        string accountKey,
        string accountEmailAddress,
        string sourceFolderId,
        char sourceFolderDirectorySeparator,
        uint sourceUidValidity,
        uint sourceUniqueId,
        string sha256,
        long fileSizeBytes,
        string? messageId,
        string subject,
        string? senderAddress,
        string? senderName,
        DateTimeOffset? messageDateUtc)
    {
        StagingFilePath =
            stagingFilePath;

        AccountKey =
            accountKey;

        AccountEmailAddress =
            accountEmailAddress;

        SourceFolderId =
            sourceFolderId;

        SourceFolderDirectorySeparator =
            sourceFolderDirectorySeparator;

        SourceUidValidity =
            sourceUidValidity;

        SourceUniqueId =
            sourceUniqueId;

        Sha256 =
            sha256;

        FileSizeBytes =
            fileSizeBytes;

        MessageId =
            messageId;

        Subject =
            subject;

        SenderAddress =
            senderAddress;

        SenderName =
            senderName;

        MessageDateUtc =
            messageDateUtc;
    }

    public string StagingFilePath { get; }

    public string AccountKey { get; }

    public string AccountEmailAddress { get; }

    public string SourceFolderId { get; }

    public char SourceFolderDirectorySeparator { get; }

    public uint SourceUidValidity { get; }

    public uint SourceUniqueId { get; }

    public string Sha256 { get; }

    public long FileSizeBytes { get; }

    public string? MessageId { get; }

    public string Subject { get; }

    public string? SenderAddress { get; }

    public string? SenderName { get; }

    public DateTimeOffset? MessageDateUtc { get; }

    public void Dispose()
    {
        try
        {
            if (File.Exists(
                    StagingFilePath))
            {
                File.Delete(
                    StagingFilePath);
            }
        }
        catch
        {
        }
    }
}