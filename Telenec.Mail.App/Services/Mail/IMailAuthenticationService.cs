namespace Telenec.Mail.App.Services.Mail;

public interface IMailAuthenticationService
{
    Task<MailAuthenticationResult> AuthenticateAsync(
        string emailAddress,
        string password,
        CancellationToken cancellationToken = default);
}