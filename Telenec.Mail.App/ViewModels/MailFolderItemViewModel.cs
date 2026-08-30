namespace Telenec.Mail.App.ViewModels;

public sealed class MailFolderItemViewModel : BaseViewModel
{
    private string _headerSubtitle;
    private int _unreadCount;

    public MailFolderItemViewModel(
        string folderId,
        string displayName,
        string headerSubtitle,
        int unreadCount = 0,
        bool hasSeparatorAfter = false,
        int messageCount = 0)
    {
        FolderId = folderId;
        DisplayName = displayName;

        _headerSubtitle =
            headerSubtitle;

        _unreadCount =
            unreadCount;

        HasSeparatorAfter =
            hasSeparatorAfter;

        MessageCount =
            messageCount;
    }

    public string FolderId { get; }

    public string DisplayName { get; }

    public string HeaderSubtitle
    {
        get =>
            _headerSubtitle;

        private set
        {
            if (_headerSubtitle == value)
            {
                return;
            }

            _headerSubtitle =
                value;

            OnPropertyChanged();
        }
    }

    public int UnreadCount
    {
        get =>
            _unreadCount;

        private set
        {
            if (_unreadCount == value)
            {
                return;
            }

            _unreadCount =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(HasUnreadCount));
        }
    }

    public int MessageCount { get; }

    public bool HasUnreadCount =>
        UnreadCount > 0;

    public bool HasSeparatorAfter { get; }

    public void DecrementUnreadCount()
    {
        if (UnreadCount <= 0)
        {
            return;
        }

        UnreadCount--;

        HeaderSubtitle =
            UnreadCount > 0
                ? $"{UnreadCount} ungelesene Nachrichten"
                : $"{MessageCount} Nachrichten";
    }

    public void IncrementUnreadCount()
    {
        if (MessageCount > 0 &&
            UnreadCount >= MessageCount)
        {
            return;
        }

        UnreadCount++;

        HeaderSubtitle =
            $"{UnreadCount} ungelesene Nachrichten";
    }
}