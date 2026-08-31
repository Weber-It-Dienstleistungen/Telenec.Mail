using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow : Window
{
    private const string MailDragDataFormat =
        "Telenec.Mail.MessageSelection";

    private readonly MainViewModel _viewModel;
    private readonly IMailAccountStore _mailAccountStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IServiceProvider _serviceProvider;

    private bool _isLoggingOut;
    private bool _isLoaded;
    private bool _isMessageDragInProgress;

    private Task? _webViewInitializationTask;
    private int _renderVersion;

    private bool _allowExternalImagesForCurrentMessage;

    private Point _messageDragStartPoint;

    private MailMessageItemViewModel?
        _messageDragCandidate;

    private IReadOnlyList<MailMessageItemViewModel>
        _messageDragSelectionSnapshot =
            Array.Empty<MailMessageItemViewModel>();

    public MainWindow(
        MainViewModel viewModel,
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        IServiceProvider serviceProvider)
    {
        InitializeComponent();

        _viewModel =
            viewModel;

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _serviceProvider =
            serviceProvider;

        DataContext =
            _viewModel;

        _viewModel.PropertyChanged +=
            MainViewModel_OnPropertyChanged;

        Loaded +=
            MainWindow_OnLoaded;

        Closed +=
            MainWindow_OnClosed;

        PreviewKeyDown +=
            MainWindow_OnPreviewKeyDown;
    }

    private async void MainWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded =
            true;

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            AccountEmailText.Text =
                account?.EmailAddress
                ?? "Telenec-Konto";
        }
        catch
        {
            AccountEmailText.Text =
                "Telenec-Konto";
        }

        await _viewModel
            .InitializeAsync();

        await RenderSelectedMessageAsync();
    }

    private void MainWindow_OnClosed(
        object? sender,
        EventArgs e)
    {
        _renderVersion++;

        _viewModel.PropertyChanged -=
            MainViewModel_OnPropertyChanged;

        PreviewKeyDown -=
            MainWindow_OnPreviewKeyDown;

        try
        {
            HtmlMailView.Dispose();
        }
        catch
        {
        }
    }

    private void MessageListBox_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _messageDragStartPoint =
            e.GetPosition(
                MessageListBox);

        _messageDragCandidate =
            GetMessageFromElement(
                e.OriginalSource as DependencyObject);

        _messageDragSelectionSnapshot =
            Array.Empty<MailMessageItemViewModel>();

        if (_messageDragCandidate is not null &&
            Keyboard.Modifiers == ModifierKeys.None &&
            MessageListBox.SelectedItems.Contains(
                _messageDragCandidate))
        {
            _messageDragSelectionSnapshot =
                GetSelectedMessages();
        }
    }

    private void MessageListBox_OnPreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_isMessageDragInProgress ||
            e.LeftButton != MouseButtonState.Pressed ||
            _messageDragCandidate is null ||
            _viewModel.IsLoading)
        {
            return;
        }

        var currentPosition =
            e.GetPosition(
                MessageListBox);

        var horizontalDistance =
            Math.Abs(
                currentPosition.X -
                _messageDragStartPoint.X);

        var verticalDistance =
            Math.Abs(
                currentPosition.Y -
                _messageDragStartPoint.Y);

        if (horizontalDistance <
                SystemParameters.MinimumHorizontalDragDistance &&
            verticalDistance <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        IReadOnlyList<MailMessageItemViewModel>
            messagesToDrag;

        if (_messageDragSelectionSnapshot.Count > 0 &&
            _messageDragSelectionSnapshot.Contains(
                _messageDragCandidate))
        {
            messagesToDrag =
                _messageDragSelectionSnapshot;
        }
        else
        {
            var currentSelection =
                GetSelectedMessages();

            messagesToDrag =
                currentSelection.Contains(
                    _messageDragCandidate)
                    ? currentSelection
                    : new[] { _messageDragCandidate };
        }

        if (messagesToDrag.Count == 0)
        {
            return;
        }

        var dataObject =
            new DataObject();

        dataObject.SetData(
            MailDragDataFormat,
            messagesToDrag.ToList());

        _isMessageDragInProgress =
            true;

        try
        {
            DragDrop.DoDragDrop(
                MessageListBox,
                dataObject,
                DragDropEffects.Move);
        }
        finally
        {
            _isMessageDragInProgress =
                false;

            _messageDragCandidate =
                null;

            _messageDragSelectionSnapshot =
                Array.Empty<MailMessageItemViewModel>();
        }
    }

    private void FolderListBox_OnDragOver(
        object sender,
        DragEventArgs e)
    {
        if (_viewModel.IsLoading ||
            !TryGetDraggedMessages(
                e.Data,
                out var messages) ||
            messages.Count == 0)
        {
            e.Effects =
                DragDropEffects.None;

            e.Handled =
                true;

            return;
        }

        var targetFolder =
            GetFolderFromElement(
                e.OriginalSource as DependencyObject);

        var sourceFolder =
            _viewModel.SelectedFolder;

        if (targetFolder is null ||
            sourceFolder is null ||
            string.Equals(
                targetFolder.FolderId,
                sourceFolder.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            e.Effects =
                DragDropEffects.None;
        }
        else
        {
            e.Effects =
                DragDropEffects.Move;
        }

        e.Handled =
            true;
    }

    private async void FolderListBox_OnDrop(
        object sender,
        DragEventArgs e)
    {
        e.Handled =
            true;

        if (_viewModel.IsLoading ||
            !TryGetDraggedMessages(
                e.Data,
                out var messages) ||
            messages.Count == 0)
        {
            return;
        }

        var targetFolder =
            GetFolderFromElement(
                e.OriginalSource as DependencyObject);

        var sourceFolder =
            _viewModel.SelectedFolder;

        if (targetFolder is null ||
            sourceFolder is null ||
            string.Equals(
                targetFolder.FolderId,
                sourceFolder.FolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _viewModel
                .MoveMessagesAsync(
                    messages,
                    targetFolder);

            e.Effects =
                DragDropEffects.Move;
        }
        catch
        {
            var messageText =
                messages.Count == 1
                    ? "Die Nachricht konnte nicht verschoben werden."
                    : "Die Nachrichten konnten nicht verschoben werden.";

            MessageBox.Show(
                messageText +
                "\n\nBitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private MailMessageItemViewModel?
        GetMessageFromElement(
            DependencyObject? element)
    {
        if (element is null)
        {
            return null;
        }

        var container =
            ItemsControl.ContainerFromElement(
                MessageListBox,
                element)
            as ListBoxItem;

        return container?
            .DataContext
            as MailMessageItemViewModel;
    }

    private MailFolderItemViewModel?
        GetFolderFromElement(
            DependencyObject? element)
    {
        if (element is null)
        {
            return null;
        }

        var container =
            ItemsControl.ContainerFromElement(
                FolderListBox,
                element)
            as ListBoxItem;

        return container?
            .DataContext
            as MailFolderItemViewModel;
    }

    private static bool TryGetDraggedMessages(
        IDataObject data,
        out IReadOnlyList<MailMessageItemViewModel> messages)
    {
        messages =
            Array.Empty<MailMessageItemViewModel>();

        if (!data.GetDataPresent(
                MailDragDataFormat))
        {
            return false;
        }

        if (data.GetData(
                MailDragDataFormat)
            is not IEnumerable<MailMessageItemViewModel>
                draggedMessages)
        {
            return false;
        }

        var snapshot =
            draggedMessages
                .Where(
                    message =>
                        message is not null)
                .ToList();

        if (snapshot.Count == 0)
        {
            return false;
        }

        messages =
            snapshot;

        return true;
    }

    private async void MainWindow_OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        /*
         * Strg + Shift + R muss vor Strg + R geprüft werden.
         *
         * Ansonsten würde HasFlag(Control) auch beim
         * Reply-All-Shortcut bereits den normalen Reply
         * auslösen.
         */
        if (e.Key == Key.R &&
            Keyboard.Modifiers ==
            (ModifierKeys.Control |
             ModifierKeys.Shift))
        {
            if (e.IsRepeat)
            {
                e.Handled =
                    true;

                return;
            }

            var message =
                _viewModel.SelectedMessage;

            if (_viewModel.IsLoading ||
                message is null)
            {
                return;
            }

            e.Handled =
                true;

            await ReplyAllToMessageFromUiAsync(
                message);

            return;
        }

        if (e.Key == Key.R &&
            Keyboard.Modifiers ==
            ModifierKeys.Control)
        {
            if (e.IsRepeat)
            {
                e.Handled =
                    true;

                return;
            }

            var message =
                _viewModel.SelectedMessage;

            if (_viewModel.IsLoading ||
                message is null)
            {
                return;
            }

            e.Handled =
                true;

            await ReplyToMessageFromUiAsync(
                message);

            return;
        }

        if (e.Key == Key.Z &&
            Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control))
        {
            if (e.IsRepeat)
            {
                e.Handled =
                    true;

                return;
            }

            if (!_viewModel.CanUndoLastMove)
            {
                return;
            }

            e.Handled =
                true;

            await UndoLastMoveFromUiAsync();

            return;
        }

        if (e.Key != Key.Delete)
        {
            return;
        }

        if (e.IsRepeat)
        {
            e.Handled =
                true;

            return;
        }

        if (_viewModel.IsLoading)
        {
            return;
        }

        var messages =
            GetSelectedMessages();

        if (messages.Count == 0)
        {
            return;
        }

        e.Handled =
            true;

        if (_viewModel.IsTrashFolderSelected)
        {
            MessageBox.Show(
                messages.Count == 1
                    ? "Die Nachricht befindet sich bereits im Papierkorb.\n\n" +
                      "Dauerhaftes Löschen ist derzeit nicht verfügbar."
                    : "Die Nachrichten befinden sich bereits im Papierkorb.\n\n" +
                      "Dauerhaftes Löschen ist derzeit nicht verfügbar.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        await DeleteMessagesFromUiAsync(
            messages);
    }

    private async Task UndoLastMoveFromUiAsync()
    {
        try
        {
            await _viewModel
                .UndoLastMoveAsync();
        }
        catch
        {
            MessageBox.Show(
                "Die letzte Aktion konnte nicht rückgängig gemacht werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MainViewModel_OnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName !=
            nameof(MainViewModel.SelectedMessage))
        {
            return;
        }

        _allowExternalImagesForCurrentMessage =
            false;

        _ =
            RenderSelectedMessageAsync();
    }

    private async Task RenderSelectedMessageAsync()
    {
        var renderVersion =
            ++_renderVersion;

        var message =
            _viewModel.SelectedMessage;

        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        if (message is null)
        {
            ShowPlainTextView();
            return;
        }

        if (!message.HasHtmlBody)
        {
            ShowPlainTextView();
            return;
        }

        if (ContainsExternalImages(
                message.HtmlBody!) &&
            !_allowExternalImagesForCurrentMessage)
        {
            ExternalImagesNotice.Visibility =
                Visibility.Visible;
        }

        PlainTextMailView.Visibility =
            Visibility.Collapsed;

        HtmlMailView.Visibility =
            Visibility.Visible;

        try
        {
            await EnsureWebViewReadyAsync();

            if (!IsCurrentMessage(
                    message,
                    renderVersion))
            {
                return;
            }

            var html =
                PrepareHtmlForMailView(
                    message.HtmlBody!);

            HtmlMailView
                .CoreWebView2
                .NavigateToString(
                    html);
        }
        catch
        {
            ShowPlainTextView();
        }
    }

    private async Task EnsureWebViewReadyAsync()
    {
        _webViewInitializationTask ??=
            InitializeWebViewAsync();

        await _webViewInitializationTask;
    }

    private async Task InitializeWebViewAsync()
    {
        await HtmlMailView
            .EnsureCoreWebView2Async();

        var coreWebView =
            HtmlMailView.CoreWebView2;

        coreWebView.Settings.IsScriptEnabled =
            false;

        coreWebView.Settings.IsWebMessageEnabled =
            false;

        coreWebView.Settings.AreHostObjectsAllowed =
            false;

        coreWebView.Settings.AreDevToolsEnabled =
            false;

        coreWebView.Settings.AreDefaultContextMenusEnabled =
            false;

        coreWebView.NewWindowRequested +=
            CoreWebView2_OnNewWindowRequested;

        coreWebView.NavigationStarting +=
            CoreWebView2_OnNavigationStarting;

        coreWebView.AddWebResourceRequestedFilter(
            "*",
            CoreWebView2WebResourceContext.Image);

        coreWebView.WebResourceRequested +=
            CoreWebView2_OnWebResourceRequested;
    }

    private void CoreWebView2_OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsExternalWebUri(
                e.Uri))
        {
            return;
        }

        e.Cancel =
            true;

        OpenExternalWebUri(
            e.Uri);
    }

    private void CoreWebView2_OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled =
            true;

        if (IsExternalWebUri(
                e.Uri))
        {
            OpenExternalWebUri(
                e.Uri);
        }
    }

    private static bool IsExternalWebUri(
        string? uri)
    {
        if (string.IsNullOrWhiteSpace(
                uri))
        {
            return false;
        }

        return
            uri.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenExternalWebUri(
        string uri)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        uri,

                    UseShellExecute =
                        true
                });
        }
        catch
        {
            MessageBox.Show(
                "Der Link konnte nicht im Standardbrowser geöffnet werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static string PrepareHtmlForMailView(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return html;
        }

        return Regex.Replace(
            html,
            @"<a\b[^>]*>",
            match =>
            {
                var tag =
                    match.Value;

                var hrefMatch =
                    Regex.Match(
                        tag,
                        @"\bhref\s*=\s*[""'](?<href>[^""']*)[""']",
                        RegexOptions.IgnoreCase);

                if (!hrefMatch.Success)
                {
                    return tag;
                }

                var href =
                    hrefMatch
                        .Groups["href"]
                        .Value
                        .Trim();

                if (!href.StartsWith(
                        "#",
                        StringComparison.Ordinal))
                {
                    return tag;
                }

                return Regex.Replace(
                    tag,
                    @"\s+target\s*=\s*[""'][^""']*[""']",
                    string.Empty,
                    RegexOptions.IgnoreCase);
            },
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline);
    }

    private void CoreWebView2_OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_allowExternalImagesForCurrentMessage)
        {
            return;
        }

        var uri =
            e.Request.Uri;

        if (string.IsNullOrWhiteSpace(
                uri))
        {
            return;
        }

        if (uri.StartsWith(
                "data:",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isExternalHttpImage =
            uri.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase);

        if (!isExternalHttpImage)
        {
            return;
        }

        e.Response =
            HtmlMailView
                .CoreWebView2
                .Environment
                .CreateWebResourceResponse(
                    new MemoryStream(
                        Array.Empty<byte>()),
                    403,
                    "Blocked",
                    "Content-Type: image/png\r\n" +
                    "Cache-Control: no-store");
    }

    private async void LoadExternalImagesButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var message =
            _viewModel.SelectedMessage;

        if (message is null ||
            !message.HasHtmlBody)
        {
            return;
        }

        _allowExternalImagesForCurrentMessage =
            true;

        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        var html =
            PrepareHtmlForMailView(
                message.HtmlBody!);

        HtmlMailView
            .CoreWebView2
            .NavigateToString(
                html);

        await Task.CompletedTask;
    }

    private async void SaveAttachmentButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not MailAttachmentData attachment)
        {
            return;
        }

        var message =
            _viewModel.SelectedMessage;

        if (message is null)
        {
            return;
        }

        var saveDialog =
            new SaveFileDialog
            {
                Title =
                    "Anhang speichern unter",

                FileName =
                    attachment.FileName,

                Filter =
                    "Alle Dateien (*.*)|*.*",

                AddExtension =
                    false,

                OverwritePrompt =
                    true,

                CheckPathExists =
                    true
            };

        var dialogResult =
            saveDialog.ShowDialog(
                this);

        if (dialogResult != true)
        {
            return;
        }

        var targetPath =
            saveDialog.FileName;

        var targetDirectory =
            Path.GetDirectoryName(
                targetPath);

        if (string.IsNullOrWhiteSpace(
                targetDirectory))
        {
            MessageBox.Show(
                "Der ausgewählte Speicherort ist ungültig.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        var temporaryPath =
            Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(targetPath)}." +
                $"{Guid.NewGuid():N}.telenec-download");

        try
        {
            await using (
                var destination =
                    new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        options:
                            FileOptions.Asynchronous |
                            FileOptions.SequentialScan))
            {
                var downloaded =
                    await _viewModel
                        .DownloadAttachmentAsync(
                            message,
                            attachment,
                            destination);

                if (!downloaded)
                {
                    MessageBox.Show(
                        "Der Anhang konnte nicht gespeichert werden.",
                        "Telenec Mail",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }
            }

            File.Move(
                temporaryPath,
                targetPath,
                overwrite: true);
        }
        catch
        {
            MessageBox.Show(
                "Der Anhang konnte nicht gespeichert werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und den ausgewählten Speicherort.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            TryDeleteFile(
                temporaryPath);
        }
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
        }
    }

    private async void MarkAsUnreadMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement menuItem ||
            menuItem.DataContext is not MailMessageItemViewModel message ||
            !message.CanMarkAsUnread)
        {
            return;
        }

        try
        {
            await _viewModel
                .MarkMessageAsUnreadAsync(
                    message);
        }
        catch
        {
            MessageBox.Show(
                "Die Nachricht konnte auf dem Mailserver nicht als ungelesen markiert werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MessageContextMenu_OnOpened(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu)
        {
            return;
        }

        var actionMenuItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag?.ToString(),
                            "MessageAction",
                            StringComparison.Ordinal));

        if (actionMenuItem is null)
        {
            return;
        }

        actionMenuItem.Header =
            _viewModel.IsTrashFolderSelected
                ? "Wiederherstellen"
                : "Löschen";
    }

    private async void ReplyMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsLoading ||
            sender is not FrameworkElement menuItem ||
            menuItem.DataContext is not MailMessageItemViewModel message)
        {
            return;
        }

        await ReplyToMessageFromUiAsync(
            message);
    }

    private async void ReplyAllMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsLoading ||
            sender is not FrameworkElement menuItem ||
            menuItem.DataContext is not MailMessageItemViewModel message)
        {
            return;
        }

        await ReplyAllToMessageFromUiAsync(
            message);
    }

    private async void ReplySelectedMessageButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var message =
            _viewModel.SelectedMessage;

        if (_viewModel.IsLoading ||
            message is null)
        {
            return;
        }

        await ReplyToMessageFromUiAsync(
            message);
    }

    private async void ReplyAllSelectedMessageButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var message =
            _viewModel.SelectedMessage;

        if (_viewModel.IsLoading ||
            message is null)
        {
            return;
        }

        await ReplyAllToMessageFromUiAsync(
            message);
    }

    private async void DeleteMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement menuItem ||
            menuItem.DataContext is not MailMessageItemViewModel clickedMessage)
        {
            return;
        }

        IReadOnlyList<MailMessageItemViewModel> messages;

        if (MessageListBox.SelectedItems.Contains(
                clickedMessage))
        {
            messages =
                GetSelectedMessages();
        }
        else
        {
            messages =
                new[] { clickedMessage };
        }

        if (_viewModel.IsTrashFolderSelected)
        {
            await RestoreMessagesFromUiAsync(
                messages);

            return;
        }

        await DeleteMessagesFromUiAsync(
            messages);
    }

    private async void DeleteSelectedMessageButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsLoading)
        {
            return;
        }

        var messages =
            GetSelectedMessages();

        if (messages.Count == 0)
        {
            return;
        }

        if (_viewModel.IsTrashFolderSelected)
        {
            await RestoreMessagesFromUiAsync(
                messages);

            return;
        }

        await DeleteMessagesFromUiAsync(
            messages);
    }

    private IReadOnlyList<MailMessageItemViewModel>
        GetSelectedMessages()
    {
        var selectedMessages =
            MessageListBox
                .SelectedItems
                .OfType<MailMessageItemViewModel>()
                .ToList();

        if (selectedMessages.Count == 0 &&
            _viewModel.SelectedMessage is not null)
        {
            selectedMessages.Add(
                _viewModel.SelectedMessage);
        }

        return selectedMessages;
    }

    private async Task RestoreMessagesFromUiAsync(
        IReadOnlyList<MailMessageItemViewModel> messages)
    {
        if (messages.Count == 0)
        {
            return;
        }

        try
        {
            var restored =
                await _viewModel
                    .RestoreMessagesFromTrashAsync(
                        messages);

            if (!restored)
            {
                MessageBox.Show(
                    messages.Count == 1
                        ? "Die Nachricht konnte nicht wiederhergestellt werden."
                        : "Die Nachrichten konnten nicht wiederhergestellt werden.",
                    "Telenec Mail",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch
        {
            var messageText =
                messages.Count == 1
                    ? "Die Nachricht konnte nicht wiederhergestellt werden."
                    : "Die Nachrichten konnten nicht wiederhergestellt werden.";

            MessageBox.Show(
                messageText +
                "\n\nBitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task DeleteMessagesFromUiAsync(
        IReadOnlyList<MailMessageItemViewModel> messages)
    {
        if (messages.Count == 0)
        {
            return;
        }

        try
        {
            await _viewModel
                .DeleteMessagesAsync(
                    messages);
        }
        catch
        {
            var messageText =
                messages.Count == 1
                    ? "Die Nachricht konnte nicht in den Papierkorb verschoben werden."
                    : "Die Nachrichten konnten nicht in den Papierkorb verschoben werden.";

            MessageBox.Show(
                messageText +
                "\n\nBitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static bool ContainsExternalImages(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return false;
        }

        if (Regex.IsMatch(
                html,
                @"<(?:img|source)\b[^>]*(?:src|srcset)\s*=\s*[""'][^""']*(?:https?://|//)",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline))
        {
            return true;
        }

        if (Regex.IsMatch(
                html,
                @"\bbackground\s*=\s*[""'][^""']*(?:https?://|//)",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline))
        {
            return true;
        }

        if (Regex.IsMatch(
                html,
                @"url\s*\(\s*[""']?(?:https?://|//)",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline))
        {
            return true;
        }

        return false;
    }

    private bool IsCurrentMessage(
        MailMessageItemViewModel message,
        int renderVersion)
    {
        return
            renderVersion ==
            _renderVersion &&
            ReferenceEquals(
                message,
                _viewModel.SelectedMessage);
    }

    private void ShowPlainTextView()
    {
        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        HtmlMailView.Visibility =
            Visibility.Collapsed;

        PlainTextMailView.Visibility =
            Visibility.Visible;
    }

    private async Task ReplyToMessageFromUiAsync(
        MailMessageItemViewModel message)
    {
        if (_viewModel.IsLoading ||
            !_viewModel.Messages.Contains(
                message))
        {
            return;
        }

        var composeWindow =
            _serviceProvider
                .GetRequiredService<ComposeWindow>();

        composeWindow.PrepareReply(
            message);

        await ShowComposeWindowAsync(
            composeWindow);
    }

    private async Task ReplyAllToMessageFromUiAsync(
        MailMessageItemViewModel message)
    {
        if (_viewModel.IsLoading ||
            !_viewModel.Messages.Contains(
                message))
        {
            return;
        }

        var composeWindow =
            _serviceProvider
                .GetRequiredService<ComposeWindow>();

        composeWindow.PrepareReplyAll(
            message);

        await ShowComposeWindowAsync(
            composeWindow);
    }

    private async Task ShowComposeWindowAsync(
        ComposeWindow composeWindow)
    {
        composeWindow.Owner =
            this;

        var result =
            composeWindow.ShowDialog();

        if (result != true)
        {
            return;
        }

        if (string.Equals(
                _viewModel.SelectedFolder?.DisplayName,
                "Gesendet",
                StringComparison.OrdinalIgnoreCase))
        {
            await _viewModel
                .ReloadAsync();
        }
    }

    private async void ComposeMailButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var composeWindow =
            _serviceProvider
                .GetRequiredService<ComposeWindow>();

        await ShowComposeWindowAsync(
            composeWindow);
    }

    private async void RefreshButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsLoading)
        {
            return;
        }

        await _viewModel
            .ReloadAsync();
    }

    private async void RetryButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsLoading)
        {
            return;
        }

        await _viewModel
            .ReloadAsync();
    }

    private void AccountMenuButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var contextMenu =
            AccountMenuButton.ContextMenu;

        if (contextMenu is null)
        {
            return;
        }

        contextMenu.PlacementTarget =
            AccountMenuButton;

        contextMenu.Placement =
            PlacementMode.Top;

        contextMenu.IsOpen =
            true;
    }

    private async void LogoutMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isLoggingOut)
        {
            return;
        }

        var confirmation =
            MessageBox.Show(
                "Möchten Sie dieses E-Mail-Konto wirklich abmelden?\n\n" +
                "Die gespeicherten Zugangsdaten werden von diesem Computer entfernt.",
                "Konto abmelden",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _isLoggingOut =
            true;

        AccountMenuButton.IsEnabled =
            false;

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is not null)
            {
                await _credentialStore
                    .DeleteAsync(
                        account.AccountId);

                await _mailAccountStore
                    .DeleteAsync(
                        account.AccountId);
            }

            var loginWindow =
                _serviceProvider
                    .GetRequiredService<LoginWindow>();

            loginWindow.PrepareKnownAccount(
                null);

            Application.Current.MainWindow =
                loginWindow;

            loginWindow.Show();

            Close();
        }
        catch
        {
            _isLoggingOut =
                false;

            AccountMenuButton.IsEnabled =
                true;

            MessageBox.Show(
                "Das Konto konnte nicht vollständig abgemeldet werden.\n\n" +
                "Bitte versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}