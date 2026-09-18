using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ContactEditWindow :
    Window
{
    private const int MaximumPhotoDimension =
        512;

    private readonly ContactsViewModel
        _viewModel;

    private readonly ContactData?
        _contact;

    private readonly List<string>
        _availableCategories =
            new();

    private readonly HashSet<string>
        _selectedCategories =
            new(
                StringComparer.CurrentCultureIgnoreCase);

    private bool
        _autoDisplayNameEnabled;

    private bool
        _updatingDisplayName;

    private bool
        _isSaving;

    private bool
        _photoChanged;

    private bool
        _photoRemoved;

    private byte[]?
        _photoData;

    private string?
        _photoMediaType;

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

        InitializeCategorySelector();

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

            UpdatePhotoPreview();

            return;
        }

        HeadingText.Text =
            "Kontakt bearbeiten";

        SubtitleText.Text =
            _contact.DisplayName;

        SaveButton.Content =
            "Änderungen speichern";

        _photoData =
            _contact.PhotoData?
                .ToArray();

        _photoMediaType =
            _contact.PhotoMediaType;

        UpdatePhotoPreview();

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

    private void InitializeCategorySelector()
    {
        CategoriesTextBox.IsReadOnly =
            true;

        CategoriesTextBox.IsReadOnlyCaretVisible =
            false;

        CategoriesTextBox.Cursor =
            Cursors.Hand;

        CategoriesTextBox.ToolTip =
            "Klicken, um Gruppen und Kategorien auszuwählen.";

        CategoriesTextBox.PreviewMouseLeftButtonDown +=
            CategoriesTextBox_OnPreviewMouseLeftButtonDown;

        foreach (var category in
                 _viewModel
                     .GetAvailableCategories())
        {
            EnsureAvailableCategory(
                category);
        }

        if (_contact is not null)
        {
            foreach (var category in
                     _contact.Categories)
            {
                var canonicalCategory =
                    EnsureAvailableCategory(
                        category);

                if (!string.IsNullOrWhiteSpace(
                        canonicalCategory))
                {
                    _selectedCategories.Add(
                        canonicalCategory);
                }
            }
        }

        UpdateCategoriesTextBox();
    }

    private void CategoriesTextBox_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled =
            true;

        OpenCategoryMenu();
    }

    private void OpenCategoryMenu()
    {
        var contextMenu =
            new ContextMenu
            {
                PlacementTarget =
                    CategoriesTextBox,

                Placement =
                    PlacementMode.Bottom
            };

        if (_availableCategories.Count ==
            0)
        {
            contextMenu.Items.Add(
                new MenuItem
                {
                    Header =
                        "Noch keine Kategorien vorhanden",

                    IsEnabled =
                        false
                });
        }
        else
        {
            foreach (var category in
                     _availableCategories
                         .OrderBy(
                             value =>
                                 value,
                             StringComparer.CurrentCultureIgnoreCase))
            {
                var menuItem =
                    new MenuItem
                    {
                        Header =
                            category,

                        Tag =
                            category,

                        IsCheckable =
                            true,

                        IsChecked =
                            _selectedCategories
                                .Contains(
                                    category),

                        StaysOpenOnClick =
                            true
                    };

                menuItem.Click +=
                    CategoryMenuItem_OnClick;

                contextMenu.Items.Add(
                    menuItem);
            }
        }

        contextMenu.Items.Add(
            new Separator());

        var addCategoryItem =
            new MenuItem
            {
                Header =
                    "+ Neue Kategorie …"
            };

        addCategoryItem.Click +=
            AddCategoryMenuItem_OnClick;

        contextMenu.Items.Add(
            addCategoryItem);

        CategoriesTextBox.ContextMenu =
            contextMenu;

        contextMenu.IsOpen =
            true;
    }

    private void CategoryMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MenuItem
            {
                Tag: string category
            } menuItem)
        {
            return;
        }

        if (menuItem.IsChecked)
        {
            _selectedCategories.Add(
                category);
        }
        else
        {
            _selectedCategories.Remove(
                category);
        }

        UpdateCategoriesTextBox();
    }

    private void AddCategoryMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new ContactCategoryWindow
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() !=
            true)
        {
            return;
        }

        var canonicalCategory =
            EnsureAvailableCategory(
                dialog.CategoryName);

        if (string.IsNullOrWhiteSpace(
                canonicalCategory))
        {
            return;
        }

        _selectedCategories.Add(
            canonicalCategory);

        UpdateCategoriesTextBox();
    }

    private string EnsureAvailableCategory(
        string? category)
    {
        var normalized =
            category?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                normalized))
        {
            return string.Empty;
        }

        var existing =
            _availableCategories
                .FirstOrDefault(
                    value =>
                        string.Equals(
                            value,
                            normalized,
                            StringComparison.CurrentCultureIgnoreCase));

        if (!string.IsNullOrWhiteSpace(
                existing))
        {
            return existing;
        }

        _availableCategories.Add(
            normalized);

        _availableCategories.Sort(
            StringComparer.CurrentCultureIgnoreCase);

        return normalized;
    }

    private void UpdateCategoriesTextBox()
    {
        CategoriesTextBox.Text =
            string.Join(
                ", ",
                GetSelectedCategories());
    }

    private IReadOnlyList<string>
        GetSelectedCategories()
    {
        return _availableCategories
            .Where(
                category =>
                    _selectedCategories
                        .Contains(
                            category))
            .OrderBy(
                category =>
                    category,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private void SelectPhotoButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new OpenFileDialog
            {
                Title =
                    "Kontaktfoto auswählen",

                Filter =
                    "Bilddateien (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|" +
                    "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
                    "PNG (*.png)|*.png",

                CheckFileExists =
                    true,

                Multiselect =
                    false
            };

        if (dialog.ShowDialog(
                this) !=
            true)
        {
            return;
        }

        try
        {
            var preparedPhoto =
                PreparePhoto(
                    dialog.FileName);

            _photoData =
                preparedPhoto.Data;

            _photoMediaType =
                preparedPhoto.MediaType;

            _photoChanged =
                true;

            _photoRemoved =
                false;

            UpdatePhotoPreview();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Das ausgewählte Bild konnte nicht als Kontaktfoto verwendet werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RemovePhotoButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        _photoData =
            null;

        _photoMediaType =
            null;

        _photoChanged =
            true;

        _photoRemoved =
            true;

        UpdatePhotoPreview();
    }

    private static PreparedPhoto PreparePhoto(
        string filePath)
    {
        using var fileStream =
            File.OpenRead(
                filePath);

        var decoder =
            BitmapDecoder.Create(
                fileStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException(
                "Die Bilddatei enthält kein lesbares Bild.");
        }

        BitmapSource source =
            decoder.Frames[0];

        if (source.PixelWidth <= 0 ||
            source.PixelHeight <= 0)
        {
            throw new InvalidOperationException(
                "Das Bild besitzt ungültige Abmessungen.");
        }

        var largestDimension =
            Math.Max(
                source.PixelWidth,
                source.PixelHeight);

        if (largestDimension >
            MaximumPhotoDimension)
        {
            var scale =
                (double)MaximumPhotoDimension /
                largestDimension;

            var transformed =
                new TransformedBitmap(
                    source,
                    new ScaleTransform(
                        scale,
                        scale));

            transformed.Freeze();

            source =
                transformed;
        }

        var encoder =
            new JpegBitmapEncoder
            {
                QualityLevel =
                    88
            };

        encoder.Frames.Add(
            BitmapFrame.Create(
                source));

        using var outputStream =
            new MemoryStream();

        encoder.Save(
            outputStream);

        var data =
            outputStream.ToArray();

        if (data.Length == 0)
        {
            throw new InvalidOperationException(
                "Das Kontaktfoto konnte nicht verarbeitet werden.");
        }

        return new PreparedPhoto(
            data,
            "image/jpeg");
    }

    private void UpdatePhotoPreview()
    {
        if (_photoData is not
            { Length: > 0 })
        {
            PhotoImageBrush.ImageSource =
                null;

            PhotoPlaceholder.Visibility =
                Visibility.Visible;

            RemovePhotoButton.IsEnabled =
                false;

            return;
        }

        try
        {
            PhotoImageBrush.ImageSource =
                CreateBitmapImage(
                    _photoData);

            PhotoPlaceholder.Visibility =
                Visibility.Collapsed;

            RemovePhotoButton.IsEnabled =
                true;
        }
        catch
        {
            PhotoImageBrush.ImageSource =
                null;

            PhotoPlaceholder.Visibility =
                Visibility.Visible;

            RemovePhotoButton.IsEnabled =
                false;
        }
    }

    private static BitmapImage CreateBitmapImage(
        byte[] data)
    {
        using var stream =
            new MemoryStream(
                data,
                writable:
                    false);

        var image =
            new BitmapImage();

        image.BeginInit();

        image.CacheOption =
            BitmapCacheOption.OnLoad;

        image.StreamSource =
            stream;

        image.EndInit();

        image.Freeze();

        return image;
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
                    GetSelectedCategories(),

                PhotoData =
                    _photoData?
                        .ToArray(),

                PhotoMediaType =
                    _photoMediaType
            };

        await _viewModel
            .CreateContactAsync(
                request);
    }

    private async Task UpdateContactAsync()
    {
        byte[]? photoData =
            null;

        string? photoMediaType =
            null;

        if (_photoChanged)
        {
            photoData =
                _photoRemoved
                    ? Array.Empty<byte>()
                    : _photoData?
                        .ToArray();

            photoMediaType =
                _photoRemoved
                    ? string.Empty
                    : _photoMediaType;
        }

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
                    GetSelectedCategories(),

                PhotoData =
                    photoData,

                PhotoMediaType =
                    photoMediaType
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

    private sealed record PreparedPhoto(
        byte[] Data,
        string MediaType);
}