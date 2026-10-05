using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _emptyTrashPlacementHookInitialized;

    private StackPanel?
        _mailListHeaderActionPanel;

    private Border?
        _emptyTrashHeaderContainer;

    internal void InitializeEmptyTrashPlacementHook()
    {
        if (_emptyTrashPlacementHookInitialized)
        {
            return;
        }

        _emptyTrashPlacementHookInitialized =
            true;

        Activated +=
            MainWindow_OnActivatedForEmptyTrashPlacement;

        Closed +=
            MainWindow_OnClosedForEmptyTrashPlacement;

        ScheduleEmptyTrashRelocation();
    }

    private void
        MainWindow_OnActivatedForEmptyTrashPlacement(
            object? sender,
            EventArgs e)
    {
        /*
         * MainWindow.PermanentDelete.cs erzeugt den Button
         * innerhalb von OnActivated().
         *
         * Das Activated-Ereignis selbst wird etwas früher
         * ausgelöst. Deshalb verschieben wir unsere Arbeit auf
         * den nächsten Dispatcher-Durchlauf.
         */
        ScheduleEmptyTrashRelocation();
    }

    private void ScheduleEmptyTrashRelocation()
    {
        _ =
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    TryRelocateEmptyTrashButton));
    }

    private void TryRelocateEmptyTrashButton()
    {
        if (_emptyTrashButton is null)
        {
            /*
             * Der Permanent-Delete-Unterbau ist eventuell noch
             * nicht initialisiert.
             *
             * Beim nächsten Activated-Ereignis versuchen wir
             * es erneut.
             */
            return;
        }

        if (_emptyTrashHeaderContainer is not null &&
            ReferenceEquals(
                _emptyTrashButton.Parent,
                _emptyTrashHeaderContainer))
        {
            return;
        }

        if (_mailListHeaderActionPanel is null)
        {
            if (RefreshButton.Parent
                is not Grid messageListHeaderGrid)
            {
                return;
            }

            messageListHeaderGrid.Children.Remove(
                RefreshButton);

            var actionPanel =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    HorizontalAlignment =
                        HorizontalAlignment.Right,

                    VerticalAlignment =
                        VerticalAlignment.Top
                };

            Grid.SetColumn(
                actionPanel,
                1);

            _mailListHeaderActionPanel =
                actionPanel;

            messageListHeaderGrid.Children.Add(
                actionPanel);
        }

        /*
         * Der Button sitzt bisher in der Aktionsgruppe der
         * Mailvorschau.
         *
         * Genau dort soll "Papierkorb leeren" nicht mehr
         * erscheinen.
         */
        if (_emptyTrashButton.Parent
            is Panel previousParent)
        {
            previousParent.Children.Remove(
                _emptyTrashButton);
        }
        else if (_emptyTrashButton.Parent
                 is Border previousBorder)
        {
            previousBorder.Child =
                null;
        }

        /*
         * Die bisherige Darstellung wird beibehalten.
         *
         * Im Kopf der Nachrichtenliste steht der Button links
         * neben "Aktualisieren".
         */
        _emptyTrashButton.Margin =
            new Thickness(
                0,
                0,
                8,
                0);

        _emptyTrashButton.VerticalAlignment =
            VerticalAlignment.Center;

        var container =
            new Border
            {
                VerticalAlignment =
                    VerticalAlignment.Center,

                Child =
                    _emptyTrashButton
            };

        /*
         * Der gesamte Container existiert nur im Papierkorb.
         *
         * Dadurch entsteht in anderen Ordnern keinerlei
         * zusätzlicher Leerraum neben dem Aktualisieren-Button.
         */
        container.SetBinding(
            VisibilityProperty,
            new Binding(
                nameof(
                    ViewModels.MainViewModel
                        .IsTrashFolderSelected))
            {
                Converter =
                    new BooleanToVisibilityConverter()
            });

        /*
         * Ein leerer Papierkorb darf nicht "geleert" werden.
         *
         * Wir deaktivieren bewusst den übergeordneten
         * Container und nicht direkt den Button.
         *
         * MainWindow.PermanentDelete.cs setzt während/nach der
         * Serveroperation weiterhin Button.IsEnabled.
         * Der übergeordnete IsEnabled-Zustand bleibt davon
         * unabhängig erhalten.
         */
        container.SetBinding(
            IsEnabledProperty,
            new Binding(
                "SelectedFolder.MessageCount")
            {
                Converter =
                    PositiveIntegerToBooleanConverter.Instance,

                FallbackValue =
                    false,

                TargetNullValue =
                    false
            });

        _emptyTrashHeaderContainer =
            container;

        _mailListHeaderActionPanel!
            .Children.Add(
                container);

        /*
         * Aktualisieren bleibt ganz rechts.
         */
        if (!ReferenceEquals(
                RefreshButton.Parent,
                _mailListHeaderActionPanel))
        {
            if (RefreshButton.Parent
                is Panel currentParent)
            {
                currentParent.Children.Remove(
                    RefreshButton);
            }

            _mailListHeaderActionPanel
                .Children.Add(
                    RefreshButton);
        }
    }

    private void MainWindow_OnClosedForEmptyTrashPlacement(
        object? sender,
        EventArgs e)
    {
        Activated -=
            MainWindow_OnActivatedForEmptyTrashPlacement;

        Closed -=
            MainWindow_OnClosedForEmptyTrashPlacement;
    }
}

internal static class
    EmptyTrashPlacementBootstrapper
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnLoaded),
            handledEventsToo:
                true);
    }

    private static void MainWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        window
            .InitializeEmptyTrashPlacementHook();
    }
}

internal sealed class
    PositiveIntegerToBooleanConverter :
        IValueConverter
{
    public static
        PositiveIntegerToBooleanConverter Instance
    { get; } =
        new();

    private PositiveIntegerToBooleanConverter()
    {
    }

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value switch
        {
            int integerValue =>
                integerValue > 0,

            long longValue =>
                longValue > 0,

            _ =>
                false
        };
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}