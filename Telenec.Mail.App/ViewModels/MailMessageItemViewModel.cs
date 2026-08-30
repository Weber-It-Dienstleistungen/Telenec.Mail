namespace Telenec.Mail.App.ViewModels;

public sealed class MailMessageItemViewModel : BaseViewModel
{
    private bool _isUnread;
    private bool _emphasizeSender;

    public MailMessageItemViewModel(
        string sender,
        string senderAddress,
        string recipientAddress,
        string subject,
        string preview,
        string displayTime,
        string displayDateTime,
        string senderInitial,
        string greeting,
        string body,
        string closing,
        string signature,
        bool isUnread = false,
        bool emphasizeSender = false,
        string? highlightTitle = null,
        string? highlightText = null,
        string? htmlBody = null,
        uint uniqueId = 0)
    {
        Sender = sender;
        SenderAddress = senderAddress;
        RecipientAddress = recipientAddress;
        Subject = subject;
        Preview = preview;
        DisplayTime = displayTime;
        DisplayDateTime = displayDateTime;
        SenderInitial = senderInitial;
        Greeting = greeting;
        Body = body;
        Closing = closing;
        Signature = signature;

        _isUnread =
            isUnread;

        _emphasizeSender =
            emphasizeSender;

        HighlightTitle = highlightTitle;
        HighlightText = highlightText;
        HtmlBody = htmlBody;
        UniqueId = uniqueId;
    }

    public string Sender { get; }

    public string SenderAddress { get; }

    public string RecipientAddress { get; }

    public string AddressLine =>
        $"{SenderAddress} → {RecipientAddress}";

    public string Subject { get; }

    public string Preview { get; }

    public string DisplayTime { get; }

    public string DisplayDateTime { get; }

    public string SenderInitial { get; }

    public string Greeting { get; }

    public string Body { get; }

    public string Closing { get; }

    public string Signature { get; }

    public bool IsUnread
    {
        get =>
            _isUnread;

        private set
        {
            if (_isUnread == value)
            {
                return;
            }

            _isUnread =
                value;

            OnPropertyChanged();
        }
    }

    public bool EmphasizeSender
    {
        get =>
            _emphasizeSender;

        private set
        {
            if (_emphasizeSender == value)
            {
                return;
            }

            _emphasizeSender =
                value;

            OnPropertyChanged();
        }
    }

    public string? HighlightTitle { get; }

    public string? HighlightText { get; }

    public string? HtmlBody { get; }

    public uint UniqueId { get; }

    public bool HasHtmlBody =>
        !string.IsNullOrWhiteSpace(
            HtmlBody);

    public bool HasHighlight =>
        !string.IsNullOrWhiteSpace(HighlightTitle) &&
        !string.IsNullOrWhiteSpace(HighlightText);

    public void MarkAsRead()
    {
        IsUnread =
            false;

        EmphasizeSender =
            false;
    }
}