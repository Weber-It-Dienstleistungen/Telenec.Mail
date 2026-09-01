using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public interface IMailDraftEditService
{
    Task<MailDraftEditData> LoadDraftAsync(
        string folderId,
        uint uniqueId,
        string? expectedMessageId,
        CancellationToken cancellationToken = default);
}