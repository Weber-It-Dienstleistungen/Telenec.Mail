namespace Telenec.Mail.App.ViewModels;

public sealed class MailMessageItemViewModel
{
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
        string? htmlBody = null)
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
        IsUnread = isUnread;
        EmphasizeSender = emphasizeSender;
        HighlightTitle = highlightTitle;
        HighlightText = highlightText;
        HtmlBody = htmlBody;
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

    public bool IsUnread { get; }

    public bool EmphasizeSender { get; }

    public string? HighlightTitle { get; }

    public string? HighlightText { get; }

    public string? HtmlBody { get; }

    public bool HasHtmlBody =>
        !string.IsNullOrWhiteSpace(
            HtmlBody);

    public bool HasHighlight =>
        !string.IsNullOrWhiteSpace(HighlightTitle) &&
        !string.IsNullOrWhiteSpace(HighlightText);
}