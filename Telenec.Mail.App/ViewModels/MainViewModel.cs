using System.Collections.ObjectModel;

namespace Telenec.Mail.App.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private MailFolderItemViewModel? _selectedFolder;
    private MailMessageItemViewModel? _selectedMessage;

    public MainViewModel()
    {
        MailFolders =
        [
            new MailFolderItemViewModel(
                displayName: "Posteingang",
                unreadCount: 12),

            new MailFolderItemViewModel(
                displayName: "Gesendet"),

            new MailFolderItemViewModel(
                displayName: "Entwürfe",
                hasSeparatorAfter: true),

            new MailFolderItemViewModel(
                displayName: "Archiv"),

            new MailFolderItemViewModel(
                displayName: "Junk"),

            new MailFolderItemViewModel(
                displayName: "Papierkorb")
        ];

        DemoMessages =
        [
            new MailMessageItemViewModel(
                sender: "Telenec Technik",
                senderAddress: "support@telenec.de",
                recipientAddress: "max.mustermann@necnet.de",
                subject: "Willkommen bei Telenec Mail",
                preview: "Ihr neues Telenec-Mailkonto ist eingerichtet und bereit...",
                displayTime: "10:42",
                displayDateTime: "Heute, 10:42",
                senderInitial: "T",
                greeting: "Guten Tag,",
                body:
                    "Ihr Telenec-Mailkonto ist eingerichtet und bereit. " +
                    "Mit Telenec Mail erhalten Sie künftig eine einfache und " +
                    "übersichtliche Anwendung für Ihre E-Mails.\n\n" +
                    "Servereinstellungen und technische Details übernimmt " +
                    "die Anwendung automatisch. Sie benötigen lediglich " +
                    "Ihre E-Mail-Adresse und Ihr Passwort.",
                closing: "Viele Grüße",
                signature: "Ihre Telenec Technik",
                isUnread: true,
                emphasizeSender: true,
                highlightTitle: "Einfach. Sicher. Telenec.",
                highlightText: "Technische Komplexität bleibt im Hintergrund."),

            new MailMessageItemViewModel(
                sender: "Stadtwerke Neustadt",
                senderAddress: "service@stadtwerke-neustadt.de",
                recipientAddress: "max.mustermann@necnet.de",
                subject: "Ihre Rechnung für August 2026",
                preview: "Guten Tag, Ihre aktuelle Rechnung steht für Sie bereit...",
                displayTime: "09:18",
                displayDateTime: "Heute, 09:18",
                senderInitial: "S",
                greeting: "Guten Tag,",
                body:
                    "Ihre aktuelle Rechnung für August 2026 steht für Sie bereit.\n\n" +
                    "Bitte prüfen Sie die angegebenen Abrechnungsdaten. " +
                    "Bei Rückfragen können Sie sich jederzeit an unseren Kundenservice wenden.",
                closing: "Freundliche Grüße",
                signature: "Ihre Stadtwerke Neustadt",
                emphasizeSender: true),

            new MailMessageItemViewModel(
                sender: "Anna Müller",
                senderAddress: "anna.mueller@example.com",
                recipientAddress: "max.mustermann@necnet.de",
                subject: "Termin am kommenden Dienstag",
                preview: "Hallo, ich wollte den vereinbarten Termin noch einmal bestätigen...",
                displayTime: "Gestern",
                displayDateTime: "Gestern, 17:26",
                senderInitial: "A",
                greeting: "Hallo,",
                body:
                    "ich wollte den vereinbarten Termin am kommenden Dienstag " +
                    "noch einmal kurz bestätigen.\n\n" +
                    "Von meiner Seite bleibt es bei der besprochenen Uhrzeit. " +
                    "Falls sich bei dir noch etwas ändert, gib mir bitte kurz Bescheid.",
                closing: "Viele Grüße",
                signature: "Anna"),

            new MailMessageItemViewModel(
                sender: "Telenec Kundenservice",
                senderAddress: "kundenservice@telenec.de",
                recipientAddress: "max.mustermann@necnet.de",
                subject: "Ihre Anfrage wurde bearbeitet",
                preview: "Vielen Dank für Ihre Nachricht. Wir haben Ihre Anfrage geprüft...",
                displayTime: "Dienstag",
                displayDateTime: "Dienstag, 14:05",
                senderInitial: "T",
                greeting: "Guten Tag,",
                body:
                    "vielen Dank für Ihre Nachricht. Wir haben Ihre Anfrage geprüft " +
                    "und die Bearbeitung abgeschlossen.\n\n" +
                    "Sollten noch Fragen offen sein, antworten Sie einfach auf diese E-Mail. " +
                    "Unser Kundenservice hilft Ihnen gerne weiter.",
                closing: "Viele Grüße",
                signature: "Ihr Telenec Kundenservice")
        ];

        _selectedFolder = MailFolders[0];
        _selectedMessage = DemoMessages[0];
    }

    public string ApplicationTitle => "Telenec Mail";

    public ObservableCollection<MailFolderItemViewModel> MailFolders { get; }

    public ObservableCollection<MailMessageItemViewModel> DemoMessages { get; }

    public MailFolderItemViewModel? SelectedFolder
    {
        get => _selectedFolder;

        set
        {
            if (ReferenceEquals(_selectedFolder, value))
            {
                return;
            }

            _selectedFolder = value;
            OnPropertyChanged();
        }
    }

    public MailMessageItemViewModel? SelectedMessage
    {
        get => _selectedMessage;

        set
        {
            if (ReferenceEquals(_selectedMessage, value))
            {
                return;
            }

            _selectedMessage = value;
            OnPropertyChanged();
        }
    }
}