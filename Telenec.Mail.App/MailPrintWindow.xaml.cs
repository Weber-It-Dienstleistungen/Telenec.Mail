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

    private string?
        _temporaryPrintDocumentPath;

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

            /*
             * NavigateToString besitzt eine Größenbegrenzung
             * von ungefähr 2 MB.
             *
             * HTML-Mails mit eingebetteten Bildern können
             * diese Grenze leicht überschreiten.
             *
             * Deshalb laden wir die Druckansicht über eine
             * temporäre lokale HTML-Datei.
             */
            var temporaryPath =
                CreateTemporaryPrintDocumentPath();

            await File.WriteAllTextAsync(
                temporaryPath,
                printDocument,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false));

            if (_isClosed)
            {
                TryDeleteTemporaryPrintDocument(
                    temporaryPath);

                return;
            }

            _temporaryPrintDocumentPath =
                temporaryPath;

            var printDocumentUri =
                new Uri(
                    temporaryPath,
                    UriKind.Absolute);

            PrintWebView
                .CoreWebView2
                .Navigate(
                    printDocumentUri.AbsoluteUri);
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
         * Script ist ausschließlich für unser eigenes
         * Druckdokument erforderlich.
         *
         * Der eigentliche Mailinhalt wird zunächst mit
         * DOMParser als inertes Dokument verarbeitet und
         * anschließend bereinigt.
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

        /*
         * Auch für die Druckansicht gilt:
         *
         * Keine externen Ressourcen still im Hintergrund
         * laden.
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
         * Links aus einer Mail dürfen die Druckansicht
         * nicht verlassen.
         */
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
         * Hat der Benutzer für genau diese Nachricht zuvor
         * "Trotzdem laden" gewählt, dürfen ausschließlich
         * externe Bilder geladen werden.
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
            /*
             * Browser statt System:
             *
             * Browser öffnet die WebView2-/Edge-
             * Druckoberfläche MIT Seitenvorschau.
             *
             * System würde lediglich den Windows-
             * Systemdruckdialog öffnen, der für unsere
             * WebView2-Seite keine Seitenansicht anbietet.
             */
            PrintWebView
                .CoreWebView2
                .ShowPrintUI(
                    CoreWebView2PrintDialogKind.Browser);
        }
        catch
        {
            MessageBox.Show(
                "Der Druckdialog konnte nicht geöffnet werden.",
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

        var temporaryPath =
            _temporaryPrintDocumentPath;

        _temporaryPrintDocumentPath =
            null;

        if (!string.IsNullOrWhiteSpace(
                temporaryPath))
        {
            TryDeleteTemporaryPrintDocument(
                temporaryPath);
        }
    }

    private static string
        CreateTemporaryPrintDocumentPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "TelenecMail_Print_" +
            Guid.NewGuid()
                .ToString("N") +
            ".html");
    }

    private static void
        TryDeleteTemporaryPrintDocument(
            string filePath)
    {
        try
        {
            if (File.Exists(
                    filePath))
            {
                File.Delete(
                    filePath);
            }
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
         * Der fremde Mailinhalt wird nicht direkt in unser
         * eigenes HTML hineinkopiert.
         *
         * Stattdessen wird er Base64-kodiert übergeben,
         * anschließend im Browser als inertes Dokument
         * geparst und bereinigt.
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

                <meta
                    http-equiv="Content-Security-Policy"
                    content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: http: https:; font-src data:;">

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
                        margin: 0 0 20px 0;
                        padding: 0 0 16px 0;
                        border-bottom: 1px solid #D8DEE4;
                    }

                    .subject {
                        margin: 0 0 14px 0;
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

                    #mailBodyHost {
                        display: block;
                        width: 100%;
                        min-width: 0;
                    }

                    @media print {

                        .print-header {
                            break-inside: avoid;
                            page-break-inside: avoid;
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

                <div id="mailBodyHost"></div>

                <script>
                    (() => {

                        const host =
                            document.getElementById(
                                "mailBodyHost");

                        const shadow =
                            host.attachShadow({
                                mode: "open"
                            });

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

                        /*
                         * DOMParser erzeugt zunächst ein
                         * inertes Dokument.
                         *
                         * Fremde Scripts werden dabei nicht
                         * ausgeführt.
                         */
                        const parser =
                            new DOMParser();

                        const mailDocument =
                            parser.parseFromString(
                                html,
                                "text/html");

                        /*
                         * Aktive oder interaktive Elemente
                         * werden vollständig entfernt.
                         */
                        const forbiddenElements =
                            mailDocument.querySelectorAll(
                                [
                                    "script",
                                    "iframe",
                                    "frame",
                                    "frameset",
                                    "object",
                                    "embed",
                                    "applet",
                                    "form",
                                    "input",
                                    "button",
                                    "textarea",
                                    "select",
                                    "option",
                                    "base",
                                    "meta",
                                    "link"
                                ].join(","));

                        for (const element
                             of forbiddenElements) {
                            element.remove();
                        }

                        /*
                         * Eventhandler wie onclick/onload
                         * dürfen ebenfalls nicht in die
                         * Druckansicht übernommen werden.
                         */
                        for (const element
                             of mailDocument
                                 .querySelectorAll("*")) {

                            for (const attribute
                                 of Array.from(
                                     element.attributes)) {

                                const name =
                                    attribute
                                        .name
                                        .toLowerCase();

                                const value =
                                    attribute
                                        .value
                                        .trim()
                                        .toLowerCase();

                                if (name.startsWith(
                                        "on") ||
                                    name === "srcdoc" ||
                                    (
                                        (
                                            name === "href" ||
                                            name === "src" ||
                                            name === "xlink:href"
                                        ) &&
                                        value.startsWith(
                                            "javascript:")
                                    )) {
                                    element.removeAttribute(
                                        attribute.name);
                                }
                            }
                        }

                        /*
                         * Styles aus der Mail bleiben innerhalb
                         * des Shadow DOM erhalten und können
                         * dadurch unseren Druckkopf nicht
                         * beeinflussen.
                         */
                        for (const styleElement
                             of mailDocument
                                 .querySelectorAll(
                                     "style")) {

                            const styleCopy =
                                document.createElement(
                                    "style");

                            styleCopy.textContent =
                                styleElement.textContent
                                ?? "";

                            shadow.appendChild(
                                styleCopy);
                        }

                        /*
                         * Diese Regeln werden absichtlich
                         * NACH den Mail-Styles eingefügt.
                         *
                         * Große Fotos werden dadurch auf die
                         * verfügbare Druckbreite und maximal
                         * ungefähr eine Seite Höhe begrenzt.
                         */
                        const printOverrides =
                            document.createElement(
                                "style");

                        printOverrides.textContent =
                            `
                            :host {
                                display: block;
                                width: 100%;
                                min-width: 0;
                            }

                            * {
                                box-sizing: border-box;
                            }

                            img {
                                max-width: 100% !important;
                                max-height: 220mm !important;
                                width: auto !important;
                                height: auto !important;
                                object-fit: contain !important;

                                break-inside: avoid !important;
                                page-break-inside: avoid !important;
                            }

                            table {
                                max-width: 100% !important;
                            }

                            pre {
                                white-space: pre-wrap !important;
                                overflow-wrap: anywhere !important;
                            }

                            body,
                            div,
                            p,
                            span,
                            td,
                            th {
                                overflow-wrap: anywhere;
                            }

                            @media print {

                                img {
                                    break-inside: avoid !important;
                                    page-break-inside: avoid !important;
                                }

                            }
                            `;

                        shadow.appendChild(
                            printOverrides);

                        const mailContent =
                            document.createElement(
                                "div");

                        mailContent.setAttribute(
                            "part",
                            "mail-content");

                        while (mailDocument.body.firstChild) {

                            mailContent.appendChild(
                                mailDocument.body.firstChild);

                        }

                        shadow.appendChild(
                            mailContent);

                        /*
                         * Links bleiben sichtbar, sind in der
                         * Druckansicht aber nicht anklickbar.
                         */
                        shadow.addEventListener(
                            "click",
                            event => {

                                const target =
                                    event.target;

                                if (!(target instanceof Element)) {
                                    return;
                                }

                                if (target.closest(
                                        "a")) {
                                    event.preventDefault();
                                }

                            });

                        async function waitForImages() {

                            const images =
                                Array.from(
                                    shadow.querySelectorAll(
                                        "img"));

                            if (images.length === 0) {
                                return;
                            }

                            const imagePromises =
                                images.map(
                                    image => {

                                        if (image.complete) {
                                            return Promise.resolve();
                                        }

                                        return new Promise(
                                            resolve => {

                                                image.addEventListener(
                                                    "load",
                                                    resolve,
                                                    {
                                                        once: true
                                                    });

                                                image.addEventListener(
                                                    "error",
                                                    resolve,
                                                    {
                                                        once: true
                                                    });

                                            });

                                    });

                            /*
                             * Eine externe Ressource darf das
                             * Druckfenster niemals dauerhaft
                             * blockieren.
                             */
                            await Promise.race([
                                Promise.all(
                                    imagePromises),

                                new Promise(
                                    resolve =>
                                        setTimeout(
                                            resolve,
                                            3000))
                            ]);

                        }

                        async function finish() {

                            await waitForImages();

                            /*
                             * Zwei Frames geben Chromium noch
                             * Gelegenheit, die endgültigen
                             * Bildgrößen und Umbrüche zu
                             * berechnen.
                             */
                            await new Promise(
                                resolve =>
                                    requestAnimationFrame(
                                        () =>
                                            requestAnimationFrame(
                                                resolve)));

                            window.chrome
                                .webview
                                .postMessage(
                                    "printReady");

                        }

                        finish();

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