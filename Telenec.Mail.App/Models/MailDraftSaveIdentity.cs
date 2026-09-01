namespace Telenec.Mail.App.Models;

public sealed record MailDraftSaveIdentity(
    string FolderId,
    uint UniqueId,
    string MessageId);