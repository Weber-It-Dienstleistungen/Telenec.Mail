using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
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
                    DeleteMessagesPermanentlyFromKeyboardAsync(
                        messages);

                base.OnPreviewKeyDown(
                    e);

                return;
            }
        }

        base.OnPreviewKeyDown(
            e);
    }

    private async Task
        DeleteMessagesPermanentlyFromKeyboardAsync(
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