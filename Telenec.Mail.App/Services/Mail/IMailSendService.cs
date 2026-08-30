using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public interface IMailSendService
{
    Task<MailSendResult> SendAsync(
        MailSendRequest request,
        CancellationToken cancellationToken = default);
}