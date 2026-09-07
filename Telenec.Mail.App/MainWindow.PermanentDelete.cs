using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string MessageActionTag =
        "MessageAction";

    private const string PermanentDeleteActionTag =
        "PermanentDeleteAction";

    private const string PermanentDeleteSeparatorTag =
        "PermanentDeleteSeparator";

    private Button?
        _permanentDeleteSelectedMessageButton;

    private Button?
        _emptyTrashButton;

    private bool
        _permanentDeleteVisibleActionInitialized;

    private bool
        _isEmptyTrashOperationRunning;

    /*
     * Der bestehende MainWindow-Keyhandler bleibt absichtlich
     * unangetastet.
     *
     * Über den Window-Override fangen wir ausschließlich die
     * Entf-Taste im Papierkorb vorher ab.
     *
     * Während "Papierkorb leeren" läuft, wird zusätzlich F5
     * blockiert.
     *
     * Dadurch kann während der irreversiblen Serveroperation
     * keine manuelle Synchronisierung über die Tastatur
     * gestartet werden.
     */
    protected override void OnPreviewKeyDown(
        KeyEventArgs e)
    {
        if (_isEmptyTrashOperationRunning &&
            (e.Key == Key.F5 ||
             e.Key == Key.Delete))
        {
            e.Handled =
                true;

            base.OnPreviewKeyDown(
                e);

            return;
        }

        if (e.Key == Key.Delete &&
            !e.IsRepeat &&
            Keyboard.FocusedElement is not TextBoxBase &&
            !_viewModel.IsLoading &&
            _viewModel.IsTrashFolderSelected)
        {
            var messages =
                GetSelectedMessages();

            if (messages.Count > 0)
            {
                /*
                 * Wichtig:
                 *
                 * Der bestehende Keyhandler darf diese
                 * Entf-Taste nicht zusätzlich verarbeiten.
                 */
                e.Handled =
                    true;

                _ =
                    DeleteMessagesPermanentlyFromUiAsync(
                        messages);

                base.OnPreviewKeyDown(
                    e);

                return;
            }
        }

        base.OnPreviewKeyDown(
            e);
    }

    /*
     * MainWindow.DraftEditing.cs verwendet bereits
     * OnContentRendered.
     *
     * Deshalb verwenden wir für diese unabhängige
     * UI-Erweiterung bewusst OnActivated.
     *
     * Der Guard stellt sicher, dass die Erweiterung trotz
     * späterer erneuter Aktivierungen des Fensters nur einmal
     * installiert wird.
     */
    protected override void OnActivated(
        EventArgs e)
    {
        base.OnActivated(
            e);

        InitializePermanentDeleteVisibleAction();
    }

    /*
     * Der vorhandene Löschen-/Wiederherstellen-Button ist
     * bereits Bestandteil des stabilen MainWindow-XAML.
     *
     * Für die Permanent-Delete-Funktionen setzen wir ihn
     * lediglich in eine kleine horizontale Aktionsgruppe.
     *
     * Im Papierkorb enthält diese Gruppe:
     *
     * - Wiederherstellen
     * - ausgewählte Nachricht endgültig löschen
     * - Papierkorb vollständig leeren
     *
     * Das große stabile MainWindow-XAML bleibt dadurch
     * unangetastet.
     */
    private void InitializePermanentDeleteVisibleAction()
    {
        if (_permanentDeleteVisibleActionInitialized)
        {
            return;
        }

        if (DeleteSelectedMessageButton.Parent
            is not Grid headerGrid)
        {
            /*
             * Falls das Fenster wider Erwarten noch nicht weit
             * genug aufgebaut ist, bleibt der Guard auf false.
             *
             * Beim nächsten Activated-Ereignis wird dann erneut
             * versucht zu initialisieren.
             */
            return;
        }

        _permanentDeleteVisibleActionInitialized =
            true;

        /*
         * Der bestehende Button lag bisher direkt in
         * Grid.Row 0 / Grid.Column 1.
         *
         * Wir ersetzen nur seine direkte Positionierung durch
         * ein StackPanel an exakt derselben Stelle.
         */
        headerGrid.Children.Remove(
            DeleteSelectedMessageButton);

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

        Grid.SetRow(
            actionPanel,
            0);

        Grid.SetColumn(
            actionPanel,
            1);

        actionPanel.Children.Add(
            DeleteSelectedMessageButton);

        var permanentDeleteButton =
            CreatePermanentDeleteSelectedMessageButton();

        _permanentDeleteSelectedMessageButton =
            permanentDeleteButton;

        actionPanel.Children.Add(
            permanentDeleteButton);

        var emptyTrashButton =
            CreateEmptyTrashButton();

        _emptyTrashButton =
            emptyTrashButton;

        actionPanel.Children.Add(
            emptyTrashButton);

        headerGrid.Children.Add(
            actionPanel);
    }

    private Button
        CreatePermanentDeleteSelectedMessageButton()
    {
        var button =
            new Button
            {
                Width =
                    36,

                Height =
                    36,

                Margin =
                    new Thickness(
                        6,
                        0,
                        0,
                        0),

                Padding =
                    new Thickness(0),

                HorizontalAlignment =
                    HorizontalAlignment.Right,

                VerticalAlignment =
                    VerticalAlignment.Top,

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                Cursor =
                    Cursors.Hand,

                ToolTip =
                    "Ausgewählte Nachricht endgültig löschen"
            };

        /*
         * Der Button ist ausschließlich im Papierkorb
         * sichtbar.
         */
        button.SetBinding(
            VisibilityProperty,
            new Binding(
                nameof(
                    MainViewModel.IsTrashFolderSelected))
            {
                Converter =
                    new BooleanToVisibilityConverter()
            });

        /*
         * Der vorhandene Wiederherstellen-/Löschen-Button
         * besitzt bereits die korrekten Enable-Regeln:
         *
         * - keine Nachricht ausgewählt -> deaktiviert
         * - Ordner wird geladen -> deaktiviert
         *
         * Der Permanent-Delete-Button übernimmt exakt diesen
         * Zustand.
         */
        button.SetBinding(
            IsEnabledProperty,
            new Binding(
                nameof(Button.IsEnabled))
            {
                Source =
                    DeleteSelectedMessageButton
            });

        var glyph =
            new TextBlock
            {
                Text =
                    "\uE74D",

                FontFamily =
                    new FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    18,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        /*
         * Irreversible Aktion bewusst farblich vom
         * Wiederherstellen-Button unterscheiden.
         */
        glyph.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Status.Error");

        button.Content =
            glyph;

        button.Click +=
            PermanentDeleteSelectedMessageButton_OnClick;

        return button;
    }

    private Button CreateEmptyTrashButton()
    {
        var button =
            new Button
            {
                Height =
                    36,

                Margin =
                    new Thickness(
                        8,
                        0,
                        0,
                        0),

                Padding =
                    new Thickness(
                        10,
                        0,
                        10,
                        0),

                HorizontalAlignment =
                    HorizontalAlignment.Right,

                VerticalAlignment =
                    VerticalAlignment.Top,

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                Cursor =
                    Cursors.Hand,

                ToolTip =
                    "Papierkorb vollständig leeren"
            };

        /*
         * Diese Aktion darf ausschließlich erscheinen, wenn
         * tatsächlich der Papierkorb ausgewählt ist.
         *
         * Der Service prüft später zusätzlich erneut den
         * echten serverseitigen Trash-Ordner.
         */
        button.SetBinding(
            VisibilityProperty,
            new Binding(
                nameof(
                    MainViewModel.IsTrashFolderSelected))
            {
                Converter =
                    new BooleanToVisibilityConverter()
            });

        var contentPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var glyph =
            new TextBlock
            {
                Text =
                    "\uE74D",

                FontFamily =
                    new FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    16,

                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0),

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        glyph.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Status.Error");

        var text =
            new TextBlock
            {
                Text =
                    "Papierkorb leeren",

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        text.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Status.Error");

        contentPanel.Children.Add(
            glyph);

        contentPanel.Children.Add(
            text);

        button.Content =
            contentPanel;

        button.Click +=
            EmptyTrashButton_OnClick;

        return button;
    }

    private async void
        PermanentDeleteSelectedMessageButton_OnClick(
            object sender,
            RoutedEventArgs e)
    {
        if (_isEmptyTrashOperationRunning ||
            _viewModel.IsLoading ||
            !_viewModel.IsTrashFolderSelected)
        {
            return;
        }

        var messages =
            GetSelectedMessages();

        if (messages.Count == 0)
        {
            return;
        }

        /*
         * Auch der sichtbare Button führt ausschließlich in
         * denselben bereits getesteten
         * Permanent-Delete-Workflow.
         *
         * Es gibt keinen zweiten Mechanismus für ausgewählte
         * Nachrichten.
         */
        await DeleteMessagesPermanentlyFromUiAsync(
            messages);
    }

    private async void EmptyTrashButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await EmptyTrashFromUiAsync();
    }

    /*
     * Das Nachrichten-Kontextmenü lebt innerhalb des
     * ItemTemplates der Nachrichtenliste.
     *
     * Über das geroutete ContextMenuOpening-Ereignis können wir
     * es erweitern, ohne das große stabile MainWindow-XAML oder
     * dessen bestehenden Code-behind-Workflow anzufassen.
     */
    protected override void OnContextMenuOpening(
        ContextMenuEventArgs e)
    {
        var contextMenu =
            FindMessageContextMenu(
                e.OriginalSource as DependencyObject);

        if (contextMenu is not null)
        {
            UpdatePermanentDeleteContextMenu(
                contextMenu);
        }

        base.OnContextMenuOpening(
            e);
    }

    private ContextMenu? FindMessageContextMenu(
        DependencyObject? source)
    {
        var current =
            source;

        while (current is not null &&
               !ReferenceEquals(
                   current,
                   this))
        {
            if (current is FrameworkElement element &&
                element.ContextMenu is ContextMenu contextMenu &&
                IsMessageContextMenu(
                    contextMenu))
            {
                return contextMenu;
            }

            current =
                GetParent(
                    current);
        }

        return null;
    }

    private static DependencyObject? GetParent(
        DependencyObject element)
    {
        /*
         * Manche Elemente im DataTemplate liegen im visuellen,
         * andere nur im logischen Baum.
         */
        if (element is Visual ||
            element is Visual3D)
        {
            var visualParent =
                VisualTreeHelper.GetParent(
                    element);

            if (visualParent is not null)
            {
                return visualParent;
            }
        }

        return LogicalTreeHelper.GetParent(
            element);
    }

    private static bool IsMessageContextMenu(
        ContextMenu contextMenu)
    {
        return contextMenu
            .Items
            .OfType<MenuItem>()
            .Any(
                item =>
                    string.Equals(
                        item.Tag?.ToString(),
                        MessageActionTag,
                        StringComparison.Ordinal));
    }

    private void UpdatePermanentDeleteContextMenu(
        ContextMenu contextMenu)
    {
        var existingPermanentDeleteItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag?.ToString(),
                            PermanentDeleteActionTag,
                            StringComparison.Ordinal));

        var existingPermanentDeleteSeparator =
            contextMenu
                .Items
                .OfType<Separator>()
                .FirstOrDefault(
                    separator =>
                        string.Equals(
                            separator.Tag?.ToString(),
                            PermanentDeleteSeparatorTag,
                            StringComparison.Ordinal));

        /*
         * Außerhalb des Papierkorbs gibt es weiterhin nur den
         * bisherigen normalen Löschworkflow.
         */
        if (!_viewModel.IsTrashFolderSelected)
        {
            if (existingPermanentDeleteItem is not null)
            {
                contextMenu.Items.Remove(
                    existingPermanentDeleteItem);
            }

            if (existingPermanentDeleteSeparator is not null)
            {
                contextMenu.Items.Remove(
                    existingPermanentDeleteSeparator);
            }

            return;
        }

        /*
         * Das Kontextmenü kann mehrfach geöffnet werden.
         * Deshalb niemals dieselbe Aktion mehrfach hinzufügen.
         */
        if (existingPermanentDeleteItem is not null)
        {
            return;
        }

        var separator =
            new Separator
            {
                Tag =
                    PermanentDeleteSeparatorTag
            };

        var permanentDeleteItem =
            new MenuItem
            {
                Header =
                    "Endgültig löschen",

                Tag =
                    PermanentDeleteActionTag
            };

        /*
         * Irreversible Aktion bewusst visuell absetzen.
         */
        permanentDeleteItem.SetResourceReference(
            Control.ForegroundProperty,
            "Status.Error");

        permanentDeleteItem.Click +=
            PermanentDeleteMenuItem_OnClick;

        contextMenu.Items.Add(
            separator);

        contextMenu.Items.Add(
            permanentDeleteItem);
    }

    private async void PermanentDeleteMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isEmptyTrashOperationRunning ||
            _viewModel.IsLoading ||
            !_viewModel.IsTrashFolderSelected ||
            sender is not MenuItem menuItem)
        {
            return;
        }

        var contextMenu =
            ItemsControl.ItemsControlFromItemContainer(
                menuItem)
            as ContextMenu;

        /*
         * ContextMenu.PlacementTarget ist als UIElement
         * typisiert. DataContext gehört jedoch zu
         * FrameworkElement.
         *
         * Deshalb erfolgt hier bewusst der sichere Cast.
         */
        var placementTarget =
            contextMenu?.PlacementTarget
            as FrameworkElement;

        var clickedMessage =
            placementTarget?.DataContext
            as MailMessageItemViewModel;

        if (clickedMessage is null)
        {
            return;
        }

        IReadOnlyList<MailMessageItemViewModel>
            messages;

        /*
         * Rechtsklick auf eine bereits markierte Nachricht:
         * die komplette Mehrfachauswahl wird verarbeitet.
         *
         * Rechtsklick auf eine andere Nachricht:
         * nur genau diese Nachricht wird verarbeitet.
         *
         * Damit entspricht das Verhalten dem bestehenden
         * Löschen/Wiederherstellen-Kontextmenü.
         */
        if (MessageListBox.SelectedItems.Contains(
                clickedMessage))
        {
            messages =
                GetSelectedMessages();
        }
        else
        {
            messages =
                new[]
                {
                    clickedMessage
                };
        }

        await DeleteMessagesPermanentlyFromUiAsync(
            messages);
    }

    private async Task DeleteMessagesPermanentlyFromUiAsync(
        IReadOnlyList<MailMessageItemViewModel> messages)
    {
        if (_isEmptyTrashOperationRunning ||
            messages.Count == 0 ||
            !_viewModel.IsTrashFolderSelected ||
            _viewModel.IsLoading)
        {
            return;
        }

        var confirmationText =
            messages.Count == 1
                ? "Möchten Sie die ausgewählte Nachricht wirklich endgültig löschen?\n\n" +
                  "Dieser Vorgang kann nicht rückgängig gemacht werden."
                : $"Möchten Sie die {messages.Count} ausgewählten Nachrichten wirklich endgültig löschen?\n\n" +
                  "Dieser Vorgang kann nicht rückgängig gemacht werden.";

        var confirmation =
            MessageBox.Show(
                this,
                confirmationText,
                "Endgültig löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var deleted =
                await _viewModel
                    .DeleteMessagesPermanentlyAsync(
                        messages);

            if (deleted)
            {
                /*
                 * Keine zusätzliche Erfolgsmeldung.
                 *
                 * Der echte Serverzustand wurde vom ViewModel
                 * bereits neu geladen und ist die maßgebliche
                 * Rückmeldung für die UI.
                 */
                return;
            }

            /*
             * Ein false bedeutet:
             *
             * Die irreversible Operation wurde gar nicht erst
             * gestartet, beispielsweise weil gerade eine
             * Synchronisierung oder eine andere
             * Nachrichtenoperation läuft oder kein eindeutig
             * gültiger UID-Zustand vorliegt.
             */
            MessageBox.Show(
                this,
                "Das endgültige Löschen wurde nicht gestartet.\n\n" +
                "Der Papierkorb wird möglicherweise gerade synchronisiert " +
                "oder der Nachrichtenstatus ist nicht eindeutig.\n\n" +
                "Bitte warten Sie kurz beziehungsweise synchronisieren Sie " +
                "den Papierkorb und versuchen Sie es anschließend erneut.",
                "Endgültig löschen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (NotSupportedException)
        {
            /*
             * Dieser Fall tritt vor der verändernden
             * Serveroperation auf.
             *
             * Ohne UIDPLUS wird bewusst nichts gelöscht.
             */
            MessageBox.Show(
                this,
                "Der Mailserver unterstützt das für ein sicheres gezieltes " +
                "Löschen erforderliche Verfahren nicht.\n\n" +
                "Die Nachricht wurde nicht endgültig gelöscht.",
                "Endgültig löschen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            /*
             * Bei einem Verbindungsabbruch kann nicht in jedem
             * Fall eindeutig festgestellt werden, ob der Server
             * die irreversible Operation bereits ausgeführt hat.
             *
             * Das ViewModel versucht deshalb bereits, den
             * tatsächlichen Serverzustand neu zu laden und führt
             * die Löschoperation ausdrücklich nicht automatisch
             * ein zweites Mal aus.
             */
            MessageBox.Show(
                this,
                messages.Count == 1
                    ? "Der endgültige Löschvorgang konnte nicht eindeutig bestätigt werden.\n\n" +
                      "Der Papierkorb wurde soweit möglich mit dem Server neu synchronisiert.\n\n" +
                      "Bitte prüfen Sie, ob die Nachricht noch vorhanden ist, bevor Sie erneut löschen."
                    : "Der endgültige Löschvorgang konnte nicht für alle ausgewählten Nachrichten eindeutig bestätigt werden.\n\n" +
                      "Der Papierkorb wurde soweit möglich mit dem Server neu synchronisiert.\n\n" +
                      "Bitte prüfen Sie, welche Nachrichten noch vorhanden sind, bevor Sie erneut löschen.",
                "Endgültig löschen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task EmptyTrashFromUiAsync()
    {
        if (_isEmptyTrashOperationRunning ||
            _viewModel.IsLoading ||
            !_viewModel.IsTrashFolderSelected)
        {
            return;
        }

        var selectedFolder =
            _viewModel.SelectedFolder;

        if (selectedFolder is null ||
            string.IsNullOrWhiteSpace(
                selectedFolder.FolderId))
        {
            return;
        }

        /*
         * Die Formulierung macht ausdrücklich deutlich, dass
         * nicht nur die momentan sichtbaren Nachrichten
         * betroffen sind.
         *
         * Das ist wegen des 20er-Pagings für diese Aktion
         * entscheidend.
         */
        var confirmation =
            MessageBox.Show(
                this,
                "Möchten Sie den Papierkorb wirklich vollständig leeren?\n\n" +
                "Alle derzeit im Papierkorb enthaltenen Nachrichten werden " +
                "endgültig gelöscht – auch Nachrichten, die aktuell nicht " +
                "in der Liste sichtbar sind.\n\n" +
                "Dieser Vorgang kann nicht rückgängig gemacht werden.",
                "Papierkorb leeren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _isEmptyTrashOperationRunning =
            true;

        if (_emptyTrashButton is not null)
        {
            _emptyTrashButton.IsEnabled =
                false;

            _emptyTrashButton.Cursor =
                Cursors.Wait;
        }

        /*
         * Während des Leerens dürfen die normalen
         * Nachrichtenaktionen nicht durch einen Mausklick
         * parallel ausgelöst werden.
         *
         * IsHitTestVisible verändert keine vorhandenen
         * Bindings der Buttons.
         */
        MessageListBox.IsHitTestVisible =
            false;

        FolderListBox.IsHitTestVisible =
            false;

        DeleteSelectedMessageButton.IsHitTestVisible =
            false;

        if (_permanentDeleteSelectedMessageButton is not null)
        {
            _permanentDeleteSelectedMessageButton
                .IsHitTestVisible =
                    false;
        }

        var restartAutomaticSynchronization =
            false;

        try
        {
            /*
             * Ein eventuell gerade laufender Auto-Sync erhält
             * über StopAutomaticSynchronization sein
             * CancellationToken.
             *
             * Wir warten anschließend ausdrücklich auf sein
             * vollständiges Ende.
             *
             * Erst danach beginnt die irreversible Operation.
             */
            restartAutomaticSynchronization =
                await PauseAutomaticSynchronizationAsync();

            /*
             * Während wir auf den Abschluss eines eventuell
             * laufenden Auto-Sync gewartet haben, könnte sich
             * der ausgewählte Ordner theoretisch geändert
             * haben.
             *
             * In diesem Fall starten wir die irreversible
             * Operation nicht mehr.
             */
            if (!_viewModel.IsTrashFolderSelected ||
                !string.Equals(
                    _viewModel.SelectedFolder?.FolderId,
                    selectedFolder.FolderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            /*
             * Über das Interface erhalten wir bewusst den
             * registrierten LoggingPermanentDeleteService.
             *
             * Dieser delegiert anschließend an den bereits
             * getesteten MailKit-Service.
             */
            var permanentDeleteService =
                _serviceProvider
                    .GetRequiredService<
                        IMailPermanentDeleteService>();

            var deletedMessageCount =
                await permanentDeleteService
                    .EmptyTrashAsync(
                        selectedFolder.FolderId);

            /*
             * Nach einer irreversiblen Aktion ist ausschließlich
             * der echte Serverzustand maßgeblich.
             *
             * Deshalb wird nicht lokal aus der Collection
             * entfernt, sondern vollständig synchronisiert.
             */
            await _viewModel
                .ReloadAsync();

            if (deletedMessageCount == 0)
            {
                MessageBox.Show(
                    this,
                    "Der Papierkorb ist bereits leer.",
                    "Papierkorb leeren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            /*
             * Bei erfolgreicher Löschung gibt es bewusst keine
             * zusätzliche Erfolgsmeldung.
             *
             * Der nun neu geladene Papierkorb ist die direkte
             * und zuverlässigste Rückmeldung für den Benutzer.
             */
        }
        catch (NotSupportedException)
        {
            await TryReloadAfterEmptyTrashAsync();

            /*
             * Dieser Fehler entsteht vor der ersten
             * verändernden Serveroperation.
             *
             * Ohne UIDPLUS wird bewusst nicht auf ein
             * unsichereres allgemeines EXPUNGE ausgewichen.
             */
            MessageBox.Show(
                this,
                "Der Mailserver unterstützt das für ein sicheres vollständiges " +
                "Leeren des Papierkorbs erforderliche Verfahren nicht.\n\n" +
                "Der Papierkorb wurde nicht geleert.",
                "Papierkorb leeren",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            /*
             * Netzwerkabbrüche und Timeouts können theoretisch
             * nach dem UID EXPUNGE auftreten.
             *
             * Deshalb darf die UI niemals behaupten, dass die
             * Löschung sicher fehlgeschlagen sei.
             *
             * Wir synchronisieren soweit möglich und lassen
             * anschließend ausschließlich den echten
             * Serverzustand entscheiden.
             */
            await TryReloadAfterEmptyTrashAsync();

            MessageBox.Show(
                this,
                "Das vollständige Leeren des Papierkorbs konnte nicht " +
                "eindeutig bestätigt werden.\n\n" +
                "Der Papierkorb wurde soweit möglich mit dem Server neu " +
                "synchronisiert.\n\n" +
                "Bitte prüfen Sie den aktuellen Inhalt, bevor Sie den " +
                "Vorgang erneut ausführen.",
                "Papierkorb leeren",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _isEmptyTrashOperationRunning =
                false;

            MessageListBox.IsHitTestVisible =
                true;

            FolderListBox.IsHitTestVisible =
                true;

            DeleteSelectedMessageButton.IsHitTestVisible =
                true;

            if (_permanentDeleteSelectedMessageButton is not null)
            {
                _permanentDeleteSelectedMessageButton
                    .IsHitTestVisible =
                        true;
            }

            if (_emptyTrashButton is not null)
            {
                _emptyTrashButton.IsEnabled =
                    true;

                _emptyTrashButton.Cursor =
                    Cursors.Hand;
            }

            /*
             * Nur wenn vorher tatsächlich ein automatischer
             * Synchronisationsloop lief, wird er wieder
             * gestartet.
             *
             * Ist das Fenster inzwischen geschlossen worden,
             * darf natürlich kein neuer Hintergrundloop mehr
             * entstehen.
             */
            if (restartAutomaticSynchronization &&
                IsVisible)
            {
                StartAutomaticSynchronization();
            }
        }
    }

    private async Task<bool>
        PauseAutomaticSynchronizationAsync()
    {
        var synchronizationTask =
            _automaticSynchronizationTask;

        var wasRunning =
            synchronizationTask is
            {
                IsCompleted: false
            };

        StopAutomaticSynchronization();

        if (synchronizationTask is null)
        {
            return false;
        }

        try
        {
            /*
             * RunAutomaticSynchronizationAsync behandelt einen
             * durch uns ausgelösten OperationCanceledException
             * selbst.
             *
             * Der zusätzliche Catch hier ist nur defensiv:
             * Das Logging-/Löschfeature darf niemals wegen
             * eines unerwarteten Fehlers im alten
             * Hintergrundloop scheitern.
             */
            await synchronizationTask;
        }
        catch
        {
        }

        return wasRunning;
    }

    private async Task TryReloadAfterEmptyTrashAsync()
    {
        try
        {
            /*
             * Auch im Fehlerfall wird ausschließlich gelesen.
             *
             * Die irreversible Operation wird ausdrücklich
             * NICHT automatisch wiederholt.
             */
            await _viewModel
                .ReloadAsync();
        }
        catch
        {
            /*
             * Ist auch die Kontrollsynchronisierung wegen einer
             * Netzwerkstörung nicht möglich, bleibt die bereits
             * angezeigte Warnung bewusst konservativ.
             */
        }
    }
}