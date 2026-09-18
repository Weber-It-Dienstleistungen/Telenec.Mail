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

        AcademicTitleTextBox.TextChanged +=
            NameSourceTextBox_OnTextChanged;

        FirstNameTextBox.TextChanged +=
            NameSourceTextBox_OnTextChanged;

        MiddleNameTextBox.TextChanged +=
            NameSourceTextBox_OnTextChanged;

        LastNameTextBox.TextChanged +=
            NameSourceTextBox_OnTextChanged;

        NameSuffixTextBox.TextChanged +=
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

        SalutationTextBox.Text =
            _contact.Salutation
            ?? string.Empty;

        AcademicTitleTextBox.Text =
            _contact.AcademicTitle
            ?? string.Empty;

        FirstNameTextBox.Text =
            _contact.FirstName
            ?? string.Empty;

        MiddleNameTextBox.Text =
            _contact.MiddleName
            ?? string.Empty;

        LastNameTextBox.Text =
            _contact.LastName
            ?? string.Empty;

        NameSuffixTextBox.Text =
            _contact.NameSuffix
            ?? string.Empty;

        NicknameTextBox.Text =
            _contact.Nickname
            ?? string.Empty;

        DisplayNameTextBox.Text =
            _contact.DisplayName
            ?? string.Empty;

        EmailAddressTextBox.Text =
            _contact.EmailAddress
            ?? string.Empty;

        BusinessEmailAddressTextBox.Text =
            _contact.BusinessEmailAddress
            ?? string.Empty;

        PrivateEmailAddressTextBox.Text =
            _contact.PrivateEmailAddress
            ?? string.Empty;

        AdditionalEmailAddressesTextBox.Text =
            JoinLines(
                _contact.AdditionalEmailAddresses);

        PhoneNumberTextBox.Text =
            _contact.PhoneNumber
            ?? string.Empty;

        MobilePhoneNumberTextBox.Text =
            _contact.MobilePhoneNumber
            ?? string.Empty;

        BusinessPhoneNumberTextBox.Text =
            _contact.BusinessPhoneNumber
            ?? string.Empty;

        PrivatePhoneNumberTextBox.Text =
            _contact.PrivatePhoneNumber
            ?? string.Empty;

        FaxNumberTextBox.Text =
            _contact.FaxNumber
            ?? string.Empty;

        AdditionalPhoneNumbersTextBox.Text =
            JoinLines(
                _contact.AdditionalPhoneNumbers);

        CompanyTextBox.Text =
            _contact.Company
            ?? string.Empty;

        DepartmentTextBox.Text =
            _contact.Department
            ?? string.Empty;

        JobTitleTextBox.Text =
            _contact.JobTitle
            ?? string.Empty;

        LoadAddress(
            _contact.HomeAddress,
            HomePostOfficeBoxTextBox,
            HomeExtendedAddressTextBox,
            HomeStreetTextBox,
            HomeCityTextBox,
            HomeRegionTextBox,
            HomePostalCodeTextBox,
            HomeCountryTextBox);

        LoadAddress(
            _contact.WorkAddress,
            WorkPostOfficeBoxTextBox,
            WorkExtendedAddressTextBox,
            WorkStreetTextBox,
            WorkCityTextBox,
            WorkRegionTextBox,
            WorkPostalCodeTextBox,
            WorkCountryTextBox);

        WebsiteTextBox.Text =
            _contact.Website
            ?? string.Empty;

        BirthdayTextBox.Text =
            _contact.Birthday
            ?? string.Empty;

        CategoriesTextBox.Text =
            string.Join(
                ", ",
                _contact.Categories);

        NotesTextBox.Text =
            _contact.Notes
            ?? string.Empty;

        var automaticDisplayName =
            BuildAutomaticDisplayName();

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

        _autoDisplayNameEnabled =
            false;
    }

    private void UpdateAutomaticDisplayName()
    {
        var automaticDisplayName =
            BuildAutomaticDisplayName();

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

    private string BuildAutomaticDisplayName()
    {
        var name =
            string.Join(
                " ",
                new[]
                {
                    AcademicTitleTextBox.Text,
                    FirstNameTextBox.Text,
                    MiddleNameTextBox.Text,
                    LastNameTextBox.Text,
                    NameSuffixTextBox.Text
                }
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .Select(
                    value =>
                        value.Trim()));

        if (!string.IsNullOrWhiteSpace(
                name))
        {
            return name;
        }

        return EmailAddressTextBox.Text?
                   .Trim()
               ?? string.Empty;
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
        var request =
            new ContactCreateRequest
            {
                Salutation =
                    NullIfWhiteSpace(
                        SalutationTextBox.Text),

                AcademicTitle =
                    NullIfWhiteSpace(
                        AcademicTitleTextBox.Text),

                FirstName =
                    NullIfWhiteSpace(
                        FirstNameTextBox.Text),

                MiddleName =
                    NullIfWhiteSpace(
                        MiddleNameTextBox.Text),

                LastName =
                    NullIfWhiteSpace(
                        LastNameTextBox.Text),

                NameSuffix =
                    NullIfWhiteSpace(
                        NameSuffixTextBox.Text),

                Nickname =
                    NullIfWhiteSpace(
                        NicknameTextBox.Text),

                DisplayName =
                    NullIfWhiteSpace(
                        DisplayNameTextBox.Text),

                EmailAddress =
                    NullIfWhiteSpace(
                        EmailAddressTextBox.Text),

                BusinessEmailAddress =
                    NullIfWhiteSpace(
                        BusinessEmailAddressTextBox.Text),

                PrivateEmailAddress =
                    NullIfWhiteSpace(
                        PrivateEmailAddressTextBox.Text),

                AdditionalEmailAddresses =
                    SplitLines(
                        AdditionalEmailAddressesTextBox.Text),

                PhoneNumber =
                    NullIfWhiteSpace(
                        PhoneNumberTextBox.Text),

                MobilePhoneNumber =
                    NullIfWhiteSpace(
                        MobilePhoneNumberTextBox.Text),

                BusinessPhoneNumber =
                    NullIfWhiteSpace(
                        BusinessPhoneNumberTextBox.Text),

                PrivatePhoneNumber =
                    NullIfWhiteSpace(
                        PrivatePhoneNumberTextBox.Text),

                FaxNumber =
                    NullIfWhiteSpace(
                        FaxNumberTextBox.Text),

                AdditionalPhoneNumbers =
                    SplitLines(
                        AdditionalPhoneNumbersTextBox.Text),

                Company =
                    NullIfWhiteSpace(
                        CompanyTextBox.Text),

                Department =
                    NullIfWhiteSpace(
                        DepartmentTextBox.Text),

                JobTitle =
                    NullIfWhiteSpace(
                        JobTitleTextBox.Text),

                HomeAddress =
                    CreateAddress(
                        HomePostOfficeBoxTextBox,
                        HomeExtendedAddressTextBox,
                        HomeStreetTextBox,
                        HomeCityTextBox,
                        HomeRegionTextBox,
                        HomePostalCodeTextBox,
                        HomeCountryTextBox),

                WorkAddress =
                    CreateAddress(
                        WorkPostOfficeBoxTextBox,
                        WorkExtendedAddressTextBox,
                        WorkStreetTextBox,
                        WorkCityTextBox,
                        WorkRegionTextBox,
                        WorkPostalCodeTextBox,
                        WorkCountryTextBox),

                Website =
                    NullIfWhiteSpace(
                        WebsiteTextBox.Text),

                Birthday =
                    NullIfWhiteSpace(
                        BirthdayTextBox.Text),

                Notes =
                    NullIfWhiteSpace(
                        NotesTextBox.Text),

                Categories =
                    SplitCategories(
                        CategoriesTextBox.Text)
            };

        await _viewModel
            .CreateContactAsync(
                request);
    }

    private async Task UpdateContactAsync()
    {
        var request =
            new ContactUpdateRequest
            {
                Salutation =
                    SalutationTextBox.Text.Trim(),

                AcademicTitle =
                    AcademicTitleTextBox.Text.Trim(),

                FirstName =
                    NullIfWhiteSpace(
                        FirstNameTextBox.Text),

                MiddleName =
                    MiddleNameTextBox.Text.Trim(),

                LastName =
                    NullIfWhiteSpace(
                        LastNameTextBox.Text),

                NameSuffix =
                    NameSuffixTextBox.Text.Trim(),

                Nickname =
                    NicknameTextBox.Text.Trim(),

                DisplayName =
                    NullIfWhiteSpace(
                        DisplayNameTextBox.Text),

                EmailAddress =
                    NullIfWhiteSpace(
                        EmailAddressTextBox.Text),

                BusinessEmailAddress =
                    BusinessEmailAddressTextBox.Text.Trim(),

                PrivateEmailAddress =
                    PrivateEmailAddressTextBox.Text.Trim(),

                AdditionalEmailAddresses =
                    SplitLines(
                        AdditionalEmailAddressesTextBox.Text),

                PhoneNumber =
                    NullIfWhiteSpace(
                        PhoneNumberTextBox.Text),

                MobilePhoneNumber =
                    MobilePhoneNumberTextBox.Text.Trim(),

                BusinessPhoneNumber =
                    BusinessPhoneNumberTextBox.Text.Trim(),

                PrivatePhoneNumber =
                    PrivatePhoneNumberTextBox.Text.Trim(),

                FaxNumber =
                    FaxNumberTextBox.Text.Trim(),

                AdditionalPhoneNumbers =
                    SplitLines(
                        AdditionalPhoneNumbersTextBox.Text),

                Company =
                    CompanyTextBox.Text.Trim(),

                Department =
                    DepartmentTextBox.Text.Trim(),

                JobTitle =
                    JobTitleTextBox.Text.Trim(),

                HomeAddress =
                    CreateAddress(
                        HomePostOfficeBoxTextBox,
                        HomeExtendedAddressTextBox,
                        HomeStreetTextBox,
                        HomeCityTextBox,
                        HomeRegionTextBox,
                        HomePostalCodeTextBox,
                        HomeCountryTextBox),

                WorkAddress =
                    CreateAddress(
                        WorkPostOfficeBoxTextBox,
                        WorkExtendedAddressTextBox,
                        WorkStreetTextBox,
                        WorkCityTextBox,
                        WorkRegionTextBox,
                        WorkPostalCodeTextBox,
                        WorkCountryTextBox),

                Website =
                    WebsiteTextBox.Text.Trim(),

                Birthday =
                    BirthdayTextBox.Text.Trim(),

                Notes =
                    NotesTextBox.Text.Trim(),

                Categories =
                    SplitCategories(
                        CategoriesTextBox.Text)
            };

        await _viewModel
            .UpdateSelectedContactAsync(
                request);
    }

    private bool HasMinimumContactData()
    {
        return
            !string.IsNullOrWhiteSpace(
                FirstNameTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                MiddleNameTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                LastNameTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                DisplayNameTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                EmailAddressTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                BusinessEmailAddressTextBox.Text) ||

            !string.IsNullOrWhiteSpace(
                PrivateEmailAddressTextBox.Text) ||

            SplitLines(
                    AdditionalEmailAddressesTextBox.Text)
                .Count >
                0;
    }

    private static void LoadAddress(
        ContactPostalAddress? address,
        TextBox postOfficeBoxTextBox,
        TextBox extendedAddressTextBox,
        TextBox streetTextBox,
        TextBox cityTextBox,
        TextBox regionTextBox,
        TextBox postalCodeTextBox,
        TextBox countryTextBox)
    {
        postOfficeBoxTextBox.Text =
            address?.PostOfficeBox
            ?? string.Empty;

        extendedAddressTextBox.Text =
            address?.ExtendedAddress
            ?? string.Empty;

        streetTextBox.Text =
            address?.Street
            ?? string.Empty;

        cityTextBox.Text =
            address?.City
            ?? string.Empty;

        regionTextBox.Text =
            address?.Region
            ?? string.Empty;

        postalCodeTextBox.Text =
            address?.PostalCode
            ?? string.Empty;

        countryTextBox.Text =
            address?.Country
            ?? string.Empty;
    }

    private static ContactPostalAddress CreateAddress(
        TextBox postOfficeBoxTextBox,
        TextBox extendedAddressTextBox,
        TextBox streetTextBox,
        TextBox cityTextBox,
        TextBox regionTextBox,
        TextBox postalCodeTextBox,
        TextBox countryTextBox)
    {
        return new ContactPostalAddress
        {
            PostOfficeBox =
                NullIfWhiteSpace(
                    postOfficeBoxTextBox.Text),

            ExtendedAddress =
                NullIfWhiteSpace(
                    extendedAddressTextBox.Text),

            Street =
                NullIfWhiteSpace(
                    streetTextBox.Text),

            City =
                NullIfWhiteSpace(
                    cityTextBox.Text),

            Region =
                NullIfWhiteSpace(
                    regionTextBox.Text),

            PostalCode =
                NullIfWhiteSpace(
                    postalCodeTextBox.Text),

            Country =
                NullIfWhiteSpace(
                    countryTextBox.Text)
        };
    }

    private static IReadOnlyList<string> SplitLines(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return Array.Empty<string>();
        }

        return value
            .Replace(
                "\r\n",
                "\n")
            .Replace(
                '\r',
                '\n')
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(
                item =>
                    !string.IsNullOrWhiteSpace(
                        item))
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitCategories(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return Array.Empty<string>();
        }

        return value
            .Replace(
                "\r\n",
                "\n")
            .Replace(
                '\r',
                '\n')
            .Split(
                new[]
                {
                    ',',
                    ';',
                    '\n'
                },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(
                item =>
                    !string.IsNullOrWhiteSpace(
                        item))
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string JoinLines(
        IEnumerable<string>? values)
    {
        if (values is null)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            values);
    }

    private static string? NullIfWhiteSpace(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value.Trim();
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