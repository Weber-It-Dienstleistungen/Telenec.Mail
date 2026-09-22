using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _printingInitialized;

    private Button?
        _printSelectedMessageButton;

    private async void MainWindowPrinting_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -=
            MainWindowPrinting_OnLoaded;

        if (_printingInitialized)
        {
            return;
        }

        _printingInitialized =
            true;

        PreviewKeyDown +=
            MainWindowPrinting_OnPreviewKeyDown;

        Closed +=
            MainWindowPrinting_OnClosed;

        /*
         * Loaded bedeutet bei WPF noch nicht zwingend,
         * dass alle ergänzenden Partial-Class-Workflows
         * ihren sichtbaren UI-Zustand bereits vollständig
         * aufgebaut haben.
         *
         * Wir warten deshalb bewusst bis ApplicationIdle.
         * Danach existiert die sichtbare Nachrichten-
         * Aktionsleiste sicher.
         */
        await Dispatcher.Yield(
            DispatcherPriority.ApplicationIdle);

        AddPrintButtonToMessageActions();
    }

    private void AddPrintButtonToMessageActions()
    {
        if (_printSelectedMessageButton is not null)
        {
            return;
        }

        /*
         * Die Draft-Erweiterung besitzt bereits eine
         * robuste Visual-Tree-Suche für die vorhandenen
         * Nachrichtenaktionen.
         *
         * Wir suchen deshalb direkt den bekannten
         * "Weiterleiten"-Button und verwenden dessen
         * StackPanel als Aktionsleiste.
         *
         * Das ist deutlich stabiler als Annahmen über
         * Grid-Zeilen oder konkrete Parent-Strukturen.
         */
        var forwardButton =
            _forwardMessageActionButton;

        forwardButton ??=
            FindVisualChildren<Button>(
                    this)
                .FirstOrDefault(
                    button =>
                        string.Equals(
                            GetButtonText(
                                button),
                            "Weiterleiten",
                            StringComparison.Ordinal));

        if (forwardButton?.Parent
            is not StackPanel actionPanel)
        {
            return;
        }

        var button =
            new Button
            {
                Width =
                    105,

                Height =
                    36,

                Margin =
                    new Thickness(
                        10,
                        0,
                        0,
                        0),

                Cursor =
                    Cursors.Hand,

                ToolTip =
                    "Diese Nachricht drucken (Strg+P)"
            };

        button.SetResourceReference(
            Control.BackgroundProperty,
            "Surface.Background");

        button.SetResourceReference(
            Control.ForegroundProperty,
            "Text.Primary");

        button.SetResourceReference(
            Control.BorderBrushProperty,
            "Border.Default");

        button.BorderThickness =
            new Thickness(1);

        var content =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "\uE749",

                FontFamily =
                    new System.Windows.Media.FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    14,

                Margin =
                    new Thickness(
                        0,
                        0,
                        7,
                        0),

                VerticalAlignment =
                    VerticalAlignment.Center
            });

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "Drucken",

                FontWeight =
                    FontWeights.SemiBold,

                VerticalAlignment =
                    VerticalAlignment.Center
            });

        button.Content =
            content;

        button.Click +=
            PrintSelectedMessageButton_OnClick;

        actionPanel.Children.Add(
            button);

        _printSelectedMessageButton =
            button;
    }

    private void PrintSelectedMessageButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        OpenSelectedMessagePrintWindow();
    }

    private void MainWindowPrinting_OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.P ||
            !Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control))
        {
            return;
        }

        if (e.IsRepeat)
        {
            e.Handled =
                true;

            return;
        }

        e.Handled =
            true;

        OpenSelectedMessagePrintWindow();
    }

    private void OpenSelectedMessagePrintWindow()
    {
        if (_viewModel.IsLoading)
        {
            return;
        }

        var message =
            _viewModel.SelectedMessage;

        if (message is null)
        {
            return;
        }

        try
        {
            var printWindow =
                new MailPrintWindow(
                    message,
                    _allowExternalImagesForCurrentMessage)
                {
                    Owner =
                        this
                };

            printWindow.ShowDialog();
        }
        catch
        {
            MessageBox.Show(
                "Die Druckansicht konnte nicht geöffnet werden.",
                "Drucken nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MainWindowPrinting_OnClosed(
        object? sender,
        EventArgs e)
    {
        Closed -=
            MainWindowPrinting_OnClosed;

        PreviewKeyDown -=
            MainWindowPrinting_OnPreviewKeyDown;

        var button =
            _printSelectedMessageButton;

        _printSelectedMessageButton =
            null;

        if (button is null)
        {
            return;
        }

        button.Click -=
            PrintSelectedMessageButton_OnClick;

        if (button.Parent
            is Panel parentPanel)
        {
            parentPanel.Children.Remove(
                button);
        }
    }
}