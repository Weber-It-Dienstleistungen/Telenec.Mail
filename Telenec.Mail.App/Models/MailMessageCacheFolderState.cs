namespace Telenec.Mail.App.Models;

public sealed record MailMessageCacheFolderState(
    string FolderId,
    uint UidValidity,
    DateTimeOffset UpdatedAtUtc);