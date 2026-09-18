using System.Windows;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ContactsWindow :
    Window
{
    private readonly ContactsViewModel
        _viewModel;

    private bool
        _initialized;

    public event Action<
        ContactData>?
        ComposeRequested;

    public ContactsWindow(
        ContactsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(
            viewModel);

        InitializeComponent();

        _viewModel =
            viewModel;

        DataContext =
            _viewModel;

        Loaded +=
            ContactsWindow_OnLoaded;
    }

    private async void ContactsWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized =
            true;

        try
        {
            await _viewModel
                .InitializeAsync();
        }
        catch (Exception exception)
        {
            ShowOperationError(
                "Die Kontakte konnten nicht geladen werden.",
                exception);
        }
    }

    private async void RefreshButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _viewModel
                .ReloadAsync();
        }
        catch (Exception exception)
        {
            ShowOperationError(
                "Die Kontakte konnten nicht aktualisiert werden.",
                exception);
        }
    }

    private void NewContactButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var editWindow =
            new ContactEditWindow(
                _viewModel)
            {
                Owner =
                    this
            };

        editWindow.ShowDialog();
    }

    private void EditContactButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var contact =
            _viewModel
                .SelectedContact;

        if (contact is null)
        {
            return;
        }

        var editWindow =
            new ContactEditWindow(
                _viewModel,
                contact)
            {
                Owner =
                    this
            };

        editWindow.ShowDialog();
    }

    private async void DeleteContactButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var contact =
            _viewModel
                .SelectedContact;

        if (contact is null)
        {
            return;
        }

        var result =
            MessageBox.Show(
                this,
                $"Soll der Kontakt „{contact.DisplayName}“ wirklich gelöscht werden?\n\n" +
                "Der Kontakt wird aus dem persönlichen Telenec-Adressbuch entfernt.",
                "Kontakt löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (result !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _viewModel
                .DeleteSelectedContactAsync();
        }
        catch (Exception exception)
        {
            ShowOperationError(
                "Der Kontakt konnte nicht gelöscht werden.",
                exception);
        }
    }

    private void ComposeMailButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var contact =
            _viewModel
                .SelectedContact;

        if (contact is null ||
            string.IsNullOrWhiteSpace(
                contact.EmailAddress))
        {
            return;
        }

        ComposeRequested?
            .Invoke(
                contact);
    }

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void ShowOperationError(
        string message,
        Exception exception)
    {
        MessageBox.Show(
            this,
            message +
            "\n\n" +
            exception.Message,
            "Telenec Mail",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}