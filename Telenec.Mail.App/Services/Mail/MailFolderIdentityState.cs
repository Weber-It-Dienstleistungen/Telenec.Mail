using System.Collections.Concurrent;

namespace Telenec.Mail.App.Services.Mail;

internal static class MailFolderIdentityState
{
    private static readonly ConcurrentDictionary<
        string,
        uint>
        UidValidityByFolder =
            new(
                StringComparer.OrdinalIgnoreCase);

    public static void Set(
        Guid accountId,
        string folderId,
        uint uidValidity)
    {
        if (accountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(
                folderId) ||
            uidValidity == 0)
        {
            return;
        }

        UidValidityByFolder[
            CreateKey(
                accountId,
                folderId)] =
                    uidValidity;
    }

    public static bool TryGet(
        Guid accountId,
        string folderId,
        out uint uidValidity)
    {
        uidValidity =
            0;

        if (accountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(
                folderId))
        {
            return false;
        }

        return UidValidityByFolder
            .TryGetValue(
                CreateKey(
                    accountId,
                    folderId),
                out uidValidity) &&
            uidValidity != 0;
    }

    private static string CreateKey(
        Guid accountId,
        string folderId)
    {
        return
            $"{accountId:D}\0{folderId.Trim()}";
    }
}