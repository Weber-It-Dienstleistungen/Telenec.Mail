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
    private static readonly TimeSpan AutoSaveInterval =
        TimeSpan.FromSeconds(30);

    private readonly ComposeMailViewModel _viewModel;

    private MailMessageItemViewModel?
        _forwardSourceMessage;

    private bool _hasComposeBaseline;

    private string _baselineRecipientAddress =
        string.Empty;

    private string _baselineCcAddress =
        string.Empty;

    private string _baselineBccAddress =
        string.Empty;

    private string _baselineSubject =
        string.Empty;

    private string _baselineBody =
        string.Empty;

    private IReadOnlyList<MailSendAttachmentData>
        _baselineAttachments =
            Array.Empty<MailSendAttachmentData>();

    private CancellationTokenSource?
        _autoSaveCancellationTokenSource;

    private bool _autoSaveSuspended;
    private bool _autoSaveWarningShown;
    private bool _allowClose;
    private bool _isProcessingCloseSave;

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

        Closed +=
            ComposeWindow_OnClosed;
    }

    public void PrepareReply(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        _viewModel.PrepareReply(
            message);
    }

    public void PrepareReplyAll(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        _viewModel.PrepareReplyAll(
            message);
    }

    public void PrepareForward(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        _forwardSourceMessage =
            message;

        _viewModel.PrepareForward(
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
            await _viewModel.InitializeAsync();
        }
        catch
        {
            MessageBox.Show(
                "Die Kontodaten konnten nicht vollständig geladen werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        CaptureComposeBaseline();

        StartAutoSaveLoop();

        if (_viewModel.FocusBodyOnLoad)
        {
            BodyTextBox.Focus();

            BodyTextBox.CaretIndex =
                0;

            return;
        }

        RecipientTextBox.Focus();
    }

    private void ComposeWindow_OnClosed(
        object? sender,
        EventArgs e)
    {
        StopAutoSaveLoop();

        Loaded -=
            ComposeWindow_OnLoaded;

        Closed -=
            ComposeWindow_OnClosed;
    }

    private void StartAutoSaveLoop()
    {
        if (_autoSaveCancellationTokenSource is not null)
        {
            return;
        }

        _autoSaveCancellationTokenSource =
            new CancellationTokenSource();

        _ =
            RunAutoSaveLoopAsync(
                _autoSaveCancellationTokenSource.Token);
    }

    private void StopAutoSaveLoop()
    {
        var cancellationTokenSource =
            _autoSaveCancellationTokenSource;

        _autoSaveCancellationTokenSource =
            null;

        if (cancellationTokenSource is null)
        {
            return;
        }

        try
        {
            cancellationTokenSource.Cancel();
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }

    private async Task RunAutoSaveLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    AutoSaveInterval,
                    cancellationToken);

                cancellationToken
                    .ThrowIfCancellationRequested();

                if (_autoSaveSuspended ||
                    _allowClose ||
                    _isProcessingCloseSave ||
                    _viewModel.IsBusy)
                {
                    continue;
                }

                if (!HasUnsavedChanges() ||
                    !_viewModel.CanSaveDraft)
                {
                    continue;
                }

                await TryAutoSaveDraftAsync(
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            _autoSaveSuspended =
                true;
        }
    }

    private async Task TryAutoSaveDraftAsync(
        CancellationToken cancellationToken)
    {
        if (_autoSaveSuspended ||
            _allowClose ||
            _isProcessingCloseSave ||
            _viewModel.IsBusy ||
            !_viewModel.CanSaveDraft ||
            !HasUnsavedChanges())
        {
            return;
        }

        var savedSnapshot =
            CreateComposeSnapshot();

        try
        {
            var result =
                await _viewModel
                    .SaveDraftAsync(
                        cancellationToken);

            if (!result.WasSaved)
            {
                return;
            }

            DraftMailboxChanged =
                true;

            ApplyAutoSaveBaseline(
                savedSnapshot);

            if (!_viewModel.IsEditingDraft)
            {
                SuspendAutoSaveWithWarning(
                    "Der aktuelle Entwurf wurde gespeichert.\n\n" +
                    "Der Mailserver hat jedoch keine ausreichend sichere neue Entwurfs-ID zurückgegeben.\n\n" +
                    "Das automatische Speichern wurde deshalb für dieses Fenster angehalten. " +
                    "Sie können die Nachricht weiter bearbeiten und weiterhin manuell speichern oder versenden.");

                return;
            }

            if (result.HasWarning)
            {
                SuspendAutoSaveWithWarning(
                    "Der Entwurf wurde automatisch gespeichert.\n\n" +
                    "Die vorherige Version konnte jedoch nicht automatisch vom Mailserver entfernt werden.\n\n" +
                    "Damit keine weiteren Dubletten entstehen, wurde das automatische Speichern für dieses Fenster angehalten.\n\n" +
                    "Im Ordner „Entwürfe“ kann zusätzlich eine ältere Version vorhanden sein.");

                return;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ArgumentException)
        {
        }
        catch
        {
        }
    }

    private void SuspendAutoSaveWithWarning(
        string message)
    {
        _autoSaveSuspended =
            true;

        if (_autoSaveWarningShown)
        {
            return;
        }

        _autoSaveWarningShown =
            true;

        MessageBox.Show(
            message,
            "Automatisches Speichern angehalten",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private ComposeSnapshot CreateComposeSnapshot()
    {
        return new ComposeSnapshot(
            RecipientAddress:
                _viewModel.RecipientAddress,

            CcAddress:
                _viewModel.CcAddress,

            BccAddress:
                _viewModel.BccAddress,

            Subject:
                _viewModel.Subject,

            Body:
                _viewModel.Body,

            Attachments:
                _viewModel.Attachments.ToArray());
    }

    private void CaptureComposeBaseline()
    {
        ApplyComposeBaseline(
            CreateComposeSnapshot());
    }

    private void ApplyComposeBaseline(
        ComposeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot);

        _baselineRecipientAddress =
            snapshot.RecipientAddress;

        _baselineCcAddress =
            snapshot.CcAddress;

        _baselineBccAddress =
            snapshot.BccAddress;

        _baselineSubject =
            snapshot.Subject;

        _baselineBody =
            snapshot.Body;

        _baselineAttachments =
            snapshot.Attachments.ToArray();

        _hasComposeBaseline =
            true;
    }

    private void ApplyAutoSaveBaseline(
        ComposeSnapshot savedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(
            savedSnapshot);

        _baselineRecipientAddress =
            savedSnapshot.RecipientAddress;

        _baselineCcAddress =
            savedSnapshot.CcAddress;

        _baselineBccAddress =
            savedSnapshot.BccAddress;

        _baselineSubject =
            savedSnapshot.Subject;

        _baselineBody =
            savedSnapshot.Body;

        _baselineAttachments =
            _viewModel.Attachments.ToArray();

        _hasComposeBaseline =
            true;
    }

    private bool HasUnsavedChanges()
    {
        if (!_hasComposeBaseline)
        {
            return false;
        }

        if (!string.Equals(
                _baselineRecipientAddress,
                _viewModel.RecipientAddress,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(
                _baselineCcAddress,
                _viewModel.CcAddress,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(
                _baselineBccAddress,
                _viewModel.BccAddress,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(
                _baselineSubject,
                _viewModel.Subject,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(
                _baselineBody,
                _viewModel.Body,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!_baselineAttachments
                .SequenceEqual(
                    _viewModel.Attachments))
        {
            return true;
        }

        return false;
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

            _allowClose =
                true;

            Close();

            return false;
        }

        try
        {
            _viewModel
                .AddForwardedAttachments(
                    sourceMessage,
                    mainViewModel.SelectedFolder.FolderId);

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

            _allowClose =
                true;

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
            _viewModel.AddAttachmentFiles(
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

        _viewModel.RemoveAttachment(
            attachment);
    }

    private void ShowCcButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.ShowCc();

        CcTextBox.Focus();

        CcTextBox.CaretIndex =
            CcTextBox.Text.Length;
    }

    private void ShowBccButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.ShowBcc();

        BccTextBox.Focus();

        BccTextBox.CaretIndex =
            BccTextBox.Text.Length;
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
        await SaveDraftAndCloseAsync();
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
                await _viewModel.SendAsync();

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

            _allowClose =
                true;

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

            if (string.Equals(
                    ex.ParamName,
                    nameof(MailSendRequest.CcAddress),
                    StringComparison.Ordinal))
            {
                _viewModel.ShowCc();

                CcTextBox.Focus();
                CcTextBox.SelectAll();

                return;
            }

            if (string.Equals(
                    ex.ParamName,
                    nameof(MailSendRequest.BccAddress),
                    StringComparison.Ordinal))
            {
                _viewModel.ShowBcc();

                BccTextBox.Focus();
                BccTextBox.SelectAll();

                return;
            }

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

    private async Task SaveDraftAndCloseAsync()
    {
        var saved =
            await TrySaveDraftAsync();

        if (!saved)
        {
            return;
        }

        _allowClose =
            true;

        DialogResult =
            false;

        Close();
    }

    private async Task<bool> TrySaveDraftAsync()
    {
        if (!_viewModel.CanSaveDraft)
        {
            MessageBox.Show(
                "Der aktuelle Inhalt kann nicht als Entwurf gespeichert werden.\n\n" +
                "Bitte ergänzen Sie den Entwurf oder schließen Sie ihn erneut und wählen Sie „Nein“, um die Änderungen zu verwerfen.",
                "Entwurf kann nicht gespeichert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return false;
        }

        try
        {
            var result =
                await _viewModel.SaveDraftAsync();

            if (result.WasSaved)
            {
                DraftMailboxChanged =
                    true;
            }

            if (result.HasWarning)
            {
                MessageBox.Show(
                    "Die neue Version des Entwurfs wurde erfolgreich gespeichert.\n\n" +
                    "Die vorherige Version konnte jedoch nicht automatisch entfernt werden.\n\n" +
                    "Im Ordner „Entwürfe“ können deshalb vorübergehend beide Versionen vorhanden sein. " +
                    "Bitte entfernen Sie dort gegebenenfalls die ältere Version manuell.",
                    "Entwurf gespeichert – alte Version noch vorhanden",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return result.WasSaved;
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
                _viewModel.ShowCc();

                CcTextBox.Focus();
                CcTextBox.SelectAll();

                return false;
            }

            if (string.Equals(
                    ex.ParamName,
                    nameof(MailSendRequest.BccAddress),
                    StringComparison.Ordinal))
            {
                _viewModel.ShowBcc();

                BccTextBox.Focus();
                BccTextBox.SelectAll();

                return false;
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

        return false;
    }

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsBusy ||
            _isProcessingCloseSave)
        {
            return;
        }

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
        if (_allowClose)
        {
            return;
        }

        if (_viewModel.IsBusy ||
            _isProcessingCloseSave)
        {
            e.Cancel =
                true;

            return;
        }

        if (!HasUnsavedChanges())
        {
            return;
        }

        var result =
            MessageBox.Show(
                "Die Nachricht enthält ungespeicherte Änderungen.\n\n" +
                "Möchten Sie die Änderungen als Entwurf speichern?\n\n" +
                "Ja = Entwurf speichern\n" +
                "Nein = Änderungen verwerfen\n" +
                "Abbrechen = Weiter bearbeiten",
                "Ungespeicherte Änderungen",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);

        switch (result)
        {
            case MessageBoxResult.No:
                _allowClose =
                    true;

                return;

            case MessageBoxResult.Yes:
                e.Cancel =
                    true;

                _ =
                    SaveDraftFromCloseRequestAsync();

                return;

            case MessageBoxResult.Cancel:
            default:
                e.Cancel =
                    true;

                return;
        }
    }

    private async Task SaveDraftFromCloseRequestAsync()
    {
        if (_isProcessingCloseSave)
        {
            return;
        }

        _isProcessingCloseSave =
            true;

        try
        {
            var saved =
                await TrySaveDraftAsync();

            if (!saved)
            {
                return;
            }

            _allowClose =
                true;

            DialogResult =
                false;

            Close();
        }
        finally
        {
            _isProcessingCloseSave =
                false;
        }
    }

    private sealed record ComposeSnapshot(
        string RecipientAddress,
        string CcAddress,
        string BccAddress,
        string Subject,
        string Body,
        IReadOnlyList<MailSendAttachmentData> Attachments);
}