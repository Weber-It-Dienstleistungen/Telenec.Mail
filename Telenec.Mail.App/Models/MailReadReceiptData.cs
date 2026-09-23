namespace Telenec.Mail.App.Models;

public sealed record MailReadReceiptData(
    string? OriginalMessageId,
    string Sender,
    string SenderAddress,
    DateTimeOffset? ReceiptDate,
    string Disposition);