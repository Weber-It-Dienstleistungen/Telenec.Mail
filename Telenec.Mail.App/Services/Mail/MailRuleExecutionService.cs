using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed record MailRuleExecutionItem(
    uint UniqueId,
    string? MessageId,
    Guid RuleId,
    string RuleName,
    string TargetFolderId);

public sealed record MailRuleExecutionPlan(
    string SourceFolderId,
    uint UidValidity,
    int ScannedMessageCount,
    IReadOnlyList<MailRuleExecutionItem> Items,
    IReadOnlyList<string> UnavailableRuleNames);

public sealed record MailRuleExecutionResult(
    int MovedMessageCount,
    int SkippedMessageCount,
    int FailedMessageCount)
{
    public IReadOnlyList<uint> MovedUniqueIds
    { get; init; } =
        Array.Empty<uint>();
}

public sealed class MailRuleExecutionService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly ILogger<MailRuleExecutionService>
        _logger;

    public MailRuleExecutionService(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        ILogger<MailRuleExecutionService> logger)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        ArgumentNullException.ThrowIfNull(
            logger);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _logger =
            logger;
    }

    /*
     * Manuelle, rückwirkende Regelausführung.
     *
     * Der komplette aktuelle Posteingang wird analysiert.
     */
    public Task<MailRuleExecutionPlan>
        AnalyzeInboxAsync(
            Guid accountId,
            IReadOnlyList<MailRuleDefinition> rules,
            CancellationToken cancellationToken = default)
    {
        return AnalyzeInboxCoreAsync(
            accountId,
            rules,
            expectedUidValidity:
                null,
            restrictedUniqueIds:
                null,
            cancellationToken);
    }

    /*
     * Automatische Regelausführung.
     *
     * Hier werden ausdrücklich nur die UIDs geprüft,
     * die der Eingangspoller gerade als neu erkannt hat.
     *
     * Dadurch werden beim Programmstart keine alten
     * Nachrichten automatisch nachträglich verarbeitet.
     */
    public Task<MailRuleExecutionPlan>
        AnalyzeInboxMessagesAsync(
            Guid accountId,
            IReadOnlyList<MailRuleDefinition> rules,
            uint expectedUidValidity,
            IReadOnlyCollection<uint> uniqueIds,
            CancellationToken cancellationToken = default)
    {
        if (expectedUidValidity == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedUidValidity),
                "Die UIDVALIDITY darf nicht 0 sein.");
        }

        ArgumentNullException.ThrowIfNull(
            uniqueIds);

        return AnalyzeInboxCoreAsync(
            accountId,
            rules,
            expectedUidValidity,
            uniqueIds,
            cancellationToken);
    }

    private async Task<MailRuleExecutionPlan>
        AnalyzeInboxCoreAsync(
            Guid accountId,
            IReadOnlyList<MailRuleDefinition> rules,
            uint? expectedUidValidity,
            IReadOnlyCollection<uint>? restrictedUniqueIds,
            CancellationToken cancellationToken)
    {
        ValidateAccountId(
            accountId);

        ArgumentNullException.ThrowIfNull(
            rules);

        var activeRules =
            rules
                .Where(
                    rule =>
                        rule.IsEnabled)
                .Where(
                    rule =>
                        rule.MatchType ==
                        MailRuleMatchType.Contains)
                .Where(
                    rule =>
                        rule.ActionType ==
                        MailRuleActionType.MoveToFolder)
                .OrderBy(
                    rule =>
                        rule.SortOrder)
                .ThenBy(
                    rule =>
                        rule.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var restrictedIds =
            restrictedUniqueIds?
                .Where(
                    uniqueId =>
                        uniqueId > 0)
                .Distinct()
                .ToArray();

        using var client =
            await CreateAuthenticatedClientAsync(
                accountId,
                cancellationToken);

        try
        {
            var inbox =
                client.Inbox;

            await inbox.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            var uidValidity =
                inbox.UidValidity;

            if (uidValidity == 0)
            {
                throw new InvalidOperationException(
                    "Der Mailserver hat für den Posteingang keine gültige UIDVALIDITY geliefert.");
            }

            /*
             * Besonders wichtig für die automatische
             * Regelausführung:
             *
             * Die UIDs stammen aus einem unmittelbar
             * vorherigen Polling-Durchlauf.
             *
             * Falls sich UIDVALIDITY zwischen Erkennung und
             * Regelanalyse geändert hat, dürfen diese UIDs
             * keinesfalls mehr benutzt werden.
             */
            if (expectedUidValidity.HasValue &&
                uidValidity !=
                    expectedUidValidity.Value)
            {
                throw new InvalidOperationException(
                    "Der Posteingang wurde seit der Erkennung neuer Nachrichten serverseitig verändert.");
            }

            var executableRules =
                new List<MailRuleDefinition>();

            var unavailableRuleNames =
                new List<string>();

            foreach (var rule in activeRules)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(
                        rule.TargetFolderId))
                {
                    unavailableRuleNames.Add(
                        rule.Name);

                    continue;
                }

                try
                {
                    var targetFolder =
                        await client.GetFolderAsync(
                            rule.TargetFolderId,
                            cancellationToken);

                    if (targetFolder.Attributes.HasFlag(
                            FolderAttributes.NoSelect) ||
                        string.Equals(
                            targetFolder.FullName,
                            inbox.FullName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        unavailableRuleNames.Add(
                            rule.Name);

                        continue;
                    }

                    executableRules.Add(
                        rule);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    unavailableRuleNames.Add(
                        rule.Name);
                }
            }

            if (inbox.Count == 0 ||
                executableRules.Count == 0 ||
                restrictedIds is
                { Length: 0 })
            {
                return new MailRuleExecutionPlan(
                    SourceFolderId:
                        inbox.FullName,

                    UidValidity:
                        uidValidity,

                    ScannedMessageCount:
                        0,

                    Items:
                        Array.Empty<
                            MailRuleExecutionItem>(),

                    UnavailableRuleNames:
                        unavailableRuleNames
                            .Distinct(
                                StringComparer.CurrentCultureIgnoreCase)
                            .ToArray());
            }

            IList<IMessageSummary>
                summaries;

            /*
             * Manuelle Ausführung:
             * kompletter Posteingang.
             *
             * Automatische Ausführung:
             * ausschließlich die gerade neu erkannten UIDs.
             *
             * In beiden Fällen werden nur UID + Envelope
             * geladen. Keine Bodies, keine Anhänge.
             */
            if (restrictedIds is null)
            {
                summaries =
                    await inbox.FetchAsync(
                        0,
                        -1,
                        MessageSummaryItems.UniqueId |
                        MessageSummaryItems.Envelope,
                        cancellationToken);
            }
            else
            {
                var requestedUniqueIds =
                    restrictedIds
                        .Select(
                            uniqueId =>
                                new UniqueId(
                                    uniqueId))
                        .ToList();

                summaries =
                    await inbox.FetchAsync(
                        requestedUniqueIds,
                        MessageSummaryItems.UniqueId |
                        MessageSummaryItems.Envelope,
                        cancellationToken);
            }

            var validSummaries =
                summaries
                    .Where(
                        summary =>
                            summary.UniqueId.IsValid)
                    .OrderBy(
                        summary =>
                            summary.Index)
                    .ToList();

            var executionItems =
                new List<MailRuleExecutionItem>();

            foreach (var summary in validSummaries)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                /*
                 * Eine Nachricht wird höchstens von einer
                 * Regel verarbeitet.
                 *
                 * Die erste passende aktive Regel gewinnt.
                 */
                var matchingRule =
                    executableRules
                        .FirstOrDefault(
                            rule =>
                                RuleMatches(
                                    rule,
                                    summary));

                if (matchingRule is null)
                {
                    continue;
                }

                executionItems.Add(
                    new MailRuleExecutionItem(
                        UniqueId:
                            summary.UniqueId.Id,

                        MessageId:
                            NormalizeMessageId(
                                summary.Envelope?
                                    .MessageId),

                        RuleId:
                            matchingRule.RuleId,

                        RuleName:
                            matchingRule.Name,

                        TargetFolderId:
                            matchingRule.TargetFolderId));
            }

            _logger.LogInformation(
                "Mail rule analysis completed. ScannedMessageCount={ScannedMessageCount}, MatchedMessageCount={MatchedMessageCount}, ActiveRuleCount={ActiveRuleCount}, UnavailableRuleCount={UnavailableRuleCount}.",
                validSummaries.Count,
                executionItems.Count,
                activeRules.Count,
                unavailableRuleNames.Count);

            return new MailRuleExecutionPlan(
                SourceFolderId:
                    inbox.FullName,

                UidValidity:
                    uidValidity,

                ScannedMessageCount:
                    validSummaries.Count,

                Items:
                    executionItems,

                UnavailableRuleNames:
                    unavailableRuleNames
                        .Distinct(
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToArray());
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task<MailRuleExecutionResult>
        ApplyAsync(
            Guid accountId,
            MailRuleExecutionPlan plan,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        ArgumentNullException.ThrowIfNull(
            plan);

        if (string.IsNullOrWhiteSpace(
                plan.SourceFolderId))
        {
            throw new ArgumentException(
                "Der Regelplan enthält keinen Quellordner.",
                nameof(plan));
        }

        if (plan.UidValidity == 0)
        {
            throw new ArgumentException(
                "Der Regelplan enthält keine gültige UIDVALIDITY.",
                nameof(plan));
        }

        if (plan.Items.Count == 0)
        {
            return new MailRuleExecutionResult(
                MovedMessageCount:
                    0,

                SkippedMessageCount:
                    0,

                FailedMessageCount:
                    0);
        }

        _logger.LogInformation(
            "Mail rule execution started. PlannedMessageCount={PlannedMessageCount}.",
            plan.Items.Count);

        using var client =
            await CreateAuthenticatedClientAsync(
                accountId,
                cancellationToken);

        try
        {
            var sourceFolder =
                await client.GetFolderAsync(
                    plan.SourceFolderId,
                    cancellationToken);

            if (sourceFolder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new InvalidOperationException(
                    "Der Posteingang kann nicht für die Regelausführung geöffnet werden.");
            }

            await sourceFolder.OpenAsync(
                FolderAccess.ReadWrite,
                cancellationToken);

            /*
             * Schutz gegen inzwischen ungültige UIDs.
             */
            if (sourceFolder.UidValidity !=
                plan.UidValidity)
            {
                throw new InvalidOperationException(
                    "Der Posteingang wurde seit der Regelanalyse serverseitig verändert. Bitte analysieren Sie die Regeln erneut.");
            }

            var requestedUniqueIds =
                plan.Items
                    .Select(
                        item =>
                            item.UniqueId)
                    .Where(
                        uniqueId =>
                            uniqueId > 0)
                    .Distinct()
                    .Select(
                        uniqueId =>
                            new UniqueId(
                                uniqueId))
                    .ToList();

            if (requestedUniqueIds.Count == 0)
            {
                return new MailRuleExecutionResult(
                    MovedMessageCount:
                        0,

                    SkippedMessageCount:
                        plan.Items.Count,

                    FailedMessageCount:
                        0);
            }

            /*
             * Unmittelbar vor der Mutation wird die
             * Nachrichtenidentität noch einmal bestätigt.
             */
            var currentSummaries =
                await sourceFolder.FetchAsync(
                    requestedUniqueIds,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope,
                    cancellationToken);

            var summariesByUniqueId =
                currentSummaries
                    .Where(
                        summary =>
                            summary.UniqueId.IsValid)
                    .GroupBy(
                        summary =>
                            summary.UniqueId.Id)
                    .ToDictionary(
                        group =>
                            group.Key,

                        group =>
                            group.First());

            var validItems =
                new List<MailRuleExecutionItem>();

            var skippedMessageCount =
                0;

            foreach (var item in plan.Items)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                if (!summariesByUniqueId.TryGetValue(
                        item.UniqueId,
                        out var summary))
                {
                    skippedMessageCount++;

                    continue;
                }

                if (!MessageIdsMatch(
                        item.MessageId,
                        summary.Envelope?
                            .MessageId))
                {
                    skippedMessageCount++;

                    continue;
                }

                validItems.Add(
                    item);
            }

            var movedMessageCount =
                0;

            var failedMessageCount =
                0;

            var movedUniqueIds =
                new HashSet<uint>();

            /*
             * Nachrichten mit demselben Zielordner werden
             * gemeinsam verschoben.
             */
            var targetGroups =
                validItems
                    .GroupBy(
                        item =>
                            item.TargetFolderId,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var targetGroup in targetGroups)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var groupItems =
                    targetGroup
                        .ToList();

                try
                {
                    var targetFolder =
                        await client.GetFolderAsync(
                            targetGroup.Key,
                            cancellationToken);

                    if (targetFolder.Attributes.HasFlag(
                            FolderAttributes.NoSelect) ||
                        string.Equals(
                            targetFolder.FullName,
                            sourceFolder.FullName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failedMessageCount +=
                            groupItems.Count;

                        continue;
                    }

                    var groupUniqueIds =
                        groupItems
                            .Select(
                                item =>
                                    new UniqueId(
                                        item.UniqueId))
                            .ToList();

                    _logger.LogInformation(
                        "Mail rule message move started. MessageCount={MessageCount}.",
                        groupUniqueIds.Count);

                    await sourceFolder.MoveToAsync(
                        groupUniqueIds,
                        targetFolder,
                        cancellationToken);

                    /*
                     * Der Erfolg wird am Quellordner
                     * bestätigt.
                     */
                    var remainingSummaries =
                        await sourceFolder.FetchAsync(
                            groupUniqueIds,
                            MessageSummaryItems.UniqueId,
                            cancellationToken);

                    var remainingUniqueIds =
                        remainingSummaries
                            .Where(
                                summary =>
                                    summary.UniqueId.IsValid)
                            .Select(
                                summary =>
                                    summary.UniqueId.Id)
                            .ToHashSet();

                    var movedItems =
                        groupItems
                            .Where(
                                item =>
                                    !remainingUniqueIds.Contains(
                                        item.UniqueId))
                            .ToList();

                    foreach (var movedItem in movedItems)
                    {
                        movedUniqueIds.Add(
                            movedItem.UniqueId);
                    }

                    var movedInGroup =
                        movedItems.Count;

                    var failedInGroup =
                        groupItems.Count -
                        movedInGroup;

                    movedMessageCount +=
                        movedInGroup;

                    failedMessageCount +=
                        failedInGroup;

                    _logger.LogInformation(
                        "Mail rule message move completed. RequestedMessageCount={RequestedMessageCount}, ConfirmedMovedMessageCount={ConfirmedMovedMessageCount}, FailedMessageCount={FailedMessageCount}.",
                        groupItems.Count,
                        movedInGroup,
                        failedInGroup);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failedMessageCount +=
                        groupItems.Count;

                    var exceptionType =
                        exception.GetType().FullName
                        ?? exception.GetType().Name;

                    _logger.LogWarning(
                        "Mail rule message move failed. MessageCount={MessageCount}, ExceptionType={ExceptionType}.",
                        groupItems.Count,
                        exceptionType);
                }
            }

            _logger.LogInformation(
                "Mail rule execution completed. MovedMessageCount={MovedMessageCount}, SkippedMessageCount={SkippedMessageCount}, FailedMessageCount={FailedMessageCount}.",
                movedMessageCount,
                skippedMessageCount,
                failedMessageCount);

            return new MailRuleExecutionResult(
                MovedMessageCount:
                    movedMessageCount,

                SkippedMessageCount:
                    skippedMessageCount,

                FailedMessageCount:
                    failedMessageCount)
            {
                MovedUniqueIds =
                    movedUniqueIds
                        .OrderBy(
                            uniqueId =>
                                uniqueId)
                        .ToArray()
            };
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static bool RuleMatches(
        MailRuleDefinition rule,
        IMessageSummary summary)
    {
        if (string.IsNullOrWhiteSpace(
                rule.ConditionValue))
        {
            return false;
        }

        var searchValue =
            rule.ConditionValue.Trim();

        return rule.ConditionField switch
        {
            MailRuleConditionField.Sender =>
                AddressListContains(
                    summary.Envelope?
                        .From,
                    searchValue),

            MailRuleConditionField.Subject =>
                TextContains(
                    summary.Envelope?
                        .Subject,
                    searchValue),

            MailRuleConditionField.Recipient =>
                AddressListContains(
                    summary.Envelope?
                        .To,
                    searchValue)
                ||
                AddressListContains(
                    summary.Envelope?
                        .Cc,
                    searchValue),

            _ =>
                false
        };
    }

    private static bool AddressListContains(
        InternetAddressList? addresses,
        string searchValue)
    {
        if (addresses is null)
        {
            return false;
        }

        foreach (var mailbox in
                 addresses.Mailboxes)
        {
            if (TextContains(
                    mailbox.Name,
                    searchValue) ||
                TextContains(
                    mailbox.Address,
                    searchValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TextContains(
        string? text,
        string searchValue)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return false;
        }

        return text.IndexOf(
                   searchValue,
                   StringComparison.OrdinalIgnoreCase)
               >= 0;
    }

    private static bool MessageIdsMatch(
        string? expectedMessageId,
        string? actualMessageId)
    {
        var expected =
            NormalizeMessageId(
                expectedMessageId);

        /*
         * Nicht jede E-Mail besitzt zwingend eine Message-ID.
         *
         * In diesem Fall reichen UID + bestätigte UIDVALIDITY
         * als IMAP-Identität.
         */
        if (expected is null)
        {
            return true;
        }

        var actual =
            NormalizeMessageId(
                actualMessageId);

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

    private async Task<ImapClient>
        CreateAuthenticatedClientAsync(
            Guid accountId,
            CancellationToken cancellationToken)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null ||
            account.AccountId !=
                accountId)
        {
            throw new InvalidOperationException(
                "Das aktive Mailkonto hat sich geändert.");
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

    private static void ValidateAccountId(
        Guid accountId)
    {
        if (accountId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(accountId));
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