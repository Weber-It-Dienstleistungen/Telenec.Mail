namespace Telenec.Mail.App.ViewModels;

public sealed class MailFolderItemViewModel
{
    public MailFolderItemViewModel(
        string displayName,
        int unreadCount = 0,
        bool hasSeparatorAfter = false)
    {
        DisplayName = displayName;
        UnreadCount = unreadCount;
        HasSeparatorAfter = hasSeparatorAfter;
    }

    public string DisplayName { get; }

    public int UnreadCount { get; }

    public bool HasUnreadCount => UnreadCount > 0;

    public bool HasSeparatorAfter { get; }
}