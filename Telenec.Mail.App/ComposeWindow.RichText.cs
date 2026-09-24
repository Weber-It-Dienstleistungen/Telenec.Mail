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

    private Border?
        _richTextToolbar;

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
        /*
         * Eine konfigurierte Signatur muss vor dem ersten
         * Befüllen des Rich-Text-Editors im ViewModel stehen.
         */
        await EnsureSignaturePreparedAsync();

        if (BodyTextBox.Parent
            is not Grid bodyGrid)
        {
            return;
        }

        /*
         * Der neue Editor wird zunächst hinter der alten
         * TextBox eingefügt.
         *
         * So bleibt der bisherige Plaintext-Editor während
         * der WebView2-Initialisierung vollständig erhalten.
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
             * Eine automatisch eingesetzte Signatur benötigt
             * eine eigene initiale DOM-Struktur.
             *
             * Das gilt sowohl für neue Nachrichten als auch
             * für Antworten und Weiterleitungen.
             *
             * Bestehende Entwürfe verwenden weiterhin
             * unverändert den normalen SetContentAsync-Pfad.
             */
            if (_signatureApplied)
            {
                var signatureContent =
                    await editor
                        .SetSignatureContentAsync(
                            _signatureText,
                            _signatureHtml,
                            _signatureFollowingPlainText);

                /*
                 * Besonders wichtig für formatierte
                 * Signaturen:
                 *
                 * Die bereits erneut bereinigte HTML-Version
                 * wird sofort ins ViewModel übernommen.
                 *
                 * Dadurch wird die Formatierung auch dann
                 * korrekt versendet, wenn der Benutzer nach
                 * dem Öffnen des Fensters nichts mehr im
                 * Nachrichtentext verändert.
                 */
                _viewModel.HtmlBody =
                    signatureContent.HtmlBody;

                /*
                 * Die Signatur ist Teil des initialen
                 * Nachrichtenzustands und keine nachträgliche
                 * Benutzeränderung.
                 *
                 * Deshalb aktualisieren wir die Baseline auch
                 * hier. Falls Loaded später nochmals eine
                 * Baseline setzt, ist das ebenfalls korrekt.
                 */
                CaptureComposeBaseline();
            }
            else
            {
                await editor
                    .SetContentAsync(
                        _viewModel.Body,
                        _viewModel.HtmlBody);
            }

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

            ConfigureRichTextLayout(
                bodyGrid,
                editor);

            BodyTextBox.Visibility =
                Visibility.Collapsed;

            editor.IsHitTestVisible =
                true;

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

            RestorePlainTextLayout(
                bodyGrid,
                editor);

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

    private void ConfigureRichTextLayout(
        Grid bodyGrid,
        ComposeHtmlEditor editor)
    {
        /*
         * Bisher besteht der Nachrichtenbereich aus genau
         * einer Zeile.
         *
         * Für Rich Text teilen wir ihn in:
         *
         * Zeile 0 = Formatierungsleiste
         * Zeile 1 = Editor
         */
        bodyGrid.RowDefinitions.Clear();

        bodyGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        bodyGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        Grid.SetRow(
            BodyTextBox,
            1);

        Grid.SetRowSpan(
            BodyTextBox,
            1);

        Grid.SetRow(
            editor,
            1);

        Grid.SetRowSpan(
            editor,
            1);

        var toolbar =
            CreateRichTextToolbar();

        _richTextToolbar =
            toolbar;

        /*
         * Das Busy-Overlay aus ComposeWindow.xaml soll
         * sowohl Editor als auch Toolbar abdecken.
         *
         * Deshalb wird die Toolbar VOR den übrigen
         * Overlay-Elementen in den Visual Tree eingefügt.
         */
        var insertionIndex =
            bodyGrid.Children.Count;

        for (var index = 0;
             index < bodyGrid.Children.Count;
             index++)
        {
            var child =
                bodyGrid.Children[index];

            if (ReferenceEquals(
                    child,
                    BodyTextBox) ||
                ReferenceEquals(
                    child,
                    editor))
            {
                continue;
            }

            insertionIndex =
                index;

            break;
        }

        bodyGrid.Children.Insert(
            insertionIndex,
            toolbar);

        Grid.SetRow(
            toolbar,
            0);

        Grid.SetRowSpan(
            toolbar,
            1);

        foreach (UIElement child
                 in bodyGrid.Children)
        {
            if (ReferenceEquals(
                    child,
                    BodyTextBox) ||
                ReferenceEquals(
                    child,
                    editor) ||
                ReferenceEquals(
                    child,
                    toolbar))
            {
                continue;
            }

            Grid.SetRow(
                child,
                0);

            Grid.SetRowSpan(
                child,
                2);
        }
    }

    private Border CreateRichTextToolbar()
    {
        var panel =
            new WrapPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        panel.Children.Add(
            CreateRichTextCommandButton(
                new TextBlock
                {
                    Text =
                        "B",

                    FontWeight =
                        FontWeights.Bold
                },
                "Fett",
                "bold",
                36));

        panel.Children.Add(
            CreateRichTextCommandButton(
                new TextBlock
                {
                    Text =
                        "I",

                    FontStyle =
                        FontStyles.Italic
                },
                "Kursiv",
                "italic",
                36));

        panel.Children.Add(
            CreateRichTextCommandButton(
                new TextBlock
                {
                    Text =
                        "U",

                    TextDecorations =
                        TextDecorations.Underline
                },
                "Unterstrichen",
                "underline",
                36));

        panel.Children.Add(
            CreateRichTextCommandButton(
                new TextBlock
                {
                    Text =
                        "• Liste"
                },
                "Aufzählung ein-/ausschalten",
                "insertUnorderedList",
                72));

        panel.Children.Add(
            CreateCommandComboBox(
                width:
                    145,

                toolTip:
                    "Schriftart",

                command:
                    "fontName",

                options:
                    new[]
                    {
                        new RichTextCommandOption(
                            "Schriftart",
                            null),

                        new RichTextCommandOption(
                            "Segoe UI",
                            "Segoe UI"),

                        new RichTextCommandOption(
                            "Arial",
                            "Arial"),

                        new RichTextCommandOption(
                            "Calibri",
                            "Calibri"),

                        new RichTextCommandOption(
                            "Verdana",
                            "Verdana"),

                        new RichTextCommandOption(
                            "Georgia",
                            "Georgia"),

                        new RichTextCommandOption(
                            "Times New Roman",
                            "Times New Roman"),

                        new RichTextCommandOption(
                            "Courier New",
                            "Courier New")
                    }));

        panel.Children.Add(
            CreateCommandComboBox(
                width:
                    110,

                toolTip:
                    "Schriftgröße",

                command:
                    "fontSize",

                options:
                    new[]
                    {
                        new RichTextCommandOption(
                            "Größe",
                            null),

                        new RichTextCommandOption(
                            "Klein",
                            "2"),

                        new RichTextCommandOption(
                            "Normal",
                            "3"),

                        new RichTextCommandOption(
                            "Groß",
                            "4"),

                        new RichTextCommandOption(
                            "Sehr groß",
                            "5")
                    }));

        panel.Children.Add(
            CreateCommandComboBox(
                width:
                    110,

                toolTip:
                    "Textfarbe",

                command:
                    "foreColor",

                options:
                    new[]
                    {
                        new RichTextCommandOption(
                            "Farbe",
                            null),

                        new RichTextCommandOption(
                            "Schwarz",
                            "#1F2328"),

                        new RichTextCommandOption(
                            "Rot",
                            "#CF222E"),

                        new RichTextCommandOption(
                            "Blau",
                            "#0969DA"),

                        new RichTextCommandOption(
                            "Grün",
                            "#238636"),

                        new RichTextCommandOption(
                            "Orange",
                            "#B7791F"),

                        new RichTextCommandOption(
                            "Lila",
                            "#8250DF"),

                        new RichTextCommandOption(
                            "Grau",
                            "#66707A")
                    }));

        var toolbar =
            new Border
            {
                Padding =
                    new Thickness(
                        8,
                        6,
                        8,
                        6),

                BorderThickness =
                    new Thickness(
                        0,
                        0,
                        0,
                        1),

                Child =
                    panel
            };

        toolbar.SetResourceReference(
            Border.BackgroundProperty,
            "Surface.Background");

        toolbar.SetResourceReference(
            Border.BorderBrushProperty,
            "Border.Subtle");

        return toolbar;
    }

    private Button CreateRichTextCommandButton(
        UIElement content,
        string toolTip,
        string command,
        double width)
    {
        var button =
            new Button
            {
                Width =
                    width,

                Height =
                    32,

                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0),

                Padding =
                    new Thickness(
                        5,
                        2,
                        5,
                        2),

                Content =
                    content,

                ToolTip =
                    toolTip,

                Cursor =
                    System.Windows.Input.Cursors.Hand,

                Focusable =
                    false,

                IsTabStop =
                    false
            };

        button.SetResourceReference(
            Control.BackgroundProperty,
            "Surface.Card");

        button.SetResourceReference(
            Control.ForegroundProperty,
            "Text.Primary");

        button.SetResourceReference(
            Control.BorderBrushProperty,
            "Border.Default");

        button.Click +=
            async (_, _) =>
            {
                await ExecuteRichTextCommandAsync(
                    command);
            };

        return button;
    }

    private ComboBox CreateCommandComboBox(
        double width,
        string toolTip,
        string command,
        IReadOnlyList<RichTextCommandOption> options)
    {
        var comboBox =
            new ComboBox
            {
                Width =
                    width,

                Height =
                    32,

                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0),

                Padding =
                    new Thickness(
                        7,
                        3,
                        7,
                        3),

                ToolTip =
                    toolTip,

                ItemsSource =
                    options,

                DisplayMemberPath =
                    nameof(
                        RichTextCommandOption.DisplayName),

                SelectedIndex =
                    0,

                VerticalContentAlignment =
                    VerticalAlignment.Center
            };

        comboBox.SetResourceReference(
            Control.BackgroundProperty,
            "Surface.Card");

        comboBox.SetResourceReference(
            Control.ForegroundProperty,
            "Text.Primary");

        comboBox.SetResourceReference(
            Control.BorderBrushProperty,
            "Border.Default");

        comboBox.SelectionChanged +=
            async (_, _) =>
            {
                if (comboBox.SelectedItem
                    is not RichTextCommandOption option ||
                    string.IsNullOrWhiteSpace(
                        option.CommandValue))
                {
                    return;
                }

                var commandValue =
                    option.CommandValue;

                /*
                 * Nach jeder Auswahl springt die ComboBox
                 * auf ihren neutralen Platzhalter zurück.
                 *
                 * Damit kann der Benutzer dieselbe Schrift,
                 * Größe oder Farbe unmittelbar erneut auf
                 * einen anderen Textbereich anwenden.
                 */
                comboBox.SelectedIndex =
                    0;

                await ExecuteRichTextCommandAsync(
                    command,
                    commandValue);
            };

        return comboBox;
    }

    private async Task ExecuteRichTextCommandAsync(
        string command,
        string? value = null)
    {
        var editor =
            _bodyHtmlEditor;

        if (editor is null ||
            _richTextEditorWindowClosed ||
            _viewModel.IsBusy)
        {
            return;
        }

        try
        {
            await editor
                .ExecuteCommandAsync(
                    command,
                    value);
        }
        catch
        {
            MessageBox.Show(
                "Die gewünschte Textformatierung konnte nicht angewendet werden.",
                "Formatierung nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RestorePlainTextLayout(
        Grid bodyGrid,
        ComposeHtmlEditor editor)
    {
        if (_richTextToolbar is not null &&
            bodyGrid.Children.Contains(
                _richTextToolbar))
        {
            bodyGrid.Children.Remove(
                _richTextToolbar);
        }

        _richTextToolbar =
            null;

        if (bodyGrid.Children.Contains(
                editor))
        {
            bodyGrid.Children.Remove(
                editor);
        }

        bodyGrid.RowDefinitions.Clear();

        Grid.SetRow(
            BodyTextBox,
            0);

        Grid.SetRowSpan(
            BodyTextBox,
            1);

        foreach (UIElement child
                 in bodyGrid.Children)
        {
            Grid.SetRow(
                child,
                0);

            Grid.SetRowSpan(
                child,
                1);
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

        _richTextToolbar =
            null;

        if (editor is null)
        {
            return;
        }

        editor.ContentChanged -=
            BodyHtmlEditor_OnContentChanged;
    }

    private sealed record RichTextCommandOption(
        string DisplayName,
        string? CommandValue);
}