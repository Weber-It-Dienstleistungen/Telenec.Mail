namespace Telenec.Mail.App.Models;

public sealed record MailSendResult(
    bool WasSent,
    bool SentCopySaved,
    bool PreviousDraftRemoved = true)
{
    public bool HasWarning =>
        WasSent &&
        !SentCopySaved;

    public bool HasDraftCleanupWarning =>
        WasSent &&
        !PreviousDraftRemoved;
}