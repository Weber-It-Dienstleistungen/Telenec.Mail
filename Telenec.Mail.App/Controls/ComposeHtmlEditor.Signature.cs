using System.Text.Json;

namespace Telenec.Mail.App.Controls;

public partial class ComposeHtmlEditor
{
    public async Task SetNewMessageSignatureContentAsync(
        string signatureText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            signatureText);

        await EnsureInitializedAsync(
            cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * Die Klartextrepräsentation bleibt mit derjenigen
         * des ComposeMailViewModel identisch:
         *
         * <Nachricht>
         * <Leerzeile>
         * <Signatur>
         *
         * Im DOM verwenden wir dafür jedoch echte Zeilen-
         * Elemente statt führender Newline-Zeichen.
         *
         * Das verhindert, dass Chromium beim Schreiben vor
         * einem führenden Newline eine zusätzliche optische
         * Leerzeile erzeugt.
         */
        PlainText =
            Environment.NewLine +
            Environment.NewLine +
            signatureText;

        HtmlBody =
            null;

        var signatureTextJson =
            JsonSerializer.Serialize(
                signatureText);

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

                editor.replaceChildren();

                /*
                 * Erste Zeile:
                 * eigentlicher Nachrichtentext.
                 *
                 * Sie ist zunächst leer und enthält deshalb
                 * ein <br>. Der Cursor wird beim ersten Fokus
                 * direkt in diese Zeile gesetzt.
                 */
                const messageLine =
                    document.createElement("div");

                messageLine.appendChild(
                    document.createElement("br"));

                editor.appendChild(
                    messageLine);

                /*
                 * Zweite Zeile:
                 * bewusster Abstand zwischen Nachricht und
                 * Signatur.
                 */
                const spacerLine =
                    document.createElement("div");

                spacerLine.appendChild(
                    document.createElement("br"));

                editor.appendChild(
                    spacerLine);

                /*
                 * Die Signatur wird zeilenweise als reiner
                 * Text aufgebaut.
                 *
                 * Dadurch kann kein Signaturinhalt als HTML
                 * oder Script interpretiert werden.
                 */
                const normalizedSignature =
                    (signatureText ?? "")
                        .replace(/\r\n/g, "\n")
                        .replace(/\r/g, "\n");

                const signatureLines =
                    normalizedSignature.split("\n");

                for (const line of signatureLines) {
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

                /*
                 * Beim allerersten Fokus soll der Benutzer
                 * grundsätzlich in der ersten Nachrichtenzeile
                 * beginnen.
                 *
                 * Danach wird die Sonderbehandlung entfernt
                 * und der Editor verhält sich wieder völlig
                 * normal.
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