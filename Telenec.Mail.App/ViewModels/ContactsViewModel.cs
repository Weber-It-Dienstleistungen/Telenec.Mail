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

            OnPropertyChanged(
                nameof(HasSelectedContact));

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

    public async Task<ContactData> CreateContactAsync(
        ContactCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        if (!HasMinimumContactData(
                request))
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
                        request,
                        cancellationToken);

            await LoadContactsCoreAsync(
                createdContact.Uid,
                cancellationToken);

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
        ContactUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        var contact =
            SelectedContact;

        if (contact is null)
        {
            return;
        }

        if (!HasMinimumContactData(
                request))
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
                        request,
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
                        ContactMatchesSearch(
                            contact,
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

    private static bool ContactMatchesSearch(
        ContactData contact,
        string searchValue)
    {
        return
            ContainsSearchValue(
                contact.DisplayName,
                searchValue) ||

            ContainsSearchValue(
                contact.FirstName,
                searchValue) ||

            ContainsSearchValue(
                contact.MiddleName,
                searchValue) ||

            ContainsSearchValue(
                contact.LastName,
                searchValue) ||

            ContainsSearchValue(
                contact.Nickname,
                searchValue) ||

            ContainsSearchValue(
                contact.EmailAddress,
                searchValue) ||

            ContainsSearchValue(
                contact.BusinessEmailAddress,
                searchValue) ||

            ContainsSearchValue(
                contact.PrivateEmailAddress,
                searchValue) ||

            ContainsAnySearchValue(
                contact.AdditionalEmailAddresses,
                searchValue) ||

            ContainsSearchValue(
                contact.PhoneNumber,
                searchValue) ||

            ContainsSearchValue(
                contact.MobilePhoneNumber,
                searchValue) ||

            ContainsSearchValue(
                contact.BusinessPhoneNumber,
                searchValue) ||

            ContainsSearchValue(
                contact.PrivatePhoneNumber,
                searchValue) ||

            ContainsSearchValue(
                contact.FaxNumber,
                searchValue) ||

            ContainsAnySearchValue(
                contact.AdditionalPhoneNumbers,
                searchValue) ||

            ContainsSearchValue(
                contact.Company,
                searchValue) ||

            ContainsSearchValue(
                contact.Department,
                searchValue) ||

            ContainsSearchValue(
                contact.JobTitle,
                searchValue) ||

            ContainsSearchValue(
                contact.HomeAddress?.Street,
                searchValue) ||

            ContainsSearchValue(
                contact.HomeAddress?.City,
                searchValue) ||

            ContainsSearchValue(
                contact.HomeAddress?.PostalCode,
                searchValue) ||

            ContainsSearchValue(
                contact.WorkAddress?.Street,
                searchValue) ||

            ContainsSearchValue(
                contact.WorkAddress?.City,
                searchValue) ||

            ContainsSearchValue(
                contact.WorkAddress?.PostalCode,
                searchValue) ||

            ContainsAnySearchValue(
                contact.Categories,
                searchValue);
    }

    private static bool HasMinimumContactData(
        ContactCreateRequest request)
    {
        return
            HasMinimumContactData(
                request.FirstName,
                request.MiddleName,
                request.LastName,
                request.DisplayName,
                request.EmailAddress,
                request.BusinessEmailAddress,
                request.PrivateEmailAddress,
                request.AdditionalEmailAddresses);
    }

    private static bool HasMinimumContactData(
        ContactUpdateRequest request)
    {
        return
            HasMinimumContactData(
                request.FirstName,
                request.MiddleName,
                request.LastName,
                request.DisplayName,
                request.EmailAddress,
                request.BusinessEmailAddress,
                request.PrivateEmailAddress,
                request.AdditionalEmailAddresses);
    }

    private static bool HasMinimumContactData(
        string? firstName,
        string? middleName,
        string? lastName,
        string? displayName,
        string? emailAddress,
        string? businessEmailAddress,
        string? privateEmailAddress,
        IEnumerable<string>? additionalEmailAddresses)
    {
        return
            !string.IsNullOrWhiteSpace(
                firstName) ||

            !string.IsNullOrWhiteSpace(
                middleName) ||

            !string.IsNullOrWhiteSpace(
                lastName) ||

            !string.IsNullOrWhiteSpace(
                displayName) ||

            !string.IsNullOrWhiteSpace(
                emailAddress) ||

            !string.IsNullOrWhiteSpace(
                businessEmailAddress) ||

            !string.IsNullOrWhiteSpace(
                privateEmailAddress) ||

            additionalEmailAddresses?
                .Any(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value)) ==
                true;
    }

    private static bool ContainsAnySearchValue(
        IEnumerable<string>? values,
        string searchValue)
    {
        if (values is null)
        {
            return false;
        }

        return values.Any(
            value =>
                ContainsSearchValue(
                    value,
                    searchValue));
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
}