using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public interface IMailMessageCacheStore
{
    Task SaveMessagesAsync(
        Guid accountId,
        string folderId,
        uint uidValidity,
        IReadOnlyCollection<MailMessageData> messages,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailMessageData>>
        GetMessagePageAsync(
            Guid accountId,
            string folderId,
            int skipMessageCount,
            int maximumMessageCount,
            CancellationToken cancellationToken = default);

    Task<MailMessageCacheFolderState?>
        GetFolderStateAsync(
            Guid accountId,
            string folderId,
            CancellationToken cancellationToken = default);

    Task ClearFolderAsync(
        Guid accountId,
        string folderId,
        CancellationToken cancellationToken = default);
}