using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls.Primitives;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow : Window
{
    private readonly IMailAccountStore _mailAccountStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IServiceProvider _serviceProvider;

    private bool _isLoggingOut;

    public MainWindow(
        MainViewModel viewModel,
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        IServiceProvider serviceProvider)
    {
        InitializeComponent();

        _mailAccountStore = mailAccountStore;
        _credentialStore = credentialStore;
        _serviceProvider = serviceProvider;

        DataContext = viewModel;

        Loaded += MainWindow_OnLoaded;
    }

    private async void MainWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            AccountEmailText.Text =
                account?.EmailAddress
                ?? "Telenec-Konto";
        }
        catch
        {
            AccountEmailText.Text =
                "Telenec-Konto";
        }
    }

    private void AccountMenuButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var contextMenu =
            AccountMenuButton.ContextMenu;

        if (contextMenu is null)
        {
            return;
        }

        contextMenu.PlacementTarget =
            AccountMenuButton;

        contextMenu.Placement =
            PlacementMode.Top;

        contextMenu.IsOpen =
            true;
    }

    private async void LogoutMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isLoggingOut)
        {
            return;
        }

        var confirmation =
            MessageBox.Show(
                "Möchten Sie dieses E-Mail-Konto wirklich abmelden?\n\n" +
                "Die gespeicherten Zugangsdaten werden von diesem Computer entfernt.",
                "Konto abmelden",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _isLoggingOut = true;
        AccountMenuButton.IsEnabled = false;

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is not null)
            {
                /*
                 * Zuerst das geschützte Credential löschen.
                 *
                 * Sollte danach das Löschen des Accountdatensatzes
                 * fehlschlagen, bleibt wenigstens kein Passwort
                 * unnötig im Windows Credential Store zurück.
                 */
                await _credentialStore.DeleteAsync(
                    account.AccountId);

                await _mailAccountStore.DeleteAsync(
                    account.AccountId);
            }

            var loginWindow =
                _serviceProvider
                    .GetRequiredService<LoginWindow>();

            loginWindow.PrepareKnownAccount(
                null);

            Application.Current.MainWindow =
                loginWindow;

            loginWindow.Show();

            Close();
        }
        catch
        {
            _isLoggingOut = false;
            AccountMenuButton.IsEnabled = true;

            MessageBox.Show(
                "Das Konto konnte nicht vollständig abgemeldet werden.\n\n" +
                "Bitte versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}