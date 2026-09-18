using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ContactEditWindow :
    Window
{
    private readonly ContactsViewModel
        _viewModel;

    private readonly ContactData?
        _contact;

    private bool
        _autoDisplayNameEnabled;

    private bool
        _updatingDisplayName;

    private bool
        _isSaving;

    public ContactEditWindow(
        ContactsViewModel viewModel,
        ContactData? contact = null)
    {
        ArgumentNullException.ThrowIfNull(
            viewModel);

        InitializeComponent();

        _viewModel =
            viewModel;

        _contact =
            contact;

        InitializeForm();

        FirstNameTextBox.TextChanged +=
            NameSourceTextBox_OnTextChanged;

        LastNameTextBox.TextChanged +=
            NameSourceTextBox_OnTextChanged;

        EmailAddressTextBox.TextChanged +=
            NameSourceTextBox_OnTextChanged;

        DisplayNameTextBox.TextChanged +=
            DisplayNameTextBox_OnTextChanged;
    }

    private void InitializeForm()
    {
        if (_contact is null)
        {
            HeadingText.Text =
                "Neuer Kontakt";

            SubtitleText.Text =
                "Neuen Kontakt im persönlichen Adressbuch anlegen";

            SaveButton.Content =
                "Kontakt speichern";

            _autoDisplayNameEnabled =
                true;

            UpdateAutomaticDisplayName();

            return;
        }

        HeadingText.Text =
            "Kontakt bearbeiten";

        SubtitleText.Text =
            _contact.DisplayName;

        SaveButton.Content =
            "Änderungen speichern";

        FirstNameTextBox.Text =
            _contact.FirstName
            ?? string.Empty;

        LastNameTextBox.Text =
            _contact.LastName
            ?? string.Empty;

        DisplayNameTextBox.Text =
            _contact.DisplayName
            ?? string.Empty;

        EmailAddressTextBox.Text =
            _contact.EmailAddress
            ?? string.Empty;

        PhoneNumberTextBox.Text =
            _contact.PhoneNumber
            ?? string.Empty;

        /*
         * Entspricht der vorhandene Anzeigename exakt dem
         * automatisch aus Vor-/Nachname bzw. E-Mail
         * erzeugbaren Namen, behandeln wir ihn als
         * automatisch.
         *
         * Dadurch wird beispielsweise aus
         *
         *   Lukas Förster
         *
         * nach Änderung des Vornamens automatisch
         *
         *   Lukas Test Förster.
         *
         * Ein bewusst individueller Anzeigename bleibt
         * dagegen unangetastet.
         */
        var automaticDisplayName =
            BuildAutomaticDisplayName(
                FirstNameTextBox.Text,
                LastNameTextBox.Text,
                EmailAddressTextBox.Text);

        _autoDisplayNameEnabled =
            string.IsNullOrWhiteSpace(
                DisplayNameTextBox.Text) ||
            string.Equals(
                DisplayNameTextBox.Text.Trim(),
                automaticDisplayName,
                StringComparison.CurrentCulture);
    }

    private void NameSourceTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!_autoDisplayNameEnabled)
        {
            return;
        }

        UpdateAutomaticDisplayName();
    }

    private void DisplayNameTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_updatingDisplayName)
        {
            return;
        }

        /*
         * Sobald der Benutzer den Anzeigenamen selbst
         * bearbeitet, ist dieser bewusst individuell.
         *
         * Änderungen an Vor-/Nachname überschreiben ihn dann
         * nicht mehr automatisch.
         */
        _autoDisplayNameEnabled =
            false;
    }

    private void UpdateAutomaticDisplayName()
    {
        var automaticDisplayName =
            BuildAutomaticDisplayName(
                FirstNameTextBox.Text,
                LastNameTextBox.Text,
                EmailAddressTextBox.Text);

        _updatingDisplayName =
            true;

        try
        {
            DisplayNameTextBox.Text =
                automaticDisplayName;
        }
        finally
        {
            _updatingDisplayName =
                false;
        }
    }

    private async void SaveButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

        if (!HasMinimumContactData())
        {
            MessageBox.Show(
                this,
                "Bitte geben Sie mindestens einen Namen oder eine E-Mail-Adresse ein.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            FirstNameTextBox.Focus();

            return;
        }

        _isSaving =
            true;

        SaveButton.IsEnabled =
            false;

        try
        {
            if (_contact is null)
            {
                await CreateContactAsync();
            }
            else
            {
                await UpdateContactAsync();
            }

            DialogResult =
                true;

            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                (_contact is null
                    ? "Der Kontakt konnte nicht gespeichert werden."
                    : "Der Kontakt konnte nicht aktualisiert werden.") +
                "\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isSaving =
                false;

            SaveButton.IsEnabled =
                true;
        }
    }

    private async Task CreateContactAsync()
    {
        _viewModel.NewFirstName =
            FirstNameTextBox.Text;

        _viewModel.NewLastName =
            LastNameTextBox.Text;

        _viewModel.NewDisplayName =
            DisplayNameTextBox.Text;

        _viewModel.NewEmailAddress =
            EmailAddressTextBox.Text;

        _viewModel.NewPhoneNumber =
            PhoneNumberTextBox.Text;

        await _viewModel
            .CreateContactAsync();
    }

    private async Task UpdateContactAsync()
    {
        _viewModel.EditFirstName =
            FirstNameTextBox.Text;

        _viewModel.EditLastName =
            LastNameTextBox.Text;

        _viewModel.EditDisplayName =
            DisplayNameTextBox.Text;

        _viewModel.EditEmailAddress =
            EmailAddressTextBox.Text;

        _viewModel.EditPhoneNumber =
            PhoneNumberTextBox.Text;

        await _viewModel
            .UpdateSelectedContactAsync();
    }

    private bool HasMinimumContactData()
    {
        return
            !string.IsNullOrWhiteSpace(
                FirstNameTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                LastNameTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                DisplayNameTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                EmailAddressTextBox.Text);
    }

    private static string BuildAutomaticDisplayName(
        string? firstName,
        string? lastName,
        string? emailAddress)
    {
        var name =
            string.Join(
                " ",
                new[]
                {
                    firstName,
                    lastName
                }
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .Select(
                    value =>
                        value!.Trim()));

        if (!string.IsNullOrWhiteSpace(
                name))
        {
            return name;
        }

        return emailAddress?
                   .Trim()
               ?? string.Empty;
    }

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            false;

        Close();
    }
}