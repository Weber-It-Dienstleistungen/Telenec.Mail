using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Telenec.Mail.App.Controls;

namespace Telenec.Mail.App;

public partial class ComposeWindow
{
    private bool _richTextEditorActivationStarted;
    private bool _richTextEditorWindowClosed;

    private ComposeHtmlEditor?
        _bodyHtmlEditor;

    protected override void OnActivated(
        EventArgs e)
    {
        base.OnActivated(
            e);

        if (_richTextEditorActivationStarted)
        {
            return;
        }

        _richTextEditorActivationStarted =
            true;

        Closed +=
            ComposeWindowRichText_OnClosed;

        _ =
            ActivateRichTextEditorAsync();
    }

    private async Task ActivateRichTextEditorAsync()
    {
        if (BodyTextBox.Parent
            is not Grid bodyGrid)
        {
            return;
        }

        /*
         * Der neue Editor wird zunächst HINTER der alten
         * TextBox eingefügt.
         *
         * Dadurch kann WebView2 vollständig initialisieren,
         * während der bestehende Plaintext-Editor sichtbar
         * und benutzbar bleibt.
         */
        var editor =
            new ComposeHtmlEditor
            {
                IsHitTestVisible =
                    false
            };

        var textBoxIndex =
            bodyGrid.Children.IndexOf(
                BodyTextBox);

        if (textBoxIndex < 0)
        {
            return;
        }

        bodyGrid.Children.Insert(
            textBoxIndex,
            editor);

        try
        {
            await editor
                .EnsureInitializedAsync();

            if (_richTextEditorWindowClosed)
            {
                bodyGrid.Children.Remove(
                    editor);

                return;
            }

            /*
             * Erst unmittelbar vor der Umschaltung wird
             * der aktuellste ViewModel-Inhalt übernommen.
             *
             * Falls der Benutzer während der WebView2-
             * Initialisierung bereits in der alten TextBox
             * geschrieben hat, geht dadurch nichts verloren.
             */
            await editor
                .SetContentAsync(
                    _viewModel.Body,
                    _viewModel.HtmlBody);

            if (_richTextEditorWindowClosed)
            {
                bodyGrid.Children.Remove(
                    editor);

                return;
            }

            editor.ContentChanged +=
                BodyHtmlEditor_OnContentChanged;

            _bodyHtmlEditor =
                editor;

            BodyTextBox.Visibility =
                Visibility.Collapsed;

            editor.IsHitTestVisible =
                true;

            /*
             * Der bisherige Loaded-Workflow setzt bei
             * Antworten und Entwürfen den Fokus zunächst
             * noch auf die alte TextBox.
             *
             * Nachdem der HTML-Editor vollständig aktiv ist,
             * übernehmen wir diesen Fokus kontrolliert.
             */
            if (_viewModel.FocusBodyOnLoad)
            {
                await Dispatcher.Yield(
                    DispatcherPriority.ApplicationIdle);

                if (!_richTextEditorWindowClosed &&
                    ReferenceEquals(
                        _bodyHtmlEditor,
                        editor))
                {
                    await editor
                        .FocusEditorAsync();
                }
            }
        }
        catch
        {
            editor.ContentChanged -=
                BodyHtmlEditor_OnContentChanged;

            if (bodyGrid.Children.Contains(
                    editor))
            {
                bodyGrid.Children.Remove(
                    editor);
            }

            _bodyHtmlEditor =
                null;

            BodyTextBox.Visibility =
                Visibility.Visible;

            if (_richTextEditorWindowClosed)
            {
                return;
            }

            MessageBox.Show(
                "Der formatierte Nachrichteneditor konnte nicht gestartet werden.\n\n" +
                "Telenec Mail verwendet für dieses Fenster automatisch den bisherigen Texteditor. " +
                "Sie können die E-Mail weiterhin normal schreiben, speichern und versenden.",
                "Formatierter Editor nicht verfügbar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BodyHtmlEditor_OnContentChanged(
        object? sender,
        ComposeHtmlEditorContentChangedEventArgs e)
    {
        if (_richTextEditorWindowClosed)
        {
            return;
        }

        /*
         * Der WebView2-Editor ist ab jetzt die Quelle für
         * beide Darstellungen:
         *
         * Body     = sicherer Plaintext-Fallback
         * HtmlBody = optionale Rich-Text-Darstellung
         *
         * Solange keine echte Formatierung vorhanden ist,
         * liefert der Editor HtmlBody = null.
         */
        _viewModel.Body =
            e.PlainText;

        _viewModel.HtmlBody =
            e.HtmlBody;
    }

    private void ComposeWindowRichText_OnClosed(
        object? sender,
        EventArgs e)
    {
        _richTextEditorWindowClosed =
            true;

        Closed -=
            ComposeWindowRichText_OnClosed;

        var editor =
            _bodyHtmlEditor;

        _bodyHtmlEditor =
            null;

        if (editor is null)
        {
            return;
        }

        editor.ContentChanged -=
            BodyHtmlEditor_OnContentChanged;
    }
}