using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public interface IMailFolderCacheStore
{
    Task SaveFoldersAsync(
        Guid accountId,
        IReadOnlyList<MailFolderData> folders,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailFolderData>>
        GetFoldersAsync(
            Guid accountId,
            CancellationToken cancellationToken = default);
}