namespace Telenec.Mail.App.Models;

public sealed record MailDraftEditData(
    string SourceFolderId,
    uint SourceUniqueId,
    string SourceMessageId,
    IReadOnlyList<string> ToAddresses,
    IReadOnlyList<string> CcAddresses,
    IReadOnlyList<string> BccAddresses,
    string Subject,
    string Body,
    string? ParentMessageId,
    IReadOnlyList<string> ParentReferences,
    IReadOnlyList<MailSendAttachmentData> Attachments,
    string? HtmlBody = null,
    MailImportanceLevel Importance = MailImportanceLevel.Normal,
    bool RequestReadReceipt = false);