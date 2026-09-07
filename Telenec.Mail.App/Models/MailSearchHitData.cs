namespace Telenec.Mail.App.Models;

public sealed record MailSearchHitData(
    string FolderId,
    uint UidValidity,
    uint UniqueId,
    string? MessageId,
    string Sender,
    string SenderAddress,
    string RecipientAddress,
    string Subject,
    DateTimeOffset Date,
    bool IsUnread);