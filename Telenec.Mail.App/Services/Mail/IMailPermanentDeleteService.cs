namespace Telenec.Mail.App.Services.Mail;

public interface IMailPermanentDeleteService
{
    Task DeletePermanentlyAsync(
        string folderId,
        uint expectedUidValidity,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default);

    Task<int> EmptyTrashAsync(
        string folderId,
        CancellationToken cancellationToken = default);
}