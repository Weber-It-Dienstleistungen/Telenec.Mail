namespace Telenec.Mail.App.Models;

public sealed record MailFolderData(
    string DisplayName,
    string HeaderSubtitle,
    int UnreadCount = 0,
    bool HasSeparatorAfter = false);