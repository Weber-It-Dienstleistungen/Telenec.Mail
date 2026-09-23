using Microsoft.Extensions.DependencyInjection;
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

        categoryMenuItem.Items.Clear();

        if (categoryMenuItem.DataContext
            is not MailMessageItemViewModel message)
        {
            return;
        }

        var isEnabled =
            !_categoryMutationIsRunning &&
            !_viewModel.IsLoading &&
            message.UniqueId > 0;

        foreach (var category in
                 MailCategoryCatalog.All)
        {
            var menuItem =
                new MenuItem
                {
                    Header =
                        category.DisplayName,

                    Tag =
                        category,

                    DataContext =
                        message,

                    IsCheckable =
                        true,

                    IsChecked =
                        message.HasCategory(
                            category),

                    IsEnabled =
                        isEnabled
                };

            menuItem.Click +=
                CategoryMenuEntry_OnClick;

            categoryMenuItem.Items.Add(
                menuItem);
        }
    }

    private async void CategoryMenuEntry_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_categoryMutationIsRunning ||
            _viewModel.IsLoading ||
            sender is not MenuItem menuItem ||
            menuItem.Tag
                is not MailCategoryDefinition category ||
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
            !message.HasCategory(
                category);

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
                    category.Keyword,
                    shouldEnable);

            message.SetKeywordState(
                category.Keyword,
                shouldEnable);

            menuItem.IsChecked =
                message.HasCategory(
                    category);
        }
        catch (NotSupportedException)
        {
            menuItem.IsChecked =
                message.HasCategory(
                    category);

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
                message.HasCategory(
                    category);

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