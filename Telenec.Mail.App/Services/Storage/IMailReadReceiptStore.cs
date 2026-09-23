using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public interface IMailReadReceiptStore
{
    Task SaveAsync(
        Guid accountId,
        MailReadReceiptData readReceipt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailReadReceiptData>>
        GetByOriginalMessageIdAsync(
            Guid accountId,
            string originalMessageId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<
        string,
        IReadOnlyList<MailReadReceiptData>>>
        GetByOriginalMessageIdsAsync(
            Guid accountId,
            IReadOnlyCollection<string> originalMessageIds,
            CancellationToken cancellationToken = default);
}