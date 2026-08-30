namespace Telenec.Mail.App.ViewModels;

public sealed class MailFolderItemViewModel
{
    public MailFolderItemViewModel(
        string folderId,
        string displayName,
        string headerSubtitle,
        int unreadCount = 0,
        bool hasSeparatorAfter = false)
    {
        FolderId = folderId;
        DisplayName = displayName;
        HeaderSubtitle = headerSubtitle;
        UnreadCount = unreadCount;
        HasSeparatorAfter = hasSeparatorAfter;
    }

    public string FolderId { get; }

    public string DisplayName { get; }

    public string HeaderSubtitle { get; }

    public int UnreadCount { get; }

    public bool HasUnreadCount =>
        UnreadCount > 0;

    public bool HasSeparatorAfter { get; }
}