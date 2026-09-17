using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Contacts;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _contactsNavigationInitialized;

    private void MainWindowContacts_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        InitializeContactsNavigation();

        if (_contactsNavigationInitialized)
        {
            Loaded -=
                MainWindowContacts_OnLoaded;
        }
    }

    private void InitializeContactsNavigation()
    {
        if (_contactsNavigationInitialized)
        {
            return;
        }

        if (FolderListBox.Parent
            is not Grid navigationGrid)
        {
            return;
        }

        var insertionRow =
            navigationGrid
                .RowDefinitions
                .Count - 1;

        if (insertionRow < 0)
        {
            return;
        }

        var elementsToMove =
            navigationGrid
                .Children
                .OfType<UIElement>()
                .Where(
                    element =>
                        Grid.GetRow(
                            element) >=
                        insertionRow)
                .ToList();

        foreach (var element in
                 elementsToMove)
        {
            Grid.SetRow(
                element,
                Grid.GetRow(
                    element) + 1);
        }

        navigationGrid
            .RowDefinitions
            .Insert(
                insertionRow,
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

        var contactsButton =
            CreateContactsNavigationButton();

        Grid.SetRow(
            contactsButton,
            insertionRow);

        navigationGrid
            .Children
            .Add(
                contactsButton);

        _contactsNavigationInitialized =
            true;
    }

    private Button CreateContactsNavigationButton()
    {
        var button =
            new Button
            {
                Height =
                    40,

                Margin =
                    new Thickness(
                        12,
                        4,
                        12,
                        10),

                Padding =
                    new Thickness(
                        14,
                        0,
                        14,
                        0),

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                Cursor =
                    Cursors.Hand,

                HorizontalContentAlignment =
                    HorizontalAlignment.Left,

                ToolTip =
                    "Kontakte öffnen"
            };

        button.SetResourceReference(
            Control.ForegroundProperty,
            "Navigation.Text");

        var content =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var icon =
            new TextBlock
            {
                Text =
                    "\uE716",

                FontFamily =
                    new FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    17,

                Width =
                    28,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        icon.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Navigation.TextSecondary");

        var label =
            new TextBlock
            {
                Text =
                    "Kontakte",

                FontSize =
                    13,

                FontWeight =
                    FontWeights.SemiBold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        label.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Navigation.Text");

        content.Children.Add(
            icon);

        content.Children.Add(
            label);

        button.Content =
            content;

        button.MouseEnter +=
            (_, _) =>
            {
                button.SetResourceReference(
                    Control.BackgroundProperty,
                    "Navigation.Hover");
            };

        button.MouseLeave +=
            (_, _) =>
            {
                button.Background =
                    Brushes.Transparent;
            };

        button.Click +=
            ContactsNavigationButton_OnClick;

        return button;
    }

    private void ContactsNavigationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var contactService =
                ActivatorUtilities
                    .CreateInstance<
                        CardDavContactService>(
                        _serviceProvider);

            var contactsViewModel =
                new ContactsViewModel(
                    contactService);

            var contactsWindow =
                new ContactsWindow(
                    contactsViewModel)
                {
                    Owner =
                        this
                };

            contactsWindow.ComposeRequested +=
                contact =>
                {
                    OpenComposeWindowForContact(
                        contactsWindow,
                        contact);
                };

            contactsWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "Die Kontaktverwaltung konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenComposeWindowForContact(
        Window owner,
        ContactData contact)
    {
        if (string.IsNullOrWhiteSpace(
                contact.EmailAddress))
        {
            return;
        }

        try
        {
            var composeWindow =
                _serviceProvider
                    .GetRequiredService<
                        ComposeWindow>();

            composeWindow.Owner =
                owner;

            composeWindow
                .PrepareNewMessageTo(
                    contact.EmailAddress);

            composeWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "Die neue E-Mail konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}