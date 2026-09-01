using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Models;

public sealed record MailDraftEditData(
    string SourceFolderId,
    uint SourceUniqueId,
    string SourceMessageId,
    IReadOnlyList<string> ToAddresses,
    IReadOnlyList<string> CcAddresses,
    string Subject,
    string Body,
    string? ParentMessageId,
    IReadOnlyList<string> ParentReferences,
    IReadOnlyList<MailSendAttachmentData> Attachments);