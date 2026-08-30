using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public sealed class DemoMailDataSource : IMailDataSource
{
    public Task<IReadOnlyList<MailFolderData>> GetFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MailFolderData> folders =
        [
            new MailFolderData(
                FolderId: "INBOX",
                DisplayName: "Posteingang",
                HeaderSubtitle: "12 ungelesene Nachrichten",
                UnreadCount: 12,
                MessageCount: 20),

            new MailFolderData(
                FolderId: "Sent",
                DisplayName: "Gesendet",
                HeaderSubtitle: "Gesendete Nachrichten"),

            new MailFolderData(
                FolderId: "Drafts",
                DisplayName: "Entwürfe",
                HeaderSubtitle: "Gespeicherte Entwürfe",
                HasSeparatorAfter: true),

            new MailFolderData(
                FolderId: "Archive",
                DisplayName: "Archiv",
                HeaderSubtitle: "Archivierte Nachrichten"),

            new MailFolderData(
                FolderId: "Junk",
                DisplayName: "Junk",
                HeaderSubtitle: "Als Junk erkannte Nachrichten"),

            new MailFolderData(
                FolderId: "Trash",
                DisplayName: "Papierkorb",
                HeaderSubtitle: "Gelöschte Nachrichten")
        ];

        return Task.FromResult(folders);
    }

    public Task<IReadOnlyList<MailMessageData>> GetMessagesAsync(
        string folderId,
        int maximumMessageCount = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MailMessageData> messages =
        [
            new MailMessageData(
                Sender: "Telenec Technik",
                SenderAddress: "support@telenec.de",
                RecipientAddress: "max.mustermann@necnet.de",
                Subject: "Willkommen bei Telenec Mail",
                Preview: "Ihr neues Telenec-Mailkonto ist eingerichtet und bereit...",
                DisplayTime: "10:42",
                DisplayDateTime: "Heute, 10:42",
                SenderInitial: "T",
                Greeting: "Guten Tag,",
                Body:
                    "Ihr Telenec-Mailkonto ist eingerichtet und bereit. " +
                    "Mit Telenec Mail erhalten Sie künftig eine einfache und " +
                    "übersichtliche Anwendung für Ihre E-Mails.\n\n" +
                    "Servereinstellungen und technische Details übernimmt " +
                    "die Anwendung automatisch. Sie benötigen lediglich " +
                    "Ihre E-Mail-Adresse und Ihr Passwort.",
                Closing: "Viele Grüße",
                Signature: "Ihre Telenec Technik",
                IsUnread: true,
                EmphasizeSender: true,
                HighlightTitle: "Einfach. Sicher. Telenec.",
                HighlightText: "Technische Komplexität bleibt im Hintergrund.",
                UniqueId: 1),

            new MailMessageData(
                Sender: "Stadtwerke Neustadt",
                SenderAddress: "service@stadtwerke-neustadt.de",
                RecipientAddress: "max.mustermann@necnet.de",
                Subject: "Ihre Rechnung für August 2026",
                Preview: "Guten Tag, Ihre aktuelle Rechnung steht für Sie bereit...",
                DisplayTime: "09:18",
                DisplayDateTime: "Heute, 09:18",
                SenderInitial: "S",
                Greeting: "Guten Tag,",
                Body:
                    "Ihre aktuelle Rechnung für August 2026 steht für Sie bereit.\n\n" +
                    "Bitte prüfen Sie die angegebenen Abrechnungsdaten. " +
                    "Bei Rückfragen können Sie sich jederzeit an unseren Kundenservice wenden.",
                Closing: "Freundliche Grüße",
                Signature: "Ihre Stadtwerke Neustadt",
                EmphasizeSender: true,
                UniqueId: 2),

            new MailMessageData(
                Sender: "Anna Müller",
                SenderAddress: "anna.mueller@example.com",
                RecipientAddress: "max.mustermann@necnet.de",
                Subject: "Termin am kommenden Dienstag",
                Preview: "Hallo, ich wollte den vereinbarten Termin noch einmal bestätigen...",
                DisplayTime: "Gestern",
                DisplayDateTime: "Gestern, 17:26",
                SenderInitial: "A",
                Greeting: "Hallo,",
                Body:
                    "ich wollte den vereinbarten Termin am kommenden Dienstag " +
                    "noch einmal kurz bestätigen.\n\n" +
                    "Von meiner Seite bleibt es bei der besprochenen Uhrzeit. " +
                    "Falls sich bei dir noch etwas ändert, gib mir bitte kurz Bescheid.",
                Closing: "Viele Grüße",
                Signature: "Anna",
                UniqueId: 3)
        ];

        return Task.FromResult(messages);
    }

    public Task MarkAsReadAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }

    public Task MarkAsUnreadAsync(
        string folderId,
        uint uniqueId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}