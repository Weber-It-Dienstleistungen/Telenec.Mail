using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public interface IMailMessageStateSource
{
    Task<MailFolderMessageStateSnapshot>
        GetMessageStatesAsync(
            string folderId,
            int maximumMessageCount = 20,
            CancellationToken cancellationToken = default);
}