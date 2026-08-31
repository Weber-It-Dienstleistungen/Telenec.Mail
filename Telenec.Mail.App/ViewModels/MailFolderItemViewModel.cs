namespace Telenec.Mail.App.ViewModels;

public sealed class MailFolderItemViewModel : BaseViewModel
{
    private string _headerSubtitle;
    private int _unreadCount;
    private int _messageCount;

    public MailFolderItemViewModel(
        string folderId,
        string displayName,
        string headerSubtitle,
        int unreadCount = 0,
        bool hasSeparatorAfter = false,
        int messageCount = 0)
    {
        FolderId =
            folderId;

        DisplayName =
            displayName;

        _headerSubtitle =
            headerSubtitle;

        _unreadCount =
            unreadCount;

        _messageCount =
            messageCount;

        HasSeparatorAfter =
            hasSeparatorAfter;
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

    public int MessageCount
    {
        get =>
            _messageCount;

        private set
        {
            if (_messageCount == value)
            {
                return;
            }

            _messageCount =
                value;

            OnPropertyChanged();
        }
    }

    public bool HasUnreadCount =>
        UnreadCount > 0;

    public bool HasSeparatorAfter { get; }

    public void UpdateState(
        string headerSubtitle,
        int unreadCount,
        int messageCount)
    {
        MessageCount =
            Math.Max(
                messageCount,
                0);

        UnreadCount =
            Math.Max(
                unreadCount,
                0);

        HeaderSubtitle =
            headerSubtitle;
    }

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