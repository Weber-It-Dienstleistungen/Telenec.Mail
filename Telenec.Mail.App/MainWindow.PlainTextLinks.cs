using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    private TextBlock?
        _plainTextBodySource;

    private TextBlock?
        _plainTextBodyWithLinks;

    private DependencyPropertyDescriptor?
        _plainTextBodyTextDescriptor;

    private void MainWindowPlainTextLinks_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -=
            MainWindowPlainTextLinks_OnLoaded;

        InitializePlainTextLinkRendering();
    }

    private void InitializePlainTextLinkRendering()
    {
        if (_plainTextBodySource is not null)
        {
            return;
        }

        var sourceTextBlock =
            FindPlainTextBodyTextBlock(
                PlainTextMailView);

        if (sourceTextBlock is null ||
            sourceTextBlock.Parent
                is not Panel parent)
        {
            return;
        }

        var sourceIndex =
            parent.Children.IndexOf(
                sourceTextBlock);

        if (sourceIndex < 0)
        {
            return;
        }

        var linkTextBlock =
            CreatePlainTextLinkTextBlock(
                sourceTextBlock);

        /*
         * Der neue TextBlock wird exakt an derselben Stelle
         * wie der bisherige Body-Text eingefügt.
         *
         * Der originale, gebundene TextBlock bleibt bestehen
         * und dient weiterhin als zuverlässige Datenquelle.
         */
        parent.Children.Insert(
            sourceIndex + 1,
            linkTextBlock);

        _plainTextBodySource =
            sourceTextBlock;

        _plainTextBodyWithLinks =
            linkTextBlock;

        _plainTextBodyTextDescriptor =
            DependencyPropertyDescriptor
                .FromProperty(
                    TextBlock.TextProperty,
                    typeof(TextBlock));

        _plainTextBodyTextDescriptor?
            .AddValueChanged(
                sourceTextBlock,
                PlainTextBodySource_OnTextChanged);

        Closed +=
            MainWindowPlainTextLinks_OnClosed;

        UpdatePlainTextLinkRendering();
    }

    private static TextBlock?
        FindPlainTextBodyTextBlock(
            DependencyObject root)
    {
        var childCount =
            VisualTreeHelper.GetChildrenCount(
                root);

        for (var index = 0;
             index < childCount;
             index++)
        {
            var child =
                VisualTreeHelper.GetChild(
                    root,
                    index);

            if (child is TextBlock textBlock)
            {
                var binding =
                    BindingOperations.GetBinding(
                        textBlock,
                        TextBlock.TextProperty);

                if (string.Equals(
                        binding?
                            .Path?
                            .Path,
                        "SelectedMessage.Body",
                        StringComparison.Ordinal))
                {
                    return textBlock;
                }
            }

            var nested =
                FindPlainTextBodyTextBlock(
                    child);

            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private TextBlock CreatePlainTextLinkTextBlock(
        TextBlock source)
    {
        return new TextBlock
        {
            Margin =
                source.Margin,

            Padding =
                source.Padding,

            FontFamily =
                source.FontFamily,

            FontSize =
                source.FontSize,

            FontStyle =
                source.FontStyle,

            FontWeight =
                source.FontWeight,

            FontStretch =
                source.FontStretch,

            Foreground =
                source.Foreground,

            LineHeight =
                source.LineHeight,

            LineStackingStrategy =
                source.LineStackingStrategy,

            TextAlignment =
                source.TextAlignment,

            TextWrapping =
                source.TextWrapping,

            TextTrimming =
                source.TextTrimming,

            Visibility =
                Visibility.Collapsed
        };
    }

    private void PlainTextBodySource_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        UpdatePlainTextLinkRendering();
    }

    private void UpdatePlainTextLinkRendering()
    {
        var source =
            _plainTextBodySource;

        var target =
            _plainTextBodyWithLinks;

        if (source is null ||
            target is null)
        {
            return;
        }

        var text =
            source.Text
            ?? string.Empty;

        if (!PlainTextWebAddressRegex.IsMatch(
                text))
        {
            target.Inlines.Clear();

            target.Visibility =
                Visibility.Collapsed;

            source.Visibility =
                Visibility.Visible;

            return;
        }

        target.Inlines.Clear();

        var currentIndex =
            0;

        foreach (Match match in
                 PlainTextWebAddressRegex.Matches(
                     text))
        {
            if (match.Index >
                currentIndex)
            {
                target.Inlines.Add(
                    new Run(
                        text.Substring(
                            currentIndex,
                            match.Index -
                            currentIndex)));
            }

            var matchedText =
                match.Value;

            var webAddress =
                TrimTrailingUrlPunctuation(
                    matchedText);

            if (TryCreateSafeWebUri(
                    webAddress,
                    out var uri))
            {
                var hyperlink =
                    new Hyperlink(
                        new Run(
                            webAddress))
                    {
                        NavigateUri =
                            uri,

                        Cursor =
                            Cursors.Hand,

                        ToolTip =
                            webAddress
                    };

                var linkBrush =
                    TryFindResource(
                        "Brand.Primary")
                    as Brush;

                if (linkBrush is not null)
                {
                    hyperlink.Foreground =
                        linkBrush;
                }

                hyperlink.RequestNavigate +=
                    PlainTextHyperlink_OnRequestNavigate;

                target.Inlines.Add(
                    hyperlink);

                /*
                 * Satzzeichen wie Punkt oder Komma gehören
                 * nicht zum eigentlichen Link und bleiben
                 * deshalb normaler Text.
                 */
                if (webAddress.Length <
                    matchedText.Length)
                {
                    target.Inlines.Add(
                        new Run(
                            matchedText[
                                webAddress.Length..]));
                }
            }
            else
            {
                /*
                 * Sollte eine Regex-Erkennung wider Erwarten
                 * keine gültige HTTP-/HTTPS-URI ergeben,
                 * bleibt sie normaler Plaintext.
                 */
                target.Inlines.Add(
                    new Run(
                        matchedText));
            }

            currentIndex =
                match.Index +
                match.Length;
        }

        if (currentIndex <
            text.Length)
        {
            target.Inlines.Add(
                new Run(
                    text[currentIndex..]));
        }

        source.Visibility =
            Visibility.Collapsed;

        target.Visibility =
            Visibility.Visible;
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
        if (_plainTextBodySource is not null)
        {
            _plainTextBodyTextDescriptor?
                .RemoveValueChanged(
                    _plainTextBodySource,
                    PlainTextBodySource_OnTextChanged);
        }

        _plainTextBodyTextDescriptor =
            null;

        _plainTextBodySource =
            null;

        _plainTextBodyWithLinks =
            null;

        Closed -=
            MainWindowPlainTextLinks_OnClosed;
    }
}