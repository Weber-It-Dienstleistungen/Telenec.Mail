namespace Telenec.Mail.App.ViewModels;

public sealed class MailMessageItemViewModel
{
    public MailMessageItemViewModel(
        string sender,
        string subject,
        string preview,
        string displayTime,
        bool isUnread = false,
        bool emphasizeSender = false)
    {
        Sender = sender;
        Subject = subject;
        Preview = preview;
        DisplayTime = displayTime;
        IsUnread = isUnread;
        EmphasizeSender = emphasizeSender;
    }

    public string Sender { get; }

    public string Subject { get; }

    public string Preview { get; }

    public string DisplayTime { get; }

    public bool IsUnread { get; }

    public bool EmphasizeSender { get; }
}