using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MailMessageWindow : Window
{
    private readonly MailMessageItemViewModel
        _message;

    private Task?
        _webViewInitializationTask;

    private bool
        _allowExternalImages;

    public MailMessageWindow(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        _message =
            message;

        InitializeComponent();

        DataContext =
            _message;

        Loaded +=
            MailMessageWindow_OnLoaded;

        Closed +=
            MailMessageWindow_OnClosed;
    }

    private async void MailMessageWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        await RenderMessageAsync();
    }

    private void MailMessageWindow_OnClosed(
        object? sender,
        EventArgs e)
    {
        Loaded -=
            MailMessageWindow_OnLoaded;

        Closed -=
            MailMessageWindow_OnClosed;

        try
        {
            HtmlMailView.Dispose();
        }
        catch
        {
        }
    }

    private async Task RenderMessageAsync()
    {
        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        if (!_message.HasHtmlBody)
        {
            ShowPlainTextView();
            return;
        }

        if (ContainsExternalImages(
                _message.HtmlBody!) &&
            !_allowExternalImages)
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

            if (HtmlMailView.CoreWebView2 is null)
            {
                ShowPlainTextView();
                return;
            }

            var html =
                PrepareHtmlForMailView(
                    _message.HtmlBody!);

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

        if (coreWebView is null)
        {
            throw new InvalidOperationException(
                "WebView2 konnte nicht initialisiert werden.");
        }

        /*
         * HTML-Mails werden bewusst als passive Dokumente
         * behandelt.
         *
         * JavaScript, WebMessages und HostObjects bleiben
         * deaktiviert.
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
         * Links aus einer Mail dürfen nicht innerhalb der
         * WebView navigieren.
         *
         * Sie werden ausschließlich an den
         * Windows-Standardbrowser übergeben.
         */
        coreWebView.NewWindowRequested +=
            CoreWebView2_OnNewWindowRequested;

        coreWebView.NavigationStarting +=
            CoreWebView2_OnNavigationStarting;

        /*
         * Nur Bildressourcen müssen für den
         * Datenschutzmechanismus beobachtet werden.
         */
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

    private void CoreWebView2_OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_allowExternalImages)
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
         * Eingebettete data:-Grafiken gehören zur Nachricht
         * selbst und werden nicht blockiert.
         */
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

        var coreWebView =
            HtmlMailView.CoreWebView2;

        if (coreWebView is null)
        {
            return;
        }

        e.Response =
            coreWebView
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
        if (!_message.HasHtmlBody)
        {
            return;
        }

        _allowExternalImages =
            true;

        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        try
        {
            await EnsureWebViewReadyAsync();

            if (HtmlMailView.CoreWebView2 is null)
            {
                return;
            }

            var html =
                PrepareHtmlForMailView(
                    _message.HtmlBody!);

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

    private void ShowPlainTextView()
    {
        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        HtmlMailView.Visibility =
            Visibility.Collapsed;

        PlainTextMailView.Visibility =
            Visibility.Visible;
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

    private static string PrepareHtmlForMailView(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return html;
        }

        /*
         * Interne Sprungmarken wie #abschnitt dürfen in der
         * Nachricht funktionieren.
         *
         * Ein gegebenenfalls gesetztes target-Attribut wird
         * deshalb bei solchen Links entfernt.
         */
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

    private void OpenExternalWebUri(
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
                this,
                "Der Link konnte nicht im Standardbrowser geöffnet werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}