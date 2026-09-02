using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _messagePagingActionInitialized;

    /*
     * OnContentRendered wird bereits vom Draft-Editing benutzt.
     * OnActivated wird bereits vom Permanent-Delete-Workflow
     * benutzt.
     *
     * Deshalb verwenden wir hier bewusst SourceInitialized nur,
     * um uns einmalig an das Loaded-Ereignis der vorhandenen
     * Nachrichtenliste zu hängen.
     *
     * Die eigentliche Layout-Erweiterung erfolgt erst bei Loaded,
     * wenn der Visual-/Logical-Tree sicher vollständig aufgebaut
     * ist.
     */
    protected override void OnSourceInitialized(
        EventArgs e)
    {
        base.OnSourceInitialized(
            e);

        MessageListBox.Loaded +=
            MessageListBox_OnLoadedForPaging;
    }

    private void MessageListBox_OnLoadedForPaging(
        object sender,
        RoutedEventArgs e)
    {
        InitializeMessagePagingAction();

        if (_messagePagingActionInitialized)
        {
            MessageListBox.Loaded -=
                MessageListBox_OnLoadedForPaging;
        }
    }

    private void InitializeMessagePagingAction()
    {
        if (_messagePagingActionInitialized)
        {
            return;
        }

        if (MessageListBox.Parent
            is not Grid messageColumnGrid)
        {
            return;
        }

        /*
         * Das vorhandene Nachrichten-Grid besitzt aktuell:
         *
         * Row 0 = Ordnerkopf
         * Row 1 = Suchbereich
         * Row 2 = Nachrichtenliste
         *
         * Wir ergänzen lediglich:
         *
         * Row 3 = "Weitere Nachrichten laden"
         *
         * Die bestehende ListBox selbst wird nicht ersetzt oder
         * neu aufgebaut.
         */
        if (messageColumnGrid.RowDefinitions.Count < 4)
        {
            messageColumnGrid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });
        }

        var loadMoreContainer =
            CreateLoadMoreMessagesContainer();

        Grid.SetRow(
            loadMoreContainer,
            3);

        messageColumnGrid.Children.Add(
            loadMoreContainer);

        _messagePagingActionInitialized =
            true;
    }

    private Border CreateLoadMoreMessagesContainer()
    {
        var container =
            new Border
            {
                Margin =
                    new Thickness(
                        18,
                        8,
                        18,
                        14),

                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };

        /*
         * Sobald der komplette Ordner sichtbar ist, verschwindet
         * nicht nur der Button, sondern die gesamte zusätzliche
         * Grid-Zeile nimmt durch Collapsed keinen sichtbaren
         * Platz mehr ein.
         */
        container.SetBinding(
            VisibilityProperty,
            new Binding(
                nameof(
                    MainViewModel.HasMoreMessages))
            {
                Converter =
                    new BooleanToVisibilityConverter()
            });

        var button =
            new Button
            {
                Height =
                    38,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                Content =
                    "Weitere 20 Nachrichten laden",

                ToolTip =
                    "Ältere Nachrichten aus diesem Ordner laden"
            };

        /*
         * Wir verwenden bewusst die bereits vorhandene
         * Telenec-Button-Gestaltung statt eine neue lokale
         * Darstellung einzuführen.
         */
        button.SetResourceReference(
            FrameworkElement.StyleProperty,
            "Button.Primary");

        /*
         * CanLoadMoreMessages berücksichtigt bereits:
         *
         * - weitere Nachrichten vorhanden
         * - normaler Ordner-Ladevorgang
         * - laufendes Paging
         * - Synchronisierung
         * - Move/Delete/Restore
         *
         * Dadurch kann derselbe Button nicht mehrfach parallel
         * ausgelöst werden.
         */
        button.SetBinding(
            IsEnabledProperty,
            new Binding(
                nameof(
                    MainViewModel.CanLoadMoreMessages)));

        button.Click +=
            LoadMoreMessagesButton_OnClick;

        container.Child =
            button;

        return container;
    }

    private async void LoadMoreMessagesButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!_viewModel.CanLoadMoreMessages)
        {
            return;
        }

        /*
         * Die UI kennt keine Paging-Details.
         *
         * Offset-Prüfung, UIDVALIDITY, Präfixkontrolle,
         * Parallelität und das tatsächliche Laden der nächsten
         * Seite bleiben vollständig im MainViewModel.
         */
        await _viewModel
            .LoadMoreMessagesAsync();
    }
}