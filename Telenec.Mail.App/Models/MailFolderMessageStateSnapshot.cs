namespace Telenec.Mail.App.Models;

public sealed record MailFolderMessageStateSnapshot(
    string FolderId,
    uint UidValidity,
    IReadOnlyList<MailMessageStateData> Messages);