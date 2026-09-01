using MailKit.Security;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ComposeWindow : Window
{
    private readonly ComposeMailViewModel _viewModel;

    private MailMessageItemViewModel?
        _forwardSourceMessage;

    /*
     * MainWindow kann nach dem Schließen erkennen, ob sich
     * der serverseitige Draft-Bestand geändert hat.
     *
     * Dadurch wird der Entwürfe-Ordner nach Speichern oder
     * Senden eines geöffneten Drafts sofort aktualisiert.
     */
    public bool DraftMailboxChanged
    {
        get;
        private set;
    }

    public ComposeWindow(
        ComposeMailViewModel viewModel)
    {
        InitializeComponent();

        _viewModel =
            viewModel;

        DataContext =
            _viewModel;

        Loaded +=
            ComposeWindow_OnLoaded;
    }

    public void PrepareReply(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        _viewModel
            .PrepareReply(
                message);
    }

    public void PrepareReplyAll(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        _viewModel
            .PrepareReplyAll(
                message);
    }

    public void PrepareForward(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        _forwardSourceMessage =
            message;

        _viewModel
            .PrepareForward(
                message);
    }

    public async Task PrepareDraftEditAsync(
        string sourceFolderId,
        uint sourceUniqueId,
        string? expectedMessageId,
        CancellationToken cancellationToken = default)
    {
        await _viewModel
            .PrepareDraftEditAsync(
                sourceFolderId,
                sourceUniqueId,
                expectedMessageId,
                cancellationToken);
    }

    private async void ComposeWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryPrepareForwardAttachments())
        {
            return;
        }

        try
        {
            await _viewModel
                .InitializeAsync();
        }
        catch
        {
            MessageBox.Show(
                "Die Kontodaten konnten nicht vollständig geladen werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (_viewModel.FocusBodyOnLoad)
        {
            BodyTextBox.Focus();

            BodyTextBox.CaretIndex =
                0;

            return;
        }

        RecipientTextBox.Focus();
    }

    private bool TryPrepareForwardAttachments()
    {
        var sourceMessage =
            _forwardSourceMessage;

        _forwardSourceMessage =
            null;

        if (sourceMessage is null ||
            !sourceMessage.HasAttachments)
        {
            return true;
        }

        /*
         * MainWindow setzt den Owner unmittelbar vor
         * ShowDialog().
         *
         * Dadurch können wir hier den aktuell ausgewählten
         * IMAP-Ordner ermitteln, ohne MainWindow oder den
         * allgemeinen Forward-Aufruf verändern zu müssen.
         */
        if (Owner?.DataContext is not MainViewModel mainViewModel ||
            mainViewModel.SelectedFolder is null ||
            string.IsNullOrWhiteSpace(
                mainViewModel.SelectedFolder.FolderId))
        {
            MessageBox.Show(
                "Die Originalanhänge konnten nicht eindeutig ihrer Servernachricht zugeordnet werden.\n\n" +
                "Die Weiterleitung wurde deshalb aus Sicherheitsgründen abgebrochen.",
                "Weiterleiten nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            Close();

            return false;
        }

        try
        {
            _viewModel
                .AddForwardedAttachments(
                    sourceMessage,
                    mainViewModel
                        .SelectedFolder
                        .FolderId);

            return true;
        }
        catch
        {
            MessageBox.Show(
                "Die Originalanhänge konnten nicht für die Weiterleitung vorbereitet werden.\n\n" +
                "Die Weiterleitung wurde nicht geöffnet, damit kein Anhang unbemerkt fehlt.",
                "Weiterleiten nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            Close();

            return false;
        }
    }

    private void AddAttachmentButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!_viewModel.CanModifyAttachments)
        {
            return;
        }

        var fileDialog =
            new OpenFileDialog
            {
                Title =
                    "Datei anhängen",

                Filter =
                    "Alle Dateien (*.*)|*.*",

                Multiselect =
                    true,

                CheckFileExists =
                    true,

                CheckPathExists =
                    true
            };

        var result =
            fileDialog.ShowDialog(
                this);

        if (result != true)
        {
            return;
        }

        try
        {
            _viewModel
                .AddAttachmentFiles(
                    fileDialog.FileNames);
        }
        catch
        {
            MessageBox.Show(
                "Mindestens eine der ausgewählten Dateien konnte nicht als Anhang geöffnet werden.\n\n" +
                "Bitte prüfen Sie, ob die Datei noch vorhanden und lesbar ist.",
                "Anhang konnte nicht hinzugefügt werden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RemoveAttachmentButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!_viewModel.CanModifyAttachments ||
            sender is not FrameworkElement element ||
            element.DataContext is not MailSendAttachmentData attachment)
        {
            return;
        }

        _viewModel
            .RemoveAttachment(
                attachment);
    }

    private async void SendButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void SaveDraftButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await SaveDraftAsync();
    }

    private async Task SendMessageAsync()
    {
        if (!_viewModel.CanSend)
        {
            return;
        }

        var wasEditingDraft =
            _viewModel.IsEditingDraft;

        try
        {
            var result =
                await _viewModel
                    .SendAsync();

            if (wasEditingDraft &&
                result.WasSent)
            {
                DraftMailboxChanged =
                    true;
            }

            if (result.HasWarning &&
                result.HasDraftCleanupWarning)
            {
                MessageBox.Show(
                    "Die E-Mail wurde erfolgreich versendet.\n\n" +
                    "Die Kopie konnte jedoch nicht im Ordner „Gesendet“ gespeichert werden.\n\n" +
                    "Zusätzlich konnte der bisherige Entwurf nicht automatisch entfernt werden.\n\n" +
                    "Bitte senden Sie die Nachricht NICHT erneut. " +
                    "Prüfen Sie lediglich den Ordner „Entwürfe“ und entfernen Sie dort gegebenenfalls die alte Version manuell.",
                    "E-Mail versendet – Hinweise",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else if (result.HasWarning)
            {
                MessageBox.Show(
                    "Die E-Mail wurde erfolgreich versendet.\n\n" +
                    "Die Kopie konnte jedoch nicht im Ordner „Gesendet“ gespeichert werden.\n\n" +
                    "Bitte senden Sie die Nachricht nicht erneut.",
                    "E-Mail versendet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else if (result.HasDraftCleanupWarning)
            {
                MessageBox.Show(
                    "Die E-Mail wurde erfolgreich versendet.\n\n" +
                    "Der bisherige Entwurf konnte jedoch nicht automatisch entfernt werden.\n\n" +
                    "Bitte senden Sie die Nachricht NICHT erneut. " +
                    "Prüfen Sie lediglich den Ordner „Entwürfe“ und entfernen Sie dort gegebenenfalls die alte Version manuell.",
                    "E-Mail versendet – Entwurf noch vorhanden",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            DialogResult =
                true;

            Close();
        }
        catch (MailSendAttachmentException ex)
        {
            MessageBox.Show(
                ex.Message +
                "\n\nDie E-Mail wurde nicht versendet.",
                "Anhang konnte nicht vorbereitet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Empfänger prüfen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            RecipientTextBox.Focus();

            RecipientTextBox.SelectAll();
        }
        catch (MailKit.Security.AuthenticationException)
        {
            MessageBox.Show(
                "Der Mailserver hat die gespeicherten Zugangsdaten nicht akzeptiert.\n\n" +
                "Die E-Mail wurde nicht versendet.",
                "Anmeldung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (SslHandshakeException)
        {
            MessageBox.Show(
                "Die sichere Verbindung zum Mailserver konnte nicht hergestellt werden.\n\n" +
                "Die E-Mail wurde nicht versendet.",
                "Sicherheitsfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                "Der Versand hat zu lange gedauert und wurde abgebrochen.\n\n" +
                "Die E-Mail wurde nicht erneut automatisch versendet.",
                "Zeitüberschreitung",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            MessageBox.Show(
                "Die E-Mail konnte nicht versendet werden.\n\n" +
                "Bitte prüfen Sie die Internetverbindung und versuchen Sie es erneut.",
                "Versand fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task SaveDraftAsync()
    {
        if (!_viewModel.CanSaveDraft)
        {
            return;
        }

        try
        {
            var result =
                await _viewModel
                    .SaveDraftAsync();

            if (result.WasSaved)
            {
                DraftMailboxChanged =
                    true;
            }

            if (result.HasWarning)
            {
                /*
                 * Wichtig:
                 *
                 * Die NEUE Version ist bereits sicher
                 * gespeichert.
                 *
                 * Nur das Entfernen der alten Version ist
                 * fehlgeschlagen.
                 */
                MessageBox.Show(
                    "Die neue Version des Entwurfs wurde erfolgreich gespeichert.\n\n" +
                    "Die vorherige Version konnte jedoch nicht automatisch entfernt werden.\n\n" +
                    "Im Ordner „Entwürfe“ können deshalb vorübergehend beide Versionen vorhanden sein. " +
                    "Bitte entfernen Sie dort gegebenenfalls die ältere Version manuell.",
                    "Entwurf gespeichert – alte Version noch vorhanden",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            /*
             * False ist hier weiterhin absichtlich korrekt:
             *
             * MainWindow wertet true ausschließlich als
             * erfolgreich versendete Nachricht.
             */
            DialogResult =
                false;

            Close();
        }
        catch (MailSendAttachmentException ex)
        {
            MessageBox.Show(
                ex.Message +
                "\n\nDer Entwurf wurde nicht gespeichert.",
                "Anhang konnte nicht vorbereitet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Adressen prüfen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (string.Equals(
                    ex.ParamName,
                    nameof(MailSendRequest.CcAddress),
                    StringComparison.Ordinal))
            {
                CcTextBox.Focus();

                CcTextBox.SelectAll();

                return;
            }

            RecipientTextBox.Focus();

            RecipientTextBox.SelectAll();
        }
        catch (MailKit.Security.AuthenticationException)
        {
            MessageBox.Show(
                "Der Mailserver hat die gespeicherten Zugangsdaten nicht akzeptiert.\n\n" +
                "Der Entwurf wurde nicht gespeichert.",
                "Anmeldung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (SslHandshakeException)
        {
            MessageBox.Show(
                "Die sichere Verbindung zum Mailserver konnte nicht hergestellt werden.\n\n" +
                "Der Entwurf wurde nicht gespeichert.",
                "Sicherheitsfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                "Das Speichern des Entwurfs hat zu lange gedauert und wurde abgebrochen.\n\n" +
                "Der Inhalt bleibt im geöffneten Fenster erhalten.",
                "Zeitüberschreitung",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(
                ex.Message +
                "\n\nDer Entwurf wurde nicht gespeichert.",
                "Entwurf konnte nicht gespeichert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            MessageBox.Show(
                "Der Entwurf konnte nicht auf dem Mailserver gespeichert werden.\n\n" +
                "Bitte prüfen Sie die Internetverbindung und versuchen Sie es erneut.",
                "Entwurf konnte nicht gespeichert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        DialogResult =
            false;

        Close();
    }

    private async void ComposeWindow_OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            !Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control))
        {
            return;
        }

        e.Handled =
            true;

        if (!_viewModel.CanSend)
        {
            return;
        }

        await SendMessageAsync();
    }

    private void ComposeWindow_OnClosing(
        object? sender,
        CancelEventArgs e)
    {
        /*
         * Weder SMTP-Versand noch IMAP-Draft-Append dürfen
         * durch Schließen des Fensters in einen undefinierten
         * Zwischenzustand gebracht werden.
         */
        if (_viewModel.IsBusy)
        {
            e.Cancel =
                true;
        }
    }
}