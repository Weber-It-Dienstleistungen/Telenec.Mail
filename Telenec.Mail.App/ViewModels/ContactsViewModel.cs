using System.Collections.ObjectModel;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Contacts;

namespace Telenec.Mail.App.ViewModels;

public sealed class ContactsViewModel :
    BaseViewModel
{
    private readonly IContactService
        _contactService;

    private readonly List<ContactData>
        _allContacts =
            new();

    private string _searchText =
        string.Empty;

    private ContactData?
        _selectedContact;

    private bool _isLoading;

    private string _statusText =
        "Kontakte werden geladen …";

    private string _newFirstName =
        string.Empty;

    private string _newLastName =
        string.Empty;

    private string _newDisplayName =
        string.Empty;

    private string _newEmailAddress =
        string.Empty;

    private string _newPhoneNumber =
        string.Empty;

    private string _editFirstName =
        string.Empty;

    private string _editLastName =
        string.Empty;

    private string _editDisplayName =
        string.Empty;

    private string _editEmailAddress =
        string.Empty;

    private string _editPhoneNumber =
        string.Empty;

    public ContactsViewModel(
        IContactService contactService)
    {
        ArgumentNullException.ThrowIfNull(
            contactService);

        _contactService =
            contactService;

        VisibleContacts =
            new ObservableCollection<ContactData>();
    }

    public ObservableCollection<ContactData>
        VisibleContacts
    { get; }

    public string SearchText
    {
        get =>
            _searchText;

        set
        {
            var normalizedValue =
                value
                ?? string.Empty;

            if (_searchText ==
                normalizedValue)
            {
                return;
            }

            _searchText =
                normalizedValue;

            OnPropertyChanged();

            ApplyFilter();
        }
    }

    public ContactData?
        SelectedContact
    {
        get =>
            _selectedContact;

        set
        {
            if (ReferenceEquals(
                    _selectedContact,
                    value))
            {
                return;
            }

            _selectedContact =
                value;

            OnPropertyChanged();

            LoadSelectedContactFields();

            OnPropertyChanged(
                nameof(HasSelectedContact));

            OnPropertyChanged(
                nameof(CanSaveSelectedContact));

            OnPropertyChanged(
                nameof(CanDeleteSelectedContact));

            OnPropertyChanged(
                nameof(CanComposeToSelectedContact));
        }
    }

    public bool HasSelectedContact =>
        SelectedContact is not null;

    public bool IsLoading
    {
        get =>
            _isLoading;

        private set
        {
            if (_isLoading ==
                value)
            {
                return;
            }

            _isLoading =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanCreateContact));

            OnPropertyChanged(
                nameof(CanSaveSelectedContact));

            OnPropertyChanged(
                nameof(CanDeleteSelectedContact));

            OnPropertyChanged(
                nameof(CanComposeToSelectedContact));
        }
    }

    public string StatusText
    {
        get =>
            _statusText;

        private set
        {
            if (_statusText ==
                value)
            {
                return;
            }

            _statusText =
                value;

            OnPropertyChanged();
        }
    }

    public string ContactCountText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(
                    SearchText))
            {
                return _allContacts.Count == 1
                    ? "1 Kontakt"
                    : $"{_allContacts.Count} Kontakte";
            }

            return $"{VisibleContacts.Count} von {_allContacts.Count} Kontakten";
        }
    }

    public string NewFirstName
    {
        get => _newFirstName;

        set
        {
            if (_newFirstName == value)
            {
                return;
            }

            _newFirstName =
                value ?? string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(CanCreateContact));
        }
    }

    public string NewLastName
    {
        get => _newLastName;

        set
        {
            if (_newLastName == value)
            {
                return;
            }

            _newLastName =
                value ?? string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(CanCreateContact));
        }
    }

    public string NewDisplayName
    {
        get => _newDisplayName;

        set
        {
            if (_newDisplayName == value)
            {
                return;
            }

            _newDisplayName =
                value ?? string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(CanCreateContact));
        }
    }

    public string NewEmailAddress
    {
        get => _newEmailAddress;

        set
        {
            if (_newEmailAddress == value)
            {
                return;
            }

            _newEmailAddress =
                value ?? string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(CanCreateContact));
        }
    }

    public string NewPhoneNumber
    {
        get => _newPhoneNumber;

        set
        {
            if (_newPhoneNumber == value)
            {
                return;
            }

            _newPhoneNumber =
                value ?? string.Empty;

            OnPropertyChanged();
        }
    }

    public string EditFirstName
    {
        get => _editFirstName;

        set
        {
            if (_editFirstName == value)
            {
                return;
            }

            _editFirstName =
                value ?? string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(CanSaveSelectedContact));
        }
    }

    public string EditLastName
    {
        get => _editLastName;

        set
        {
            if (_editLastName == value)
            {
                return;
            }

            _editLastName =
                value ?? string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(CanSaveSelectedContact));
        }
    }

    public string EditDisplayName
    {
        get => _editDisplayName;

        set
        {
            if (_editDisplayName == value)
            {
                return;
            }

            _editDisplayName =
                value ?? string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(CanSaveSelectedContact));
        }
    }

    public string EditEmailAddress
    {
        get => _editEmailAddress;

        set
        {
            if (_editEmailAddress == value)
            {
                return;
            }

            _editEmailAddress =
                value ?? string.Empty;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSaveSelectedContact));
        }
    }

    public string EditPhoneNumber
    {
        get => _editPhoneNumber;

        set
        {
            if (_editPhoneNumber == value)
            {
                return;
            }

            _editPhoneNumber =
                value ?? string.Empty;

            OnPropertyChanged();
        }
    }

    public bool CanCreateContact =>
        !IsLoading &&
        HasMinimumContactData(
            NewFirstName,
            NewLastName,
            NewDisplayName,
            NewEmailAddress);

    public bool CanSaveSelectedContact =>
        !IsLoading &&
        SelectedContact is not null &&
        HasMinimumContactData(
            EditFirstName,
            EditLastName,
            EditDisplayName,
            EditEmailAddress);

    public bool CanDeleteSelectedContact =>
        !IsLoading &&
        SelectedContact is not null;

    public bool CanComposeToSelectedContact =>
        !IsLoading &&
        SelectedContact is not null &&
        !string.IsNullOrWhiteSpace(
            SelectedContact.EmailAddress);

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await ReloadAsync(
            cancellationToken);
    }

    public async Task ReloadAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading =
            true;

        StatusText =
            "Kontakte werden geladen …";

        try
        {
            await LoadContactsCoreAsync(
                SelectedContact?.Uid,
                cancellationToken);

            StatusText =
                "Kontakte sind aktuell.";
        }
        finally
        {
            IsLoading =
                false;
        }
    }

    public async Task<ContactData>
        CreateContactAsync(
            CancellationToken cancellationToken = default)
    {
        if (!CanCreateContact)
        {
            throw new InvalidOperationException(
                "Bitte geben Sie mindestens einen Namen oder eine E-Mail-Adresse ein.");
        }

        IsLoading =
            true;

        StatusText =
            "Kontakt wird gespeichert …";

        try
        {
            var createdContact =
                await _contactService
                    .CreateAsync(
                        new ContactCreateRequest
                        {
                            FirstName =
                                NullIfWhiteSpace(
                                    NewFirstName),

                            LastName =
                                NullIfWhiteSpace(
                                    NewLastName),

                            DisplayName =
                                NullIfWhiteSpace(
                                    NewDisplayName),

                            EmailAddress =
                                NullIfWhiteSpace(
                                    NewEmailAddress),

                            PhoneNumber =
                                NullIfWhiteSpace(
                                    NewPhoneNumber)
                        },
                        cancellationToken);

            await LoadContactsCoreAsync(
                createdContact.Uid,
                cancellationToken);

            ClearNewContactFields();

            StatusText =
                "Kontakt wurde gespeichert.";

            return createdContact;
        }
        finally
        {
            IsLoading =
                false;
        }
    }

    public async Task UpdateSelectedContactAsync(
        CancellationToken cancellationToken = default)
    {
        var contact =
            SelectedContact;

        if (contact is null)
        {
            return;
        }

        if (!CanSaveSelectedContact)
        {
            throw new InvalidOperationException(
                "Bitte geben Sie mindestens einen Namen oder eine E-Mail-Adresse ein.");
        }

        IsLoading =
            true;

        StatusText =
            "Kontakt wird aktualisiert …";

        try
        {
            var updatedContact =
                await _contactService
                    .UpdateAsync(
                        contact,
                        new ContactUpdateRequest
                        {
                            FirstName =
                                NullIfWhiteSpace(
                                    EditFirstName),

                            LastName =
                                NullIfWhiteSpace(
                                    EditLastName),

                            DisplayName =
                                NullIfWhiteSpace(
                                    EditDisplayName),

                            EmailAddress =
                                NullIfWhiteSpace(
                                    EditEmailAddress),

                            PhoneNumber =
                                NullIfWhiteSpace(
                                    EditPhoneNumber)
                        },
                        cancellationToken);

            await LoadContactsCoreAsync(
                updatedContact.Uid,
                cancellationToken);

            StatusText =
                "Kontakt wurde aktualisiert.";
        }
        finally
        {
            IsLoading =
                false;
        }
    }

    public async Task DeleteSelectedContactAsync(
        CancellationToken cancellationToken = default)
    {
        var contact =
            SelectedContact;

        if (contact is null)
        {
            return;
        }

        IsLoading =
            true;

        StatusText =
            "Kontakt wird gelöscht …";

        try
        {
            await _contactService
                .DeleteAsync(
                    contact,
                    cancellationToken);

            await LoadContactsCoreAsync(
                preferredUid:
                    null,
                cancellationToken);

            StatusText =
                "Kontakt wurde gelöscht.";
        }
        finally
        {
            IsLoading =
                false;
        }
    }

    private async Task LoadContactsCoreAsync(
        string? preferredUid,
        CancellationToken cancellationToken)
    {
        var contacts =
            await _contactService
                .GetAllAsync(
                    cancellationToken);

        _allContacts.Clear();

        _allContacts.AddRange(
            contacts);

        ApplyFilter();

        if (!string.IsNullOrWhiteSpace(
                preferredUid))
        {
            SelectedContact =
                VisibleContacts
                    .FirstOrDefault(
                        contact =>
                            string.Equals(
                                contact.Uid,
                                preferredUid,
                                StringComparison.OrdinalIgnoreCase));

            if (SelectedContact is not null)
            {
                return;
            }
        }

        SelectedContact =
            VisibleContacts
                .FirstOrDefault();
    }

    private void ApplyFilter()
    {
        var searchValue =
            SearchText.Trim();

        var selectedResourcePath =
            SelectedContact?
                .ResourcePath;

        VisibleContacts.Clear();

        IEnumerable<ContactData>
            result =
                _allContacts;

        if (!string.IsNullOrWhiteSpace(
                searchValue))
        {
            result =
                result.Where(
                    contact =>
                        ContainsSearchValue(
                            contact.DisplayName,
                            searchValue) ||

                        ContainsSearchValue(
                            contact.FirstName,
                            searchValue) ||

                        ContainsSearchValue(
                            contact.LastName,
                            searchValue) ||

                        ContainsSearchValue(
                            contact.EmailAddress,
                            searchValue) ||

                        ContainsSearchValue(
                            contact.PhoneNumber,
                            searchValue));
        }

        foreach (var contact in
                 result)
        {
            VisibleContacts.Add(
                contact);
        }

        if (!string.IsNullOrWhiteSpace(
                selectedResourcePath))
        {
            SelectedContact =
                VisibleContacts
                    .FirstOrDefault(
                        contact =>
                            string.Equals(
                                contact.ResourcePath,
                                selectedResourcePath,
                                StringComparison.OrdinalIgnoreCase));
        }

        SelectedContact ??=
            VisibleContacts
                .FirstOrDefault();

        OnPropertyChanged(
            nameof(ContactCountText));
    }

    private void LoadSelectedContactFields()
    {
        EditFirstName =
            SelectedContact?
                .FirstName
            ?? string.Empty;

        EditLastName =
            SelectedContact?
                .LastName
            ?? string.Empty;

        EditDisplayName =
            SelectedContact?
                .DisplayName
            ?? string.Empty;

        EditEmailAddress =
            SelectedContact?
                .EmailAddress
            ?? string.Empty;

        EditPhoneNumber =
            SelectedContact?
                .PhoneNumber
            ?? string.Empty;
    }

    private void ClearNewContactFields()
    {
        NewFirstName =
            string.Empty;

        NewLastName =
            string.Empty;

        NewDisplayName =
            string.Empty;

        NewEmailAddress =
            string.Empty;

        NewPhoneNumber =
            string.Empty;
    }

    private static bool HasMinimumContactData(
        string? firstName,
        string? lastName,
        string? displayName,
        string? emailAddress)
    {
        return
            !string.IsNullOrWhiteSpace(
                firstName) ||

            !string.IsNullOrWhiteSpace(
                lastName) ||

            !string.IsNullOrWhiteSpace(
                displayName) ||

            !string.IsNullOrWhiteSpace(
                emailAddress);
    }

    private static bool ContainsSearchValue(
        string? value,
        string searchValue)
    {
        return
            !string.IsNullOrWhiteSpace(
                value) &&

            value.Contains(
                searchValue,
                StringComparison.CurrentCultureIgnoreCase);
    }

    private static string? NullIfWhiteSpace(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value.Trim();
    }
}