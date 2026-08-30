using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public interface IMailAccountStore
{
    Task<MailAccount?> GetActiveAccountAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MailAccount account,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}