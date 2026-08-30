using MailKit.Security;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
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
    private bool _isLoading;
    private bool _hasLoadError;
    private bool _isEmptyFolder;

    private string _loadingMessage =
        "Postfach wird geladen …";

    private string _loadErrorMessage =
        string.Empty;

    private string _connectionStatusText =
        "Verbindung wird hergestellt …";

    private MailConnectionState _connectionState =
        MailConnectionState.Connecting;

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

            var selectedFolder =
                _selectedFolder;

            if (_isInitialized &&
                selectedFolder is not null &&
                value is not null &&
                value.IsUnread)
            {
                _ =
                    MarkMessageAsReadAsync(
                        selectedFolder,
                        value);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;

        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading =
                value;

            OnPropertyChanged();
        }
    }

    public string LoadingMessage
    {
        get => _loadingMessage;

        private set
        {
            if (_loadingMessage == value)
            {
                return;
            }

            _loadingMessage =
                value;

            OnPropertyChanged();
        }
    }

    public bool HasLoadError
    {
        get => _hasLoadError;

        private set
        {
            if (_hasLoadError == value)
            {
                return;
            }

            _hasLoadError =
                value;

            OnPropertyChanged();
        }
    }

    public string LoadErrorMessage
    {
        get => _loadErrorMessage;

        private set
        {
            if (_loadErrorMessage == value)
            {
                return;
            }

            _loadErrorMessage =
                value;

            OnPropertyChanged();
        }
    }

    public bool IsEmptyFolder
    {
        get => _isEmptyFolder;

        private set
        {
            if (_isEmptyFolder == value)
            {
                return;
            }

            _isEmptyFolder =
                value;

            OnPropertyChanged();
        }
    }

    public MailConnectionState ConnectionState
    {
        get => _connectionState;

        private set
        {
            if (_connectionState == value)
            {
                return;
            }

            _connectionState =
                value;

            OnPropertyChanged();
        }
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;

        private set
        {
            if (_connectionStatusText == value)
            {
                return;
            }

            _connectionStatusText =
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

        await InitializeCoreAsync(
            preferredFolderId: null,
            cancellationToken);
    }

    public async Task ReloadAsync(
        CancellationToken cancellationToken = default)
    {
        var preferredFolderId =
            SelectedFolder?.FolderId;

        CancelCurrentFolderLoad();

        _isInitialized =
            false;

        await InitializeCoreAsync(
            preferredFolderId,
            cancellationToken);
    }

    public async Task<bool> MarkMessageAsUnreadAsync(
        MailMessageItemViewModel message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        var folder =
            _selectedFolder;

        if (folder is null ||
            message.IsUnread ||
            message.UniqueId == 0 ||
            !Messages.Contains(
                message))
        {
            return false;
        }

        await _mailDataSource
            .MarkAsUnreadAsync(
                folder.FolderId,
                message.UniqueId,
                cancellationToken);

        if (message.IsUnread)
        {
            return true;
        }

        message.MarkAsUnread();

        folder.IncrementUnreadCount();

        return true;
    }

    public Task<bool> DeleteMessageAsync(
        MailMessageItemViewModel message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        return DeleteMessagesAsync(
            new[] { message },
            cancellationToken);
    }

    public async Task<bool> DeleteMessagesAsync(
        IReadOnlyList<MailMessageItemViewModel> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            messages);

        var folder =
            _selectedFolder;

        if (folder is null ||
            IsLoading ||
            messages.Count == 0)
        {
            return false;
        }

        var messagesToDelete =
            messages
                .Where(
                    message =>
                        message is not null &&
                        message.UniqueId > 0 &&
                        Messages.Contains(
                            message))
                .GroupBy(
                    message =>
                        message.UniqueId)
                .Select(
                    group =>
                        group.First())
                .ToList();

        if (messagesToDelete.Count == 0)
        {
            return false;
        }

        var uniqueIds =
            messagesToDelete
                .Select(
                    message =>
                        message.UniqueId)
                .ToList();

        await _mailDataSource
            .MoveToTrashAsync(
                folder.FolderId,
                uniqueIds,
                cancellationToken);

        await ReloadAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> MoveMessagesAsync(
        IReadOnlyList<MailMessageItemViewModel> messages,
        MailFolderItemViewModel targetFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            messages);

        ArgumentNullException.ThrowIfNull(
            targetFolder);

        var sourceFolder =
            _selectedFolder;

        if (sourceFolder is null ||
            IsLoading ||
            messages.Count == 0 ||
            !MailFolders.Contains(
                targetFolder))
        {
            return false;
        }

        if (string.Equals(
                sourceFolder.FolderId,
                targetFolder.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var messagesToMove =
            messages
                .Where(
                    message =>
                        message is not null &&
                        message.UniqueId > 0 &&
                        Messages.Contains(
                            message))
                .GroupBy(
                    message =>
                        message.UniqueId)
                .Select(
                    group =>
                        group.First())
                .ToList();

        if (messagesToMove.Count == 0)
        {
            return false;
        }

        var uniqueIds =
            messagesToMove
                .Select(
                    message =>
                        message.UniqueId)
                .ToList();

        /*
         * Der Server wird zuerst geändert.
         * Vor erfolgreichem IMAP-MOVE wird lokal nichts entfernt.
         */
        await _mailDataSource
            .MoveMessagesAsync(
                sourceFolder.FolderId,
                targetFolder.FolderId,
                uniqueIds,
                cancellationToken);

        /*
         * Danach einmal vollständige Synchronisation.
         * Der Quellordner bleibt ausgewählt.
         */
        await ReloadAsync(
            cancellationToken);

        return true;
    }

    private async Task InitializeCoreAsync(
        string? preferredFolderId,
        CancellationToken cancellationToken)
    {
        BeginLoading(
            "Postfach wird geladen …",
            "Verbindung wird hergestellt …");

        MailFolders.Clear();
        Messages.Clear();

        _selectedFolder =
            null;

        OnPropertyChanged(
            nameof(SelectedFolder));

        SelectedMessage =
            null;

        try
        {
            var folders =
                await _mailDataSource
                    .GetFoldersAsync(
                        cancellationToken);

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
                            folder.HasSeparatorAfter,

                        messageCount:
                            folder.MessageCount));
            }

            _selectedFolder =
                !string.IsNullOrWhiteSpace(
                    preferredFolderId)
                    ? MailFolders.FirstOrDefault(
                        folder =>
                            string.Equals(
                                folder.FolderId,
                                preferredFolderId,
                                StringComparison.OrdinalIgnoreCase))
                    : null;

            _selectedFolder ??=
                MailFolders.FirstOrDefault();

            OnPropertyChanged(
                nameof(SelectedFolder));

            _isInitialized =
                true;

            if (_selectedFolder is null)
            {
                SetConnected();

                IsLoading =
                    false;

                IsEmptyFolder =
                    true;

                return;
            }

            await LoadFolderMessagesAsync(
                _selectedFolder,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            IsLoading =
                false;
        }
        catch (Exception ex)
        {
            _isInitialized =
                false;

            IsLoading =
                false;

            SetErrorState(ex);
        }
    }

    private async Task LoadFolderMessagesAsync(
        MailFolderItemViewModel folder,
        CancellationToken cancellationToken = default)
    {
        var previousSource =
            _folderLoadCancellationSource;

        previousSource?.Cancel();

        var loadSource =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        _folderLoadCancellationSource =
            loadSource;

        previousSource?.Dispose();

        var token =
            loadSource.Token;

        BeginLoading(
            $"E-Mails aus „{folder.DisplayName}“ werden geladen …",
            "Synchronisieren …");

        Messages.Clear();

        SelectedMessage =
            null;

        try
        {
            var messages =
                await _mailDataSource
                    .GetMessagesAsync(
                        folder.FolderId,
                        maximumMessageCount: 20,
                        cancellationToken: token);

            token.ThrowIfCancellationRequested();

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
                            message.HighlightText,

                        htmlBody:
                            message.HtmlBody,

                        uniqueId:
                            message.UniqueId));
            }

            SelectedMessage =
                Messages.FirstOrDefault();

            IsEmptyFolder =
                Messages.Count == 0;

            SetConnected();
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(
                    _folderLoadCancellationSource,
                    loadSource))
            {
                SetErrorState(ex);
            }
        }
        finally
        {
            var isCurrentLoad =
                ReferenceEquals(
                    _folderLoadCancellationSource,
                    loadSource);

            if (isCurrentLoad)
            {
                _folderLoadCancellationSource =
                    null;

                IsLoading =
                    false;
            }

            loadSource.Dispose();
        }
    }

    private async Task MarkMessageAsReadAsync(
        MailFolderItemViewModel folder,
        MailMessageItemViewModel message)
    {
        if (!message.IsUnread ||
            message.UniqueId == 0)
        {
            return;
        }

        try
        {
            await _mailDataSource
                .MarkAsReadAsync(
                    folder.FolderId,
                    message.UniqueId);

            if (!message.IsUnread)
            {
                return;
            }

            message.MarkAsRead();

            folder.DecrementUnreadCount();
        }
        catch
        {
        }
    }

    private void BeginLoading(
        string loadingMessage,
        string connectionStatus)
    {
        HasLoadError =
            false;

        LoadErrorMessage =
            string.Empty;

        IsEmptyFolder =
            false;

        LoadingMessage =
            loadingMessage;

        ConnectionState =
            MailConnectionState.Connecting;

        ConnectionStatusText =
            connectionStatus;

        IsLoading =
            true;
    }

    private void SetConnected()
    {
        HasLoadError =
            false;

        LoadErrorMessage =
            string.Empty;

        ConnectionState =
            MailConnectionState.Connected;

        ConnectionStatusText =
            "Verbunden";
    }

    private void SetErrorState(
        Exception exception)
    {
        HasLoadError =
            true;

        IsEmptyFolder =
            false;

        switch (exception)
        {
            case MailKit.Security.AuthenticationException:
                ConnectionState =
                    MailConnectionState.AuthenticationRequired;

                ConnectionStatusText =
                    "Anmeldung erforderlich";

                LoadErrorMessage =
                    "Die gespeicherten Zugangsdaten wurden vom Mailserver nicht akzeptiert. " +
                    "Bitte melden Sie das Konto ab und anschließend erneut an.";
                break;

            case SslHandshakeException:
                ConnectionState =
                    MailConnectionState.SecurityError;

                ConnectionStatusText =
                    "Sicherheitsfehler";

                LoadErrorMessage =
                    "Die sichere Verbindung zum Mailserver konnte nicht geprüft werden. " +
                    "Aus Sicherheitsgründen wurde die Verbindung abgebrochen.";
                break;

            case SocketException:
            case IOException:
                ConnectionState =
                    MailConnectionState.Offline;

                ConnectionStatusText =
                    "Offline";

                LoadErrorMessage =
                    "Der Mailserver ist momentan nicht erreichbar. " +
                    "Bitte prüfen Sie Ihre Internetverbindung.";
                break;

            default:
                ConnectionState =
                    MailConnectionState.Error;

                ConnectionStatusText =
                    "Verbindungsfehler";

                LoadErrorMessage =
                    "Die E-Mail-Daten konnten momentan nicht geladen werden. " +
                    "Bitte versuchen Sie es erneut.";
                break;
        }
    }

    private void CancelCurrentFolderLoad()
    {
        var source =
            _folderLoadCancellationSource;

        _folderLoadCancellationSource =
            null;

        if (source is null)
        {
            return;
        }

        source.Cancel();
        source.Dispose();
    }
}