using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private static readonly bool
        MessageSortingClassHandlerRegistered =
            RegisterMessageSortingClassHandler();

    private readonly Dictionary<
        MailSortField,
        Button>
        _messageSortButtons =
            new();

    private bool
        _messageSortingUiInitialized;

    private bool
        _messageSortChangeRunning;

    private static bool
        RegisterMessageSortingClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnMessageSortingLoaded));

        return true;
    }

    private static void MainWindow_OnMessageSortingLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MainWindow window ||
            !ReferenceEquals(
                e.OriginalSource,
                window))
        {
            return;
        }

        /*
         * Die bestehende Suche baut ihre Oberfläche ebenfalls
         * im Loaded-Durchlauf auf.
         *
         * Wir warten deshalb bis der aktuelle Loaded-Event
         * vollständig abgearbeitet wurde. Danach können wir
         * den fertigen Suchbereich gefahrlos um die
         * Sortierleiste ergänzen.
         */
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(
                window.InitializeMessageSortingUi));
    }

    private void InitializeMessageSortingUi()
    {
        if (_messageSortingUiInitialized)
        {
            return;
        }

        if (!_searchUiInitialized ||
            _searchHostBorder is null ||
            MessageListBox.Parent
                is not Grid messageColumnGrid)
        {
            return;
        }

        if (_searchHostBorder.Parent
            is not Grid)
        {
            return;
        }

        /*
         * Die vorhandene Suchleiste liegt in Zeile 1.
         *
         * Statt eine neue Grid-Zeile einzufügen – was mit
         * Paging und Suchergebnissen kollidieren könnte –
         * fassen wir Suche und Sortierung in derselben
         * bestehenden Zeile vertikal zusammen.
         */
        messageColumnGrid.Children.Remove(
            _searchHostBorder);

        var searchAndSortHost =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        18,
                        0,
                        18,
                        14)
            };

        Grid.SetRow(
            searchAndSortHost,
            1);

        _searchHostBorder.Margin =
            new Thickness(
                0,
                0,
                0,
                8);

        searchAndSortHost.Children.Add(
            _searchHostBorder);

        var sortBar =
            CreateMessageSortBar();

        /*
         * Während einer aktiven Suche wird die normale
         * Nachrichtenliste bereits ausgeblendet.
         *
         * Die Sortierleiste folgt exakt dieser Sichtbarkeit,
         * während die Suchleiste selbst weiterhin verfügbar
         * bleibt.
         */
        sortBar.SetBinding(
            VisibilityProperty,
            new Binding(
                nameof(
                    UIElement.Visibility))
            {
                Source =
                    MessageListBox
            });

        searchAndSortHost.Children.Add(
            sortBar);

        messageColumnGrid.Children.Add(
            searchAndSortHost);

        _messageSortingUiInitialized =
            true;

        UpdateMessageSortButtons();
    }

    private FrameworkElement CreateMessageSortBar()
    {
        var grid =
            new Grid
            {
                Height =
                    32
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var label =
            new TextBlock
            {
                Text =
                    "Sortieren:",

                FontSize =
                    11,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        label.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Muted");

        Grid.SetColumn(
            label,
            0);

        grid.Children.Add(
            label);

        var buttonPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                HorizontalAlignment =
                    HorizontalAlignment.Right,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        Grid.SetColumn(
            buttonPanel,
            1);

        buttonPanel.Children.Add(
            CreateMessageSortButton(
                MailSortField.Sender,
                "Von",
                "Nach Absender sortieren"));

        buttonPanel.Children.Add(
            CreateMessageSortButton(
                MailSortField.Subject,
                "Betreff",
                "Nach Betreff sortieren"));

        buttonPanel.Children.Add(
            CreateMessageSortButton(
                MailSortField.Date,
                "Datum",
                "Nach Datum sortieren"));

        grid.Children.Add(
            buttonPanel);

        return grid;
    }

    private Button CreateMessageSortButton(
        MailSortField field,
        string text,
        string toolTip)
    {
        var button =
            new Button
            {
                MinWidth =
                    68,

                Height =
                    28,

                Margin =
                    new Thickness(
                        5,
                        0,
                        0,
                        0),

                Padding =
                    new Thickness(
                        9,
                        0,
                        9,
                        0),

                BorderThickness =
                    new Thickness(
                        1),

                FontSize =
                    11,

                Cursor =
                    Cursors.Hand,

                Content =
                    text,

                ToolTip =
                    toolTip,

                Tag =
                    field
            };

        button.SetResourceReference(
            Control.BackgroundProperty,
            "Surface.Card");

        button.SetResourceReference(
            Control.BorderBrushProperty,
            "Border.Default");

        button.SetResourceReference(
            Control.ForegroundProperty,
            "Text.Secondary");

        button.Click +=
            MessageSortButton_OnClick;

        _messageSortButtons[
            field] =
                button;

        return button;
    }

    private async void MessageSortButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_messageSortChangeRunning ||
            sender is not Button button ||
            button.Tag
                is not MailSortField field)
        {
            return;
        }

        _messageSortChangeRunning =
            true;

        /*
         * Der Zustand wird vor dem Reload umgeschaltet.
         *
         * Sowohl die normale Nachrichtendatenquelle als auch
         * die UID-/Flag-Synchronisation lesen anschließend
         * exakt denselben Sortierzustand.
         */
        MailSortState.Toggle(
            field);

        UpdateMessageSortButtons();

        try
        {
            await _viewModel
                .ReloadAsync();
        }
        finally
        {
            _messageSortChangeRunning =
                false;

            UpdateMessageSortButtons();
        }
    }

    private void UpdateMessageSortButtons()
    {
        var current =
            MailSortState.Current;

        foreach (var pair in
                 _messageSortButtons)
        {
            var field =
                pair.Key;

            var button =
                pair.Value;

            var isActive =
                field ==
                current.Field;

            var label =
                field switch
                {
                    MailSortField.Sender =>
                        "Von",

                    MailSortField.Subject =>
                        "Betreff",

                    MailSortField.Date =>
                        "Datum",

                    _ =>
                        string.Empty
                };

            button.Content =
                isActive
                    ? label +
                      (current.Descending
                          ? " ↓"
                          : " ↑")
                    : label;

            button.FontWeight =
                isActive
                    ? FontWeights.SemiBold
                    : FontWeights.Normal;

            button.IsEnabled =
                !_messageSortChangeRunning;

            button.SetResourceReference(
                Control.BackgroundProperty,
                isActive
                    ? "Brand.PrimaryLight"
                    : "Surface.Card");

            button.SetResourceReference(
                Control.BorderBrushProperty,
                isActive
                    ? "Brand.Primary"
                    : "Border.Default");

            button.SetResourceReference(
                Control.ForegroundProperty,
                isActive
                    ? "Brand.Primary"
                    : "Text.Secondary");
        }
    }
}