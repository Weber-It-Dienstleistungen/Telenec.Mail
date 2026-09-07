using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.IO;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class MailKitSearchService :
    IMailSearchService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private const int MaximumAllowedResultCount =
        500;

    private const int MessageLoadRetryCount =
        2;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly ImapMailDataSource
        _mailDataSource;

    private readonly ILogger<MailKitSearchService>
        _logger;

    public MailKitSearchService(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        ImapMailDataSource mailDataSource,
        ILogger<MailKitSearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        ArgumentNullException.ThrowIfNull(
            mailDataSource);

        ArgumentNullException.ThrowIfNull(
            logger);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _mailDataSource =
            mailDataSource;

        _logger =
            logger;
    }

    public async Task<IReadOnlyList<MailSearchHitData>>
        SearchFolderAsync(
            string folderId,
            string searchText,
            int maximumResultCount = 200,
            CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId);

        var normalizedSearchText =
            NormalizeSearchText(
                searchText);

        var normalizedMaximumResultCount =
            NormalizeMaximumResultCount(
                maximumResultCount);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    folderId,
                    cancellationToken);

            if (folder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new InvalidOperationException(
                    "Der ausgewählte Mailordner kann nicht durchsucht werden.");
            }

            return await SearchFolderCoreAsync(
                folder,
                normalizedSearchText,
                normalizedMaximumResultCount,
                cancellationToken);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task<IReadOnlyList<MailSearchHitData>>
        SearchMailboxAsync(
            string searchText,
            int maximumResultCount = 200,
            CancellationToken cancellationToken = default)
    {
        var normalizedSearchText =
            NormalizeSearchText(
                searchText);

        var normalizedMaximumResultCount =
            NormalizeMaximumResultCount(
                maximumResultCount);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var searchableFolders =
                await GetSearchableFoldersAsync(
                    client,
                    cancellationToken);

            var results =
                new List<MailSearchHitData>();

            foreach (var folder in searchableFolders)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var folderResults =
                    await SearchFolderCoreAsync(
                        folder,
                        normalizedSearchText,
                        normalizedMaximumResultCount,
                        cancellationToken);

                results.AddRange(
                    folderResults);
            }

            return results
                .OrderByDescending(
                    result =>
                        result.Date)
                .ThenBy(
                    result =>
                        result.FolderId,
                    StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(
                    result =>
                        result.UniqueId)
                .Take(
                    normalizedMaximumResultCount)
                .ToList();
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task<MailMessageData?>
        LoadMessageAsync(
            MailSearchHitData searchHit,
            CancellationToken cancellationToken = default)
    {
        ValidateSearchHit(
            searchHit);

        for (var attempt = 0;
             attempt < MessageLoadRetryCount;
             attempt++)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var messageIndex =
                await FindCurrentMessageIndexAsync(
                    searchHit,
                    cancellationToken);

            if (!messageIndex.HasValue)
            {
                return null;
            }

            var messages =
                await _mailDataSource
                    .GetMessagePageAsync(
                        searchHit.FolderId,
                        skipMessageCount:
                            messageIndex.Value,
                        maximumMessageCount:
                            1,
                        cancellationToken:
                            cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (messages.Count != 1)
            {
                continue;
            }

            var message =
                messages[0];

            if (message.UniqueId !=
                searchHit.UniqueId)
            {
                continue;
            }

            if (!MessageIdsMatch(
                    searchHit.MessageId,
                    message.MessageId))
            {
                return null;
            }

            var identityConfirmed =
                await ConfirmSearchHitIdentityAsync(
                    searchHit,
                    cancellationToken);

            if (!identityConfirmed)
            {
                return null;
            }

            return message;
        }

        return null;
    }

    public async Task DownloadAttachmentAsync(
        MailSearchHitData searchHit,
        string partSpecifier,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ValidateSearchHit(
            searchHit);

        if (string.IsNullOrWhiteSpace(
                partSpecifier))
        {
            throw new ArgumentException(
                "Der MIME-Part darf nicht leer sein.",
                nameof(partSpecifier));
        }

        ArgumentNullException.ThrowIfNull(
            destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "Der Zielstream ist nicht beschreibbar.",
                nameof(destination));
        }

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    searchHit.FolderId,
                    cancellationToken);

            if (folder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new InvalidOperationException(
                    "Der Ursprungsordner des Suchtreffers kann nicht geöffnet werden.");
            }

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            ValidateFolderIdentity(
                folder,
                searchHit);

            var identityBeforeDownload =
                await ConfirmSearchHitIdentityInOpenFolderAsync(
                    folder,
                    searchHit,
                    cancellationToken);

            if (!identityBeforeDownload)
            {
                throw new InvalidOperationException(
                    "Die E-Mail ist in diesem Serverzustand nicht mehr eindeutig verfügbar.");
            }

            if (folder is not IImapFolder imapFolder)
            {
                throw new InvalidOperationException(
                    "Der Mailordner unterstützt keinen gezielten IMAP-Anhangabruf.");
            }

            var entity =
                await imapFolder
                    .GetBodyPartAsync(
                        new UniqueId(
                            searchHit.UniqueId),
                        partSpecifier,
                        cancellationToken);

            if (entity is MimePart mimePart)
            {
                var content =
                    mimePart.Content
                    ?? throw new InvalidDataException(
                        "Der Anhang enthält keinen Dateinhalt.");

                await content
                    .DecodeToAsync(
                        destination,
                        cancellationToken);
            }
            else if (entity is MessagePart messagePart)
            {
                var attachedMessage =
                    messagePart.Message
                    ?? throw new InvalidDataException(
                        "Die angehängte E-Mail enthält keine Nachrichtendaten.");

                await attachedMessage
                    .WriteToAsync(
                        destination,
                        cancellationToken);
            }
            else
            {
                await entity
                    .WriteToAsync(
                        destination,
                        contentOnly: true,
                        cancellationToken);
            }

            await destination
                .FlushAsync(
                    cancellationToken);

            ValidateFolderIdentity(
                folder,
                searchHit);

            var identityAfterDownload =
                await ConfirmSearchHitIdentityInOpenFolderAsync(
                    folder,
                    searchHit,
                    cancellationToken);

            if (!identityAfterDownload)
            {
                throw new InvalidOperationException(
                    "Die Identität der E-Mail konnte nach dem Download nicht bestätigt werden.");
            }
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public Task<bool> MarkAsReadAsync(
        MailSearchHitData searchHit,
        CancellationToken cancellationToken = default)
    {
        return SetSeenStateAsync(
            searchHit,
            markAsSeen: true,
            cancellationToken);
    }

    public Task<bool> MarkAsUnreadAsync(
        MailSearchHitData searchHit,
        CancellationToken cancellationToken = default)
    {
        return SetSeenStateAsync(
            searchHit,
            markAsSeen: false,
            cancellationToken);
    }

    /*
     * Löschen aus einer Suchansicht bedeutet weiterhin:
     *
     * Nachricht in den serverseitigen Papierkorb verschieben.
     *
     * Kein EXPUNGE.
     * Kein Permanent Delete.
     *
     * Die irreversible Löschlogik bleibt vollständig in ihrem
     * bestehenden, separat abgesicherten Workflow.
     */
    public async Task<MailMoveResult>
        MoveToTrashAsync(
            MailSearchHitData searchHit,
            CancellationToken cancellationToken = default)
    {
        ValidateSearchHit(
            searchHit);

        _logger.LogInformation(
            "Search message move started. Operation={Operation}.",
            "Move to trash");

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var trashFolder =
                await GetTrashFolderAsync(
                    client,
                    cancellationToken);

            var result =
                await MoveSearchHitCoreAsync(
                    client,
                    searchHit,
                    trashFolder,
                    cancellationToken);

            _logger.LogInformation(
                "Search message move completed. Operation={Operation}, UndoMappingAvailable={UndoMappingAvailable}.",
                "Move to trash",
                result.CanUndo);

            return result;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Search message move could not be confirmed. Operation={Operation}, ExceptionType={ExceptionType}.",
                "Move to trash",
                exception.GetType().Name);

            throw;
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task<MailMoveResult>
        MoveAsync(
            MailSearchHitData searchHit,
            string targetFolderId,
            CancellationToken cancellationToken = default)
    {
        ValidateSearchHit(
            searchHit);

        ValidateFolderId(
            targetFolderId);

        _logger.LogInformation(
            "Search message move started. Operation={Operation}.",
            "Message move");

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var targetFolder =
                await client.GetFolderAsync(
                    targetFolderId,
                    cancellationToken);

            if (targetFolder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new InvalidOperationException(
                    "Der Zielordner kann keine Nachrichten aufnehmen.");
            }

            var result =
                await MoveSearchHitCoreAsync(
                    client,
                    searchHit,
                    targetFolder,
                    cancellationToken);

            _logger.LogInformation(
                "Search message move completed. Operation={Operation}, UndoMappingAvailable={UndoMappingAvailable}.",
                "Message move",
                result.CanUndo);

            return result;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Search message move could not be confirmed. Operation={Operation}, ExceptionType={ExceptionType}.",
                "Message move",
                exception.GetType().Name);

            throw;
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    /*
     * Read/Unread arbeitet ebenfalls ausschließlich im
     * Ursprungsordner des Suchtreffers.
     *
     * true:
     * Der Zustand wurde durch diese Operation tatsächlich
     * verändert.
     *
     * false:
     * Der Server befand sich bereits im gewünschten Zustand.
     */
    private async Task<bool> SetSeenStateAsync(
        MailSearchHitData searchHit,
        bool markAsSeen,
        CancellationToken cancellationToken)
    {
        ValidateSearchHit(
            searchHit);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    searchHit.FolderId,
                    cancellationToken);

            if (folder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new InvalidOperationException(
                    "Der Ursprungsordner des Suchtreffers kann nicht geöffnet werden.");
            }

            await folder.OpenAsync(
                FolderAccess.ReadWrite,
                cancellationToken);

            ValidateFolderIdentity(
                folder,
                searchHit);

            var summaryBefore =
                await GetSearchHitSummaryAsync(
                    folder,
                    searchHit,
                    includeFlags: true,
                    cancellationToken);

            if (summaryBefore is null ||
                !MessageIdsMatch(
                    searchHit.MessageId,
                    summaryBefore.Envelope?.MessageId))
            {
                throw new InvalidOperationException(
                    "Die E-Mail ist in diesem Serverzustand nicht mehr eindeutig verfügbar.");
            }

            var isCurrentlySeen =
                summaryBefore.Flags.HasValue &&
                summaryBefore.Flags.Value.HasFlag(
                    MessageFlags.Seen);

            if (isCurrentlySeen ==
                markAsSeen)
            {
                return false;
            }

            var uniqueId =
                new UniqueId(
                    searchHit.UniqueId);

            if (markAsSeen)
            {
                await folder.AddFlagsAsync(
                    new[]
                    {
                        uniqueId
                    },
                    MessageFlags.Seen,
                    silent: true,
                    cancellationToken);
            }
            else
            {
                await folder.RemoveFlagsAsync(
                    new[]
                    {
                        uniqueId
                    },
                    MessageFlags.Seen,
                    silent: true,
                    cancellationToken);
            }

            ValidateFolderIdentity(
                folder,
                searchHit);

            var summaryAfter =
                await GetSearchHitSummaryAsync(
                    folder,
                    searchHit,
                    includeFlags: true,
                    cancellationToken);

            if (summaryAfter is null ||
                !MessageIdsMatch(
                    searchHit.MessageId,
                    summaryAfter.Envelope?.MessageId))
            {
                throw new InvalidOperationException(
                    "Der Nachrichtenstatus konnte nach der Änderung nicht eindeutig bestätigt werden.");
            }

            var isSeenAfter =
                summaryAfter.Flags.HasValue &&
                summaryAfter.Flags.Value.HasFlag(
                    MessageFlags.Seen);

            if (isSeenAfter !=
                markAsSeen)
            {
                throw new InvalidOperationException(
                    markAsSeen
                        ? "Der Mailserver hat die Nachricht nicht als gelesen bestätigt."
                        : "Der Mailserver hat die Nachricht nicht als ungelesen bestätigt.");
            }

            return true;
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    /*
     * Verschieben erfolgt absichtlich innerhalb derselben
     * IMAP-Verbindung, in der vorher UIDVALIDITY und
     * Nachrichtenidentität geprüft wurden.
     *
     * Dadurch entsteht zwischen Sicherheitsprüfung und
     * Mutation keine zweite Verbindung mit einem theoretisch
     * anderen Ordnerzustand.
     */
    private static async Task<MailMoveResult>
        MoveSearchHitCoreAsync(
            ImapClient client,
            MailSearchHitData searchHit,
            IMailFolder targetFolder,
            CancellationToken cancellationToken)
    {
        var sourceFolder =
            await client.GetFolderAsync(
                searchHit.FolderId,
                cancellationToken);

        if (sourceFolder.Attributes.HasFlag(
                FolderAttributes.NoSelect))
        {
            throw new InvalidOperationException(
                "Der Quellordner kann nicht geöffnet werden.");
        }

        if (targetFolder.Attributes.HasFlag(
                FolderAttributes.NoSelect))
        {
            throw new InvalidOperationException(
                "Der Zielordner kann keine Nachrichten aufnehmen.");
        }

        if (string.Equals(
                sourceFolder.FullName,
                targetFolder.FullName,
                StringComparison.OrdinalIgnoreCase))
        {
            return new MailMoveResult(
                SourceFolderId:
                    sourceFolder.FullName,

                TargetFolderId:
                    targetFolder.FullName,

                UidMappings:
                    Array.Empty<
                        MailMoveUidMapping>());
        }

        await sourceFolder.OpenAsync(
            FolderAccess.ReadWrite,
            cancellationToken);

        ValidateFolderIdentity(
            sourceFolder,
            searchHit);

        var identityConfirmed =
            await ConfirmSearchHitIdentityInOpenFolderAsync(
                sourceFolder,
                searchHit,
                cancellationToken);

        if (!identityConfirmed)
        {
            throw new InvalidOperationException(
                "Die E-Mail ist in diesem Serverzustand nicht mehr eindeutig verfügbar.");
        }

        var sourceUniqueId =
            new UniqueId(
                searchHit.UniqueId);

        var uniqueIdMap =
            await sourceFolder.MoveToAsync(
                new[]
                {
                    sourceUniqueId
                },
                targetFolder,
                cancellationToken);

        /*
         * Der Server muss bestätigen, dass die Quell-UID aus
         * dem Ursprungsordner verschwunden ist.
         *
         * Wird die Verbindung genau nach MOVE unterbrochen,
         * kann der Zustand mehrdeutig sein. In diesem Fall
         * werfen wir und die UI synchronisiert anschließend,
         * statt die Operation automatisch zu wiederholen.
         */
        var remainingSourceMessages =
            await sourceFolder.FetchAsync(
                new[]
                {
                    sourceUniqueId
                },
                MessageSummaryItems.UniqueId,
                cancellationToken);

        if (remainingSourceMessages.Any(
                summary =>
                    summary.UniqueId.IsValid &&
                    summary.UniqueId ==
                        sourceUniqueId))
        {
            throw new InvalidOperationException(
                "Der Mailserver hat das Verschieben der Nachricht nicht bestätigt.");
        }

        var mappings =
            new List<
                MailMoveUidMapping>();

        if (uniqueIdMap.TryGetValue(
                sourceUniqueId,
                out var targetUniqueId) &&
            targetUniqueId.IsValid)
        {
            mappings.Add(
                new MailMoveUidMapping(
                    SourceUniqueId:
                        sourceUniqueId.Id,

                    TargetUniqueId:
                        targetUniqueId.Id));

            /*
             * Wenn der Server eine Ziel-UID geliefert hat,
             * prüfen wir auch dort noch einmal die Message-ID.
             *
             * Die Quell-UID ist zu diesem Zeitpunkt bereits
             * bestätigt verschwunden.
             */
            await targetFolder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            var targetSummaries =
                await targetFolder.FetchAsync(
                    new[]
                    {
                        targetUniqueId
                    },
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope,
                    cancellationToken);

            var targetSummary =
                targetSummaries.FirstOrDefault(
                    summary =>
                        summary.UniqueId.IsValid &&
                        summary.UniqueId ==
                            targetUniqueId);

            if (targetSummary is null ||
                !MessageIdsMatch(
                    searchHit.MessageId,
                    targetSummary.Envelope?.MessageId))
            {
                throw new InvalidOperationException(
                    "Die verschobene Nachricht konnte im Zielordner nicht eindeutig bestätigt werden.");
            }
        }

        return new MailMoveResult(
            SourceFolderId:
                sourceFolder.FullName,

            TargetFolderId:
                targetFolder.FullName,

            UidMappings:
                mappings);
    }

    private static async Task<IReadOnlyList<MailSearchHitData>>
        SearchFolderCoreAsync(
            IMailFolder folder,
            string searchText,
            int maximumResultCount,
            CancellationToken cancellationToken)
    {
        if (folder.Attributes.HasFlag(
                FolderAttributes.NoSelect))
        {
            return Array.Empty<
                MailSearchHitData>();
        }

        await folder.OpenAsync(
            FolderAccess.ReadOnly,
            cancellationToken);

        var uidValidity =
            folder.UidValidity;

        if (uidValidity == 0)
        {
            throw new InvalidOperationException(
                "Der Mailserver hat für den Suchordner keine gültige UIDVALIDITY geliefert.");
        }

        var query =
            SearchQuery.MessageContains(
                searchText);

        var uniqueIds =
            await GetSearchResultUniqueIdsAsync(
                folder,
                query,
                maximumResultCount,
                cancellationToken);

        if (uniqueIds.Count == 0)
        {
            return Array.Empty<
                MailSearchHitData>();
        }

        var summaries =
            await folder.FetchAsync(
                uniqueIds,
                MessageSummaryItems.UniqueId |
                MessageSummaryItems.Envelope |
                MessageSummaryItems.Flags,
                cancellationToken);

        return summaries
            .Where(
                summary =>
                    summary.UniqueId.IsValid)
            .Select(
                summary =>
                    CreateSearchHit(
                        folder.FullName,
                        uidValidity,
                        summary))
            .OrderByDescending(
                result =>
                    result.Date)
            .ThenByDescending(
                result =>
                    result.UniqueId)
            .Take(
                maximumResultCount)
            .ToList();
    }

    private static async Task<IList<UniqueId>>
        GetSearchResultUniqueIdsAsync(
            IMailFolder folder,
            SearchQuery query,
            int maximumResultCount,
            CancellationToken cancellationToken)
    {
        try
        {
            var sortedUniqueIds =
                await folder.SortAsync(
                    query,
                    new[]
                    {
                        OrderBy.ReverseDate
                    },
                    cancellationToken);

            return sortedUniqueIds
                .Take(
                    maximumResultCount)
                .ToList();
        }
        catch (NotSupportedException)
        {
            var searchedUniqueIds =
                await folder.SearchAsync(
                    query,
                    cancellationToken);

            return searchedUniqueIds
                .Where(
                    uniqueId =>
                        uniqueId.IsValid)
                .OrderByDescending(
                    uniqueId =>
                        uniqueId.Id)
                .Take(
                    maximumResultCount)
                .ToList();
        }
    }

    private async Task<int?>
        FindCurrentMessageIndexAsync(
            MailSearchHitData searchHit,
            CancellationToken cancellationToken)
    {
        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    searchHit.FolderId,
                    cancellationToken);

            if (folder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                return null;
            }

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            if (folder.UidValidity !=
                searchHit.UidValidity)
            {
                return null;
            }

            var orderedUniqueIds =
                await GetAllMessageUniqueIdsAsync(
                    folder,
                    cancellationToken);

            for (var index = 0;
                 index < orderedUniqueIds.Count;
                 index++)
            {
                if (orderedUniqueIds[index].Id ==
                    searchHit.UniqueId)
                {
                    return index;
                }
            }

            return null;
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static async Task<IList<UniqueId>>
        GetAllMessageUniqueIdsAsync(
            IMailFolder folder,
            CancellationToken cancellationToken)
    {
        try
        {
            return await folder.SortAsync(
                SearchQuery.All,
                new[]
                {
                    OrderBy.ReverseDate
                },
                cancellationToken);
        }
        catch (NotSupportedException)
        {
            var summaries =
                await folder.FetchAsync(
                    0,
                    -1,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope,
                    cancellationToken);

            return summaries
                .Where(
                    summary =>
                        summary.UniqueId.IsValid)
                .OrderByDescending(
                    GetMessageSortDate)
                .ThenByDescending(
                    summary =>
                        summary.Index)
                .Select(
                    summary =>
                        summary.UniqueId)
                .ToList();
        }
    }

    private async Task<bool>
        ConfirmSearchHitIdentityAsync(
            MailSearchHitData searchHit,
            CancellationToken cancellationToken)
    {
        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    searchHit.FolderId,
                    cancellationToken);

            if (folder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                return false;
            }

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            if (folder.UidValidity !=
                searchHit.UidValidity)
            {
                return false;
            }

            return await ConfirmSearchHitIdentityInOpenFolderAsync(
                folder,
                searchHit,
                cancellationToken);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static async Task<bool>
        ConfirmSearchHitIdentityInOpenFolderAsync(
            IMailFolder folder,
            MailSearchHitData searchHit,
            CancellationToken cancellationToken)
    {
        var summary =
            await GetSearchHitSummaryAsync(
                folder,
                searchHit,
                includeFlags: false,
                cancellationToken);

        if (summary is null)
        {
            return false;
        }

        return MessageIdsMatch(
            searchHit.MessageId,
            summary.Envelope?.MessageId);
    }

    private static async Task<IMessageSummary?>
        GetSearchHitSummaryAsync(
            IMailFolder folder,
            MailSearchHitData searchHit,
            bool includeFlags,
            CancellationToken cancellationToken)
    {
        if (folder.UidValidity !=
            searchHit.UidValidity)
        {
            return null;
        }

        var items =
            MessageSummaryItems.UniqueId |
            MessageSummaryItems.Envelope;

        if (includeFlags)
        {
            items |=
                MessageSummaryItems.Flags;
        }

        var summaries =
            await folder.FetchAsync(
                new[]
                {
                    new UniqueId(
                        searchHit.UniqueId)
                },
                items,
                cancellationToken);

        return summaries.FirstOrDefault(
            summary =>
                summary.UniqueId.IsValid &&
                summary.UniqueId.Id ==
                    searchHit.UniqueId);
    }

    private static void ValidateFolderIdentity(
        IMailFolder folder,
        MailSearchHitData searchHit)
    {
        if (folder.UidValidity !=
            searchHit.UidValidity)
        {
            throw new InvalidOperationException(
                "Der Ordner wurde seit der Suche serverseitig verändert.");
        }
    }

    private static DateTimeOffset GetMessageSortDate(
        IMessageSummary summary)
    {
        return summary.Envelope?.Date
            ?? DateTimeOffset.MinValue;
    }

    private static MailSearchHitData
        CreateSearchHit(
            string folderId,
            uint uidValidity,
            IMessageSummary summary)
    {
        var senderMailbox =
            summary.Envelope?
                .From?
                .Mailboxes
                .FirstOrDefault();

        var senderAddress =
            senderMailbox?.Address
            ?? string.Empty;

        var senderName =
            !string.IsNullOrWhiteSpace(
                senderMailbox?.Name)
                ? senderMailbox.Name
                : senderAddress;

        if (string.IsNullOrWhiteSpace(
                senderName))
        {
            senderName =
                "Unbekannter Absender";
        }

        var recipientAddress =
            summary.Envelope?
                .To?
                .Mailboxes
                .FirstOrDefault()?
                .Address
            ?? string.Empty;

        var subject =
            summary.Envelope?.Subject;

        if (string.IsNullOrWhiteSpace(
                subject))
        {
            subject =
                "(Kein Betreff)";
        }

        var date =
            summary.Envelope?.Date
            ?? DateTimeOffset.MinValue;

        var isUnread =
            !summary.Flags.HasValue ||
            !summary.Flags.Value.HasFlag(
                MessageFlags.Seen);

        return new MailSearchHitData(
            FolderId:
                folderId,

            UidValidity:
                uidValidity,

            UniqueId:
                summary.UniqueId.Id,

            MessageId:
                NormalizeMessageId(
                    summary.Envelope?.MessageId),

            Sender:
                senderName,

            SenderAddress:
                senderAddress,

            RecipientAddress:
                recipientAddress,

            Subject:
                subject,

            Date:
                date,

            IsUnread:
                isUnread);
    }

    private static bool MessageIdsMatch(
        string? expectedMessageId,
        string? actualMessageId)
    {
        var expected =
            NormalizeMessageId(
                expectedMessageId);

        var actual =
            NormalizeMessageId(
                actualMessageId);

        /*
         * Fehlt beim ursprünglichen Treffer eine Message-ID,
         * bleiben UID + UIDVALIDITY die Identitätsanker.
         */
        if (expected is null)
        {
            return true;
        }

        return string.Equals(
            expected,
            actual,
            StringComparison.Ordinal);
    }

    private static string? NormalizeMessageId(
        string? messageId)
    {
        if (string.IsNullOrWhiteSpace(
                messageId))
        {
            return null;
        }

        return messageId.Trim();
    }

    private static async Task<IReadOnlyList<IMailFolder>>
        GetSearchableFoldersAsync(
            ImapClient client,
            CancellationToken cancellationToken)
    {
        var folders =
            new List<IMailFolder>();

        if (client.PersonalNamespaces.Count > 0)
        {
            var personalFolders =
                await client.GetFoldersAsync(
                    client.PersonalNamespaces[0],
                    StatusItems.None,
                    false,
                    cancellationToken);

            folders.AddRange(
                personalFolders);
        }

        var inboxAlreadyIncluded =
            folders.Any(
                folder =>
                    string.Equals(
                        folder.FullName,
                        client.Inbox.FullName,
                        StringComparison.OrdinalIgnoreCase));

        if (!inboxAlreadyIncluded)
        {
            folders.Insert(
                0,
                client.Inbox);
        }

        return folders
            .Where(
                folder =>
                    !folder.Attributes.HasFlag(
                        FolderAttributes.NoSelect))
            .GroupBy(
                folder =>
                    folder.FullName,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group =>
                    group.First())
            .ToList();
    }

    private static async Task<IMailFolder>
        GetTrashFolderAsync(
            ImapClient client,
            CancellationToken cancellationToken)
    {
        var specialUseTrash =
            client.GetFolder(
                SpecialFolder.Trash);

        if (specialUseTrash is not null &&
            !specialUseTrash.Attributes.HasFlag(
                FolderAttributes.NoSelect))
        {
            return specialUseTrash;
        }

        if (client.PersonalNamespaces.Count > 0)
        {
            var folders =
                await client.GetFoldersAsync(
                    client.PersonalNamespaces[0],
                    StatusItems.None,
                    false,
                    cancellationToken);

            var fallbackTrash =
                folders.FirstOrDefault(
                    folder =>
                        !folder.Attributes.HasFlag(
                            FolderAttributes.NoSelect) &&
                        IsTrashFolderName(
                            folder.Name));

            if (fallbackTrash is not null)
            {
                return fallbackTrash;
            }
        }

        throw new InvalidOperationException(
            "Auf dem Mailserver konnte kein Papierkorb ermittelt werden.");
    }

    private static bool IsTrashFolderName(
        string folderName)
    {
        if (string.IsNullOrWhiteSpace(
                folderName))
        {
            return false;
        }

        return folderName
            .Trim()
            .ToLowerInvariant() switch
        {
            "trash" => true,
            "deleted items" => true,
            "deleted messages" => true,
            "papierkorb" => true,
            "gelöschte elemente" => true,
            "geloeschte elemente" => true,
            _ => false
        };
    }

    private static string NormalizeSearchText(
        string searchText)
    {
        if (string.IsNullOrWhiteSpace(
                searchText))
        {
            throw new ArgumentException(
                "Der Suchbegriff darf nicht leer sein.",
                nameof(searchText));
        }

        return searchText.Trim();
    }

    private static int NormalizeMaximumResultCount(
        int maximumResultCount)
    {
        if (maximumResultCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumResultCount),
                "Die maximale Trefferanzahl muss größer als 0 sein.");
        }

        return Math.Min(
            maximumResultCount,
            MaximumAllowedResultCount);
    }

    private static void ValidateFolderId(
        string folderId)
    {
        if (string.IsNullOrWhiteSpace(
                folderId))
        {
            throw new ArgumentException(
                "Der Mailordner darf nicht leer sein.",
                nameof(folderId));
        }
    }

    private static void ValidateSearchHit(
        MailSearchHitData searchHit)
    {
        ArgumentNullException.ThrowIfNull(
            searchHit);

        ValidateFolderId(
            searchHit.FolderId);

        if (searchHit.UidValidity == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(searchHit),
                "Der Suchtreffer enthält keine gültige UIDVALIDITY.");
        }

        if (searchHit.UniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(searchHit),
                "Der Suchtreffer enthält keine gültige UID.");
        }
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
            await client.ConnectAsync(
                ImapHost,
                ImapPort,
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

    private static async Task DisconnectSafelyAsync(
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
}