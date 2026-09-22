using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Telenec.Mail.App.Controls;

public partial class ComposeHtmlEditor :
    UserControl
{
    private static readonly HashSet<string>
        SupportedCommands =
            new(
                StringComparer.Ordinal)
            {
                "bold",
                "italic",
                "underline",
                "insertUnorderedList",
                "foreColor",
                "fontName",
                "fontSize"
            };

    private readonly SemaphoreSlim
        _initializationLock =
            new(
                1,
                1);

    private bool _isInitialized;

    public ComposeHtmlEditor()
    {
        InitializeComponent();
    }

    public event EventHandler<
        ComposeHtmlEditorContentChangedEventArgs>?
        ContentChanged;

    public string PlainText
    {
        get;
        private set;
    } =
        string.Empty;

    public string? HtmlBody
    {
        get;
        private set;
    }

    public async Task EnsureInitializedAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock
            .WaitAsync(
                cancellationToken);

        try
        {
            if (_isInitialized)
            {
                return;
            }

            await EditorWebView
                .EnsureCoreWebView2Async();

            cancellationToken
                .ThrowIfCancellationRequested();

            ConfigureWebView();

            await NavigateToEditorAsync(
                cancellationToken);

            _isInitialized =
                true;
        }
        finally
        {
            _initializationLock
                .Release();
        }
    }

    public async Task SetContentAsync(
        string? plainText,
        string? htmlBody,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(
            cancellationToken);

        var normalizedPlainText =
            plainText
            ?? string.Empty;

        var normalizedHtmlBody =
            string.IsNullOrWhiteSpace(
                htmlBody)
                ? null
                : htmlBody;

        PlainText =
            normalizedPlainText;

        HtmlBody =
            normalizedHtmlBody;

        var plainTextJson =
            JsonSerializer.Serialize(
                normalizedPlainText);

        var htmlBodyJson =
            JsonSerializer.Serialize(
                normalizedHtmlBody);

        await EditorWebView
            .CoreWebView2
            .ExecuteScriptAsync(
                "window.telenecEditor.setContent(" +
                htmlBodyJson +
                ", " +
                plainTextJson +
                ");");
    }

    public async Task<
        ComposeHtmlEditorContent> GetContentAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(
            cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        var result =
            await EditorWebView
                .CoreWebView2
                .ExecuteScriptAsync(
                    "window.telenecEditor.getContent();");

        var content =
            JsonSerializer.Deserialize<
                ComposeHtmlEditorContent>(
                    result);

        if (content is null)
        {
            return new ComposeHtmlEditorContent(
                PlainText:
                    PlainText,

                HtmlBody:
                    HtmlBody);
        }

        PlainText =
            content.PlainText
            ?? string.Empty;

        HtmlBody =
            string.IsNullOrWhiteSpace(
                content.HtmlBody)
                ? null
                : content.HtmlBody;

        return new ComposeHtmlEditorContent(
            PlainText:
                PlainText,

            HtmlBody:
                HtmlBody);
    }

    public async Task SetReadOnlyAsync(
        bool isReadOnly,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(
            cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        var value =
            isReadOnly
                ? "true"
                : "false";

        await EditorWebView
            .CoreWebView2
            .ExecuteScriptAsync(
                $"window.telenecEditor.setReadOnly({value});");
    }

    public async Task FocusEditorAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(
            cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        EditorWebView.Focus();

        await EditorWebView
            .CoreWebView2
            .ExecuteScriptAsync(
                "window.telenecEditor.focus();");
    }

    public async Task ExecuteCommandAsync(
        string command,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                command))
        {
            throw new ArgumentException(
                "Der Editor-Befehl darf nicht leer sein.",
                nameof(command));
        }

        if (!SupportedCommands.Contains(
                command))
        {
            throw new ArgumentException(
                "Der angegebene Editor-Befehl wird nicht unterstützt.",
                nameof(command));
        }

        await EnsureInitializedAsync(
            cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        var commandJson =
            JsonSerializer.Serialize(
                command);

        var valueJson =
            JsonSerializer.Serialize(
                value);

        await EditorWebView
            .CoreWebView2
            .ExecuteScriptAsync(
                "window.telenecEditor.executeCommand(" +
                commandJson +
                ", " +
                valueJson +
                ");");
    }

    private void ConfigureWebView()
    {
        var coreWebView =
            EditorWebView
                .CoreWebView2;

        coreWebView.Settings.AreDevToolsEnabled =
            false;

        coreWebView.Settings.IsStatusBarEnabled =
            false;

        /*
         * Der Composer soll niemals selbständig Inhalte
         * aus dem Internet nachladen.
         *
         * Das ist besonders wichtig, sobald später HTML-
         * Entwürfe aus anderen Clients bearbeitet werden.
         */
        coreWebView.AddWebResourceRequestedFilter(
            "http://*",
            CoreWebView2WebResourceContext.All);

        coreWebView.AddWebResourceRequestedFilter(
            "https://*",
            CoreWebView2WebResourceContext.All);

        coreWebView.WebResourceRequested +=
            CoreWebView2_OnWebResourceRequested;

        coreWebView.WebMessageReceived +=
            CoreWebView2_OnWebMessageReceived;
    }

    private async Task NavigateToEditorAsync(
        CancellationToken cancellationToken)
    {
        var navigationCompleted =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        void NavigationCompletedHandler(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            EditorWebView.NavigationCompleted -=
                NavigationCompletedHandler;

            if (e.IsSuccess)
            {
                navigationCompleted
                    .TrySetResult(
                        true);

                return;
            }

            navigationCompleted
                .TrySetException(
                    new InvalidOperationException(
                        "Der HTML-Editor konnte nicht initialisiert werden."));
        }

        EditorWebView.NavigationCompleted +=
            NavigationCompletedHandler;

        try
        {
            EditorWebView
                .NavigateToString(
                    EditorDocument);

            await navigationCompleted
                .Task
                .WaitAsync(
                    cancellationToken);
        }
        catch
        {
            EditorWebView.NavigationCompleted -=
                NavigationCompletedHandler;

            throw;
        }
    }

    private void CoreWebView2_OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri =
            e.Request.Uri;

        if (string.IsNullOrWhiteSpace(
                uri))
        {
            return;
        }

        if (!uri.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase) &&
            !uri.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Response =
            EditorWebView
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

        if (string.IsNullOrWhiteSpace(
                message))
        {
            return;
        }

        EditorMessage? editorMessage;

        try
        {
            editorMessage =
                JsonSerializer.Deserialize<
                    EditorMessage>(
                        message);
        }
        catch
        {
            return;
        }

        if (editorMessage is null ||
            !string.Equals(
                editorMessage.Type,
                "contentChanged",
                StringComparison.Ordinal))
        {
            return;
        }

        PlainText =
            editorMessage.PlainText
            ?? string.Empty;

        HtmlBody =
            string.IsNullOrWhiteSpace(
                editorMessage.HtmlBody)
                ? null
                : editorMessage.HtmlBody;

        ContentChanged?.Invoke(
            this,
            new ComposeHtmlEditorContentChangedEventArgs(
                PlainText,
                HtmlBody));
    }

    private sealed record EditorMessage(
        string Type,
        string? PlainText,
        string? HtmlBody);

    private const string EditorDocument =
        """
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="utf-8">
            <meta
                http-equiv="Content-Security-Policy"
                content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data:;">
            <style>
                html,
                body {
                    width: 100%;
                    height: 100%;
                    margin: 0;
                    padding: 0;
                    overflow: hidden;
                    background: transparent;
                }

                body {
                    font-family: "Segoe UI", sans-serif;
                    font-size: 14px;
                    color: #1F2328;
                }

                #editor {
                    box-sizing: border-box;
                    width: 100%;
                    height: 100%;
                    min-height: 100%;
                    padding: 16px;
                    overflow-y: auto;
                    overflow-x: hidden;
                    outline: none;
                    white-space: pre-wrap;
                    overflow-wrap: anywhere;
                }

                #editor[contenteditable="false"] {
                    cursor: default;
                }
            </style>
        </head>

        <body>
            <div
                id="editor"
                contenteditable="true"
                spellcheck="true"></div>

            <script>
                (() => {
                    const editor =
                        document.getElementById("editor");

                    let suppressChange =
                        false;

                    function hasRichFormatting() {
                        /*
                         * DIV, P und BR werden von contenteditable
                         * auch bei völlig normalem Plaintext für
                         * Absatz- und Zeilenumbrüche erzeugt.
                         *
                         * Diese Elemente allein machen aus einer
                         * Nachricht deshalb noch keine HTML-Mail.
                         */
                        const elements =
                            editor.querySelectorAll("*");

                        for (const element of elements) {
                            const tagName =
                                element.tagName
                                    .toUpperCase();

                            if (tagName === "BR") {
                                continue;
                            }

                            if (tagName === "DIV" ||
                                tagName === "P") {
                                if (element.attributes.length === 0) {
                                    continue;
                                }

                                return true;
                            }

                            /*
                             * Jeder andere HTML-Knoten stellt
                             * entweder Formatierung oder einen
                             * sonstigen Rich-Content-Inhalt dar.
                             */
                            return true;
                        }

                        return false;
                    }

                    function getHtmlBody() {
                        if (!hasRichFormatting()) {
                            return null;
                        }

                        const html =
                            editor.innerHTML ?? "";

                        if (html.trim().length === 0) {
                            return null;
                        }

                        return html;
                    }

                    function notifyChanged() {
                        if (suppressChange) {
                            return;
                        }

                        window.chrome.webview.postMessage(
                            JSON.stringify({
                                Type: "contentChanged",
                                PlainText: editor.innerText ?? "",
                                HtmlBody: getHtmlBody()
                            }));
                    }

                    function insertPlainText(text) {
                        const selection =
                            window.getSelection();

                        if (!selection ||
                            selection.rangeCount === 0) {
                            editor.appendChild(
                                document.createTextNode(text));

                            return;
                        }

                        const range =
                            selection.getRangeAt(0);

                        range.deleteContents();

                        const fragment =
                            document.createDocumentFragment();

                        const lines =
                            text
                                .replace(/\r\n/g, "\n")
                                .replace(/\r/g, "\n")
                                .split("\n");

                        lines.forEach(
                            (line, index) => {
                                if (index > 0) {
                                    fragment.appendChild(
                                        document.createElement("br"));
                                }

                                fragment.appendChild(
                                    document.createTextNode(line));
                            });

                        const lastNode =
                            fragment.lastChild;

                        range.insertNode(
                            fragment);

                        if (lastNode) {
                            range.setStartAfter(
                                lastNode);

                            range.collapse(
                                true);

                            selection.removeAllRanges();

                            selection.addRange(
                                range);
                        }
                    }

                    editor.addEventListener(
                        "input",
                        notifyChanged);

                    editor.addEventListener(
                        "paste",
                        event => {
                            event.preventDefault();

                            const text =
                                event.clipboardData
                                    ?.getData("text/plain")
                                ?? "";

                            insertPlainText(
                                text);

                            notifyChanged();
                        });

                    editor.addEventListener(
                        "drop",
                        event => {
                            event.preventDefault();
                        });

                    editor.addEventListener(
                        "click",
                        event => {
                            const link =
                                event.target.closest?.("a");

                            if (link) {
                                event.preventDefault();
                            }
                        });

                    window.telenecEditor = {
                        setContent: (
                            htmlBody,
                            plainText) => {
                            suppressChange =
                                true;

                            try {
                                if (typeof htmlBody === "string" &&
                                    htmlBody.trim().length > 0) {
                                    editor.innerHTML =
                                        htmlBody;
                                }
                                else {
                                    editor.textContent =
                                        plainText ?? "";
                                }
                            }
                            finally {
                                suppressChange =
                                    false;
                            }
                        },

                        getContent: () => ({
                            PlainText:
                                editor.innerText ?? "",

                            HtmlBody:
                                getHtmlBody()
                        }),

                        setReadOnly: isReadOnly => {
                            editor.contentEditable =
                                isReadOnly
                                    ? "false"
                                    : "true";
                        },

                        focus: () => {
                            editor.focus();
                        },

                        executeCommand: (
                            command,
                            value) => {
                            editor.focus();

                            document.execCommand(
                                command,
                                false,
                                value ?? null);

                            notifyChanged();
                        }
                    };
                })();
            </script>
        </body>
        </html>
        """;
}

public sealed record ComposeHtmlEditorContent(
    string? PlainText,
    string? HtmlBody);

public sealed class ComposeHtmlEditorContentChangedEventArgs :
    EventArgs
{
    public ComposeHtmlEditorContentChangedEventArgs(
        string plainText,
        string? htmlBody)
    {
        PlainText =
            plainText;

        HtmlBody =
            htmlBody;
    }

    public string PlainText
    {
        get;
    }

    public string? HtmlBody
    {
        get;
    }
}