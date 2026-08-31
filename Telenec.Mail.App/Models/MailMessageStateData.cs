namespace Telenec.Mail.App.Models;

public sealed record MailMessageStateData(
    uint UniqueId,
    bool IsUnread);