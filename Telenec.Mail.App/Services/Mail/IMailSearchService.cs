using System.IO;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public interface IMailSearchService
{
    Task<IReadOnlyList<MailSearchHitData>>
        SearchFolderAsync(
            string folderId,
            string searchText,
            int maximumResultCount = 200,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailSearchHitData>>
        SearchMailboxAsync(
            string searchText,
            int maximumResultCount = 200,
            CancellationToken cancellationToken = default);

    Task<MailMessageData?>
        LoadMessageAsync(
            MailSearchHitData searchHit,
            CancellationToken cancellationToken = default);

    Task DownloadAttachmentAsync(
        MailSearchHitData searchHit,
        string partSpecifier,
        Stream destination,
        CancellationToken cancellationToken = default);

    Task<bool> MarkAsReadAsync(
        MailSearchHitData searchHit,
        CancellationToken cancellationToken = default);

    Task<bool> MarkAsUnreadAsync(
        MailSearchHitData searchHit,
        CancellationToken cancellationToken = default);

    Task<MailMoveResult> MoveToTrashAsync(
        MailSearchHitData searchHit,
        CancellationToken cancellationToken = default);

    Task<MailMoveResult> MoveAsync(
        MailSearchHitData searchHit,
        string targetFolderId,
        CancellationToken cancellationToken = default);
}