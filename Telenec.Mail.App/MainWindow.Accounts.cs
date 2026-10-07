using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string AddAccountMenuItemTag =
        "AccountManagement.AddAccount";

    private bool
        _isAccountOperationInProgress;

    private Grid?
        _accountNavigationGrid;

    private async void MainWindowAccounts_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -=
            MainWindowAccounts_OnLoaded;

        EnsureAccountManagementMenu();

        await RefreshActiveAccountPresentationAsync();
    }

    private async Task RefreshActiveAccountPresentationAsync()
    {
        var accounts =
            await _mailAccountStore
                .GetAccountsAsync();

        var activeAccount =
            accounts.FirstOrDefault(
                account =>
                    account.IsActive);

        if (activeAccount is null)
        {
            AccountEmailText.Text =
                "Telenec-Konto";

            return;
        }

        AccountEmailText.Text =
            activeAccount.EmailAddress;

        if (!EnsureAccountNavigationHost())
        {
            return;
        }

        RebuildAccountNavigation(
            accounts,
            activeAccount.AccountId);
    }

    private bool EnsureAccountNavigationHost()
    {
        if (_accountNavigationGrid is not null)
        {
            return true;
        }

        if (FolderListBox.Parent
            is not Grid navigationGrid)
        {
            return false;
        }

        var folderRow =
            Grid.GetRow(
                FolderListBox);

        navigationGrid.Children.Remove(
            FolderListBox);

        var accountNavigationGrid =
            new Grid();

        Grid.SetRow(
            accountNavigationGrid,
            folderRow);

        navigationGrid.Children.Add(
            accountNavigationGrid);

        _accountNavigationGrid =
            accountNavigationGrid;

        return true;
    }

    private void RebuildAccountNavigation(
        IReadOnlyList<MailAccount> accounts,
        Guid activeAccountId)
    {
        var host =
            _accountNavigationGrid;

        if (host is null)
        {
            return;
        }

        host.Children.Clear();
        host.RowDefinitions.Clear();

        var orderedAccounts =
            accounts
                .OrderByDescending(
                    account =>
                        account.CreatedAtUtc)
                .ThenBy(
                    account =>
                        account.EmailAddress,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var currentRow =
            0;

        foreach (var account in
                 orderedAccounts)
        {
            var isActive =
                account.AccountId ==
                activeAccountId;

            host.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

            var accountHeader =
                CreateAccountHeader(
                    account,
                    isActive);

            Grid.SetRow(
                accountHeader,
                currentRow);

            host.Children.Add(
                accountHeader);

            currentRow++;

            if (!isActive)
            {
                continue;
            }

            host.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            Grid.SetRow(
                FolderListBox,
                currentRow);

            host.Children.Add(
                FolderListBox);

            currentRow++;
        }
    }

    private Button CreateAccountHeader(
        MailAccount account,
        bool isActive)
    {
        var button =
            new Button
            {
                Tag =
                    account.AccountId,

                Height =
                    38,

                Margin =
                    new Thickness(
                        10,
                        1,
                        10,
                        3),

                Padding =
                    new Thickness(
                        10,
                        0,
                        10,
                        0),

                BorderThickness =
                    new Thickness(
                        0),

                Background =
                    Brushes.Transparent,

                Cursor =
                    Cursors.Hand,

                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                Focusable =
                    false,

                ToolTip =
                    isActive
                        ? "Aktives E-Mail-Konto"
                        : "Zu diesem E-Mail-Konto wechseln"
            };

        button.SetResourceReference(
            Control.ForegroundProperty,
            "Navigation.Text");

        if (isActive)
        {
            button.SetResourceReference(
                Control.BackgroundProperty,
                "Navigation.Selected");
        }

        var contentGrid =
            new Grid();

        contentGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        contentGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var chevron =
            new TextBlock
            {
                Text =
                    isActive
                        ? "▾"
                        : "▸",

                Width =
                    18,

                Margin =
                    new Thickness(
                        0,
                        0,
                        5,
                        0),

                FontSize =
                    13,

                VerticalAlignment =
                    VerticalAlignment.Center,

                TextAlignment =
                    TextAlignment.Center
            };

        chevron.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Navigation.TextSecondary");

        Grid.SetColumn(
            chevron,
            0);

        contentGrid.Children.Add(
            chevron);

        var emailText =
            new TextBlock
            {
                Text =
                    account.EmailAddress,

                FontSize =
                    12,

                FontWeight =
                    isActive
                        ? FontWeights.SemiBold
                        : FontWeights.Normal,

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        emailText.SetResourceReference(
            TextBlock.ForegroundProperty,
            isActive
                ? "Navigation.Text"
                : "Navigation.TextSecondary");

        Grid.SetColumn(
            emailText,
            1);

        contentGrid.Children.Add(
            emailText);

        button.Content =
            contentGrid;

        button.Click +=
            AccountHeaderButton_OnClick;

        return button;
    }

    private async void AccountHeaderButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isAccountOperationInProgress ||
            sender is not Button button ||
            button.Tag is not Guid accountId)
        {
            return;
        }

        MailAccount? activeAccount;

        try
        {
            activeAccount =
                await _mailAccountStore
                    .GetActiveAccountAsync();
        }
        catch
        {
            return;
        }

        if (activeAccount is null ||
            activeAccount.AccountId ==
            accountId)
        {
            return;
        }

        await SwitchToAccountAsync(
            activeAccount,
            accountId);
    }

    private async Task SwitchToAccountAsync(
        MailAccount previousAccount,
        Guid targetAccountId)
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

        var accountWasChanged =
            false;

        try
        {
            var targetCredential =
                await _credentialStore
                    .ReadAsync(
                        targetAccountId);

            if (targetCredential is null)
            {
                MessageBox.Show(
                    this,
                    "Für dieses E-Mail-Konto sind keine gespeicherten Zugangsdaten vorhanden.\n\n" +
                    "Bitte entfernen Sie das Konto und fügen Sie es erneut hinzu.",
                    "Telenec Mail",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (!await PauseAccountRuntimeAsync())
            {
                return;
            }

            runtimePaused =
                true;

            await _mailAccountStore
                .SetActiveAccountAsync(
                    targetAccountId);

            accountWasChanged =
                true;

            var mailboxInitializationTask =
                _viewModel
                    .ReinitializeForAccountSwitchAsync();

            await RefreshActiveAccountPresentationAsync();

            await mailboxInitializationTask;

            await ResumeAccountRuntimeAsync();

            runtimePaused =
                false;
        }
        catch (Exception exception)
        {
            if (accountWasChanged)
            {
                try
                {
                    await _mailAccountStore
                        .SetActiveAccountAsync(
                            previousAccount.AccountId);

                    var restoreTask =
                        _viewModel
                            .ReinitializeForAccountSwitchAsync();

                    await RefreshActiveAccountPresentationAsync();

                    await restoreTask;
                }
                catch
                {
                }
            }

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
                "Das E-Mail-Konto konnte nicht geöffnet werden.\n\n" +
                "Fehlerdetails:\n" +
                $"{exception.GetType().Name}: {exception.Message}",
                "Telenec Mail",
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

    private void EnsureAccountManagementMenu()
    {
        var contextMenu =
            AccountMenuButton.ContextMenu;

        if (contextMenu is null)
        {
            return;
        }

        var removeAccountMenuItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Header?.ToString(),
                            "Konto abmelden",
                            StringComparison.Ordinal) ||
                        string.Equals(
                            item.Header?.ToString(),
                            "Konto entfernen",
                            StringComparison.Ordinal));

        if (removeAccountMenuItem is not null)
        {
            /*
             * Der XAML-Handler stammt noch aus der alten
             * Ein-Konto-Logik.
             *
             * Wir lösen ihn hier ab, ohne die große
             * MainWindow.xaml.cs anfassen zu müssen.
             */
            removeAccountMenuItem.Click -=
                LogoutMenuItem_OnClick;

            removeAccountMenuItem.Click -=
                RemoveAccountMenuItem_OnClick;

            removeAccountMenuItem.Header =
                "Konto entfernen";

            removeAccountMenuItem.Click +=
                RemoveAccountMenuItem_OnClick;
        }

        var existingAddAccountItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag?.ToString(),
                            AddAccountMenuItemTag,
                            StringComparison.Ordinal));

        if (existingAddAccountItem is not null)
        {
            return;
        }

        if (removeAccountMenuItem is null)
        {
            return;
        }

        var removeAccountIndex =
            contextMenu.Items.IndexOf(
                removeAccountMenuItem);

        if (removeAccountIndex < 0)
        {
            return;
        }

        var addAccountMenuItem =
            new MenuItem
            {
                Header =
                    "Konto hinzufügen",

                Tag =
                    AddAccountMenuItemTag
            };

        addAccountMenuItem.Click +=
            AddAccountMenuItem_OnClick;

        contextMenu.Items.Insert(
            removeAccountIndex,
            new Separator());

        contextMenu.Items.Insert(
            removeAccountIndex,
            addAccountMenuItem);
    }

    private async void RemoveAccountMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isAccountOperationInProgress ||
            _isLoggingOut)
        {
            return;
        }

        MailAccount? accountToRemove;

        IReadOnlyList<MailAccount> accounts;

        try
        {
            accountToRemove =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            accounts =
                await _mailAccountStore
                    .GetAccountsAsync();
        }
        catch
        {
            MessageBox.Show(
                this,
                "Die Kontoinformationen konnten nicht geladen werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        if (accountToRemove is null)
        {
            return;
        }

        var remainingAccounts =
            accounts
                .Where(
                    account =>
                        account.AccountId !=
                        accountToRemove.AccountId)
                .OrderByDescending(
                    account =>
                        account.CreatedAtUtc)
                .ThenBy(
                    account =>
                        account.EmailAddress,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        MailAccount? nextAccount =
            null;

        if (remainingAccounts.Count > 0)
        {
            /*
             * Wir wählen nur ein Konto, das tatsächlich noch
             * gespeicherte Zugangsdaten besitzt.
             *
             * Damit entfernen wir das aktuelle Konto niemals,
             * bevor wir sicher wissen, dass ein anderes Konto
             * automatisch geöffnet werden kann.
             */
            foreach (var candidate in
                     remainingAccounts)
            {
                try
                {
                    var credential =
                        await _credentialStore
                            .ReadAsync(
                                candidate.AccountId);

                    if (credential is not null)
                    {
                        nextAccount =
                            candidate;

                        break;
                    }
                }
                catch
                {
                }
            }

            if (nextAccount is null)
            {
                MessageBox.Show(
                    this,
                    "Das aktuelle Konto kann momentan nicht entfernt werden.\n\n" +
                    "Für die übrigen eingerichteten Konten sind keine verwendbaren " +
                    "gespeicherten Zugangsdaten vorhanden.",
                    "Telenec Mail",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }
        }

        var confirmationText =
            remainingAccounts.Count > 0
                ? $"Möchten Sie das E-Mail-Konto\n\n" +
                  $"„{accountToRemove.EmailAddress}“\n\n" +
                  "wirklich von diesem Computer entfernen?\n\n" +
                  "Die gespeicherten Zugangsdaten und die lokale Konfiguration " +
                  "dieses Kontos werden entfernt. Das Postfach auf dem Mailserver " +
                  "bleibt unverändert.\n\n" +
                  "Anschließend wird automatisch zu einem Ihrer übrigen Konten gewechselt."
                : $"Möchten Sie das E-Mail-Konto\n\n" +
                  $"„{accountToRemove.EmailAddress}“\n\n" +
                  "wirklich von diesem Computer entfernen?\n\n" +
                  "Die gespeicherten Zugangsdaten und die lokale Konfiguration " +
                  "dieses Kontos werden entfernt. Das Postfach auf dem Mailserver " +
                  "bleibt unverändert.\n\n" +
                  "Da dies das letzte eingerichtete Konto ist, wird anschließend " +
                  "die Anmeldeseite geöffnet.";

        var confirmation =
            MessageBox.Show(
                this,
                confirmationText,
                "Konto entfernen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _isAccountOperationInProgress =
            true;

        _isLoggingOut =
            true;

        AccountMenuButton.IsEnabled =
            false;

        var runtimePaused =
            false;

        try
        {
            /*
             * FALL 1:
             * Es existiert mindestens ein weiteres nutzbares
             * Konto.
             *
             * Wir wechseln ZUERST erfolgreich dorthin.
             * Erst danach wird das bisherige Konto gelöscht.
             */
            if (nextAccount is not null)
            {
                if (!await PauseAccountRuntimeAsync())
                {
                    return;
                }

                runtimePaused =
                    true;

                await _mailAccountStore
                    .SetActiveAccountAsync(
                        nextAccount.AccountId);

                try
                {
                    var mailboxInitializationTask =
                        _viewModel
                            .ReinitializeForAccountSwitchAsync();

                    await RefreshActiveAccountPresentationAsync();

                    await mailboxInitializationTask;

                    await ResumeAccountRuntimeAsync();

                    runtimePaused =
                        false;
                }
                catch
                {
                    /*
                     * Der Wechsel selbst ist fehlgeschlagen.
                     *
                     * Das zu entfernende Konto existiert zu
                     * diesem Zeitpunkt noch vollständig und
                     * kann deshalb sicher wieder aktiviert
                     * werden.
                     */
                    await _mailAccountStore
                        .SetActiveAccountAsync(
                            accountToRemove.AccountId);

                    var restoreTask =
                        _viewModel
                            .ReinitializeForAccountSwitchAsync();

                    await RefreshActiveAccountPresentationAsync();

                    await restoreTask;

                    await ResumeAccountRuntimeAsync();

                    runtimePaused =
                        false;

                    throw;
                }

                /*
                 * Erst jetzt ist bewiesen, dass das nächste
                 * Konto funktioniert.
                 *
                 * Wir merken uns die Credential-Daten, damit
                 * wir sie wiederherstellen können, falls das
                 * SQLite-Löschen wider Erwarten scheitert.
                 */
                StoredCredential?
                    storedCredential = null;

                var credentialDeleted =
                    false;

                var accountDeleted =
                    false;

                try
                {
                    storedCredential =
                        await _credentialStore
                            .ReadAsync(
                                accountToRemove.AccountId);

                    await _credentialStore
                        .DeleteAsync(
                            accountToRemove.AccountId);

                    credentialDeleted =
                        true;

                    await _mailAccountStore
                        .DeleteAsync(
                            accountToRemove.AccountId);

                    accountDeleted =
                        true;

                    /*
                     * Der alte Account-Header verschwindet
                     * jetzt aus dem Accordion.
                     */
                    await RefreshActiveAccountPresentationAsync();
                }
                catch
                {
                    /*
                     * Wenn SQLite NICHT gelöscht hat, besteht
                     * das Konto samt lokaler Daten noch.
                     *
                     * Dann stellen wir ein bereits gelöschtes
                     * Credential nach Möglichkeit wieder her.
                     */
                    if (!accountDeleted &&
                        credentialDeleted &&
                        storedCredential is not null)
                    {
                        try
                        {
                            await _credentialStore
                                .SaveAsync(
                                    accountToRemove.AccountId,
                                    storedCredential.UserName,
                                    storedCredential.Password);
                        }
                        catch
                        {
                        }
                    }

                    await RefreshActiveAccountPresentationAsync();

                    MessageBox.Show(
                        this,
                        "Das Konto konnte nicht vollständig entfernt werden.\n\n" +
                        "Der Wechsel zum anderen Konto war erfolgreich. " +
                        "Bitte versuchen Sie das Entfernen später erneut.",
                        "Telenec Mail",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            /*
             * FALL 2:
             * Das letzte eingerichtete Konto wird entfernt.
             *
             * Das LoginWindow wird bereits vor der
             * destruktiven Operation erzeugt, damit dessen
             * Auflösung über DI nicht erst danach scheitern
             * kann.
             */
            var loginWindow =
                _serviceProvider
                    .GetRequiredService<
                        LoginWindow>();

            loginWindow.PrepareKnownAccount(
                null);

            StoredCredential?
                lastCredential = null;

            var lastCredentialDeleted =
                false;

            if (!await PauseAccountRuntimeAsync())
            {
                return;
            }

            runtimePaused =
                true;

            try
            {
                lastCredential =
                    await _credentialStore
                        .ReadAsync(
                            accountToRemove.AccountId);

                await _credentialStore
                    .DeleteAsync(
                        accountToRemove.AccountId);

                lastCredentialDeleted =
                    true;

                await _mailAccountStore
                    .DeleteAsync(
                        accountToRemove.AccountId);
            }
            catch
            {
                /*
                 * Ist das Account-Löschen fehlgeschlagen,
                 * versuchen wir das Credential wieder
                 * herzustellen.
                 */
                if (lastCredentialDeleted &&
                    lastCredential is not null)
                {
                    try
                    {
                        await _credentialStore
                            .SaveAsync(
                                accountToRemove.AccountId,
                                lastCredential.UserName,
                                lastCredential.Password);
                    }
                    catch
                    {
                    }
                }

                throw;
            }

            /*
             * Ab hier existiert bewusst kein eingerichtetes
             * Konto mehr.
             */
            runtimePaused =
                false;

            Application.Current.MainWindow =
                loginWindow;

            loginWindow.Show();

            Close();
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
                "Das Konto konnte nicht vollständig entfernt werden.\n\n" +
                "Fehlerdetails:\n" +
                $"{exception.GetType().Name}: {exception.Message}",
                "Telenec Mail",
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

            _isLoggingOut =
                false;

            _isAccountOperationInProgress =
                false;

            AccountMenuButton.IsEnabled =
                true;
        }
    }

    private async void AddAccountMenuItem_OnClick(
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

        MailAccount? previousAccount =
            null;

        var accountWasChanged =
            false;

        try
        {
            previousAccount =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (!await PauseAccountRuntimeAsync(
                    resetVisualState:
                        false))
            {
                return;
            }

            runtimePaused =
                true;

            var loginViewModel =
                _serviceProvider
                    .GetRequiredService<
                        LoginViewModel>();

            var addAccountWindow =
                new AddAccountWindow(
                    loginViewModel)
                {
                    Owner =
                        this
                };

            var result =
                addAccountWindow.ShowDialog();

            if (result != true)
            {
                await ResumeAccountRuntimeAsync();

                runtimePaused =
                    false;

                return;
            }

            var newActiveAccount =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            accountWasChanged =
                previousAccount is not null &&
                newActiveAccount is not null &&
                previousAccount.AccountId !=
                newActiveAccount.AccountId;

            ResetAccountScopedVisualState();

            var mailboxInitializationTask =
                _viewModel
                    .ReinitializeForAccountSwitchAsync();

            await RefreshActiveAccountPresentationAsync();

            await mailboxInitializationTask;

            await ResumeAccountRuntimeAsync();

            runtimePaused =
                false;
        }
        catch (Exception exception)
        {
            if (accountWasChanged &&
                previousAccount is not null)
            {
                try
                {
                    await _mailAccountStore
                        .SetActiveAccountAsync(
                            previousAccount.AccountId);

                    var restoreTask =
                        _viewModel
                            .ReinitializeForAccountSwitchAsync();

                    await RefreshActiveAccountPresentationAsync();

                    await restoreTask;
                }
                catch
                {
                }
            }

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
                "Das E-Mail-Konto konnte nicht hinzugefügt werden.\n\n" +
                "Fehlerdetails:\n" +
                $"{exception.GetType().Name}: {exception.Message}",
                "Telenec Mail",
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