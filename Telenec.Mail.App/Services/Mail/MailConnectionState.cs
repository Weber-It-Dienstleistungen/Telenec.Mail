namespace Telenec.Mail.App.Services.Mail;

public enum MailConnectionState
{
    Connecting,
    Connected,
    Offline,
    AuthenticationRequired,
    SecurityError,
    Error
}