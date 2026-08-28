using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public interface IMailDataSource
{
    IReadOnlyList<MailFolderData> GetFolders();

    IReadOnlyList<MailMessageData> GetMessages();
}