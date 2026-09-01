namespace Telenec.Mail.App.Services.Mail;

public sealed class MailDraftEditException :
    Exception
{
    public MailDraftEditException(
        string message)
        : base(message)
    {
    }

    public MailDraftEditException(
        string message,
        Exception innerException)
        : base(
            message,
            innerException)
    {
    }
}