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

    Task MarkAsUnreadAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default);

    Task MoveToTrashAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default);

    Task MoveToTrashAsync(
        string folderId,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default);

    Task MoveMessagesAsync(
        string sourceFolderId,
        string targetFolderId,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default);
}