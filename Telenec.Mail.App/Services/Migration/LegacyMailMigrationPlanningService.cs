using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Migration;

public sealed class LegacyMailMigrationPlanningService
{
    private const string ProofOfWorkFolderName =
        "Telenec-Migrationstest";

    public LegacyMailMigrationPlan CreatePlan(
        LegacyMailboxInventoryResult sourceInventory,
        IReadOnlyList<MailFolderData> targetFolders)
    {
        ArgumentNullException.ThrowIfNull(
            sourceInventory);

        ArgumentNullException.ThrowIfNull(
            targetFolders);

        var sourceFolders =
            sourceInventory
                .Folders
                .Where(
                    folder =>
                        !string.Equals(
                            folder.FullName,
                            ProofOfWorkFolderName,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var excludedFolderCount =
            sourceInventory.Folders.Count -
            sourceFolders.Length;

        var items =
            sourceFolders
                .Select(
                    sourceFolder =>
                        CreatePlanItem(
                            sourceFolder,
                            targetFolders))
                .ToList();

        /*
         * Mehrere historische Systemordner können bewusst
         * auf denselben modernen Zielordner zeigen.
         *
         * Beispiele:
         *
         * Deleted Messages
         * INBOX.Trash
         * Trash
         *
         *        ↓
         *
         * Papierkorb
         */
        var targetGroups =
            items
                .Where(
                    item =>
                        item.CanMigrate)
                .GroupBy(
                    item =>
                        item.TargetKey,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.Count(),
                    StringComparer.OrdinalIgnoreCase);

        items =
            items
                .Select(
                    item =>
                    {
                        if (!item.CanMigrate ||
                            !targetGroups.TryGetValue(
                                item.TargetKey,
                                out var sourceCount) ||
                            sourceCount <= 1)
                        {
                            return item;
                        }

                        return item with
                        {
                            IsMergedTarget =
                                true,

                            ActionText =
                                item.ActionText +
                                " · mehrere Altordner werden zusammengeführt"
                        };
                    })
                .ToList();

        var totalMessageCount =
            sourceFolders.Sum(
                folder =>
                    (long)folder.MessageCount);

        var plannedMessageCount =
            items
                .Where(
                    item =>
                        item.CanMigrate)
                .Sum(
                    item =>
                        (long)item.MessageCount);

        var blockedMessageCount =
            Math.Max(
                totalMessageCount -
                plannedMessageCount,
                0);

        var newTargetFolderCount =
            items
                .Where(
                    item =>
                        item.RequiresTargetCreation &&
                        item.CanMigrate)
                .Select(
                    item =>
                        item.TargetKey)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        var mergeTargetCount =
            targetGroups
                .Count(
                    group =>
                        group.Value > 1);

        return new LegacyMailMigrationPlan(
            Folders:
                items,

            SourceFolderCount:
                sourceFolders.Length,

            TotalMessageCount:
                totalMessageCount,

            PlannedMessageCount:
                plannedMessageCount,

            BlockedMessageCount:
                blockedMessageCount,

            NewTargetFolderCount:
                newTargetFolderCount,

            MergeTargetCount:
                mergeTargetCount,

            ExcludedFolderCount:
                excludedFolderCount);
    }

    private static LegacyMailMigrationFolderPlanItem
        CreatePlanItem(
            LegacyMailboxFolderInventory sourceFolder,
            IReadOnlyList<MailFolderData> targetFolders)
    {
        var role =
            GetSourceFolderRole(
                sourceFolder);

        if (role !=
            LegacyMailFolderRole.Custom)
        {
            return CreateSystemFolderPlanItem(
                sourceFolder,
                role,
                targetFolders);
        }

        return CreateCustomFolderPlanItem(
            sourceFolder,
            targetFolders);
    }

    private static LegacyMailMigrationFolderPlanItem
        CreateSystemFolderPlanItem(
            LegacyMailboxFolderInventory sourceFolder,
            LegacyMailFolderRole role,
            IReadOnlyList<MailFolderData> targetFolders)
    {
        var targetFolder =
            FindTargetSystemFolder(
                role,
                targetFolders);

        var targetDisplayName =
            GetRoleDisplayName(
                role);

        if (targetFolder is null)
        {
            return new LegacyMailMigrationFolderPlanItem(
                SourceFolderName:
                    sourceFolder.FullName,

                TargetFolderId:
                    null,

                TargetDisplayName:
                    targetDisplayName,

                TargetKey:
                    $"system:{role}",

                MessageCount:
                    sourceFolder.MessageCount,

                IsSystemMapping:
                    true,

                RequiresTargetCreation:
                    false,

                CanMigrate:
                    false,

                IsMergedTarget:
                    false,

                ActionText:
                    "Ziel-Systemordner wurde nicht gefunden");
        }

        return new LegacyMailMigrationFolderPlanItem(
            SourceFolderName:
                sourceFolder.FullName,

            TargetFolderId:
                targetFolder.FolderId,

            TargetDisplayName:
                targetFolder.DisplayName,

            TargetKey:
                $"system:{role}",

            MessageCount:
                sourceFolder.MessageCount,

            IsSystemMapping:
                true,

            RequiresTargetCreation:
                false,

            CanMigrate:
                true,

            IsMergedTarget:
                false,

            ActionText:
                "vorhandener Systemordner");
    }

    private static LegacyMailMigrationFolderPlanItem
        CreateCustomFolderPlanItem(
            LegacyMailboxFolderInventory sourceFolder,
            IReadOnlyList<MailFolderData> targetFolders)
    {
        var existingTarget =
            FindExistingCustomTarget(
                sourceFolder,
                targetFolders);

        var logicalTargetName =
            CreateLogicalDisplayPath(
                sourceFolder);

        if (existingTarget is not null)
        {
            return new LegacyMailMigrationFolderPlanItem(
                SourceFolderName:
                    sourceFolder.FullName,

                TargetFolderId:
                    existingTarget.FolderId,

                TargetDisplayName:
                    existingTarget.DisplayName,

                TargetKey:
                    $"custom:{existingTarget.FolderId}",

                MessageCount:
                    sourceFolder.MessageCount,

                IsSystemMapping:
                    false,

                RequiresTargetCreation:
                    false,

                CanMigrate:
                    true,

                IsMergedTarget:
                    false,

                ActionText:
                    "Zielordner bereits vorhanden");
        }

        return new LegacyMailMigrationFolderPlanItem(
            SourceFolderName:
                sourceFolder.FullName,

            TargetFolderId:
                null,

            TargetDisplayName:
                logicalTargetName,

            TargetKey:
                $"custom-new:{NormalizeKey(logicalTargetName)}",

            MessageCount:
                sourceFolder.MessageCount,

            IsSystemMapping:
                false,

            RequiresTargetCreation:
                true,

            CanMigrate:
                true,

            IsMergedTarget:
                false,

            ActionText:
                "Zielordner wird neu angelegt");
    }

    private static MailFolderData?
        FindTargetSystemFolder(
            LegacyMailFolderRole role,
            IReadOnlyList<MailFolderData> targetFolders)
    {
        return role switch
        {
            LegacyMailFolderRole.Inbox =>
                FindTargetFolder(
                    targetFolders,
                    "Posteingang",
                    "INBOX"),

            LegacyMailFolderRole.Sent =>
                FindTargetFolder(
                    targetFolders,
                    "Gesendet"),

            LegacyMailFolderRole.Drafts =>
                FindTargetFolder(
                    targetFolders,
                    "Entwürfe"),

            LegacyMailFolderRole.Trash =>
                FindTargetFolder(
                    targetFolders,
                    "Papierkorb"),

            LegacyMailFolderRole.Junk =>
                FindTargetFolder(
                    targetFolders,
                    "Spam",
                    "Junk"),

            LegacyMailFolderRole.Archive =>
                FindTargetFolder(
                    targetFolders,
                    "Archiv"),

            _ =>
                null
        };
    }

    private static MailFolderData?
        FindTargetFolder(
            IReadOnlyList<MailFolderData> targetFolders,
            params string[] names)
    {
        foreach (var name in
                 names)
        {
            var byDisplayName =
                targetFolders
                    .FirstOrDefault(
                        folder =>
                            string.Equals(
                                folder.DisplayName,
                                name,
                                StringComparison.OrdinalIgnoreCase));

            if (byDisplayName is not null)
            {
                return byDisplayName;
            }

            var byFolderId =
                targetFolders
                    .FirstOrDefault(
                        folder =>
                            string.Equals(
                                folder.FolderId,
                                name,
                                StringComparison.OrdinalIgnoreCase));

            if (byFolderId is not null)
            {
                return byFolderId;
            }
        }

        return null;
    }

    private static MailFolderData?
        FindExistingCustomTarget(
            LegacyMailboxFolderInventory sourceFolder,
            IReadOnlyList<MailFolderData> targetFolders)
    {
        var byFullName =
            targetFolders
                .FirstOrDefault(
                    folder =>
                        string.Equals(
                            folder.FolderId,
                            sourceFolder.FullName,
                            StringComparison.OrdinalIgnoreCase));

        if (byFullName is not null)
        {
            return byFullName;
        }

        /*
         * Bei flachen Ordnern darf zusätzlich der sichtbare
         * Name verglichen werden.
         *
         * Bei Hierarchien wäre das zu unsicher, weil z. B.
         * zwei verschiedene Unterordner denselben Blattnamen
         * besitzen können.
         */
        var hasHierarchy =
            sourceFolder.DirectorySeparator !=
            '\0' &&
            sourceFolder.FullName.Contains(
                sourceFolder.DirectorySeparator);

        if (hasHierarchy)
        {
            return null;
        }

        return targetFolders
            .FirstOrDefault(
                folder =>
                    string.Equals(
                        folder.DisplayName,
                        sourceFolder.FullName,
                        StringComparison.OrdinalIgnoreCase));
    }

    private static LegacyMailFolderRole
        GetSourceFolderRole(
            LegacyMailboxFolderInventory folder)
    {
        var normalized =
            NormalizeFolderName(
                folder.FullName);

        return normalized switch
        {
            "inbox" =>
                LegacyMailFolderRole.Inbox,

            "posteingang" =>
                LegacyMailFolderRole.Inbox,

            "sent" =>
                LegacyMailFolderRole.Sent,

            "sent items" =>
                LegacyMailFolderRole.Sent,

            "sent messages" =>
                LegacyMailFolderRole.Sent,

            "gesendet" =>
                LegacyMailFolderRole.Sent,

            "inbox.sent" =>
                LegacyMailFolderRole.Sent,

            "inbox.sent items" =>
                LegacyMailFolderRole.Sent,

            "inbox.sent messages" =>
                LegacyMailFolderRole.Sent,

            "inbox.gesendet" =>
                LegacyMailFolderRole.Sent,

            "draft" =>
                LegacyMailFolderRole.Drafts,

            "drafts" =>
                LegacyMailFolderRole.Drafts,

            "entwurf" =>
                LegacyMailFolderRole.Drafts,

            "entwürfe" =>
                LegacyMailFolderRole.Drafts,

            "inbox.draft" =>
                LegacyMailFolderRole.Drafts,

            "inbox.drafts" =>
                LegacyMailFolderRole.Drafts,

            "inbox.entwurf" =>
                LegacyMailFolderRole.Drafts,

            "inbox.entwürfe" =>
                LegacyMailFolderRole.Drafts,

            "trash" =>
                LegacyMailFolderRole.Trash,

            "deleted items" =>
                LegacyMailFolderRole.Trash,

            "deleted messages" =>
                LegacyMailFolderRole.Trash,

            "papierkorb" =>
                LegacyMailFolderRole.Trash,

            "inbox.trash" =>
                LegacyMailFolderRole.Trash,

            "inbox.deleted items" =>
                LegacyMailFolderRole.Trash,

            "inbox.deleted messages" =>
                LegacyMailFolderRole.Trash,

            "inbox.papierkorb" =>
                LegacyMailFolderRole.Trash,

            "junk" =>
                LegacyMailFolderRole.Junk,

            "spam" =>
                LegacyMailFolderRole.Junk,

            "inbox.junk" =>
                LegacyMailFolderRole.Junk,

            "inbox.spam" =>
                LegacyMailFolderRole.Junk,

            "archive" =>
                LegacyMailFolderRole.Archive,

            "archives" =>
                LegacyMailFolderRole.Archive,

            "archiv" =>
                LegacyMailFolderRole.Archive,

            "inbox.archive" =>
                LegacyMailFolderRole.Archive,

            "inbox.archives" =>
                LegacyMailFolderRole.Archive,

            "inbox.archiv" =>
                LegacyMailFolderRole.Archive,

            _ =>
                LegacyMailFolderRole.Custom
        };
    }

    private static string
        GetRoleDisplayName(
            LegacyMailFolderRole role)
    {
        return role switch
        {
            LegacyMailFolderRole.Inbox =>
                "Posteingang",

            LegacyMailFolderRole.Sent =>
                "Gesendet",

            LegacyMailFolderRole.Drafts =>
                "Entwürfe",

            LegacyMailFolderRole.Trash =>
                "Papierkorb",

            LegacyMailFolderRole.Junk =>
                "Spam",

            LegacyMailFolderRole.Archive =>
                "Archiv",

            _ =>
                "Ordner"
        };
    }

    private static string
        CreateLogicalDisplayPath(
            LegacyMailboxFolderInventory folder)
    {
        if (folder.DirectorySeparator ==
            '\0')
        {
            return folder.FullName;
        }

        return folder.FullName.Replace(
            folder.DirectorySeparator.ToString(),
            " / ",
            StringComparison.Ordinal);
    }

    private static string
        NormalizeFolderName(
            string value)
    {
        return value
            .Trim()
            .Replace(
                '\\',
                '.')
            .Replace(
                '/',
                '.')
            .ToLowerInvariant();
    }

    private static string
        NormalizeKey(
            string value)
    {
        return value
            .Trim()
            .ToLowerInvariant();
    }
}

public enum LegacyMailFolderRole
{
    Custom = 0,
    Inbox = 1,
    Sent = 2,
    Drafts = 3,
    Trash = 4,
    Junk = 5,
    Archive = 6
}

public sealed record LegacyMailMigrationFolderPlanItem(
    string SourceFolderName,
    string? TargetFolderId,
    string TargetDisplayName,
    string TargetKey,
    int MessageCount,
    bool IsSystemMapping,
    bool RequiresTargetCreation,
    bool CanMigrate,
    bool IsMergedTarget,
    string ActionText);

public sealed record LegacyMailMigrationPlan(
    IReadOnlyList<LegacyMailMigrationFolderPlanItem> Folders,
    int SourceFolderCount,
    long TotalMessageCount,
    long PlannedMessageCount,
    long BlockedMessageCount,
    int NewTargetFolderCount,
    int MergeTargetCount,
    int ExcludedFolderCount)
{
    public bool IsReady =>
        BlockedMessageCount == 0 &&
        Folders.All(
            folder =>
                folder.CanMigrate);
}