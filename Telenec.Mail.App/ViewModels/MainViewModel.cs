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
                subject: "Willkommen bei Telenec Mail",
                preview: "Ihr neues Telenec-Mailkonto ist eingerichtet und bereit...",
                displayTime: "10:42",
                isUnread: true,
                emphasizeSender: true),

            new MailMessageItemViewModel(
                sender: "Stadtwerke Neustadt",
                subject: "Ihre Rechnung für August 2026",
                preview: "Guten Tag, Ihre aktuelle Rechnung steht für Sie bereit...",
                displayTime: "09:18",
                emphasizeSender: true),

            new MailMessageItemViewModel(
                sender: "Anna Müller",
                subject: "Termin am kommenden Dienstag",
                preview: "Hallo, ich wollte den vereinbarten Termin noch einmal bestätigen...",
                displayTime: "Gestern"),

            new MailMessageItemViewModel(
                sender: "Telenec Kundenservice",
                subject: "Ihre Anfrage wurde bearbeitet",
                preview: "Vielen Dank für Ihre Nachricht. Wir haben Ihre Anfrage geprüft...",
                displayTime: "Dienstag")
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