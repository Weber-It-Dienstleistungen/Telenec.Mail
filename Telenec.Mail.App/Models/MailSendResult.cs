namespace Telenec.Mail.App.Models;

public sealed record MailSendResult(
    bool WasSent,
    bool SentCopySaved)
{
    public bool HasWarning =>
        WasSent &&
        !SentCopySaved;
}