using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public interface IMailDataSource
{
    Task<IReadOnlyList<MailFolderData>> GetFoldersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailMessageData>> GetMessagesAsync(
        string folderId,
        int maximumMessageCount = 20,
        CancellationToken cancellationToken = default);

    Task MarkAsReadAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default);
}