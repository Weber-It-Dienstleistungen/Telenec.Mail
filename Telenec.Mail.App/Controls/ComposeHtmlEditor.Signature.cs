using System.Text.Json;

namespace Telenec.Mail.App.Controls;

public partial class ComposeHtmlEditor
{
    public async Task<ComposeHtmlEditorContent>
        SetSignatureContentAsync(
            string signatureText,
            string? signatureHtml,
            string? followingPlainText,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            signatureText);

        await EnsureInitializedAsync(
            cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        var normalizedSignatureHtml =
            string.IsNullOrWhiteSpace(
                signatureHtml)
                ? null
                : signatureHtml;

        var normalizedFollowingPlainText =
            string.IsNullOrEmpty(
                followingPlainText)
                ? null
                : followingPlainText;

        var signatureTextJson =
            JsonSerializer.Serialize(
                signatureText);

        var signatureHtmlJson =
            JsonSerializer.Serialize(
                normalizedSignatureHtml);

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

                const signatureHtml =
                    {{signatureHtmlJson}};

                const followingPlainText =
                    {{followingPlainTextJson}};

                let sanitizedSignatureNodes =
                    null;

                /*
                 * Formatierte Signaturen werden zuerst über
                 * den bereits vorhandenen Editorpfad geladen.
                 *
                 * setContent führt dabei denselben Sanitizer
                 * aus wie beim normalen Mailverfassen.
                 *
                 * Erst die daraus entstandenen, bereinigten
                 * DOM-Knoten werden anschließend übernommen.
                 */
                if (typeof signatureHtml === "string" &&
                    signatureHtml.trim().length > 0) {
                    window.telenecEditor.setContent(
                        signatureHtml,
                        signatureText);

                    sanitizedSignatureNodes =
                        Array.from(
                            editor.childNodes)
                            .map(
                                node =>
                                    node.cloneNode(
                                        true));
                }

                editor.replaceChildren();

                /*
                 * Erste Zeile:
                 * eigentlicher Nachrichtentext.
                 */
                const messageLine =
                    document.createElement("div");

                messageLine.appendChild(
                    document.createElement("br"));

                editor.appendChild(
                    messageLine);

                /*
                 * Abstand zwischen Nachricht und Signatur.
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
                 * Gibt es nach der Bereinigung verwertbares
                 * HTML, übernehmen wir diese Knoten.
                 *
                 * Andernfalls verwenden wir den immer
                 * vorhandenen Klartext-Fallback.
                 */
                if (sanitizedSignatureNodes &&
                    sanitizedSignatureNodes.length > 0) {
                    for (const node of
                         sanitizedSignatureNodes) {
                        editor.appendChild(
                            node);
                    }
                }
                else {
                    appendPlainTextLines(
                        signatureText);
                }

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

        /*
         * Dadurch erhalten wir unmittelbar die bereits
         * bereinigte HTML-Version des vollständigen
         * Editorinhalts.
         */
        return await GetContentAsync(
            cancellationToken);
    }
}