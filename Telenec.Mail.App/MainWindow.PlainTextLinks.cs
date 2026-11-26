using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    /*
     * Wir erkennen bewusst ausschließlich vollständige
     * HTTP-/HTTPS-Adressen.
     *
     * Andere URI-Schemata werden nicht automatisch anklickbar
     * gemacht.
     */
    private static readonly Regex
        PlainTextWebAddressRegex =
            new(
                @"https?://[^\s<>""']+",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled);

    private RichTextBox? _plainTextReader;

    private void MainWindowPlainTextLinks_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= MainWindowPlainTextLinks_OnLoaded;

        if (_plainTextReader is not null ||
            PlainTextMailView.Content is not StackPanel originalPanel)
        {
            return;
        }

        var reader = new RichTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            IsDocumentEnabled = true,
            IsUndoEnabled = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Margin = originalPanel.Margin,
            MaxWidth = originalPanel.MaxWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontSize = 14,
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontSize = 14
            }
        };

        reader.SetResourceReference(
            Control.ForegroundProperty,
            "Text.Primary");

        reader.Document.SetResourceReference(
            FlowDocument.ForegroundProperty,
            "Text.Primary");

        System.Windows.Automation.AutomationProperties.SetName(
            reader,
            "Nachrichtentext");

        reader.ContextMenu = CreatePlainTextSelectionMenu(reader);

        // Nur die innere Leseansicht scrollt. Damit bleiben auch
        // Auswahl und Tastaturnavigation bei langen Mails nutzbar.
        PlainTextMailView.VerticalScrollBarVisibility =
            ScrollBarVisibility.Disabled;
        PlainTextMailView.HorizontalScrollBarVisibility =
            ScrollBarVisibility.Disabled;

        _plainTextReader = reader;
        PlainTextMailView.Content = reader;

        _viewModel.PropertyChanged += PlainTextReader_OnMessageChanged;
        Closed += MainWindowPlainTextLinks_OnClosed;

        UpdatePlainTextLinkRendering();
    }

    private void PlainTextReader_OnMessageChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(ViewModels.MainViewModel.SelectedMessage))
        {
            UpdatePlainTextLinkRendering();
        }
    }

    private void UpdatePlainTextLinkRendering()
    {
        var reader = _plainTextReader;

        if (reader is null)
        {
            return;
        }

        var document = reader.Document;
        document.Blocks.Clear();

        var message = _viewModel.SelectedMessage;

        if (message is not null)
        {
            AddPlainTextParagraph(document, reader, message.Greeting,
                new Thickness(0, 0, 0, 20));

            AddPlainTextParagraph(document, reader, message.Body,
                new Thickness(0));

            if (message.HasHighlight)
            {
                var highlight = new Paragraph
                {
                    Margin = new Thickness(0, 28, 0, 28),
                    Padding = new Thickness(18),
                    LineHeight = 22
                };

                highlight.SetResourceReference(
                    TextElement.BackgroundProperty,
                    "Brand.PrimaryLight");

                if (!string.IsNullOrEmpty(message.HighlightTitle))
                {
                    var title = new Span { FontWeight = FontWeights.SemiBold };
                    AppendPlainTextInlines(title.Inlines, reader, message.HighlightTitle);
                    highlight.Inlines.Add(title);
                }

                if (!string.IsNullOrEmpty(message.HighlightText))
                {
                    if (highlight.Inlines.Count > 0)
                    {
                        highlight.Inlines.Add(new LineBreak());
                    }

                    var detail = new Span { FontSize = 12 };
                    detail.SetResourceReference(
                        TextElement.ForegroundProperty,
                        "Text.Secondary");
                    AppendPlainTextInlines(detail.Inlines, reader, message.HighlightText);
                    highlight.Inlines.Add(detail);
                }

                document.Blocks.Add(highlight);
            }

            AddPlainTextParagraph(document, reader, message.Closing,
                new Thickness(0));

            AddPlainTextParagraph(document, reader, message.Signature,
                new Thickness(0, 5, 0, 0), true);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph());
        }

        reader.Selection.Select(document.ContentStart, document.ContentStart);
        reader.ScrollToHome();
    }

    private void AddPlainTextParagraph(
        FlowDocument document,
        RichTextBox reader,
        string? text,
        Thickness margin,
        bool bold = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var paragraph = new Paragraph
        {
            Margin = margin,
            LineHeight = 22,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
        };

        AppendPlainTextInlines(paragraph.Inlines, reader, text);
        document.Blocks.Add(paragraph);
    }

    private void AppendPlainTextInlines(
        InlineCollection inlines,
        RichTextBox reader,
        string text)
    {
        var currentIndex = 0;

        foreach (Match match in PlainTextWebAddressRegex.Matches(text))
        {
            if (match.Index > currentIndex)
            {
                AddPlainTextRuns(inlines, text[currentIndex..match.Index]);
            }

            var matchedText = match.Value;
            var webAddress = TrimTrailingUrlPunctuation(matchedText);

            if (TryCreateSafeWebUri(webAddress, out var uri))
            {
                var hyperlink = new Hyperlink(new Run(webAddress))
                {
                    NavigateUri = uri,
                    Cursor = Cursors.Hand,
                    ToolTip = webAddress
                };

                hyperlink.SetResourceReference(
                    TextElement.ForegroundProperty,
                    "Brand.Primary");

                hyperlink.RequestNavigate +=
                    PlainTextHyperlink_OnRequestNavigate;

                hyperlink.ContextMenu =
                    CreatePlainTextHyperlinkContextMenu(reader, webAddress, uri);

                inlines.Add(hyperlink);

                if (webAddress.Length < matchedText.Length)
                {
                    AddPlainTextRuns(inlines, matchedText[webAddress.Length..]);
                }
            }
            else
            {
                AddPlainTextRuns(inlines, matchedText);
            }

            currentIndex = match.Index + match.Length;
        }

        if (currentIndex < text.Length)
        {
            AddPlainTextRuns(inlines, text[currentIndex..]);
        }
    }

    private static void AddPlainTextRuns(InlineCollection inlines, string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                inlines.Add(new LineBreak());
            }

            if (lines[index].Length > 0)
            {
                inlines.Add(new Run(lines[index]));
            }
        }
    }

    private static ContextMenu CreatePlainTextSelectionMenu(RichTextBox reader)
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem
        {
            Header = "Kopieren",
            Command = ApplicationCommands.Copy,
            CommandTarget = reader,
            InputGestureText = "Strg+C"
        });

        menu.Items.Add(new MenuItem
        {
            Header = "Alles auswählen",
            Command = ApplicationCommands.SelectAll,
            CommandTarget = reader,
            InputGestureText = "Strg+A"
        });

        return menu;
    }

    private static bool TryCreateSafeWebUri(
        string value,
        out Uri uri)
    {
        uri =
            null!;

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var candidate))
        {
            return false;
        }

        if (!string.Equals(
                candidate.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                candidate.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri =
            candidate;

        return true;
    }

    private static string TrimTrailingUrlPunctuation(
        string value)
    {
        var length =
            value.Length;

        while (length > 0)
        {
            var lastCharacter =
                value[length - 1];

            switch (lastCharacter)
            {
                case '.':
                case ',':
                case ';':
                case ':':
                case '!':
                    length--;
                    continue;

                case ')':

                    if (CountCharacter(
                            value,
                            length,
                            ')') >
                        CountCharacter(
                            value,
                            length,
                            '('))
                    {
                        length--;
                        continue;
                    }

                    break;

                case ']':

                    if (CountCharacter(
                            value,
                            length,
                            ']') >
                        CountCharacter(
                            value,
                            length,
                            '['))
                    {
                        length--;
                        continue;
                    }

                    break;

                case '}':

                    if (CountCharacter(
                            value,
                            length,
                            '}') >
                        CountCharacter(
                            value,
                            length,
                            '{'))
                    {
                        length--;
                        continue;
                    }

                    break;
            }

            break;
        }

        return value[..length];
    }

    private static int CountCharacter(
        string value,
        int length,
        char character)
    {
        var count =
            0;

        for (var index = 0;
             index < length;
             index++)
        {
            if (value[index] ==
                character)
            {
                count++;
            }
        }

        return count;
    }

    private static ContextMenu CreatePlainTextHyperlinkContextMenu(
        RichTextBox reader,
        string webAddress,
        Uri uri)
    {
        var contextMenu = CreatePlainTextSelectionMenu(reader);
        contextMenu.Items.Add(new Separator());

        var openItem =
            new MenuItem
            {
                Header =
                    "Link öffnen"
            };

        openItem.Click +=
            (_, _) =>
                OpenExternalWebUri(
                    uri.AbsoluteUri);

        var copyItem =
            new MenuItem
            {
                Header =
                    "Link-Adresse kopieren"
            };

        copyItem.Click +=
            (_, _) =>
            {
                try
                {
                    Clipboard.SetText(
                        webAddress);
                }
                catch
                {
                    MessageBox.Show(
                        "Die Link-Adresse konnte nicht in die Zwischenablage kopiert werden.",
                        "Telenec Mail",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            };

        contextMenu.Items.Add(
            openItem);

        contextMenu.Items.Add(
            copyItem);

        return contextMenu;
    }

    private void PlainTextHyperlink_OnRequestNavigate(
        object sender,
        RequestNavigateEventArgs e)
    {
        if (!IsExternalWebUri(
                e.Uri.AbsoluteUri))
        {
            return;
        }

        e.Handled =
            true;

        /*
         * Wir verwenden exakt denselben bereits vorhandenen
         * Browser-Öffnungsweg wie bei HTML-Mails.
         */
        OpenExternalWebUri(
            e.Uri.AbsoluteUri);
    }

    private void MainWindowPlainTextLinks_OnClosed(
        object? sender,
        EventArgs e)
    {
        _viewModel.PropertyChanged -= PlainTextReader_OnMessageChanged;
        _plainTextReader = null;
        Closed -= MainWindowPlainTextLinks_OnClosed;
    }
}