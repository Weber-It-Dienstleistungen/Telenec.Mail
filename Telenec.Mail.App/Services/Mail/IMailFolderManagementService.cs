namespace Telenec.Mail.App.Services.Mail;

public interface IMailFolderManagementService
{
    Task<string> CreateFolderAsync(
        string folderName,
        CancellationToken cancellationToken = default);

    Task<string> CreateSubfolderAsync(
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken = default);

    Task DeleteFolderAsync(
        string folderId,
        CancellationToken cancellationToken = default);
}