using System.Collections.ObjectModel;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private MailFolderItemViewModel? _selectedFolder;
    private MailMessageItemViewModel? _selectedMessage;

    public MainViewModel(IMailDataSource mailDataSource)
    {
        ArgumentNullException.ThrowIfNull(mailDataSource);

        MailFolders = new ObservableCollection<MailFolderItemViewModel>(
            mailDataSource
                .GetFolders()
                .Select(folder => new MailFolderItemViewModel(
                    displayName: folder.DisplayName,
                    headerSubtitle: folder.HeaderSubtitle,
                    unreadCount: folder.UnreadCount,
                    hasSeparatorAfter: folder.HasSeparatorAfter)));

        DemoMessages = new ObservableCollection<MailMessageItemViewModel>(
            mailDataSource
                .GetMessages()
                .Select(message => new MailMessageItemViewModel(
                    sender: message.Sender,
                    senderAddress: message.SenderAddress,
                    recipientAddress: message.RecipientAddress,
                    subject: message.Subject,
                    preview: message.Preview,
                    displayTime: message.DisplayTime,
                    displayDateTime: message.DisplayDateTime,
                    senderInitial: message.SenderInitial,
                    greeting: message.Greeting,
                    body: message.Body,
                    closing: message.Closing,
                    signature: message.Signature,
                    isUnread: message.IsUnread,
                    emphasizeSender: message.EmphasizeSender,
                    highlightTitle: message.HighlightTitle,
                    highlightText: message.HighlightText)));

        _selectedFolder = MailFolders.FirstOrDefault();
        _selectedMessage = DemoMessages.FirstOrDefault();
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