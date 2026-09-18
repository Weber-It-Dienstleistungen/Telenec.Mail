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

    private bool
        _senderContactActionInitialized;

    private bool
        _senderContactActionRunning;

    private Button?
        _senderContactActionButton;

    private void MainWindowContacts_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        InitializeContactsNavigation();

        InitializeSenderContactAction();

        if (_contactsNavigationInitialized &&
            _senderContactActionInitialized)
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

    private void InitializeSenderContactAction()
    {
        if (_senderContactActionInitialized)
        {
            return;
        }

        var senderTextBlock =
            FindTextBlockByBindingPath(
                this,
                "SelectedMessage.Sender");

        if (senderTextBlock is null ||
            senderTextBlock.Parent
                is not StackPanel senderStackPanel)
        {
            return;
        }

        var senderIndex =
            senderStackPanel
                .Children
                .IndexOf(
                    senderTextBlock);

        if (senderIndex < 0)
        {
            return;
        }

        senderStackPanel
            .Children
            .RemoveAt(
                senderIndex);

        var senderRow =
            new Grid();

        senderRow
            .ColumnDefinitions
            .Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

        senderRow
            .ColumnDefinitions
            .Add(
                new ColumnDefinition
                {
                    Width =
                        GridLength.Auto
                });

        senderTextBlock.TextTrimming =
            TextTrimming.CharacterEllipsis;

        senderTextBlock.VerticalAlignment =
            VerticalAlignment.Center;

        Grid.SetColumn(
            senderTextBlock,
            0);

        senderRow
            .Children
            .Add(
                senderTextBlock);

        var actionButton =
            new Button
            {
                Content =
                    "Zu Kontakten hinzufügen",

                Margin =
                    new Thickness(
                        12,
                        0,
                        0,
                        0),

                Padding =
                    new Thickness(
                        6,
                        2,
                        6,
                        2),

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                FontSize =
                    11,

                Cursor =
                    Cursors.Hand,

                VerticalAlignment =
                    VerticalAlignment.Center,

                ToolTip =
                    "Absender als Kontakt speichern"
            };

        actionButton.SetResourceReference(
            Control.ForegroundProperty,
            "Brand.Primary");

        actionButton.Click +=
            SenderContactActionButton_OnClick;

        Grid.SetColumn(
            actionButton,
            1);

        senderRow
            .Children
            .Add(
                actionButton);

        senderStackPanel
            .Children
            .Insert(
                senderIndex,
                senderRow);

        _senderContactActionButton =
            actionButton;

        _senderContactActionInitialized =
            true;
    }

    private static TextBlock?
        FindTextBlockByBindingPath(
            DependencyObject root,
            string bindingPath)
    {
        var childCount =
            VisualTreeHelper
                .GetChildrenCount(
                    root);

        for (var index = 0;
             index < childCount;
             index++)
        {
            var child =
                VisualTreeHelper
                    .GetChild(
                        root,
                        index);

            if (child is TextBlock textBlock)
            {
                var bindingExpression =
                    textBlock
                        .GetBindingExpression(
                            TextBlock.TextProperty);

                var currentPath =
                    bindingExpression?
                        .ParentBinding?
                        .Path?
                        .Path;

                if (string.Equals(
                        currentPath,
                        bindingPath,
                        StringComparison.Ordinal))
                {
                    return textBlock;
                }
            }

            var nestedResult =
                FindTextBlockByBindingPath(
                    child,
                    bindingPath);

            if (nestedResult is not null)
            {
                return nestedResult;
            }
        }

        return null;
    }

    private async void SenderContactActionButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_senderContactActionRunning)
        {
            return;
        }

        var message =
            GetDisplayedMessageForContactAction();

        if (message is null)
        {
            return;
        }

        var senderAddress =
            message
                .SenderAddress?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                senderAddress))
        {
            MessageBox.Show(
                this,
                "Für diesen Absender ist keine E-Mail-Adresse verfügbar.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _senderContactActionRunning =
            true;

        if (_senderContactActionButton is not null)
        {
            _senderContactActionButton.IsEnabled =
                false;

            _senderContactActionButton.Content =
                "Kontakte werden geprüft …";
        }

        try
        {
            var contactsViewModel =
                CreateContactsViewModel();

            await contactsViewModel
                .InitializeAsync();

            var existingContact =
                contactsViewModel
                    .FindContactByEmail(
                        senderAddress);

            if (existingContact is not null)
            {
                var result =
                    MessageBox.Show(
                        this,
                        $"Die E-Mail-Adresse „{senderAddress}“ ist bereits im Kontakt " +
                        $"„{existingContact.DisplayName}“ gespeichert.\n\n" +
                        "Möchten Sie den vorhandenen Kontakt anzeigen?",
                        "Kontakt bereits vorhanden",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information,
                        MessageBoxResult.Yes);

                if (result ==
                    MessageBoxResult.Yes)
                {
                    contactsViewModel
                        .SelectedContact =
                            existingContact;

                    ShowContactsWindow(
                        contactsViewModel);
                }

                return;
            }

            var senderDisplayName =
                GetSenderDisplayNameForNewContact(
                    message);

            var editWindow =
                new ContactEditWindow(
                    contactsViewModel,
                    contact:
                        null,
                    initialDisplayName:
                        senderDisplayName,
                    initialEmailAddress:
                        senderAddress)
                {
                    Owner =
                        this
                };

            editWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Der Absender konnte nicht zu den Kontakten hinzugefügt werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _senderContactActionRunning =
                false;

            if (_senderContactActionButton is not null)
            {
                _senderContactActionButton.Content =
                    "Zu Kontakten hinzufügen";

                _senderContactActionButton.IsEnabled =
                    true;
            }
        }
    }

    private MailMessageItemViewModel?
        GetDisplayedMessageForContactAction()
    {
        if (_searchIsActive)
        {
            return _searchPreviewMessage;
        }

        return _viewModel
            .SelectedMessage;
    }

    private static string?
        GetSenderDisplayNameForNewContact(
            MailMessageItemViewModel message)
    {
        var senderName =
            message
                .Sender?
                .Trim();

        var senderAddress =
            message
                .SenderAddress?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                senderName) ||
            string.Equals(
                senderName,
                "Unbekannter Absender",
                StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(
                senderName,
                senderAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return senderName;
    }

    private ContactsViewModel
        CreateContactsViewModel()
    {
        var contactService =
            ActivatorUtilities
                .CreateInstance<
                    CardDavContactService>(
                    _serviceProvider);

        return new ContactsViewModel(
            contactService);
    }

    private void ContactsNavigationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var contactsViewModel =
                CreateContactsViewModel();

            ShowContactsWindow(
                contactsViewModel);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Die Kontaktverwaltung konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowContactsWindow(
        ContactsViewModel contactsViewModel)
    {
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
                owner,
                "Die neue E-Mail konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}