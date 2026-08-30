namespace Telenec.Mail.App.Services.Mail;

public enum MailAuthenticationStatus
{
    Success,
    InvalidCredentials,
    CertificateError,
    Timeout,
    ServerUnavailable,
    Failed
}