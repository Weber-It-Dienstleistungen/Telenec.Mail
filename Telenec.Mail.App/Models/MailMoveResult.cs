namespace Telenec.Mail.App.Models;

public sealed record MailMoveResult(
    string SourceFolderId,
    string TargetFolderId,
    IReadOnlyList<MailMoveUidMapping> UidMappings)
{
    public bool CanUndo =>
        UidMappings.Count > 0;

    public IReadOnlyList<uint> TargetUniqueIds =>
        UidMappings
            .Select(
                mapping =>
                    mapping.TargetUniqueId)
            .ToList();
}

public sealed record MailMoveUidMapping(
    uint SourceUniqueId,
    uint TargetUniqueId);