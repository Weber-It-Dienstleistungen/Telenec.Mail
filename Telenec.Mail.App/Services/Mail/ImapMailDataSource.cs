using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class ImapMailDataSource : IMailDataSource
{
    private const string ImapHost = "mail.necnet.de";
    private const int ImapPort = 993;

    private const int MaximumWebViewHtmlBytes =
        2 * 1024 * 1024;

    private static readonly Regex CidReferenceRegex =
        new(
            @"\bcid:(?<contentId>[^""'\s<>)]+)",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

    private readonly IMailAccountStore _mailAccountStore;
    private readonly ICredentialStore _credentialStore;

    public ImapMailDataSource(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;
    }

    public async Task<IReadOnlyList<MailFolderData>> GetFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folders =
                new List<IMailFolder>();

            if (client.PersonalNamespaces.Count > 0)
            {
                var serverFolders =
                    await client.GetFoldersAsync(
                        client.PersonalNamespaces[0],
                        StatusItems.Count |
                        StatusItems.Unread,
                        false,
                        cancellationToken);

                folders.AddRange(
                    serverFolders);
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
                await TryUpdateFolderStatusAsync(
                    client.Inbox,
                    cancellationToken);

                folders.Insert(
                    0,
                    client.Inbox);
            }

            var uniqueFolders =
                folders
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
                    .OrderBy(
                        folder =>
                            GetFolderSortOrder(
                                folder,
                                client.Inbox.FullName))
                    .ThenBy(
                        folder =>
                            GetDisplayName(
                                folder,
                                client.Inbox.FullName),
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            return uniqueFolders
                .Select(
                    folder =>
                    {
                        var unreadCount =
                            Math.Max(
                                folder.Unread,
                                0);

                        var messageCount =
                            Math.Max(
                                folder.Count,
                                0);

                        var subtitle =
                            unreadCount > 0
                                ? $"{unreadCount} ungelesene Nachrichten"
                                : $"{messageCount} Nachrichten";

                        return new MailFolderData(
                            FolderId:
                                folder.FullName,

                            DisplayName:
                                GetDisplayName(
                                    folder,
                                    client.Inbox.FullName),

                            HeaderSubtitle:
                                subtitle,

                            UnreadCount:
                                unreadCount,

                            MessageCount:
                                messageCount);
                    })
                .ToList();
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public Task<IReadOnlyList<MailMessageData>> GetMessagesAsync(
        string folderId,
        int maximumMessageCount = 20,
        CancellationToken cancellationToken = default)
    {
        return GetMessagePageAsync(
            folderId,
            skipMessageCount: 0,
            maximumMessageCount,
            cancellationToken);
    }

    public async Task<IReadOnlyList<MailMessageData>> GetMessagePageAsync(
        string folderId,
        int skipMessageCount,
        int maximumMessageCount,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId,
            nameof(folderId));

        if (skipMessageCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skipMessageCount),
                "Die Anzahl der zu überspringenden Nachrichten darf nicht negativ sein.");
        }

        if (maximumMessageCount <= 0)
        {
            return Array.Empty<
                MailMessageData>();
        }

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    folderId,
                    cancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            if (folder.Count == 0 ||
                skipMessageCount >= folder.Count)
            {
                return Array.Empty<
                    MailMessageData>();
            }

            var uniqueIds =
                await GetMessagePageUniqueIdsAsync(
                    folder,
                    skipMessageCount,
                    maximumMessageCount,
                    cancellationToken);

            if (uniqueIds.Count == 0)
            {
                return Array.Empty<
                    MailMessageData>();
            }

            var summaries =
                await folder.FetchAsync(
                    uniqueIds,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope |
                    MessageSummaryItems.Flags |
                    MessageSummaryItems.BodyStructure |
                    MessageSummaryItems.References,
                    MailImportanceSummaryService.HeaderFields,
                    cancellationToken);

            /*
             * Ein UID-Fetch muss nicht in derselben
             * Reihenfolge zurückkommen, in der die UIDs
             * angefordert wurden.
             *
             * Die endgültige Reihenfolge wird deshalb exakt
             * anhand der zuvor global sortierten UID-Liste
             * wiederhergestellt.
             */
            var orderedSummaries =
                MailSortOrder
                    .RestoreRequestedUniqueIdOrder(
                        summaries,
                        uniqueIds);

            var messages =
                new List<MailMessageData>();

            foreach (var summary in orderedSummaries)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var bodyContent =
                    await GetBodyContentAsync(
                        folder,
                        summary,
                        cancellationToken);

                var readReceipt =
                    await TryDetectReadReceiptAsync(
                        folder,
                        summary,
                        cancellationToken);

                messages.Add(
                    CreateMessageData(
                        summary,
                        bodyContent,
                        readReceipt));
            }

            return messages;
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task DownloadAttachmentAsync(
        string folderId,
        uint uniqueId,
        string partSpecifier,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId,
            nameof(folderId));

        if (uniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uniqueId),
                "Die Nachrichten-ID muss größer als 0 sein.");
        }

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
                    folderId,
                    cancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            if (folder is not IImapFolder imapFolder)
            {
                throw new InvalidOperationException(
                    "Der Mailordner unterstützt keinen gezielten IMAP-Anhangabruf.");
            }

            var entity =
                await imapFolder
                    .GetBodyPartAsync(
                        new UniqueId(
                            uniqueId),
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
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static async Task<IList<UniqueId>>
        GetMessagePageUniqueIdsAsync(
            IMailFolder folder,
            int skipMessageCount,
            int maximumMessageCount,
            CancellationToken cancellationToken)
    {
        var sort =
            MailSortState.Effective;

        try
        {
            /*
             * Entscheidend:
             *
             * Zuerst wird der GESAMTE Ordner durch den
             * Mailserver sortiert.
             *
             * Erst anschließend schneiden wir die gewünschte
             * 20er-Seite aus der sortierten UID-Liste.
             *
             * Damit sortieren die UI-Schaltflächen nicht nur
             * die gerade geladenen Nachrichten.
             */
            var sortedUniqueIds =
                await folder.SortAsync(
                    SearchQuery.All,
                    MailSortOrder
                        .CreateServerOrder(
                            sort),
                    cancellationToken);

            return sortedUniqueIds
                .Skip(
                    skipMessageCount)
                .Take(
                    maximumMessageCount)
                .ToList();
        }
        catch (NotSupportedException)
        {
            /*
             * Fallback für Server ohne IMAP SORT bzw. ohne
             * Unterstützung eines einzelnen Sortierkriteriums.
             *
             * Wir laden lediglich UID + Envelope des gesamten
             * Ordners, sortieren diese leichten Daten lokal und
             * laden anschließend weiterhin nur die Bodies der
             * tatsächlich benötigten Seite.
             */
            var lightweightSummaries =
                await folder.FetchAsync(
                    0,
                    -1,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope,
                    cancellationToken);

            return MailSortOrder
                .SortSummaries(
                    lightweightSummaries,
                    sort)
                .Skip(
                    skipMessageCount)
                .Take(
                    maximumMessageCount)
                .Select(
                    summary =>
                        summary.UniqueId)
                .ToList();
        }
    }

    public async Task MarkAsReadAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId,
            nameof(folderId));

        if (uniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uniqueId),
                "Die Nachrichten-ID muss größer als 0 sein.");
        }

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    folderId,
                    cancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadWrite,
                cancellationToken);

            await folder.AddFlagsAsync(
                new UniqueId(
                    uniqueId),
                MessageFlags.Seen,
                silent: true,
                cancellationToken);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task MarkAsUnreadAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId,
            nameof(folderId));

        if (uniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uniqueId),
                "Die Nachrichten-ID muss größer als 0 sein.");
        }

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    folderId,
                    cancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadWrite,
                cancellationToken);

            await folder.RemoveFlagsAsync(
                new UniqueId(
                    uniqueId),
                MessageFlags.Seen,
                silent: true,
                cancellationToken);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task SetKeywordAsync(
        string folderId,
        uint uniqueId,
        string keyword,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId,
            nameof(folderId));

        if (uniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uniqueId),
                "Die Nachrichten-ID muss größer als 0 sein.");
        }

        var normalizedKeyword =
            NormalizeKeyword(
                keyword);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var folder =
                await client.GetFolderAsync(
                    folderId,
                    cancellationToken);

            await folder.OpenAsync(
                FolderAccess.ReadWrite,
                cancellationToken);

            if (!folder.PermanentFlags.HasFlag(
                    MessageFlags.UserDefined))
            {
                throw new NotSupportedException(
                    "Der Mailserver unterstützt in diesem Ordner keine benutzerdefinierten Kategorien.");
            }

            var uniqueIdValue =
                new UniqueId(
                    uniqueId);

            var keywords =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    normalizedKeyword
                };

            if (isEnabled)
            {
                await folder.AddFlagsAsync(
                    uniqueIdValue,
                    MessageFlags.None,
                    keywords,
                    silent: true,
                    cancellationToken);
            }
            else
            {
                await folder.RemoveFlagsAsync(
                    uniqueIdValue,
                    MessageFlags.None,
                    keywords,
                    silent: true,
                    cancellationToken);
            }

            var summaries =
                await folder.FetchAsync(
                    new[]
                    {
                        uniqueIdValue
                    },
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Flags,
                    cancellationToken);

            var summary =
                summaries.FirstOrDefault(
                    current =>
                        current.UniqueId.IsValid &&
                        current.UniqueId ==
                            uniqueIdValue);

            if (summary is null)
            {
                throw new InvalidOperationException(
                    "Die Nachricht konnte nach der Kategorieänderung nicht mehr eindeutig bestätigt werden.");
            }

            var keywordIsPresent =
                summary.Keywords?
                    .Any(
                        currentKeyword =>
                            string.Equals(
                                currentKeyword,
                                normalizedKeyword,
                                StringComparison.OrdinalIgnoreCase))
                == true;

            if (keywordIsPresent !=
                isEnabled)
            {
                throw new InvalidOperationException(
                    "Der Mailserver hat die Kategorieänderung nicht bestätigt.");
            }
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public Task<MailMoveResult> MoveToTrashAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        return MoveToTrashAsync(
            folderId,
            new[] { uniqueId },
            cancellationToken);
    }

    public async Task<MailMoveResult> MoveToTrashAsync(
        string folderId,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId,
            nameof(folderId));

        var normalizedUniqueIds =
            NormalizeUniqueIds(
                uniqueIds);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            var trashFolder =
                await GetTrashFolderAsync(
                    client,
                    cancellationToken);

            return await MoveMessagesCoreAsync(
                client,
                folderId,
                trashFolder,
                normalizedUniqueIds,
                cancellationToken);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task<MailMoveResult> MoveMessagesAsync(
        string sourceFolderId,
        string targetFolderId,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            sourceFolderId,
            nameof(sourceFolderId));

        ValidateFolderId(
            targetFolderId,
            nameof(targetFolderId));

        var normalizedUniqueIds =
            NormalizeUniqueIds(
                uniqueIds);

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

            return await MoveMessagesCoreAsync(
                client,
                sourceFolderId,
                targetFolder,
                normalizedUniqueIds,
                cancellationToken);
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static async Task<MailMoveResult> MoveMessagesCoreAsync(
        ImapClient client,
        string sourceFolderId,
        IMailFolder targetFolder,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken)
    {
        var sourceFolder =
            await client.GetFolderAsync(
                sourceFolderId,
                cancellationToken);

        if (sourceFolder.Attributes.HasFlag(
                FolderAttributes.NoSelect))
        {
            throw new InvalidOperationException(
                "Der Quellordner kann nicht geöffnet werden.");
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
                    Array.Empty<MailMoveUidMapping>());
        }

        await sourceFolder.OpenAsync(
            FolderAccess.ReadWrite,
            cancellationToken);

        var sourceUniqueIds =
            uniqueIds
                .Select(
                    uniqueId =>
                        new UniqueId(
                            uniqueId))
                .ToList();

        var uniqueIdMap =
            await sourceFolder.MoveToAsync(
                sourceUniqueIds,
                targetFolder,
                cancellationToken);

        var mappings =
            new List<MailMoveUidMapping>();

        foreach (var sourceUniqueId in sourceUniqueIds)
        {
            if (!uniqueIdMap.TryGetValue(
                    sourceUniqueId,
                    out var targetUniqueId) ||
                !targetUniqueId.IsValid)
            {
                continue;
            }

            mappings.Add(
                new MailMoveUidMapping(
                    SourceUniqueId:
                        sourceUniqueId.Id,

                    TargetUniqueId:
                        targetUniqueId.Id));
        }

        return new MailMoveResult(
            SourceFolderId:
                sourceFolder.FullName,

            TargetFolderId:
                targetFolder.FullName,

            UidMappings:
                mappings);
    }

    private static string NormalizeKeyword(
        string keyword)
    {
        if (string.IsNullOrWhiteSpace(
                keyword))
        {
            throw new ArgumentException(
                "Das IMAP-Keyword darf nicht leer sein.",
                nameof(keyword));
        }

        var normalized =
            keyword.Trim();

        if (!Regex.IsMatch(
                normalized,
                "^[A-Za-z0-9][A-Za-z0-9._-]*$",
                RegexOptions.CultureInvariant))
        {
            throw new ArgumentException(
                "Das IMAP-Keyword enthält unzulässige Zeichen.",
                nameof(keyword));
        }

        return normalized;
    }

    private static void ValidateFolderId(
        string folderId,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                folderId))
        {
            throw new ArgumentException(
                "Der Ordner darf nicht leer sein.",
                parameterName);
        }
    }

    private static IReadOnlyList<uint> NormalizeUniqueIds(
        IReadOnlyList<uint> uniqueIds)
    {
        ArgumentNullException.ThrowIfNull(
            uniqueIds);

        var normalized =
            uniqueIds
                .Where(
                    uniqueId =>
                        uniqueId > 0)
                .Distinct()
                .ToList();

        if (normalized.Count == 0)
        {
            throw new ArgumentException(
                "Es wurde keine gültige Nachrichten-ID übergeben.",
                nameof(uniqueIds));
        }

        return normalized;
    }

    private static async Task<IMailFolder> GetTrashFolderAsync(
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
            string.IsNullOrEmpty(
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

    private static async Task<MessageBodyContent>
        GetBodyContentAsync(
            IMailFolder folder,
            IMessageSummary summary,
            CancellationToken cancellationToken)
    {
        string plainText =
            string.Empty;

        string? htmlBody =
            null;

        var inlinePartSpecifiers =
            new HashSet<string>(
                StringComparer.Ordinal);

        if (summary.TextBody is not null)
        {
            var textEntity =
                await folder.GetBodyPartAsync(
                    summary.UniqueId,
                    summary.TextBody,
                    cancellationToken);

            if (textEntity is TextPart textPart)
            {
                plainText =
                    NormalizeBodyText(
                        textPart.Text
                        ?? string.Empty);
            }
        }

        if (summary.HtmlBody is not null)
        {
            var htmlEntity =
                await folder.GetBodyPartAsync(
                    summary.UniqueId,
                    summary.HtmlBody,
                    cancellationToken);

            if (htmlEntity is TextPart htmlPart)
            {
                htmlBody =
                    htmlPart.Text
                    ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(
                plainText) &&
            !string.IsNullOrWhiteSpace(
                htmlBody))
        {
            plainText =
                ConvertHtmlToPlainText(
                    htmlBody);
        }

        if (!string.IsNullOrWhiteSpace(
                htmlBody))
        {
            var cidResolution =
                await ResolveCidImagesAsync(
                    folder,
                    summary,
                    htmlBody,
                    cancellationToken);

            htmlBody =
                cidResolution.HtmlBody;

            inlinePartSpecifiers.UnionWith(
                cidResolution.InlinePartSpecifiers);
        }

        return new MessageBodyContent(
            PlainText:
                plainText,

            HtmlBody:
                htmlBody,

            InlinePartSpecifiers:
                inlinePartSpecifiers);
    }

    private static async Task<CidResolutionResult>
        ResolveCidImagesAsync(
            IMailFolder folder,
            IMessageSummary summary,
            string htmlBody,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                htmlBody))
        {
            return new CidResolutionResult(
                HtmlBody:
                    htmlBody,

                InlinePartSpecifiers:
                    new HashSet<string>(
                        StringComparer.Ordinal));
        }

        var referencedContentIds =
            CidReferenceRegex
                .Matches(
                    htmlBody)
                .Cast<Match>()
                .Select(
                    match =>
                        NormalizeContentId(
                            match
                                .Groups["contentId"]
                                .Value))
                .Where(
                    contentId =>
                        !string.IsNullOrWhiteSpace(
                            contentId))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (referencedContentIds.Count == 0)
        {
            return new CidResolutionResult(
                HtmlBody:
                    htmlBody,

                InlinePartSpecifiers:
                    new HashSet<string>(
                        StringComparer.Ordinal));
        }

        var imagePartsByContentId =
            new Dictionary<string, BodyPartBasic>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var bodyPart in
                 summary.BodyParts.OfType<BodyPartBasic>())
        {
            if (!string.Equals(
                    bodyPart.ContentType.MediaType,
                    "image",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    bodyPart.ContentId) ||
                string.IsNullOrWhiteSpace(
                    bodyPart.PartSpecifier))
            {
                continue;
            }

            var normalizedContentId =
                NormalizeContentId(
                    bodyPart.ContentId);

            if (string.IsNullOrWhiteSpace(
                    normalizedContentId))
            {
                continue;
            }

            imagePartsByContentId.TryAdd(
                normalizedContentId,
                bodyPart);
        }

        if (imagePartsByContentId.Count == 0)
        {
            return new CidResolutionResult(
                HtmlBody:
                    htmlBody,

                InlinePartSpecifiers:
                    new HashSet<string>(
                        StringComparer.Ordinal));
        }

        var resolvedHtml =
            htmlBody;

        var inlinePartSpecifiers =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var referencedContentId in
                 referencedContentIds)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (!imagePartsByContentId.TryGetValue(
                    referencedContentId,
                    out var bodyPart))
            {
                continue;
            }

            try
            {
                var entity =
                    await folder.GetBodyPartAsync(
                        summary.UniqueId,
                        bodyPart,
                        cancellationToken);

                if (entity is not MimePart mimePart ||
                    mimePart.Content is null ||
                    !string.Equals(
                        mimePart.ContentType.MediaType,
                        "image",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var buffer =
                    new MemoryStream();

                await mimePart.Content
                    .DecodeToAsync(
                        buffer,
                        cancellationToken);

                if (buffer.Length == 0)
                {
                    continue;
                }

                var mimeType =
                    mimePart.ContentType.MimeType;

                if (string.IsNullOrWhiteSpace(
                        mimeType))
                {
                    continue;
                }

                var dataUri =
                    $"data:{mimeType};base64," +
                    Convert.ToBase64String(
                        buffer.ToArray());

                var candidateHtml =
                    CidReferenceRegex.Replace(
                        resolvedHtml,
                        match =>
                        {
                            var matchedContentId =
                                NormalizeContentId(
                                    match
                                        .Groups["contentId"]
                                        .Value);

                            return string.Equals(
                                    matchedContentId,
                                    referencedContentId,
                                    StringComparison.OrdinalIgnoreCase)
                                ? dataUri
                                : match.Value;
                        });

                if (string.Equals(
                        candidateHtml,
                        resolvedHtml,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var candidateHtmlSize =
                    Encoding.UTF8.GetByteCount(
                        candidateHtml);

                if (candidateHtmlSize >
                    MaximumWebViewHtmlBytes)
                {
                    continue;
                }

                resolvedHtml =
                    candidateHtml;

                inlinePartSpecifiers.Add(
                    bodyPart.PartSpecifier);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        return new CidResolutionResult(
            HtmlBody:
                resolvedHtml,

            InlinePartSpecifiers:
                inlinePartSpecifiers);
    }

    private static async Task<MailReadReceiptData?>
        TryDetectReadReceiptAsync(
            IMailFolder folder,
            IMessageSummary summary,
            CancellationToken cancellationToken)
    {
        if (!MayContainReadReceipt(
                summary.Body))
        {
            return null;
        }

        try
        {
            using var message =
                await folder.GetMessageAsync(
                    summary.UniqueId,
                    cancellationToken);

            return MailReadReceiptDetectionService
                .Detect(
                    message);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool MayContainReadReceipt(
        BodyPart? bodyPart)
    {
        if (bodyPart is null)
        {
            return false;
        }

        if (bodyPart is BodyPartMultipart multipart)
        {
            return multipart
                .BodyParts
                .Any(
                    MayContainReadReceipt);
        }

        if (bodyPart is BodyPartMessage messagePart)
        {
            if (IsReadReceiptMimeType(
                    messagePart.ContentType.MimeType))
            {
                return true;
            }

            return MayContainReadReceipt(
                messagePart.Body);
        }

        if (bodyPart is not BodyPartBasic basicPart)
        {
            return false;
        }

        return IsReadReceiptMimeType(
            basicPart.ContentType.MimeType);
    }

    private static bool IsReadReceiptMimeType(
        string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(
                mimeType))
        {
            return false;
        }

        return
            string.Equals(
                mimeType,
                "message/disposition-notification",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                mimeType,
                "application/ms-tnef",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                mimeType,
                "application/vnd.ms-tnef",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeContentId(
        string? contentId)
    {
        if (string.IsNullOrWhiteSpace(
                contentId))
        {
            return string.Empty;
        }

        var normalized =
            WebUtility
                .HtmlDecode(
                    contentId)
                .Trim();

        try
        {
            normalized =
                Uri.UnescapeDataString(
                    normalized);
        }
        catch (UriFormatException)
        {
        }

        return normalized
            .Trim()
            .Trim(
                '<',
                '>');
    }

    private static MailMessageData CreateMessageData(
        IMessageSummary summary,
        MessageBodyContent bodyContent,
        MailReadReceiptData? readReceipt)
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

        var toAddresses =
            GetMailboxAddresses(
                summary.Envelope?.To);

        var ccAddresses =
            GetMailboxAddresses(
                summary.Envelope?.Cc);

        var replyToAddresses =
            GetMailboxAddresses(
                summary.Envelope?.ReplyTo);

        var recipientAddress =
            toAddresses.FirstOrDefault()
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
            ?? DateTimeOffset.Now;

        var isUnread =
            !summary.Flags.HasValue ||
            !summary.Flags.Value.HasFlag(
                MessageFlags.Seen);

        var preview =
            CreatePreview(
                bodyContent.PlainText);

        var senderInitial =
            senderName
                .Trim()
                .FirstOrDefault();

        var hasSmimeSignature =
            HasSmimeSignature(
                summary);

        var attachments =
            CreateAttachmentData(
                summary,
                bodyContent.InlinePartSpecifiers);

        var messageId =
            summary.Envelope?
                .MessageId?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                messageId))
        {
            messageId =
                null;
        }

        var references =
            summary.References?
                .Where(
                    reference =>
                        !string.IsNullOrWhiteSpace(
                            reference))
                .Select(
                    reference =>
                        reference.Trim())
                .ToArray()
            ?? Array.Empty<string>();

        var importance =
            MailImportanceSummaryService.Read(
                summary);

        var keywords =
            summary.Keywords?
                .Where(
                    keyword =>
                        !string.IsNullOrWhiteSpace(
                            keyword))
                .Select(
                    keyword =>
                        keyword.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? Array.Empty<string>();

        return new MailMessageData(
            Sender:
                senderName,

            SenderAddress:
                senderAddress,

            RecipientAddress:
                recipientAddress,

            Subject:
                subject,

            Preview:
                preview,

            DisplayTime:
                FormatDisplayTime(
                    date),

            DisplayDateTime:
                FormatDisplayDateTime(
                    date),

            SenderInitial:
                senderInitial == default
                    ? "?"
                    : senderInitial
                        .ToString()
                        .ToUpperInvariant(),

            Greeting:
                string.Empty,

            Body:
                string.IsNullOrWhiteSpace(
                    bodyContent.PlainText)
                    ? "(Für diese Nachricht ist kein darstellbarer Textinhalt verfügbar.)"
                    : bodyContent.PlainText,

            Closing:
                string.Empty,

            Signature:
                string.Empty,

            IsUnread:
                isUnread,

            EmphasizeSender:
                isUnread,

            HtmlBody:
                bodyContent.HtmlBody,

            UniqueId:
                summary.UniqueId.Id,

            Attachments:
                attachments,

            HasSmimeSignature:
                hasSmimeSignature,

            MessageId:
                messageId,

            References:
                references,

            ToAddresses:
                toAddresses,

            CcAddresses:
                ccAddresses,

            ReplyToAddresses:
                replyToAddresses,

            Importance:
                importance,

            ReadReceipt:
                readReceipt,

            Keywords:
                keywords);
    }

    private static IReadOnlyList<string>
        GetMailboxAddresses(
            InternetAddressList? addressList)
    {
        if (addressList is null)
        {
            return Array.Empty<string>();
        }

        return addressList
            .Mailboxes
            .Select(
                mailbox =>
                    mailbox.Address?.Trim())
            .Where(
                address =>
                    !string.IsNullOrWhiteSpace(
                        address))
            .Select(
                address =>
                    address!)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasSmimeSignature(
        IMessageSummary summary)
    {
        return summary
            .Attachments
            .Any(
                IsSmimeSignaturePart);
    }

    private static IReadOnlyList<MailAttachmentData>
        CreateAttachmentData(
            IMessageSummary summary,
            IReadOnlySet<string> inlinePartSpecifiers)
    {
        var attachmentCandidates =
            summary
                .BodyParts
                .OfType<BodyPartBasic>()
                .Where(
                    bodyPart =>
                        !string.IsNullOrWhiteSpace(
                            bodyPart.PartSpecifier))
                .Where(
                    bodyPart =>
                        !inlinePartSpecifiers.Contains(
                            bodyPart.PartSpecifier))
                .Where(
                    bodyPart =>
                        !IsSmimeSignaturePart(
                            bodyPart))
                .Where(
                    bodyPart =>
                        bodyPart.IsAttachment ||
                        string.Equals(
                            bodyPart.ContentType.MediaType,
                            "image",
                            StringComparison.OrdinalIgnoreCase))
                .GroupBy(
                    bodyPart =>
                        bodyPart.PartSpecifier,
                    StringComparer.Ordinal)
                .Select(
                    group =>
                        group.First())
                .ToList();

        var attachments =
            new List<MailAttachmentData>();

        var attachmentNumber =
            0;

        foreach (var attachment in
                 attachmentCandidates)
        {
            var partSpecifier =
                attachment.PartSpecifier;

            if (string.IsNullOrWhiteSpace(
                    partSpecifier))
            {
                continue;
            }

            attachmentNumber++;

            var fileName =
                GetSafeAttachmentFileName(
                    attachment,
                    attachmentNumber);

            var contentType =
                attachment.ContentType.MimeType;

            if (string.IsNullOrWhiteSpace(
                    contentType))
            {
                contentType =
                    "application/octet-stream";
            }

            attachments.Add(
                new MailAttachmentData(
                    PartSpecifier:
                        partSpecifier,

                    FileName:
                        fileName,

                    ContentType:
                        contentType,

                    EncodedSizeBytes:
                        attachment.Octets));
        }

        return attachments;
    }

    private static bool IsSmimeSignaturePart(
        BodyPartBasic attachment)
    {
        var mimeType =
            attachment.ContentType.MimeType;

        if (string.IsNullOrWhiteSpace(
                mimeType))
        {
            return false;
        }

        return
            string.Equals(
                mimeType,
                "application/pkcs7-signature",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                mimeType,
                "application/x-pkcs7-signature",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeAttachmentFileName(
        BodyPartBasic attachment,
        int attachmentNumber)
    {
        var fileName =
            attachment.FileName?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            if (string.Equals(
                    attachment.ContentType.MimeType,
                    "message/rfc822",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                    $"Angehängte Nachricht {attachmentNumber}.eml";
            }

            return
                $"Anhang {attachmentNumber}";
        }

        try
        {
            var safeFileName =
                Path.GetFileName(
                    fileName);

            return string.IsNullOrWhiteSpace(
                    safeFileName)
                ? $"Anhang {attachmentNumber}"
                : safeFileName;
        }
        catch
        {
            return
                $"Anhang {attachmentNumber}";
        }
    }

    private static string GetDisplayName(
        IMailFolder folder,
        string inboxFullName)
    {
        if (string.Equals(
                folder.FullName,
                inboxFullName,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Posteingang";
        }

        var name =
            folder.Name.Trim();

        return name.ToLowerInvariant() switch
        {
            "sent" => "Gesendet",
            "sent items" => "Gesendet",
            "sent messages" => "Gesendet",
            "gesendet" => "Gesendet",
            "drafts" => "Entwürfe",
            "draft" => "Entwürfe",
            "entwürfe" => "Entwürfe",
            "trash" => "Papierkorb",
            "deleted items" => "Papierkorb",
            "papierkorb" => "Papierkorb",
            "junk" => "Junk",
            "spam" => "Spam",
            "archive" => "Archiv",
            "archives" => "Archiv",
            _ => name
        };
    }

    private static int GetFolderSortOrder(
        IMailFolder folder,
        string inboxFullName)
    {
        var displayName =
            GetDisplayName(
                folder,
                inboxFullName);

        return displayName switch
        {
            "Posteingang" => 0,
            "Gesendet" => 1,
            "Entwürfe" => 2,
            "Archiv" => 3,
            "Junk" => 4,
            "Spam" => 4,
            "Papierkorb" => 5,
            _ => 100
        };
    }

    private static string CreatePreview(
        string body)
    {
        if (string.IsNullOrWhiteSpace(
                body))
        {
            return
                "Kein Nachrichtentext verfügbar.";
        }

        var preview =
            Regex.Replace(
                    body,
                    @"\s+",
                    " ")
                .Trim();

        const int maximumLength =
            140;

        if (preview.Length <=
            maximumLength)
        {
            return preview;
        }

        return preview[..maximumLength]
            .TrimEnd()
            + "…";
    }

    private static string ConvertHtmlToPlainText(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return string.Empty;
        }

        var text =
            Regex.Replace(
                html,
                @"<script\b[^>]*>.*?</script>",
                " ",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline);

        text =
            Regex.Replace(
                text,
                @"<style\b[^>]*>.*?</style>",
                " ",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline);

        text =
            Regex.Replace(
                text,
                @"<br\s*/?>",
                "\n",
                RegexOptions.IgnoreCase);

        text =
            Regex.Replace(
                text,
                @"</p\s*>",
                "\n\n",
                RegexOptions.IgnoreCase);

        text =
            Regex.Replace(
                text,
                @"<[^>]+>",
                " ");

        text =
            WebUtility.HtmlDecode(
                text);

        return NormalizeBodyText(
            text);
    }

    private static string NormalizeBodyText(
        string text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return string.Empty;
        }

        var normalized =
            text.Replace(
                    "\r\n",
                    "\n")
                .Replace(
                    '\r',
                    '\n');

        normalized =
            Regex.Replace(
                normalized,
                @"[ \t]+",
                " ");

        normalized =
            Regex.Replace(
                normalized,
                @" *\n *",
                "\n");

        normalized =
            Regex.Replace(
                normalized,
                @"\n{3,}",
                "\n\n");

        return normalized.Trim();
    }

    private static string FormatDisplayTime(
        DateTimeOffset value)
    {
        var local =
            value.LocalDateTime;

        var today =
            DateTime.Today;

        if (local.Date ==
            today)
        {
            return local.ToString(
                "HH:mm");
        }

        if (local.Date ==
            today.AddDays(-1))
        {
            return "Gestern";
        }

        if (local.Year ==
            today.Year)
        {
            return local.ToString(
                "dd.MM.");
        }

        return local.ToString(
            "dd.MM.yyyy");
    }

    private static string FormatDisplayDateTime(
        DateTimeOffset value)
    {
        var local =
            value.LocalDateTime;

        var today =
            DateTime.Today;

        if (local.Date ==
            today)
        {
            return
                $"Heute, {local:HH:mm}";
        }

        if (local.Date ==
            today.AddDays(-1))
        {
            return
                $"Gestern, {local:HH:mm}";
        }

        return local.ToString(
            "dd.MM.yyyy, HH:mm");
    }

    private static async Task TryUpdateFolderStatusAsync(
        IMailFolder folder,
        CancellationToken cancellationToken)
    {
        try
        {
            await folder.StatusAsync(
                StatusItems.Count |
                StatusItems.Unread,
                cancellationToken);
        }
        catch (NotSupportedException)
        {
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

    private sealed record CidResolutionResult(
        string HtmlBody,
        IReadOnlySet<string> InlinePartSpecifiers);

    private sealed record MessageBodyContent(
        string PlainText,
        string? HtmlBody,
        IReadOnlySet<string> InlinePartSpecifiers);
}