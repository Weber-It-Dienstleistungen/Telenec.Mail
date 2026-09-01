namespace Telenec.Mail.App.Models;

public sealed record MailSendRequest(
    string RecipientAddress,
    string Subject,
    string Body,
    string? CcAddress = null,
    string? BccAddress = null,
    string? ParentMessageId = null,
    IReadOnlyList<string>? ParentReferences = null,
    IReadOnlyList<MailSendAttachmentData>? Attachments = null);