namespace Telenec.Mail.App.Models;

public sealed record MailDraftSaveResult(
    bool WasSaved,
    bool PreviousDraftRemoved)
{
    public bool HasWarning =>
        WasSaved &&
        !PreviousDraftRemoved;
}