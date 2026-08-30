namespace Telenec.Mail.App.Models;

public sealed record MailSendRequest(
    string RecipientAddress,
    string Subject,
    string Body);