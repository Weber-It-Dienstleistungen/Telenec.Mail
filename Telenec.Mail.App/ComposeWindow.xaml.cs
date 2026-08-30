using MailKit.Security;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ComposeWindow : Window
{
    private readonly ComposeMailViewModel _viewModel;

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

    private async void ComposeWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
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

        RecipientTextBox.Focus();
    }

    private async void SendButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        if (!_viewModel.CanSend)
        {
            return;
        }

        try
        {
            var result =
                await _viewModel
                    .SendAsync();

            if (result.HasWarning)
            {
                MessageBox.Show(
                    "Die E-Mail wurde erfolgreich versendet.\n\n" +
                    "Die Kopie konnte jedoch nicht im Ordner „Gesendet“ gespeichert werden.\n\n" +
                    "Bitte senden Sie die Nachricht nicht erneut.",
                    "E-Mail versendet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            DialogResult =
                true;

            Close();
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

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsSending)
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
        if (_viewModel.IsSending)
        {
            e.Cancel =
                true;
        }
    }
}