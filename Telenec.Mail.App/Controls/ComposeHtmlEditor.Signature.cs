using System.Text.Json;

namespace Telenec.Mail.App.Controls;

public partial class ComposeHtmlEditor
{
    public async Task SetSignatureContentAsync(
        string signatureText,
        string? followingPlainText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            signatureText);

        await EnsureInitializedAsync(
            cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        var normalizedFollowingPlainText =
            string.IsNullOrEmpty(
                followingPlainText)
                ? null
                : followingPlainText;

        PlainText =
            Environment.NewLine +
            Environment.NewLine +
            signatureText;

        if (normalizedFollowingPlainText is not null)
        {
            PlainText +=
                Environment.NewLine +
                Environment.NewLine +
                normalizedFollowingPlainText;
        }

        HtmlBody =
            null;

        var signatureTextJson =
            JsonSerializer.Serialize(
                signatureText);

        var followingPlainTextJson =
            JsonSerializer.Serialize(
                normalizedFollowingPlainText);

        var script =
            $$"""
            (() => {
                const editor =
                    document.getElementById("editor");

                if (!editor) {
                    return;
                }

                const signatureText =
                    {{signatureTextJson}};

                const followingPlainText =
                    {{followingPlainTextJson}};

                editor.replaceChildren();

                /*
                 * Erste Zeile:
                 * eigentlicher Nachrichtentext.
                 *
                 * Sie ist zunächst leer. Der Benutzer beginnt
                 * beim ersten Fokus genau an dieser Stelle.
                 */
                const messageLine =
                    document.createElement("div");

                messageLine.appendChild(
                    document.createElement("br"));

                editor.appendChild(
                    messageLine);

                /*
                 * Abstand zwischen neuem Nachrichtentext und
                 * Signatur.
                 */
                const signatureSpacerLine =
                    document.createElement("div");

                signatureSpacerLine.appendChild(
                    document.createElement("br"));

                editor.appendChild(
                    signatureSpacerLine);

                function appendPlainTextLines(text) {
                    const normalizedText =
                        (text ?? "")
                            .replace(/\r\n/g, "\n")
                            .replace(/\r/g, "\n");

                    const lines =
                        normalizedText.split("\n");

                    for (const line of lines) {
                        const lineElement =
                            document.createElement("div");

                        if (line.length === 0) {
                            lineElement.appendChild(
                                document.createElement("br"));
                        }
                        else {
                            lineElement.textContent =
                                line;
                        }

                        editor.appendChild(
                            lineElement);
                    }
                }

                /*
                 * Signatur ausschließlich als Text einsetzen.
                 *
                 * Dadurch kann Signaturinhalt nicht als HTML
                 * oder Script interpretiert werden.
                 */
                appendPlainTextLines(
                    signatureText);

                if (typeof followingPlainText === "string" &&
                    followingPlainText.length > 0) {
                    /*
                     * Zwischen Signatur und vorhandenem
                     * Antwort-/Weiterleitungsblock bleibt
                     * genau eine Leerzeile.
                     */
                    const contentSpacerLine =
                        document.createElement("div");

                    contentSpacerLine.appendChild(
                        document.createElement("br"));

                    editor.appendChild(
                        contentSpacerLine);

                    appendPlainTextLines(
                        followingPlainText);
                }

                /*
                 * Beim allerersten Fokus soll der Benutzer
                 * grundsätzlich in der ersten Nachrichtenzeile
                 * beginnen.
                 *
                 * Danach verhält sich der Editor wieder
                 * vollständig normal.
                 */
                let initialCaretPending =
                    true;

                function placeInitialCaret() {
                    if (!initialCaretPending) {
                        return;
                    }

                    initialCaretPending =
                        false;

                    editor.removeEventListener(
                        "pointerdown",
                        onPointerDown);

                    editor.removeEventListener(
                        "focus",
                        onFocus);

                    window.setTimeout(
                        () => {
                            const selection =
                                window.getSelection();

                            if (!selection) {
                                return;
                            }

                            const range =
                                document.createRange();

                            range.selectNodeContents(
                                messageLine);

                            range.collapse(
                                true);

                            selection.removeAllRanges();

                            selection.addRange(
                                range);
                        },
                        0);
                }

                function onPointerDown() {
                    placeInitialCaret();
                }

                function onFocus() {
                    placeInitialCaret();
                }

                editor.addEventListener(
                    "pointerdown",
                    onPointerDown);

                editor.addEventListener(
                    "focus",
                    onFocus);
            })();
            """;

        await EditorWebView
            .CoreWebView2
            .ExecuteScriptAsync(
                script);
    }
}