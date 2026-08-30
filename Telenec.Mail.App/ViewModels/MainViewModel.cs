using System.Collections.ObjectModel;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private readonly IMailDataSource _mailDataSource;

    private MailFolderItemViewModel? _selectedFolder;
    private MailMessageItemViewModel? _selectedMessage;

    private CancellationTokenSource?
        _folderLoadCancellationSource;

    private bool _isInitialized;

    public MainViewModel(
        IMailDataSource mailDataSource)
    {
        ArgumentNullException.ThrowIfNull(
            mailDataSource);

        _mailDataSource =
            mailDataSource;

        MailFolders =
            new ObservableCollection<
                MailFolderItemViewModel>();

        Messages =
            new ObservableCollection<
                MailMessageItemViewModel>();
    }

    public string ApplicationTitle =>
        "Telenec Mail";

    public ObservableCollection<
        MailFolderItemViewModel> MailFolders
    { get; }

    public ObservableCollection<
        MailMessageItemViewModel> Messages
    { get; }

    /*
     * Übergangsalias für die bestehende XAML.
     * Wird später sauber auf "Messages" umgestellt.
     */
    public ObservableCollection<
        MailMessageItemViewModel> DemoMessages =>
        Messages;

    public MailFolderItemViewModel? SelectedFolder
    {
        get => _selectedFolder;

        set
        {
            if (ReferenceEquals(
                    _selectedFolder,
                    value))
            {
                return;
            }

            _selectedFolder =
                value;

            OnPropertyChanged();

            if (_isInitialized &&
                value is not null)
            {
                _ =
                    LoadFolderMessagesAsync(
                        value);
            }
        }
    }

    public MailMessageItemViewModel? SelectedMessage
    {
        get => _selectedMessage;

        set
        {
            if (ReferenceEquals(
                    _selectedMessage,
                    value))
            {
                return;
            }

            _selectedMessage =
                value;

            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        var folders =
            await _mailDataSource
                .GetFoldersAsync(
                    cancellationToken);

        MailFolders.Clear();

        foreach (var folder in folders)
        {
            MailFolders.Add(
                new MailFolderItemViewModel(
                    folderId:
                        folder.FolderId,

                    displayName:
                        folder.DisplayName,

                    headerSubtitle:
                        folder.HeaderSubtitle,

                    unreadCount:
                        folder.UnreadCount,

                    hasSeparatorAfter:
                        folder.HasSeparatorAfter));
        }

        _selectedFolder =
            MailFolders.FirstOrDefault();

        OnPropertyChanged(
            nameof(SelectedFolder));

        _isInitialized =
            true;

        if (_selectedFolder is not null)
        {
            await LoadFolderMessagesAsync(
                _selectedFolder,
                cancellationToken);
        }
        else
        {
            Messages.Clear();

            SelectedMessage =
                null;
        }
    }

    private async Task LoadFolderMessagesAsync(
        MailFolderItemViewModel folder,
        CancellationToken cancellationToken = default)
    {
        _folderLoadCancellationSource?
            .Cancel();

        _folderLoadCancellationSource?
            .Dispose();

        _folderLoadCancellationSource =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        var token =
            _folderLoadCancellationSource.Token;

        try
        {
            var messages =
                await _mailDataSource
                    .GetMessagesAsync(
                        folder.FolderId,
                        maximumMessageCount: 20,
                        cancellationToken: token);

            token.ThrowIfCancellationRequested();

            Messages.Clear();

            foreach (var message in messages)
            {
                Messages.Add(
                    new MailMessageItemViewModel(
                        sender:
                            message.Sender,

                        senderAddress:
                            message.SenderAddress,

                        recipientAddress:
                            message.RecipientAddress,

                        subject:
                            message.Subject,

                        preview:
                            message.Preview,

                        displayTime:
                            message.DisplayTime,

                        displayDateTime:
                            message.DisplayDateTime,

                        senderInitial:
                            message.SenderInitial,

                        greeting:
                            message.Greeting,

                        body:
                            message.Body,

                        closing:
                            message.Closing,

                        signature:
                            message.Signature,

                        isUnread:
                            message.IsUnread,

                        emphasizeSender:
                            message.EmphasizeSender,

                        highlightTitle:
                            message.HighlightTitle,

                        highlightText:
                            message.HighlightText));
            }

            SelectedMessage =
                Messages.FirstOrDefault();
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // Normaler Zustand bei schnellem Ordnerwechsel.
        }
    }
}