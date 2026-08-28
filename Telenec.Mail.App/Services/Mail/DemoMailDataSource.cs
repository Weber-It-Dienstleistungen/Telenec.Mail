using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Mail;

public sealed class DemoMailDataSource : IMailDataSource
{
    public IReadOnlyList<MailFolderData> GetFolders()
    {
        return
        [
            new MailFolderData(
                DisplayName: "Posteingang",
                HeaderSubtitle: "12 ungelesene Nachrichten",
                UnreadCount: 12),

            new MailFolderData(
                DisplayName: "Gesendet",
                HeaderSubtitle: "Gesendete Nachrichten"),

            new MailFolderData(
                DisplayName: "Entwürfe",
                HeaderSubtitle: "Gespeicherte Entwürfe",
                HasSeparatorAfter: true),

            new MailFolderData(
                DisplayName: "Archiv",
                HeaderSubtitle: "Archivierte Nachrichten"),

            new MailFolderData(
                DisplayName: "Junk",
                HeaderSubtitle: "Als Junk erkannte Nachrichten"),

            new MailFolderData(
                DisplayName: "Papierkorb",
                HeaderSubtitle: "Gelöschte Nachrichten")
        ];
    }

    public IReadOnlyList<MailMessageData> GetMessages()
    {
        return
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
                HighlightText: "Technische Komplexität bleibt im Hintergrund."),

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
                EmphasizeSender: true),

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
                Signature: "Anna"),

            new MailMessageData(
                Sender: "Telenec Kundenservice",
                SenderAddress: "kundenservice@telenec.de",
                RecipientAddress: "max.mustermann@necnet.de",
                Subject: "Ihre Anfrage wurde bearbeitet",
                Preview: "Vielen Dank für Ihre Nachricht. Wir haben Ihre Anfrage geprüft...",
                DisplayTime: "Dienstag",
                DisplayDateTime: "Dienstag, 14:05",
                SenderInitial: "T",
                Greeting: "Guten Tag,",
                Body:
                    "vielen Dank für Ihre Nachricht. Wir haben Ihre Anfrage geprüft " +
                    "und die Bearbeitung abgeschlossen.\n\n" +
                    "Sollten noch Fragen offen sein, antworten Sie einfach auf diese E-Mail. " +
                    "Unser Kundenservice hilft Ihnen gerne weiter.",
                Closing: "Viele Grüße",
                Signature: "Ihr Telenec Kundenservice")
        ];
    }
}