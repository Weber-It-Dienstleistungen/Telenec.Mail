using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _categoryMutationIsRunning;

    private void CategoryMenuItem_OnSubmenuOpened(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MenuItem categoryMenuItem)
        {
            return;
        }

        var redCategoryMenuItem =
            categoryMenuItem
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag?.ToString(),
                            "RedCategory",
                            StringComparison.Ordinal));

        if (redCategoryMenuItem is null)
        {
            return;
        }

        if (categoryMenuItem.DataContext
            is not MailMessageItemViewModel message)
        {
            redCategoryMenuItem.IsChecked =
                false;

            redCategoryMenuItem.IsEnabled =
                false;

            return;
        }

        redCategoryMenuItem.IsChecked =
            message.HasRedCategory;

        redCategoryMenuItem.IsEnabled =
            !_categoryMutationIsRunning &&
            !_viewModel.IsLoading &&
            message.UniqueId > 0;
    }

    private async void RedCategoryMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_categoryMutationIsRunning ||
            _viewModel.IsLoading ||
            sender is not MenuItem menuItem ||
            menuItem.DataContext
                is not MailMessageItemViewModel message ||
            message.UniqueId == 0 ||
            !_viewModel.Messages.Contains(
                message))
        {
            return;
        }

        var folder =
            _viewModel.SelectedFolder;

        if (folder is null)
        {
            return;
        }

        var shouldEnable =
            !message.HasRedCategory;

        _categoryMutationIsRunning =
            true;

        menuItem.IsEnabled =
            false;

        try
        {
            var mailDataSource =
                _serviceProvider
                    .GetRequiredService<
                        IMailDataSource>();

            await mailDataSource
                .SetKeywordAsync(
                    folder.FolderId,
                    message.UniqueId,
                    MailMessageItemViewModel
                        .RedCategoryKeyword,
                    shouldEnable);

            message.SetKeywordState(
                MailMessageItemViewModel
                    .RedCategoryKeyword,
                shouldEnable);

            menuItem.IsChecked =
                message.HasRedCategory;
        }
        catch (NotSupportedException)
        {
            menuItem.IsChecked =
                message.HasRedCategory;

            MessageBox.Show(
                this,
                "Der Mailserver unterstützt in diesem Ordner keine benutzerdefinierten Kategorien.",
                "Kategorie",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch
        {
            menuItem.IsChecked =
                message.HasRedCategory;

            MessageBox.Show(
                this,
                "Die Kategorie konnte auf dem Mailserver nicht geändert werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Kategorie",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _categoryMutationIsRunning =
                false;

            menuItem.IsEnabled =
                true;
        }
    }
}