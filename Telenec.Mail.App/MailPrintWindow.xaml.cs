using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MailPrintWindow :
    Window
{
    private readonly MailMessageItemViewModel
        _message;

    private readonly bool
        _allowExternalImages;

    private bool
        _isPrintReady;

    private bool
        _isClosed;

    public MailPrintWindow(
        MailMessageItemViewModel message,
        bool allowExternalImages)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        InitializeComponent();

        _message =
            message;

        _allowExternalImages =
            allowExternalImages;
    }

    private async void MailPrintWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await PrintWebView
                .EnsureCoreWebView2Async();

            if (_isClosed)
            {
                return;
            }

            ConfigureWebView();

            var printDocument =
                BuildPrintDocument(
                    _message);

            PrintWebView
                .CoreWebView2
                .NavigateToString(
                    printDocument);
        }
        catch
        {
            if (_isClosed)
            {
                return;
            }

            MessageBox.Show(
                "Die Druckvorschau konnte nicht erstellt werden.",
                "Drucken nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Close();
        }
    }

    private void ConfigureWebView()
    {
        var coreWebView =
            PrintWebView
                .CoreWebView2;

        /*
         * Das äußere Druckdokument stammt vollständig
         * von Telenec Mail und verwendet ein kleines
         * internes Script ausschließlich dafür, die
         * eingebettete Mail auf ihre Druckhöhe zu bringen.
         *
         * Der eigentliche Mailinhalt läuft dagegen in
         * einem sandboxed iframe OHNE Scriptfreigabe.
         */
        coreWebView.Settings.IsScriptEnabled =
            true;

        coreWebView.Settings.IsWebMessageEnabled =
            true;

        coreWebView.Settings.AreHostObjectsAllowed =
            false;

        coreWebView.Settings.AreDevToolsEnabled =
            false;

        coreWebView.Settings.AreDefaultContextMenusEnabled =
            false;

        coreWebView.Settings.IsStatusBarEnabled =
            false;

        coreWebView.WebMessageReceived +=
            CoreWebView2_OnWebMessageReceived;

        coreWebView.NewWindowRequested +=
            CoreWebView2_OnNewWindowRequested;

        coreWebView.NavigationStarting +=
            CoreWebView2_OnNavigationStarting;

        coreWebView.FrameNavigationStarting +=
            CoreWebView2_OnFrameNavigationStarting;

        /*
         * Für die Druckansicht beobachten wir ALLE
         * Netzwerkressourcen.
         *
         * Externe CSS-, Script-, Font- und sonstige
         * Ressourcen dürfen nicht still im Hintergrund
         * nachgeladen werden.
         *
         * Wurde für die aktuell angezeigte Mail zuvor
         * ausdrücklich "Trotzdem laden" gewählt, dürfen
         * ausschließlich externe Bilder geladen werden.
         */
        coreWebView.AddWebResourceRequestedFilter(
            "*",
            CoreWebView2WebResourceContext.All);

        coreWebView.WebResourceRequested +=
            CoreWebView2_OnWebResourceRequested;
    }

    private void CoreWebView2_OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message;

        try
        {
            message =
                e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (!string.Equals(
                message,
                "printReady",
                StringComparison.Ordinal))
        {
            return;
        }

        _isPrintReady =
            true;

        LoadingOverlay.Visibility =
            Visibility.Collapsed;

        PrintButton.IsEnabled =
            true;
    }

    private void CoreWebView2_OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        /*
         * Die Druckvorschau öffnet keinerlei Links.
         */
        e.Handled =
            true;
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

        /*
         * Ein Klick innerhalb einer Mail darf die
         * Druckvorschau nicht auf eine Webseite
         * navigieren.
         */
        e.Cancel =
            true;
    }

    private void CoreWebView2_OnFrameNavigationStarting(
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
    }

    private void CoreWebView2_OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri =
            e.Request.Uri;

        if (!IsExternalWebUri(
                uri))
        {
            return;
        }

        /*
         * Nur wenn externe Bilder für genau diese Mail
         * bereits ausdrücklich freigegeben wurden, darf
         * WebView2 HTTP(S)-Bilder laden.
         *
         * Alles andere bleibt auch beim Drucken blockiert.
         */
        if (_allowExternalImages &&
            e.ResourceContext ==
                CoreWebView2WebResourceContext.Image)
        {
            return;
        }

        e.Response =
            PrintWebView
                .CoreWebView2
                .Environment
                .CreateWebResourceResponse(
                    new MemoryStream(
                        Array.Empty<byte>()),
                    403,
                    "Blocked",
                    "Content-Type: text/plain\r\n" +
                    "Cache-Control: no-store");
    }

    private void PrintButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!_isPrintReady ||
            PrintWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            PrintWebView
                .CoreWebView2
                .ShowPrintUI(
                    CoreWebView2PrintDialogKind.System);
        }
        catch
        {
            MessageBox.Show(
                "Der Windows-Druckdialog konnte nicht geöffnet werden.",
                "Drucken nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void MailPrintWindow_OnClosed(
        object? sender,
        EventArgs e)
    {
        _isClosed =
            true;

        try
        {
            var coreWebView =
                PrintWebView
                    .CoreWebView2;

            if (coreWebView is not null)
            {
                coreWebView.WebMessageReceived -=
                    CoreWebView2_OnWebMessageReceived;

                coreWebView.NewWindowRequested -=
                    CoreWebView2_OnNewWindowRequested;

                coreWebView.NavigationStarting -=
                    CoreWebView2_OnNavigationStarting;

                coreWebView.FrameNavigationStarting -=
                    CoreWebView2_OnFrameNavigationStarting;

                coreWebView.WebResourceRequested -=
                    CoreWebView2_OnWebResourceRequested;
            }
        }
        catch
        {
        }

        try
        {
            PrintWebView.Dispose();
        }
        catch
        {
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

    private static string BuildPrintDocument(
        MailMessageItemViewModel message)
    {
        var subject =
            HtmlEncode(
                string.IsNullOrWhiteSpace(
                    message.Subject)
                    ? "(Ohne Betreff)"
                    : message.Subject);

        var sender =
            CreateSenderDescription(
                message);

        var toAddresses =
            message.ToAddresses.Count > 0
                ? string.Join(
                    "; ",
                    message.ToAddresses)
                : message.RecipientAddress;

        var ccAddresses =
            message.CcAddresses.Count > 0
                ? string.Join(
                    "; ",
                    message.CcAddresses)
                : string.Empty;

        var attachmentSummary =
            CreateAttachmentSummary(
                message);

        var bodyHtml =
            message.HasHtmlBody
                ? message.HtmlBody!
                : CreatePlainTextBodyHtml(
                    message);

        /*
         * Der eigentliche Mailinhalt wird Base64-kodiert
         * an das interne Druckdokument übergeben.
         *
         * Dadurch kann fremdes HTML weder unser äußeres
         * Dokument noch das interne Script syntaktisch
         * verlassen.
         */
        var bodyBase64 =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    bodyHtml));

        var ccRow =
            string.IsNullOrWhiteSpace(
                ccAddresses)
                ? string.Empty
                : $"""
                  <div class="meta-row">
                      <div class="meta-label">Cc:</div>
                      <div class="meta-value">{HtmlEncode(ccAddresses)}</div>
                  </div>
                  """;

        var attachmentRow =
            string.IsNullOrWhiteSpace(
                attachmentSummary)
                ? string.Empty
                : $"""
                  <div class="meta-row">
                      <div class="meta-label">Anlagen:</div>
                      <div class="meta-value">{HtmlEncode(attachmentSummary)}</div>
                  </div>
                  """;

        var signatureRow =
            message.HasSmimeSignature
                ? """
                  <div class="security-note">
                      Digital signiert (S/MIME) · Signatur noch nicht geprüft
                  </div>
                  """
                : string.Empty;

        return
            $$"""
            <!DOCTYPE html>
            <html lang="de">
            <head>
                <meta charset="utf-8">

                <meta
                    name="viewport"
                    content="width=device-width, initial-scale=1">

                <style>
                    @page {
                        margin: 16mm;
                    }

                    * {
                        box-sizing: border-box;
                    }

                    html,
                    body {
                        margin: 0;
                        padding: 0;
                        background: white;
                    }

                    body {
                        font-family:
                            "Segoe UI",
                            Arial,
                            sans-serif;

                        font-size: 11pt;
                        line-height: 1.45;
                        color: #1F2328;
                    }

                    .print-header {
                        margin: 0 0 22px 0;
                        padding: 0 0 18px 0;
                        border-bottom: 1px solid #D8DEE4;
                    }

                    .subject {
                        margin: 0 0 16px 0;
                        font-size: 20pt;
                        line-height: 1.2;
                        font-weight: 600;
                        overflow-wrap: anywhere;
                    }

                    .meta-row {
                        display: grid;
                        grid-template-columns: 76px 1fr;
                        gap: 10px;
                        margin-top: 5px;
                    }

                    .meta-label {
                        font-weight: 600;
                        color: #66707A;
                    }

                    .meta-value {
                        overflow-wrap: anywhere;
                    }

                    .security-note {
                        display: inline-block;
                        margin-top: 12px;
                        padding: 6px 9px;
                        border: 1px solid #D8DEE4;
                        border-radius: 4px;
                        font-size: 9.5pt;
                        font-weight: 600;
                    }

                    #mailBody {
                        display: block;
                        width: 100%;
                        min-height: 200px;
                        border: 0;
                        overflow: hidden;
                        background: white;
                    }

                    @media print {
                        .print-header {
                            break-inside: avoid;
                        }
                    }
                </style>
            </head>

            <body>

                <section class="print-header">

                    <h1 class="subject">
                        {{subject}}
                    </h1>

                    <div class="meta-row">
                        <div class="meta-label">Von:</div>
                        <div class="meta-value">{{HtmlEncode(sender)}}</div>
                    </div>

                    <div class="meta-row">
                        <div class="meta-label">An:</div>
                        <div class="meta-value">{{HtmlEncode(toAddresses)}}</div>
                    </div>

                    {{ccRow}}

                    <div class="meta-row">
                        <div class="meta-label">Datum:</div>
                        <div class="meta-value">{{HtmlEncode(message.DisplayDateTime)}}</div>
                    </div>

                    {{attachmentRow}}

                    {{signatureRow}}

                </section>

                <!--
                    sandbox erlaubt dem Host das Lesen der
                    Dokumenthöhe, aber ausdrücklich KEINE
                    Scripts aus der E-Mail.
                -->
                <iframe
                    id="mailBody"
                    sandbox="allow-same-origin"></iframe>

                <script>
                    (() => {
                        const frame =
                            document.getElementById(
                                "mailBody");

                        const encoded =
                            "{{bodyBase64}}";

                        const bytes =
                            Uint8Array.from(
                                atob(encoded),
                                character =>
                                    character.charCodeAt(0));

                        const html =
                            new TextDecoder(
                                "utf-8")
                                .decode(
                                    bytes);

                        let resizeObserver =
                            null;

                        let readyReported =
                            false;

                        function resizeFrame() {
                            try {
                                const documentElement =
                                    frame
                                        .contentDocument
                                        ?.documentElement;

                                const body =
                                    frame
                                        .contentDocument
                                        ?.body;

                                if (!documentElement) {
                                    return;
                                }

                                const height =
                                    Math.max(
                                        documentElement.scrollHeight,
                                        documentElement.offsetHeight,
                                        body?.scrollHeight ?? 0,
                                        body?.offsetHeight ?? 0,
                                        200);

                                frame.style.height =
                                    `${height + 20}px`;

                                if (!readyReported) {
                                    readyReported =
                                        true;

                                    window.chrome
                                        .webview
                                        .postMessage(
                                            "printReady");
                                }
                            }
                            catch {
                                if (!readyReported) {
                                    readyReported =
                                        true;

                                    window.chrome
                                        .webview
                                        .postMessage(
                                            "printReady");
                                }
                            }
                        }

                        frame.addEventListener(
                            "load",
                            () => {
                                requestAnimationFrame(
                                    () => {
                                        requestAnimationFrame(
                                            resizeFrame);
                                    });

                                try {
                                    const documentElement =
                                        frame
                                            .contentDocument
                                            ?.documentElement;

                                    if (documentElement &&
                                        typeof ResizeObserver !==
                                            "undefined") {
                                        resizeObserver =
                                            new ResizeObserver(
                                                resizeFrame);

                                        resizeObserver.observe(
                                            documentElement);
                                    }
                                }
                                catch {
                                }
                            });

                        frame.srcdoc =
                            html;
                    })();
                </script>

            </body>
            </html>
            """;
    }

    private static string CreateSenderDescription(
        MailMessageItemViewModel message)
    {
        var sender =
            message.Sender?
                .Trim()
            ?? string.Empty;

        var address =
            message.SenderAddress?
                .Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(
                address))
        {
            return sender;
        }

        if (string.IsNullOrWhiteSpace(
                sender) ||
            string.Equals(
                sender,
                address,
                StringComparison.OrdinalIgnoreCase))
        {
            return address;
        }

        return
            $"{sender} <{address}>";
    }

    private static string CreateAttachmentSummary(
        MailMessageItemViewModel message)
    {
        if (!message.HasAttachments)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            message.Attachments
                .Select(
                    attachment =>
                        attachment.FileName)
                .Where(
                    fileName =>
                        !string.IsNullOrWhiteSpace(
                            fileName)));
    }

    private static string CreatePlainTextBodyHtml(
        MailMessageItemViewModel message)
    {
        var builder =
            new StringBuilder();

        builder.Append(
            """
            <!DOCTYPE html>
            <html lang="de">
            <head>
                <meta charset="utf-8">

                <style>
                    html,
                    body {
                        margin: 0;
                        padding: 0;
                        background: white;
                    }

                    body {
                        font-family:
                            "Segoe UI",
                            Arial,
                            sans-serif;

                        font-size: 11pt;
                        line-height: 1.5;
                        color: #1F2328;
                        overflow-wrap: anywhere;
                    }

                    .text-block {
                        white-space: pre-wrap;
                    }

                    .section {
                        margin-top: 18px;
                    }

                    .highlight {
                        margin-top: 20px;
                        padding: 12px;
                        border: 1px solid #D8DEE4;
                        border-radius: 5px;
                    }

                    .highlight-title {
                        font-weight: 600;
                        margin-bottom: 5px;
                    }

                    .signature {
                        margin-top: 5px;
                        font-weight: 600;
                    }
                </style>
            </head>

            <body>
            """);

        if (!string.IsNullOrWhiteSpace(
                message.Greeting))
        {
            builder.Append(
                "<div class=\"text-block\">");

            builder.Append(
                HtmlEncode(
                    message.Greeting));

            builder.Append(
                "</div>");
        }

        if (!string.IsNullOrWhiteSpace(
                message.Body))
        {
            builder.Append(
                "<div class=\"text-block section\">");

            builder.Append(
                HtmlEncode(
                    message.Body));

            builder.Append(
                "</div>");
        }

        if (message.HasHighlight)
        {
            builder.Append(
                "<div class=\"highlight\">");

            builder.Append(
                "<div class=\"highlight-title\">");

            builder.Append(
                HtmlEncode(
                    message.HighlightTitle!));

            builder.Append(
                "</div>");

            builder.Append(
                "<div class=\"text-block\">");

            builder.Append(
                HtmlEncode(
                    message.HighlightText!));

            builder.Append(
                "</div>");

            builder.Append(
                "</div>");
        }

        if (!string.IsNullOrWhiteSpace(
                message.Closing))
        {
            builder.Append(
                "<div class=\"text-block section\">");

            builder.Append(
                HtmlEncode(
                    message.Closing));

            builder.Append(
                "</div>");
        }

        if (!string.IsNullOrWhiteSpace(
                message.Signature))
        {
            builder.Append(
                "<div class=\"text-block signature\">");

            builder.Append(
                HtmlEncode(
                    message.Signature));

            builder.Append(
                "</div>");
        }

        builder.Append(
            """
            </body>
            </html>
            """);

        return builder.ToString();
    }

    private static string HtmlEncode(
        string? value)
    {
        return WebUtility.HtmlEncode(
            value
            ?? string.Empty);
    }
}