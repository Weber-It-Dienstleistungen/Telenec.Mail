namespace Telenec.Mail.App.Services.Mail;

public sealed class MailSendAttachmentException :
    Exception
{
    public MailSendAttachmentException(
        string message,
        Exception? innerException = null)
        : base(
            message,
            innerException)
    {
    }
}