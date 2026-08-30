using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IMailAccountStore _mailAccountStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IServiceProvider _serviceProvider;

    private bool _isLoggingOut;
    private bool _isLoaded;

    private Task? _webViewInitializationTask;
    private int _renderVersion;

    /*
     * Sicherheitsstandard:
     *
     * Jede neu ausgewählte Mail startet wieder mit
     * blockierten externen Bildern.
     */
    private bool _allowExternalImagesForCurrentMessage;

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

        try
        {
            HtmlMailView.Dispose();
        }
        catch
        {
            // Beim Schließen nicht relevant.
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

        /*
         * Neue Mail = neue Sicherheitsentscheidung.
         */
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

        /*
         * WebView vor Initialisierung sichtbar machen.
         * Das ist der inzwischen bestätigte funktionierende Pfad.
         */
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

            /*
             * Roher HTML-Inhalt bleibt komplett unverändert.
             */
            HtmlMailView
                .CoreWebView2
                .NavigateToString(
                    message.HtmlBody!);
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

        /*
         * JavaScript bleibt deaktiviert.
         */
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

        /*
         * Popups bleiben gesperrt.
         */
        coreWebView.NewWindowRequested +=
            (_, args) =>
            {
                args.Handled =
                    true;
            };

        /*
         * Ausschließlich Bildressourcen beobachten.
         *
         * Ganz bewusst KEIN Filter für HTML, CSS,
         * Navigation oder andere Ressourcen.
         */
        coreWebView.AddWebResourceRequestedFilter(
            "*",
            CoreWebView2WebResourceContext.Image);

        coreWebView.WebResourceRequested +=
            CoreWebView2_OnWebResourceRequested;
    }

    private void CoreWebView2_OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs e)
    {
        /*
         * Benutzer hat Bilder für diese Nachricht
         * ausdrücklich freigegeben.
         */
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

        /*
         * data:-Bilder sind bereits Bestandteil der Mail
         * und verlassen den Rechner nicht.
         */
        if (uri.StartsWith(
                "data:",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        /*
         * Nur echte externe HTTP-/HTTPS-Bilder blockieren.
         */
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

        /*
         * Leere Antwort statt Netzwerkabruf.
         *
         * Cache-Control: no-store ist wichtig, damit ein späteres
         * "Trotzdem laden" dieselbe URL erneut anfordern kann.
         */
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

        /*
         * Bewusste Freigabe nur für diese eine Mail.
         */
        _allowExternalImagesForCurrentMessage =
            true;

        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        /*
         * Mail erneut rendern.
         *
         * Die Bildrequests laufen jetzt durch den gleichen
         * WebResourceRequested-Handler, werden dort aber
         * nicht mehr blockiert.
         */
        HtmlMailView
            .CoreWebView2
            .NavigateToString(
                message.HtmlBody!);

        await Task.CompletedTask;
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

    private static bool ContainsExternalImages(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return false;
        }

        /*
         * Klassische IMG/SOURCE-Tags.
         */
        if (Regex.IsMatch(
                html,
                @"<(?:img|source)\b[^>]*(?:src|srcset)\s*=\s*[""'][^""']*(?:https?://|//)",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline))
        {
            return true;
        }

        /*
         * Alte HTML-Mails nutzen gelegentlich
         * background="https://..."
         */
        if (Regex.IsMatch(
                html,
                @"\bbackground\s*=\s*[""'][^""']*(?:https?://|//)",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline))
        {
            return true;
        }

        /*
         * CSS background-image / url(...).
         */
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