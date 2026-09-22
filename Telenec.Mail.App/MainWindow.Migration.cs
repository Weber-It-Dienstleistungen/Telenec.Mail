using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Migration;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string MigrationMenuItemTag =
        "Telenec.Mail.MailboxMigration";

    private static readonly bool
        MigrationMenuClassHandlerRegistered =
            RegisterMigrationMenuClassHandler();

    private bool
        _migrationMenuInstalled;

    private static bool
        RegisterMigrationMenuClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnMigrationMenuLoaded));

        return true;
    }

    private static void MainWindow_OnMigrationMenuLoaded(
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

        window.InstallMigrationMenu();
    }

    private void InstallMigrationMenu()
    {
        if (_migrationMenuInstalled)
        {
            return;
        }

        var contextMenu =
            AccountMenuButton.ContextMenu;

        if (contextMenu is null)
        {
            return;
        }

        var existingMigrationItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag as string,
                            MigrationMenuItemTag,
                            StringComparison.Ordinal));

        if (existingMigrationItem is not null)
        {
            _migrationMenuInstalled =
                true;

            return;
        }

        var migrationMenuItem =
            new MenuItem
            {
                Header =
                    "Postfach migrieren …",

                Tag =
                    MigrationMenuItemTag
            };

        migrationMenuItem.Click +=
            MigrationMenuItem_OnClick;

        contextMenu.Items.Insert(
            0,
            migrationMenuItem);

        contextMenu.Items.Insert(
            1,
            new Separator());

        _migrationMenuInstalled =
            true;
    }

    private async void MigrationMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is null ||
                string.IsNullOrWhiteSpace(
                    account.EmailAddress))
            {
                MessageBox.Show(
                    this,
                    "Das aktuell angemeldete Telenec-Mail-Konto konnte nicht ermittelt werden.",
                    "Postfachmigration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var targetContactsViewModel =
                CreateContactsViewModel();

            var contactMigrationService =
                new LegacyContactMigrationService(
                    _mailAccountStore,
                    _credentialStore);

            var mailProofOfWorkService =
                new LegacyMailProofOfWorkService(
                    _mailAccountStore,
                    _credentialStore);

            var mailboxMigrationService =
                new LegacyMailboxMigrationService(
                    _mailAccountStore,
                    _credentialStore);

            var targetMailFolders =
                _viewModel
                    .MailFolders
                    .Select(
                        folder =>
                            new MailFolderData(
                                FolderId:
                                    folder.FolderId,

                                DisplayName:
                                    folder.DisplayName,

                                HeaderSubtitle:
                                    folder.HeaderSubtitle,

                                UnreadCount:
                                    folder.UnreadCount,

                                HasSeparatorAfter:
                                    folder.HasSeparatorAfter,

                                MessageCount:
                                    folder.MessageCount))
                    .ToArray();

            var migrationWindow =
                new MailboxMigrationWindow(
                    account.EmailAddress,
                    targetContactsViewModel,
                    contactMigrationService,
                    mailProofOfWorkService)
                {
                    Owner =
                        this,

                    TargetMailFolders =
                        targetMailFolders,

                    MailMigrationService =
                        mailboxMigrationService
                };

            migrationWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Der Migrationsassistent konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Postfachmigration",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}