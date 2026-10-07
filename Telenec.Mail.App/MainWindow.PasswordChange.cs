using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Services.Account;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string PasswordChangeMenuItemTag =
        "Telenec.Mail.Account.PasswordChange";

    private static readonly bool
        PasswordChangeMenuClassHandlerRegistered =
            RegisterPasswordChangeMenuClassHandler();

    private bool
        _passwordChangeMenuInstalled;

    private static bool RegisterPasswordChangeMenuClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnPasswordChangeMenuLoaded));

        return true;
    }

    private static void MainWindow_OnPasswordChangeMenuLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        if (!ReferenceEquals(
                e.OriginalSource,
                window))
        {
            return;
        }

        /*
         * Die Account-Verwaltung wird zuerst aufgebaut.
         * Dadurch können wir unseren Menüpunkt anschließend
         * zuverlässig direkt vor "Konto hinzufügen"
         * platzieren.
         */
        window.EnsureAccountManagementMenu();
        window.InstallPasswordChangeMenu();
    }

    private void InstallPasswordChangeMenu()
    {
        if (_passwordChangeMenuInstalled)
        {
            return;
        }

        var contextMenu =
            AccountMenuButton.ContextMenu;

        if (contextMenu is null)
        {
            return;
        }

        var existingItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag as string,
                            PasswordChangeMenuItemTag,
                            StringComparison.Ordinal));

        if (existingItem is not null)
        {
            _passwordChangeMenuInstalled =
                true;

            return;
        }

        var passwordChangeItem =
            new MenuItem
            {
                Header =
                    "Passwort ändern …",

                Tag =
                    PasswordChangeMenuItemTag
            };

        passwordChangeItem.Click +=
            PasswordChangeMenuItem_OnClick;

        var addAccountItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag as string,
                            AddAccountMenuItemTag,
                            StringComparison.Ordinal));

        if (addAccountItem is not null)
        {
            var addAccountIndex =
                contextMenu.Items.IndexOf(
                    addAccountItem);

            contextMenu.Items.Insert(
                addAccountIndex,
                passwordChangeItem);

            _passwordChangeMenuInstalled =
                true;

            return;
        }

        var removeAccountItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Header?.ToString(),
                            "Konto entfernen",
                            StringComparison.Ordinal) ||
                        string.Equals(
                            item.Header?.ToString(),
                            "Konto abmelden",
                            StringComparison.Ordinal));

        if (removeAccountItem is not null)
        {
            var removeAccountIndex =
                contextMenu.Items.IndexOf(
                    removeAccountItem);

            contextMenu.Items.Insert(
                removeAccountIndex,
                passwordChangeItem);
        }
        else
        {
            contextMenu.Items.Add(
                passwordChangeItem);
        }

        _passwordChangeMenuInstalled =
            true;
    }

    private async void PasswordChangeMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isAccountOperationInProgress)
        {
            return;
        }

        _isAccountOperationInProgress =
            true;

        AccountMenuButton.IsEnabled =
            false;

        var runtimePaused =
            false;

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is null)
            {
                MessageBox.Show(
                    this,
                    "Das aktuell aktive E-Mail-Konto konnte nicht ermittelt werden.",
                    "Passwort ändern",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var authenticationService =
                _serviceProvider
                    .GetRequiredService<
                        IMailAuthenticationService>();

            using var passwordChangeService =
                new AccountPasswordChangeApiClient();

            var passwordWindow =
                new ChangePasswordWindow(
                    account,
                    _credentialStore,
                    passwordChangeService,
                    authenticationService)
                {
                    Owner =
                        this
                };

            var passwordChanged =
                passwordWindow.ShowDialog() ==
                true;

            if (!passwordChanged)
            {
                return;
            }

            /*
             * Der Server und der Credential Manager verwenden
             * jetzt das neue Passwort.
             *
             * Wir bauen deshalb die aktive Mail-Laufzeit
             * einmal kontrolliert neu auf, damit sämtliche
             * künftigen Verbindungen sofort das neue
             * Credential verwenden.
             */
            if (!await PauseAccountRuntimeAsync(
                    resetVisualState:
                        false))
            {
                MessageBox.Show(
                    this,
                    "Das Passwort wurde erfolgreich geändert.\n\n" +
                    "Die aktuelle Postfachansicht konnte momentan nicht neu verbunden werden. " +
                    "Spätestens beim nächsten Verbindungsaufbau verwendet Telenec Mail automatisch das neue Passwort.",
                    "Passwort geändert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            runtimePaused =
                true;

            await _viewModel
                .ReinitializeForAccountSwitchAsync();

            await ResumeAccountRuntimeAsync();

            runtimePaused =
                false;
        }
        catch (Exception exception)
        {
            if (runtimePaused)
            {
                try
                {
                    await ResumeAccountRuntimeAsync();

                    runtimePaused =
                        false;
                }
                catch
                {
                }
            }

            MessageBox.Show(
                this,
                "Der Passwortdialog konnte nicht vollständig abgeschlossen werden.\n\n" +
                "Falls das Passwort bereits erfolgreich geändert wurde, bleiben die neuen Zugangsdaten erhalten.\n\n" +
                $"Fehlerdetails:\n{exception.GetType().Name}: {exception.Message}",
                "Passwort ändern",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (runtimePaused)
            {
                try
                {
                    await ResumeAccountRuntimeAsync();
                }
                catch
                {
                }
            }

            _isAccountOperationInProgress =
                false;

            AccountMenuButton.IsEnabled =
                true;
        }
    }
}