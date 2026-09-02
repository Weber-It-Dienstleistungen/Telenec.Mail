using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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

    /*
     * Der bestehende MainWindow-Keyhandler bleibt absichtlich
     * unangetastet.
     *
     * Über die WPF-OnPreviewKeyDown-Klassenbehandlung fangen wir
     * ausschließlich die Entf-Taste im Papierkorb vorher ab.
     *
     * Alle anderen Tastaturaktionen laufen weiterhin durch den
     * bisherigen MainWindow_OnPreviewKeyDown-Workflow.
     */
    protected override void OnPreviewKeyDown(
        KeyEventArgs e)
    {
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
                 * Der bestehende Keyhandler darf diese Entf-Taste
                 * nicht zusätzlich verarbeiten.
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
        if (_viewModel.IsLoading ||
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
        if (messages.Count == 0 ||
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
}