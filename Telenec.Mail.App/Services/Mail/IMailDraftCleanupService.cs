namespace Telenec.Mail.App.Services.Mail;

public interface IMailDraftCleanupService
{
    Task<bool> TryDeleteDraftAsync(
        string folderId,
        uint uniqueId,
        string expectedMessageId,
        CancellationToken cancellationToken = default);
}