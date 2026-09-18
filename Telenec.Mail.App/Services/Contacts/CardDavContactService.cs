using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Contacts;

public sealed class CardDavContactService :
    IContactService
{
    private static readonly Uri BaseUri =
        new("https://dav.necnet.de/");

    private static readonly HttpMethod ReportMethod =
        new("REPORT");

    private static readonly HttpClient HttpClient =
        CreateHttpClient();

    private const string DefaultCollectionHref =
        "contacts";

    private const string AddressBookQueryXml =
        """
        <?xml version="1.0" encoding="UTF-8" ?>
        <card:addressbook-query
            xmlns:d="DAV:"
            xmlns:card="urn:ietf:params:xml:ns:carddav">
          <d:prop>
            <d:getetag />
            <card:address-data />
          </d:prop>
        </card:addressbook-query>
        """;

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    private readonly ILogger<CardDavContactService>
        _logger;

    public CardDavContactService(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore,
        ILogger<CardDavContactService> logger)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        ArgumentNullException.ThrowIfNull(
            logger);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;

        _logger =
            logger;
    }

    public async Task<IReadOnlyList<ContactData>>
        GetAllAsync(
            CancellationToken cancellationToken = default)
    {
        var credential =
            await GetStoredCredentialAsync(
                cancellationToken);

        var addressBookUri =
            BuildDefaultAddressBookUri(
                credential.UserName);

        using var request =
            CreateAuthenticatedRequest(
                ReportMethod,
                addressBookUri,
                credential.UserName,
                credential.Password);

        request.Headers.TryAddWithoutValidation(
            "Depth",
            "1");

        request.Content =
            new StringContent(
                AddressBookQueryXml,
                Encoding.UTF8,
                "application/xml");

        _logger.LogInformation(
            "CardDAV contact retrieval started.");

        using var response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"CardDAV contact retrieval failed with HTTP status " +
                $"{(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        var responseXml =
            await response.Content
                .ReadAsStringAsync(
                    cancellationToken);

        var contacts =
            ParseAddressBookResponse(
                responseXml);

        _logger.LogInformation(
            "CardDAV contact retrieval completed. Contacts={ContactCount}.",
            contacts.Count);

        return contacts;
    }

    public async Task<ContactData> CreateAsync(
        ContactCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        var credential =
            await GetStoredCredentialAsync(
                cancellationToken);

        var normalizedContact =
            NormalizeContactValues(
                request);

        var uid =
            Guid.NewGuid()
                .ToString("D");

        var resourceName =
            $"{uid}.vcf";

        var addressBookUri =
            BuildDefaultAddressBookUri(
                credential.UserName);

        var resourceUri =
            new Uri(
                addressBookUri,
                resourceName);

        var vCard =
            CreateVCard(
                uid,
                normalizedContact,
                Array.Empty<string>());

        using var httpRequest =
            CreateAuthenticatedRequest(
                HttpMethod.Put,
                resourceUri,
                credential.UserName,
                credential.Password);

        httpRequest.Headers.TryAddWithoutValidation(
            "If-None-Match",
            "*");

        httpRequest.Content =
            CreateVCardContent(
                vCard);

        _logger.LogInformation(
            "CardDAV contact creation started.");

        using var response =
            await HttpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"CardDAV contact creation failed with HTTP status " +
                $"{(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        _logger.LogInformation(
            "CardDAV contact creation completed successfully.");

        return CreateContactData(
            resourceUri.AbsolutePath,
            response.Headers.ETag?.Tag,
            uid,
            normalizedContact,
            Array.Empty<string>());
    }

    public async Task<ContactData> UpdateAsync(
        ContactData contact,
        ContactUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            contact);

        ArgumentNullException.ThrowIfNull(
            request);

        var credential =
            await GetStoredCredentialAsync(
                cancellationToken);

        /*
         * Die momentane Oberfläche kennt erst einen Teil der
         * erweiterten Felder.
         *
         * Deshalb werden Felder, die im Request noch nicht
         * gesetzt werden, aus dem bestehenden Kontakt
         * übernommen.
         *
         * So zerstört eine Bearbeitung in Telenec Mail keine
         * zusätzlichen Informationen, die beispielsweise
         * Roundcube bereits in die vCard geschrieben hat.
         */
        var normalizedContact =
            NormalizeContactValues(
                contact,
                request);

        var uid =
            !string.IsNullOrWhiteSpace(
                contact.Uid)
                ? contact.Uid.Trim()
                : Guid.NewGuid()
                    .ToString("D");

        var resourceUri =
            BuildResourceUri(
                contact.ResourcePath);

        var vCard =
            CreateVCard(
                uid,
                normalizedContact,
                contact.PreservedVCardLines);

        using var httpRequest =
            CreateAuthenticatedRequest(
                HttpMethod.Put,
                resourceUri,
                credential.UserName,
                credential.Password);

        if (!string.IsNullOrWhiteSpace(
                contact.ETag))
        {
            httpRequest.Headers.TryAddWithoutValidation(
                "If-Match",
                contact.ETag);
        }

        httpRequest.Content =
            CreateVCardContent(
                vCard);

        _logger.LogInformation(
            "CardDAV contact update started.");

        using var response =
            await HttpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"CardDAV contact update failed with HTTP status " +
                $"{(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        _logger.LogInformation(
            "CardDAV contact update completed successfully.");

        return CreateContactData(
            resourceUri.AbsolutePath,
            response.Headers.ETag?.Tag
                ?? contact.ETag,
            uid,
            normalizedContact,
            contact.PreservedVCardLines);
    }

    public async Task DeleteAsync(
        ContactData contact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            contact);

        var credential =
            await GetStoredCredentialAsync(
                cancellationToken);

        var resourceUri =
            BuildResourceUri(
                contact.ResourcePath);

        using var request =
            CreateAuthenticatedRequest(
                HttpMethod.Delete,
                resourceUri,
                credential.UserName,
                credential.Password);

        if (!string.IsNullOrWhiteSpace(
                contact.ETag))
        {
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                contact.ETag);
        }

        _logger.LogInformation(
            "CardDAV contact deletion started.");

        using var response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"CardDAV contact deletion failed with HTTP status " +
                $"{(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        _logger.LogInformation(
            "CardDAV contact deletion completed successfully.");
    }

    private async Task<StoredCredential>
        GetStoredCredentialAsync(
            CancellationToken cancellationToken)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            throw new InvalidOperationException(
                "Es ist kein aktives Mailkonto eingerichtet.");
        }

        var credential =
            await _credentialStore
                .ReadAsync(
                    account.AccountId,
                    cancellationToken);

        if (credential is null ||
            string.IsNullOrWhiteSpace(
                credential.UserName) ||
            string.IsNullOrEmpty(
                credential.Password))
        {
            throw new InvalidOperationException(
                "Für das Mailkonto sind keine Zugangsdaten gespeichert.");
        }

        return credential;
    }

    private static HttpRequestMessage
        CreateAuthenticatedRequest(
            HttpMethod method,
            Uri uri,
            string userName,
            string password)
    {
        var request =
            new HttpRequestMessage(
                method,
                uri);

        var authenticationValue =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    $"{userName}:{password}"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                authenticationValue);

        return request;
    }

    private static HttpContent CreateVCardContent(
        string vCard)
    {
        var content =
            new StringContent(
                vCard,
                Encoding.UTF8);

        content.Headers.ContentType =
            MediaTypeHeaderValue.Parse(
                "text/vcard; charset=utf-8");

        return content;
    }

    private static Uri BuildDefaultAddressBookUri(
        string userName)
    {
        var normalizedUserName =
            userName
                .Trim()
                .ToLowerInvariant();

        var encodedUserName =
            Uri.EscapeDataString(
                normalizedUserName);

        return new Uri(
            BaseUri,
            $"{encodedUserName}/{DefaultCollectionHref}/");
    }

    private static Uri BuildResourceUri(
        string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(
                resourcePath))
        {
            throw new InvalidOperationException(
                "Der CardDAV-Kontakt besitzt keinen gültigen Ressourcenpfad.");
        }

        if (Uri.TryCreate(
                resourcePath,
                UriKind.Absolute,
                out var absoluteUri))
        {
            if (!string.Equals(
                    absoluteUri.Scheme,
                    BaseUri.Scheme,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    absoluteUri.Host,
                    BaseUri.Host,
                    StringComparison.OrdinalIgnoreCase) ||
                absoluteUri.Port !=
                    BaseUri.Port)
            {
                throw new InvalidOperationException(
                    "Der CardDAV-Kontakt verweist auf einen unerwarteten Server.");
            }

            return absoluteUri;
        }

        return new Uri(
            BaseUri,
            resourcePath.TrimStart('/'));
    }

    private static IReadOnlyList<ContactData>
        ParseAddressBookResponse(
            string responseXml)
    {
        if (string.IsNullOrWhiteSpace(
                responseXml))
        {
            return Array.Empty<ContactData>();
        }

        var document =
            XDocument.Parse(
                responseXml);

        XNamespace dav =
            "DAV:";

        XNamespace cardDav =
            "urn:ietf:params:xml:ns:carddav";

        var contacts =
            new List<ContactData>();

        foreach (var responseElement in
                 document.Descendants(
                     dav + "response"))
        {
            var href =
                responseElement
                    .Element(
                        dav + "href")?
                    .Value
                    .Trim();

            var addressData =
                responseElement
                    .Descendants(
                        cardDav + "address-data")
                    .FirstOrDefault()?
                    .Value;

            if (string.IsNullOrWhiteSpace(
                    href) ||
                string.IsNullOrWhiteSpace(
                    addressData))
            {
                continue;
            }

            var eTag =
                responseElement
                    .Descendants(
                        dav + "getetag")
                    .FirstOrDefault()?
                    .Value
                    .Trim();

            var contact =
                ParseVCard(
                    href,
                    eTag,
                    addressData);

            if (contact is not null)
            {
                contacts.Add(
                    contact);
            }
        }

        return contacts
            .OrderBy(
                contact =>
                    contact.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static ContactData? ParseVCard(
        string resourcePath,
        string? eTag,
        string vCard)
    {
        var lines =
            UnfoldVCardLines(
                vCard);

        string? uid =
            null;

        string? salutation =
            null;

        string? academicTitle =
            null;

        string? firstName =
            null;

        string? middleName =
            null;

        string? lastName =
            null;

        string? nameSuffix =
            null;

        string? nickname =
            null;

        string? displayName =
            null;

        string? company =
            null;

        string? department =
            null;

        string? jobTitle =
            null;

        ContactPostalAddress? homeAddress =
            null;

        ContactPostalAddress? workAddress =
            null;

        string? website =
            null;

        string? birthday =
            null;

        string? notes =
            null;

        byte[]? photoData =
            null;

        string? photoMediaType =
            null;

        var emails =
            new List<TypedContactValue>();

        var phoneNumbers =
            new List<TypedContactValue>();

        var categories =
            new List<string>();

        var preservedLines =
            new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(
                    line))
            {
                continue;
            }

            var separatorIndex =
                line.IndexOf(
                    ':');

            if (separatorIndex <= 0)
            {
                preservedLines.Add(
                    line);

                continue;
            }

            var propertyPart =
                line[..separatorIndex];

            var valuePart =
                line[(separatorIndex + 1)..];

            var propertyName =
                GetPropertyName(
                    propertyPart);

            var types =
                GetPropertyTypes(
                    propertyPart);

            switch (propertyName)
            {
                case "BEGIN":
                case "END":
                case "VERSION":
                case "REV":
                    break;

                case "UID":
                    uid =
                        NullIfWhiteSpace(
                            UnescapeVCardValue(
                                valuePart));
                    break;

                case "FN":
                    displayName =
                        NullIfWhiteSpace(
                            UnescapeVCardValue(
                                valuePart));
                    break;

                case "N":
                    {
                        var parts =
                            SplitEscaped(
                                valuePart,
                                ';');

                        lastName =
                            GetUnescapedPart(
                                parts,
                                0);

                        firstName =
                            GetUnescapedPart(
                                parts,
                                1);

                        middleName =
                            GetUnescapedPart(
                                parts,
                                2);

                        /*
                         * Der vierte N-Bestandteil ist der
                         * Honorific Prefix.
                         *
                         * Für unser Modell verwenden wir ihn
                         * zunächst als akademischen Titel.
                         */
                        academicTitle =
                            GetUnescapedPart(
                                parts,
                                3);

                        nameSuffix =
                            GetUnescapedPart(
                                parts,
                                4);

                        break;
                    }

                case "NICKNAME":
                    nickname =
                        NullIfWhiteSpace(
                            UnescapeVCardValue(
                                valuePart));
                    break;

                case "X-TELENEC-SALUTATION":
                    salutation =
                        NullIfWhiteSpace(
                            UnescapeVCardValue(
                                valuePart));
                    break;

                case "EMAIL":
                    AddTypedValue(
                        emails,
                        valuePart,
                        types);
                    break;

                case "TEL":
                    AddTypedValue(
                        phoneNumbers,
                        valuePart,
                        types);
                    break;

                case "ORG":
                    {
                        var parts =
                            SplitEscaped(
                                valuePart,
                                ';');

                        company =
                            GetUnescapedPart(
                                parts,
                                0);

                        if (parts.Count > 1)
                        {
                            var departments =
                                parts
                                    .Skip(1)
                                    .Select(
                                        UnescapeVCardValue)
                                    .Where(
                                        value =>
                                            !string.IsNullOrWhiteSpace(
                                                value))
                                    .ToArray();

                            if (departments.Length > 0)
                            {
                                department =
                                    string.Join(
                                        " / ",
                                        departments);
                            }
                        }

                        break;
                    }

                case "TITLE":
                    jobTitle =
                        NullIfWhiteSpace(
                            UnescapeVCardValue(
                                valuePart));
                    break;

                case "ADR":
                    {
                        var address =
                            ParsePostalAddress(
                                valuePart);

                        if (types.Contains(
                                "HOME"))
                        {
                            homeAddress ??=
                                address;
                        }
                        else if (types.Contains(
                                     "WORK"))
                        {
                            workAddress ??=
                                address;
                        }
                        else
                        {
                            /*
                             * Unbekannter Adresstyp:
                             * lieber unverändert behalten als
                             * Information wegwerfen.
                             */
                            preservedLines.Add(
                                line);
                        }

                        break;
                    }

                case "URL":
                    if (string.IsNullOrWhiteSpace(
                            website))
                    {
                        website =
                            NullIfWhiteSpace(
                                UnescapeVCardValue(
                                    valuePart));
                    }
                    else
                    {
                        preservedLines.Add(
                            line);
                    }

                    break;

                case "BDAY":
                    if (string.IsNullOrWhiteSpace(
                            birthday))
                    {
                        birthday =
                            NullIfWhiteSpace(
                                UnescapeVCardValue(
                                    valuePart));
                    }
                    else
                    {
                        preservedLines.Add(
                            line);
                    }

                    break;

                case "NOTE":
                    {
                        var notePart =
                            NullIfWhiteSpace(
                                UnescapeVCardValue(
                                    valuePart));

                        if (!string.IsNullOrWhiteSpace(
                                notePart))
                        {
                            notes =
                                string.IsNullOrWhiteSpace(
                                    notes)
                                    ? notePart
                                    : notes +
                                      Environment.NewLine +
                                      notePart;
                        }

                        break;
                    }

                case "CATEGORIES":
                    {
                        var categoryParts =
                            SplitEscaped(
                                valuePart,
                                ',');

                        foreach (var categoryPart in
                                 categoryParts)
                        {
                            var category =
                                NullIfWhiteSpace(
                                    UnescapeVCardValue(
                                        categoryPart));

                            if (string.IsNullOrWhiteSpace(
                                    category))
                            {
                                continue;
                            }

                            if (!categories.Contains(
                                    category,
                                    StringComparer.CurrentCultureIgnoreCase))
                            {
                                categories.Add(
                                    category);
                            }
                        }

                        break;
                    }

                case "PHOTO":
                    if (TryParsePhoto(
                            propertyPart,
                            valuePart,
                            out var parsedPhotoData,
                            out var parsedPhotoMediaType))
                    {
                        photoData =
                            parsedPhotoData;

                        photoMediaType =
                            parsedPhotoMediaType;
                    }
                    else
                    {
                        /*
                         * Beispielsweise ein PHOTO;VALUE=URI
                         * wird derzeit nicht aktiv geladen.
                         *
                         * Wir behalten die Zeile aber, damit
                         * ein späteres Speichern sie nicht
                         * zerstört.
                         */
                        preservedLines.Add(
                            line);
                    }

                    break;

                default:
                    /*
                     * Fremde / herstellerspezifische Felder
                     * werden bewusst erhalten.
                     *
                     * Beispiele wären X-ABLabel oder andere
                     * Eigenschaften externer CardDAV-Clients.
                     */
                    preservedLines.Add(
                        line);
                    break;
            }
        }

        var emailValues =
            CreateEmailSnapshot(
                emails);

        var phoneValues =
            CreatePhoneSnapshot(
                phoneNumbers);

        displayName =
            BuildDisplayName(
                displayName,
                firstName,
                middleName,
                lastName,
                emailValues.Primary);

        if (string.IsNullOrWhiteSpace(
                displayName))
        {
            return null;
        }

        return new ContactData
        {
            ResourcePath =
                resourcePath,

            ETag =
                eTag,

            Uid =
                uid,

            Salutation =
                salutation,

            AcademicTitle =
                academicTitle,

            FirstName =
                firstName,

            MiddleName =
                middleName,

            LastName =
                lastName,

            NameSuffix =
                nameSuffix,

            Nickname =
                nickname,

            DisplayName =
                displayName,

            EmailAddress =
                emailValues.Primary,

            BusinessEmailAddress =
                emailValues.Business,

            PrivateEmailAddress =
                emailValues.Private,

            AdditionalEmailAddresses =
                emailValues.Additional,

            PhoneNumber =
                phoneValues.Primary,

            MobilePhoneNumber =
                phoneValues.Mobile,

            BusinessPhoneNumber =
                phoneValues.Business,

            PrivatePhoneNumber =
                phoneValues.Private,

            FaxNumber =
                phoneValues.Fax,

            AdditionalPhoneNumbers =
                phoneValues.Additional,

            Company =
                company,

            Department =
                department,

            JobTitle =
                jobTitle,

            HomeAddress =
                homeAddress,

            WorkAddress =
                workAddress,

            Website =
                website,

            Birthday =
                birthday,

            Notes =
                notes,

            Categories =
                categories,

            PhotoData =
                photoData,

            PhotoMediaType =
                photoMediaType,

            PreservedVCardLines =
                preservedLines
        };
    }

    private static IReadOnlyList<string>
        UnfoldVCardLines(
            string vCard)
    {
        var normalized =
            vCard
                .Replace(
                    "\r\n",
                    "\n")
                .Replace(
                    '\r',
                    '\n');

        var sourceLines =
            normalized.Split(
                '\n');

        var result =
            new List<string>();

        foreach (var sourceLine in
                 sourceLines)
        {
            if ((sourceLine.StartsWith(
                     ' ') ||
                 sourceLine.StartsWith(
                     '\t')) &&
                result.Count > 0)
            {
                result[^1] +=
                    sourceLine[1..];

                continue;
            }

            result.Add(
                sourceLine);
        }

        return result;
    }

    private static string GetPropertyName(
        string propertyPart)
    {
        var rawName =
            propertyPart
                .Split(
                    ';',
                    2)[0]
                .Trim();

        /*
         * vCard erlaubt Gruppen wie:
         *
         * item1.EMAIL
         *
         * Für unsere Verarbeitung ist in diesem Fall nur
         * EMAIL die eigentliche Eigenschaft.
         */
        var groupSeparatorIndex =
            rawName.LastIndexOf(
                '.');

        if (groupSeparatorIndex >= 0 &&
            groupSeparatorIndex <
            rawName.Length - 1)
        {
            rawName =
                rawName[
                    (groupSeparatorIndex + 1)..];
        }

        return rawName
            .ToUpperInvariant();
    }

    private static HashSet<string>
        GetPropertyTypes(
            string propertyPart)
    {
        var result =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var segments =
            propertyPart.Split(
                ';');

        foreach (var segment in
                 segments.Skip(1))
        {
            var trimmed =
                segment.Trim();

            if (string.IsNullOrWhiteSpace(
                    trimmed))
            {
                continue;
            }

            var equalsIndex =
                trimmed.IndexOf(
                    '=');

            string value;

            if (equalsIndex >= 0)
            {
                var parameterName =
                    trimmed[..equalsIndex]
                        .Trim();

                if (!string.Equals(
                        parameterName,
                        "TYPE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value =
                    trimmed[
                        (equalsIndex + 1)..];
            }
            else
            {
                /*
                 * Einige ältere vCard-3.0-Varianten schreiben
                 * Typen auch ohne TYPE=.
                 */
                value =
                    trimmed;
            }

            foreach (var typeValue in
                     value.Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                var normalized =
                    typeValue
                        .Trim()
                        .Trim('"')
                        .ToUpperInvariant();

                if (!string.IsNullOrWhiteSpace(
                        normalized))
                {
                    result.Add(
                        normalized);
                }
            }
        }

        return result;
    }

    private static string?
        GetParameterValue(
            string propertyPart,
            string parameterName)
    {
        var segments =
            propertyPart.Split(
                ';');

        foreach (var segment in
                 segments.Skip(1))
        {
            var equalsIndex =
                segment.IndexOf(
                    '=');

            if (equalsIndex <= 0)
            {
                continue;
            }

            var name =
                segment[..equalsIndex]
                    .Trim();

            if (!string.Equals(
                    name,
                    parameterName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return segment[
                    (equalsIndex + 1)..]
                .Trim()
                .Trim('"');
        }

        return null;
    }

    private static void AddTypedValue(
        ICollection<TypedContactValue> target,
        string rawValue,
        IReadOnlySet<string> types)
    {
        var value =
            NullIfWhiteSpace(
                UnescapeVCardValue(
                    rawValue));

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return;
        }

        target.Add(
            new TypedContactValue(
                value,
                new HashSet<string>(
                    types,
                    StringComparer.OrdinalIgnoreCase)));
    }

    private static EmailSnapshot
        CreateEmailSnapshot(
            IReadOnlyList<TypedContactValue> values)
    {
        var distinct =
            DistinctTypedValues(
                values);

        var business =
            distinct
                .FirstOrDefault(
                    value =>
                        value.Types.Contains(
                            "WORK"))?
                .Value;

        var privateAddress =
            distinct
                .FirstOrDefault(
                    value =>
                        value.Types.Contains(
                            "HOME"))?
                .Value;

        var preferred =
            distinct
                .FirstOrDefault(
                    value =>
                        value.Types.Contains(
                            "PREF"))?
                .Value;

        var primary =
            preferred
            ?? business
            ?? privateAddress
            ?? distinct
                .FirstOrDefault()?
                .Value;

        var excluded =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        AddIfNotEmpty(
            excluded,
            primary);

        AddIfNotEmpty(
            excluded,
            business);

        AddIfNotEmpty(
            excluded,
            privateAddress);

        var additional =
            distinct
                .Select(
                    value =>
                        value.Value)
                .Where(
                    value =>
                        !excluded.Contains(
                            value))
                .ToArray();

        return new EmailSnapshot(
            primary,
            business,
            privateAddress,
            additional);
    }

    private static PhoneSnapshot
        CreatePhoneSnapshot(
            IReadOnlyList<TypedContactValue> values)
    {
        var distinct =
            DistinctTypedValues(
                values);

        var mobile =
            distinct
                .FirstOrDefault(
                    value =>
                        value.Types.Contains(
                            "CELL") ||
                        value.Types.Contains(
                            "MOBILE"))?
                .Value;

        var business =
            distinct
                .FirstOrDefault(
                    value =>
                        value.Types.Contains(
                            "WORK") &&
                        !value.Types.Contains(
                            "FAX"))?
                .Value;

        var privatePhone =
            distinct
                .FirstOrDefault(
                    value =>
                        value.Types.Contains(
                            "HOME") &&
                        !value.Types.Contains(
                            "FAX"))?
                .Value;

        var fax =
            distinct
                .FirstOrDefault(
                    value =>
                        value.Types.Contains(
                            "FAX"))?
                .Value;

        var preferred =
            distinct
                .FirstOrDefault(
                    value =>
                        value.Types.Contains(
                            "PREF"))?
                .Value;

        var primary =
            preferred
            ?? mobile
            ?? business
            ?? privatePhone
            ?? distinct
                .FirstOrDefault()?
                .Value;

        var excluded =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        AddIfNotEmpty(
            excluded,
            primary);

        AddIfNotEmpty(
            excluded,
            mobile);

        AddIfNotEmpty(
            excluded,
            business);

        AddIfNotEmpty(
            excluded,
            privatePhone);

        AddIfNotEmpty(
            excluded,
            fax);

        var additional =
            distinct
                .Select(
                    value =>
                        value.Value)
                .Where(
                    value =>
                        !excluded.Contains(
                            value))
                .ToArray();

        return new PhoneSnapshot(
            primary,
            mobile,
            business,
            privatePhone,
            fax,
            additional);
    }

    private static IReadOnlyList<TypedContactValue>
        DistinctTypedValues(
            IReadOnlyList<TypedContactValue> values)
    {
        var result =
            new List<TypedContactValue>();

        var indexByValue =
            new Dictionary<string, TypedContactValue>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var item in values)
        {
            if (!indexByValue.TryGetValue(
                    item.Value,
                    out var existing))
            {
                var copy =
                    new TypedContactValue(
                        item.Value,
                        new HashSet<string>(
                            item.Types,
                            StringComparer.OrdinalIgnoreCase));

                indexByValue.Add(
                    item.Value,
                    copy);

                result.Add(
                    copy);

                continue;
            }

            foreach (var type in
                     item.Types)
            {
                existing.Types.Add(
                    type);
            }
        }

        return result;
    }

    private static void AddIfNotEmpty(
        ISet<string> target,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(
                value))
        {
            target.Add(
                value);
        }
    }

    private static ContactPostalAddress
        ParsePostalAddress(
            string valuePart)
    {
        var parts =
            SplitEscaped(
                valuePart,
                ';');

        return new ContactPostalAddress
        {
            PostOfficeBox =
                GetUnescapedPart(
                    parts,
                    0),

            ExtendedAddress =
                GetUnescapedPart(
                    parts,
                    1),

            Street =
                GetUnescapedPart(
                    parts,
                    2),

            City =
                GetUnescapedPart(
                    parts,
                    3),

            Region =
                GetUnescapedPart(
                    parts,
                    4),

            PostalCode =
                GetUnescapedPart(
                    parts,
                    5),

            Country =
                GetUnescapedPart(
                    parts,
                    6)
        };
    }

    private static bool TryParsePhoto(
        string propertyPart,
        string valuePart,
        out byte[]? photoData,
        out string? photoMediaType)
    {
        photoData =
            null;

        photoMediaType =
            null;

        var encoding =
            GetParameterValue(
                propertyPart,
                "ENCODING");

        if (!string.Equals(
                encoding,
                "B",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                encoding,
                "BASE64",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var normalizedBase64 =
                new string(
                    valuePart
                        .Where(
                            character =>
                                !char.IsWhiteSpace(
                                    character))
                        .ToArray());

            photoData =
                Convert.FromBase64String(
                    normalizedBase64);

            var photoType =
                GetParameterValue(
                    propertyPart,
                    "TYPE");

            photoMediaType =
                NormalizePhotoMediaType(
                    photoType);

            return photoData.Length > 0;
        }
        catch
        {
            photoData =
                null;

            photoMediaType =
                null;

            return false;
        }
    }

    private static string? NormalizePhotoMediaType(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var firstValue =
            value
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(
                firstValue))
        {
            return null;
        }

        return firstValue
            .Trim()
            .Trim('"')
            .ToUpperInvariant()
            switch
        {
            "JPEG" =>
                "image/jpeg",

            "JPG" =>
                "image/jpeg",

            "PNG" =>
                "image/png",

            "GIF" =>
                "image/gif",

            _ when firstValue.Contains(
                '/',
                StringComparison.Ordinal) =>
                firstValue.ToLowerInvariant(),

            _ =>
                null
        };
    }

    private static string?
        GetUnescapedPart(
            IReadOnlyList<string> parts,
            int index)
    {
        if (index < 0 ||
            index >= parts.Count)
        {
            return null;
        }

        return NullIfWhiteSpace(
            UnescapeVCardValue(
                parts[index]));
    }

    private static IReadOnlyList<string>
        SplitEscaped(
            string value,
            char separator)
    {
        var result =
            new List<string>();

        var current =
            new StringBuilder();

        var escaped =
            false;

        foreach (var character in
                 value)
        {
            if (escaped)
            {
                current.Append(
                    '\\');

                current.Append(
                    character);

                escaped =
                    false;

                continue;
            }

            if (character == '\\')
            {
                escaped =
                    true;

                continue;
            }

            if (character == separator)
            {
                result.Add(
                    current.ToString());

                current.Clear();

                continue;
            }

            current.Append(
                character);
        }

        if (escaped)
        {
            current.Append(
                '\\');
        }

        result.Add(
            current.ToString());

        return result;
    }

    private static NormalizedContact
        NormalizeContactValues(
            ContactCreateRequest request)
    {
        var primaryEmail =
            FirstNonEmpty(
                request.EmailAddress,
                request.BusinessEmailAddress,
                request.PrivateEmailAddress,
                request.AdditionalEmailAddresses?
                    .FirstOrDefault());

        var primaryPhone =
            FirstNonEmpty(
                request.PhoneNumber,
                request.MobilePhoneNumber,
                request.BusinessPhoneNumber,
                request.PrivatePhoneNumber,
                request.AdditionalPhoneNumbers?
                    .FirstOrDefault());

        var displayName =
            BuildDisplayName(
                request.DisplayName,
                request.FirstName,
                request.MiddleName,
                request.LastName,
                primaryEmail);

        if (string.IsNullOrWhiteSpace(
                displayName))
        {
            throw new ArgumentException(
                "Der Kontakt benötigt mindestens einen Namen oder eine E-Mail-Adresse.");
        }

        return new NormalizedContact(
            Salutation:
                NullIfWhiteSpace(
                    request.Salutation),

            AcademicTitle:
                NullIfWhiteSpace(
                    request.AcademicTitle),

            FirstName:
                NullIfWhiteSpace(
                    request.FirstName),

            MiddleName:
                NullIfWhiteSpace(
                    request.MiddleName),

            LastName:
                NullIfWhiteSpace(
                    request.LastName),

            NameSuffix:
                NullIfWhiteSpace(
                    request.NameSuffix),

            Nickname:
                NullIfWhiteSpace(
                    request.Nickname),

            DisplayName:
                displayName,

            EmailAddress:
                NullIfWhiteSpace(
                    primaryEmail),

            BusinessEmailAddress:
                NullIfWhiteSpace(
                    request.BusinessEmailAddress),

            PrivateEmailAddress:
                NullIfWhiteSpace(
                    request.PrivateEmailAddress),

            AdditionalEmailAddresses:
                NormalizeList(
                    request.AdditionalEmailAddresses),

            PhoneNumber:
                NullIfWhiteSpace(
                    primaryPhone),

            MobilePhoneNumber:
                NullIfWhiteSpace(
                    request.MobilePhoneNumber),

            BusinessPhoneNumber:
                NullIfWhiteSpace(
                    request.BusinessPhoneNumber),

            PrivatePhoneNumber:
                NullIfWhiteSpace(
                    request.PrivatePhoneNumber),

            FaxNumber:
                NullIfWhiteSpace(
                    request.FaxNumber),

            AdditionalPhoneNumbers:
                NormalizeList(
                    request.AdditionalPhoneNumbers),

            Company:
                NullIfWhiteSpace(
                    request.Company),

            Department:
                NullIfWhiteSpace(
                    request.Department),

            JobTitle:
                NullIfWhiteSpace(
                    request.JobTitle),

            HomeAddress:
                NormalizeAddress(
                    request.HomeAddress),

            WorkAddress:
                NormalizeAddress(
                    request.WorkAddress),

            Website:
                NullIfWhiteSpace(
                    request.Website),

            Birthday:
                NullIfWhiteSpace(
                    request.Birthday),

            Notes:
                NullIfWhiteSpace(
                    request.Notes),

            Categories:
                NormalizeList(
                    request.Categories),

            PhotoData:
                ClonePhoto(
                    request.PhotoData),

            PhotoMediaType:
                NullIfWhiteSpace(
                    request.PhotoMediaType));
    }

    private static NormalizedContact
        NormalizeContactValues(
            ContactData existing,
            ContactUpdateRequest request)
    {
        var businessEmail =
            request.BusinessEmailAddress is not null
                ? NullIfWhiteSpace(
                    request.BusinessEmailAddress)
                : existing.BusinessEmailAddress;

        var privateEmail =
            request.PrivateEmailAddress is not null
                ? NullIfWhiteSpace(
                    request.PrivateEmailAddress)
                : existing.PrivateEmailAddress;

        var requestedPrimaryEmail =
            NullIfWhiteSpace(
                request.EmailAddress);

        /*
         * Die alte UI bearbeitet bisher nur EmailAddress.
         *
         * War die primäre Adresse gleichzeitig die
         * geschäftliche/private Adresse, ziehen wir die
         * Typisierung bei einer Änderung mit.
         */
        if (!string.Equals(
                requestedPrimaryEmail,
                existing.EmailAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    existing.BusinessEmailAddress,
                    existing.EmailAddress,
                    StringComparison.OrdinalIgnoreCase) &&
                request.BusinessEmailAddress is null)
            {
                businessEmail =
                    requestedPrimaryEmail;
            }

            if (string.Equals(
                    existing.PrivateEmailAddress,
                    existing.EmailAddress,
                    StringComparison.OrdinalIgnoreCase) &&
                request.PrivateEmailAddress is null)
            {
                privateEmail =
                    requestedPrimaryEmail;
            }
        }

        var mobilePhone =
            request.MobilePhoneNumber is not null
                ? NullIfWhiteSpace(
                    request.MobilePhoneNumber)
                : existing.MobilePhoneNumber;

        var businessPhone =
            request.BusinessPhoneNumber is not null
                ? NullIfWhiteSpace(
                    request.BusinessPhoneNumber)
                : existing.BusinessPhoneNumber;

        var privatePhone =
            request.PrivatePhoneNumber is not null
                ? NullIfWhiteSpace(
                    request.PrivatePhoneNumber)
                : existing.PrivatePhoneNumber;

        var requestedPrimaryPhone =
            NullIfWhiteSpace(
                request.PhoneNumber);

        if (!string.Equals(
                requestedPrimaryPhone,
                existing.PhoneNumber,
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    existing.MobilePhoneNumber,
                    existing.PhoneNumber,
                    StringComparison.OrdinalIgnoreCase) &&
                request.MobilePhoneNumber is null)
            {
                mobilePhone =
                    requestedPrimaryPhone;
            }

            if (string.Equals(
                    existing.BusinessPhoneNumber,
                    existing.PhoneNumber,
                    StringComparison.OrdinalIgnoreCase) &&
                request.BusinessPhoneNumber is null)
            {
                businessPhone =
                    requestedPrimaryPhone;
            }

            if (string.Equals(
                    existing.PrivatePhoneNumber,
                    existing.PhoneNumber,
                    StringComparison.OrdinalIgnoreCase) &&
                request.PrivatePhoneNumber is null)
            {
                privatePhone =
                    requestedPrimaryPhone;
            }
        }

        var displayName =
            BuildDisplayName(
                request.DisplayName,
                request.FirstName,
                request.MiddleName
                    ?? existing.MiddleName,
                request.LastName,
                requestedPrimaryEmail);

        if (string.IsNullOrWhiteSpace(
                displayName))
        {
            throw new ArgumentException(
                "Der Kontakt benötigt mindestens einen Namen oder eine E-Mail-Adresse.");
        }

        return new NormalizedContact(
            Salutation:
                request.Salutation
                ?? existing.Salutation,

            AcademicTitle:
                request.AcademicTitle
                ?? existing.AcademicTitle,

            FirstName:
                NullIfWhiteSpace(
                    request.FirstName),

            MiddleName:
                request.MiddleName
                ?? existing.MiddleName,

            LastName:
                NullIfWhiteSpace(
                    request.LastName),

            NameSuffix:
                request.NameSuffix
                ?? existing.NameSuffix,

            Nickname:
                request.Nickname
                ?? existing.Nickname,

            DisplayName:
                displayName,

            EmailAddress:
                requestedPrimaryEmail,

            BusinessEmailAddress:
                NullIfWhiteSpace(
                    businessEmail),

            PrivateEmailAddress:
                NullIfWhiteSpace(
                    privateEmail),

            AdditionalEmailAddresses:
                request.AdditionalEmailAddresses is not null
                    ? NormalizeList(
                        request.AdditionalEmailAddresses)
                    : NormalizeList(
                        existing.AdditionalEmailAddresses),

            PhoneNumber:
                requestedPrimaryPhone,

            MobilePhoneNumber:
                NullIfWhiteSpace(
                    mobilePhone),

            BusinessPhoneNumber:
                NullIfWhiteSpace(
                    businessPhone),

            PrivatePhoneNumber:
                NullIfWhiteSpace(
                    privatePhone),

            FaxNumber:
                request.FaxNumber
                ?? existing.FaxNumber,

            AdditionalPhoneNumbers:
                request.AdditionalPhoneNumbers is not null
                    ? NormalizeList(
                        request.AdditionalPhoneNumbers)
                    : NormalizeList(
                        existing.AdditionalPhoneNumbers),

            Company:
                request.Company
                ?? existing.Company,

            Department:
                request.Department
                ?? existing.Department,

            JobTitle:
                request.JobTitle
                ?? existing.JobTitle,

            HomeAddress:
                request.HomeAddress
                ?? existing.HomeAddress,

            WorkAddress:
                request.WorkAddress
                ?? existing.WorkAddress,

            Website:
                request.Website
                ?? existing.Website,

            Birthday:
                request.Birthday
                ?? existing.Birthday,

            Notes:
                request.Notes
                ?? existing.Notes,

            Categories:
                request.Categories is not null
                    ? NormalizeList(
                        request.Categories)
                    : NormalizeList(
                        existing.Categories),

            PhotoData:
                request.PhotoData is not null
                    ? ClonePhoto(
                        request.PhotoData)
                    : ClonePhoto(
                        existing.PhotoData),

            PhotoMediaType:
                request.PhotoMediaType
                ?? existing.PhotoMediaType);
    }

    private static ContactData CreateContactData(
        string resourcePath,
        string? eTag,
        string uid,
        NormalizedContact contact,
        IReadOnlyList<string> preservedLines)
    {
        return new ContactData
        {
            ResourcePath =
                resourcePath,

            ETag =
                eTag,

            Uid =
                uid,

            Salutation =
                contact.Salutation,

            AcademicTitle =
                contact.AcademicTitle,

            FirstName =
                contact.FirstName,

            MiddleName =
                contact.MiddleName,

            LastName =
                contact.LastName,

            NameSuffix =
                contact.NameSuffix,

            Nickname =
                contact.Nickname,

            DisplayName =
                contact.DisplayName,

            EmailAddress =
                contact.EmailAddress,

            BusinessEmailAddress =
                contact.BusinessEmailAddress,

            PrivateEmailAddress =
                contact.PrivateEmailAddress,

            AdditionalEmailAddresses =
                contact.AdditionalEmailAddresses,

            PhoneNumber =
                contact.PhoneNumber,

            MobilePhoneNumber =
                contact.MobilePhoneNumber,

            BusinessPhoneNumber =
                contact.BusinessPhoneNumber,

            PrivatePhoneNumber =
                contact.PrivatePhoneNumber,

            FaxNumber =
                contact.FaxNumber,

            AdditionalPhoneNumbers =
                contact.AdditionalPhoneNumbers,

            Company =
                contact.Company,

            Department =
                contact.Department,

            JobTitle =
                contact.JobTitle,

            HomeAddress =
                contact.HomeAddress,

            WorkAddress =
                contact.WorkAddress,

            Website =
                contact.Website,

            Birthday =
                contact.Birthday,

            Notes =
                contact.Notes,

            Categories =
                contact.Categories,

            PhotoData =
                ClonePhoto(
                    contact.PhotoData),

            PhotoMediaType =
                contact.PhotoMediaType,

            PreservedVCardLines =
                preservedLines.ToArray()
        };
    }

    private static string CreateVCard(
        string uid,
        NormalizedContact contact,
        IReadOnlyList<string> preservedLines)
    {
        var builder =
            new StringBuilder();

        AppendFoldedLine(
            builder,
            "BEGIN:VCARD");

        AppendFoldedLine(
            builder,
            "VERSION:3.0");

        AppendProperty(
            builder,
            "UID",
            uid);

        var nameValue =
            string.Join(
                ";",
                new[]
                {
                    EscapeVCardValue(
                        contact.LastName),

                    EscapeVCardValue(
                        contact.FirstName),

                    EscapeVCardValue(
                        contact.MiddleName),

                    EscapeVCardValue(
                        contact.AcademicTitle),

                    EscapeVCardValue(
                        contact.NameSuffix)
                });

        AppendFoldedLine(
            builder,
            $"N:{nameValue}");

        AppendProperty(
            builder,
            "FN",
            contact.DisplayName);

        if (!string.IsNullOrWhiteSpace(
                contact.Salutation))
        {
            AppendProperty(
                builder,
                "X-TELENEC-SALUTATION",
                contact.Salutation);
        }

        if (!string.IsNullOrWhiteSpace(
                contact.Nickname))
        {
            AppendProperty(
                builder,
                "NICKNAME",
                contact.Nickname);
        }

        AppendEmailProperties(
            builder,
            contact);

        AppendPhoneProperties(
            builder,
            contact);

        if (!string.IsNullOrWhiteSpace(
                contact.Company) ||
            !string.IsNullOrWhiteSpace(
                contact.Department))
        {
            AppendFoldedLine(
                builder,
                "ORG:" +
                EscapeVCardValue(
                    contact.Company) +
                ";" +
                EscapeVCardValue(
                    contact.Department));
        }

        if (!string.IsNullOrWhiteSpace(
                contact.JobTitle))
        {
            AppendProperty(
                builder,
                "TITLE",
                contact.JobTitle);
        }

        AppendAddressProperty(
            builder,
            "HOME",
            contact.HomeAddress);

        AppendAddressProperty(
            builder,
            "WORK",
            contact.WorkAddress);

        if (!string.IsNullOrWhiteSpace(
                contact.Website))
        {
            AppendProperty(
                builder,
                "URL",
                contact.Website);
        }

        if (!string.IsNullOrWhiteSpace(
                contact.Birthday))
        {
            AppendProperty(
                builder,
                "BDAY",
                contact.Birthday);
        }

        if (!string.IsNullOrWhiteSpace(
                contact.Notes))
        {
            AppendProperty(
                builder,
                "NOTE",
                contact.Notes);
        }

        if (contact.Categories.Count > 0)
        {
            var categoryValue =
                string.Join(
                    ",",
                    contact.Categories
                        .Select(
                            EscapeVCardValue));

            AppendFoldedLine(
                builder,
                $"CATEGORIES:{categoryValue}");
        }

        if (contact.PhotoData is
            { Length: > 0 })
        {
            var photoType =
                GetPhotoTypeToken(
                    contact.PhotoMediaType);

            var base64 =
                Convert.ToBase64String(
                    contact.PhotoData);

            AppendFoldedLine(
                builder,
                $"PHOTO;ENCODING=b;TYPE={photoType}:{base64}");
        }

        /*
         * Unbekannte Daten anderer Clients werden nach den
         * von uns verwalteten Feldern wieder eingefügt.
         */
        foreach (var preservedLine in
                 preservedLines)
        {
            if (!ShouldAppendPreservedLine(
                    preservedLine,
                    contact))
            {
                continue;
            }

            AppendFoldedLine(
                builder,
                preservedLine);
        }

        AppendFoldedLine(
            builder,
            "REV:" +
            DateTime.UtcNow.ToString(
                "yyyyMMdd'T'HHmmss'Z'"));

        AppendFoldedLine(
            builder,
            "END:VCARD");

        return builder.ToString();
    }

    private static void AppendEmailProperties(
        StringBuilder builder,
        NormalizedContact contact)
    {
        var written =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(
                contact.EmailAddress))
        {
            var types =
                new List<string>
                {
                    "INTERNET",
                    "PREF"
                };

            if (string.Equals(
                    contact.EmailAddress,
                    contact.BusinessEmailAddress,
                    StringComparison.OrdinalIgnoreCase))
            {
                types.Add(
                    "WORK");
            }

            if (string.Equals(
                    contact.EmailAddress,
                    contact.PrivateEmailAddress,
                    StringComparison.OrdinalIgnoreCase))
            {
                types.Add(
                    "HOME");
            }

            AppendTypedProperty(
                builder,
                "EMAIL",
                types,
                contact.EmailAddress);

            written.Add(
                contact.EmailAddress);
        }

        AppendTypedValueIfNeeded(
            builder,
            written,
            "EMAIL",
            new[]
            {
                "INTERNET",
                "WORK"
            },
            contact.BusinessEmailAddress);

        AppendTypedValueIfNeeded(
            builder,
            written,
            "EMAIL",
            new[]
            {
                "INTERNET",
                "HOME"
            },
            contact.PrivateEmailAddress);

        foreach (var additional in
                 contact.AdditionalEmailAddresses)
        {
            AppendTypedValueIfNeeded(
                builder,
                written,
                "EMAIL",
                new[]
                {
                    "INTERNET"
                },
                additional);
        }
    }

    private static void AppendPhoneProperties(
        StringBuilder builder,
        NormalizedContact contact)
    {
        var written =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(
                contact.PhoneNumber))
        {
            var types =
                new List<string>
                {
                    "VOICE",
                    "PREF"
                };

            if (string.Equals(
                    contact.PhoneNumber,
                    contact.MobilePhoneNumber,
                    StringComparison.OrdinalIgnoreCase))
            {
                types.Add(
                    "CELL");
            }

            if (string.Equals(
                    contact.PhoneNumber,
                    contact.BusinessPhoneNumber,
                    StringComparison.OrdinalIgnoreCase))
            {
                types.Add(
                    "WORK");
            }

            if (string.Equals(
                    contact.PhoneNumber,
                    contact.PrivatePhoneNumber,
                    StringComparison.OrdinalIgnoreCase))
            {
                types.Add(
                    "HOME");
            }

            AppendTypedProperty(
                builder,
                "TEL",
                types,
                contact.PhoneNumber);

            written.Add(
                contact.PhoneNumber);
        }

        AppendTypedValueIfNeeded(
            builder,
            written,
            "TEL",
            new[]
            {
                "CELL"
            },
            contact.MobilePhoneNumber);

        AppendTypedValueIfNeeded(
            builder,
            written,
            "TEL",
            new[]
            {
                "WORK",
                "VOICE"
            },
            contact.BusinessPhoneNumber);

        AppendTypedValueIfNeeded(
            builder,
            written,
            "TEL",
            new[]
            {
                "HOME",
                "VOICE"
            },
            contact.PrivatePhoneNumber);

        AppendTypedValueIfNeeded(
            builder,
            written,
            "TEL",
            new[]
            {
                "FAX"
            },
            contact.FaxNumber);

        foreach (var additional in
                 contact.AdditionalPhoneNumbers)
        {
            AppendTypedValueIfNeeded(
                builder,
                written,
                "TEL",
                new[]
                {
                    "VOICE"
                },
                additional);
        }
    }

    private static void AppendTypedValueIfNeeded(
        StringBuilder builder,
        ISet<string> written,
        string propertyName,
        IEnumerable<string> types,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return;
        }

        var normalized =
            value.Trim();

        if (!written.Add(
                normalized))
        {
            return;
        }

        AppendTypedProperty(
            builder,
            propertyName,
            types,
            normalized);
    }

    private static void AppendTypedProperty(
        StringBuilder builder,
        string propertyName,
        IEnumerable<string> types,
        string value)
    {
        var typeValue =
            string.Join(
                ",",
                types
                    .Where(
                        type =>
                            !string.IsNullOrWhiteSpace(
                                type))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase));

        AppendFoldedLine(
            builder,
            $"{propertyName};TYPE={typeValue}:" +
            EscapeVCardValue(
                value));
    }

    private static void AppendAddressProperty(
        StringBuilder builder,
        string type,
        ContactPostalAddress? address)
    {
        if (address is null ||
            IsAddressEmpty(
                address))
        {
            return;
        }

        var addressValue =
            string.Join(
                ";",
                new[]
                {
                    EscapeVCardValue(
                        address.PostOfficeBox),

                    EscapeVCardValue(
                        address.ExtendedAddress),

                    EscapeVCardValue(
                        address.Street),

                    EscapeVCardValue(
                        address.City),

                    EscapeVCardValue(
                        address.Region),

                    EscapeVCardValue(
                        address.PostalCode),

                    EscapeVCardValue(
                        address.Country)
                });

        AppendFoldedLine(
            builder,
            $"ADR;TYPE={type}:{addressValue}");
    }

    private static void AppendProperty(
        StringBuilder builder,
        string propertyName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return;
        }

        AppendFoldedLine(
            builder,
            $"{propertyName}:" +
            EscapeVCardValue(
                value));
    }

    private static void AppendFoldedLine(
        StringBuilder builder,
        string line)
    {
        const int firstLineLength =
            75;

        const int continuationLength =
            74;

        if (line.Length <=
            firstLineLength)
        {
            builder.AppendLine(
                line);

            return;
        }

        builder.AppendLine(
            line[..firstLineLength]);

        var index =
            firstLineLength;

        while (index <
               line.Length)
        {
            var remaining =
                line.Length -
                index;

            var length =
                Math.Min(
                    continuationLength,
                    remaining);

            builder.Append(' ');

            builder.AppendLine(
                line.Substring(
                    index,
                    length));

            index +=
                length;
        }
    }

    private static bool ShouldAppendPreservedLine(
        string line,
        NormalizedContact contact)
    {
        if (string.IsNullOrWhiteSpace(
                line))
        {
            return false;
        }

        var separatorIndex =
            line.IndexOf(
                ':');

        var propertyPart =
            separatorIndex > 0
                ? line[..separatorIndex]
                : line;

        var propertyName =
            GetPropertyName(
                propertyPart);

        switch (propertyName)
        {
            case "BEGIN":
            case "END":
            case "VERSION":
            case "UID":
            case "N":
            case "FN":
            case "REV":
            case "X-TELENEC-SALUTATION":
                return false;

            case "PHOTO":
                /*
                 * Haben wir eigene Binärdaten, ist unser
                 * PHOTO-Feld maßgeblich.
                 *
                 * Sonst darf beispielsweise ein fremdes
                 * PHOTO;VALUE=URI erhalten bleiben.
                 */
                return contact.PhotoData is not
                { Length: > 0 };

            default:
                return true;
        }
    }

    private static string GetPhotoTypeToken(
        string? mediaType)
    {
        return mediaType?
                   .Trim()
                   .ToLowerInvariant()
               switch
        {
            "image/png" =>
                "PNG",

            "image/gif" =>
                "GIF",

            _ =>
                "JPEG"
        };
    }

    private static string BuildDisplayName(
        string? explicitDisplayName,
        string? firstName,
        string? middleName,
        string? lastName,
        string? emailAddress)
    {
        if (!string.IsNullOrWhiteSpace(
                explicitDisplayName))
        {
            return explicitDisplayName
                .Trim();
        }

        var name =
            string.Join(
                " ",
                new[]
                {
                    firstName,
                    middleName,
                    lastName
                }
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .Select(
                    value =>
                        value!.Trim()))
                .Trim();

        if (!string.IsNullOrWhiteSpace(
                name))
        {
            return name;
        }

        return emailAddress?
                   .Trim()
               ?? string.Empty;
    }

    private static string? FirstNonEmpty(
        params string?[] values)
    {
        return values
            .FirstOrDefault(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value))?
            .Trim();
    }

    private static IReadOnlyList<string>
        NormalizeList(
            IEnumerable<string>? values)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value))
            .Select(
                value =>
                    value.Trim())
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static ContactPostalAddress?
        NormalizeAddress(
            ContactPostalAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        var normalized =
            new ContactPostalAddress
            {
                PostOfficeBox =
                    NullIfWhiteSpace(
                        address.PostOfficeBox),

                ExtendedAddress =
                    NullIfWhiteSpace(
                        address.ExtendedAddress),

                Street =
                    NullIfWhiteSpace(
                        address.Street),

                City =
                    NullIfWhiteSpace(
                        address.City),

                Region =
                    NullIfWhiteSpace(
                        address.Region),

                PostalCode =
                    NullIfWhiteSpace(
                        address.PostalCode),

                Country =
                    NullIfWhiteSpace(
                        address.Country)
            };

        return IsAddressEmpty(
                normalized)
            ? null
            : normalized;
    }

    private static bool IsAddressEmpty(
        ContactPostalAddress address)
    {
        return
            string.IsNullOrWhiteSpace(
                address.PostOfficeBox) &&
            string.IsNullOrWhiteSpace(
                address.ExtendedAddress) &&
            string.IsNullOrWhiteSpace(
                address.Street) &&
            string.IsNullOrWhiteSpace(
                address.City) &&
            string.IsNullOrWhiteSpace(
                address.Region) &&
            string.IsNullOrWhiteSpace(
                address.PostalCode) &&
            string.IsNullOrWhiteSpace(
                address.Country);
    }

    private static byte[]? ClonePhoto(
        byte[]? photoData)
    {
        return photoData is null
            ? null
            : photoData.ToArray();
    }

    private static string EscapeVCardValue(
        string? value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }

        return value
            .Replace(
                "\\",
                "\\\\")
            .Replace(
                ";",
                "\\;")
            .Replace(
                ",",
                "\\,")
            .Replace(
                "\r\n",
                "\\n")
            .Replace(
                "\r",
                "\\n")
            .Replace(
                "\n",
                "\\n");
    }

    private static string UnescapeVCardValue(
        string? value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }

        var builder =
            new StringBuilder();

        var escaped =
            false;

        foreach (var character in
                 value)
        {
            if (!escaped)
            {
                if (character == '\\')
                {
                    escaped =
                        true;

                    continue;
                }

                builder.Append(
                    character);

                continue;
            }

            switch (character)
            {
                case 'n':
                case 'N':
                    builder.AppendLine();
                    break;

                default:
                    builder.Append(
                        character);
                    break;
            }

            escaped =
                false;
        }

        if (escaped)
        {
            builder.Append(
                '\\');
        }

        return builder
            .ToString()
            .Trim();
    }

    private static string? NullIfWhiteSpace(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value.Trim();
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout =
                TimeSpan.FromSeconds(15)
        };
    }

    private sealed record TypedContactValue(
        string Value,
        HashSet<string> Types);

    private sealed record EmailSnapshot(
        string? Primary,
        string? Business,
        string? Private,
        IReadOnlyList<string> Additional);

    private sealed record PhoneSnapshot(
        string? Primary,
        string? Mobile,
        string? Business,
        string? Private,
        string? Fax,
        IReadOnlyList<string> Additional);

    private sealed record NormalizedContact(
        string? Salutation,
        string? AcademicTitle,
        string? FirstName,
        string? MiddleName,
        string? LastName,
        string? NameSuffix,
        string? Nickname,
        string DisplayName,
        string? EmailAddress,
        string? BusinessEmailAddress,
        string? PrivateEmailAddress,
        IReadOnlyList<string> AdditionalEmailAddresses,
        string? PhoneNumber,
        string? MobilePhoneNumber,
        string? BusinessPhoneNumber,
        string? PrivatePhoneNumber,
        string? FaxNumber,
        IReadOnlyList<string> AdditionalPhoneNumbers,
        string? Company,
        string? Department,
        string? JobTitle,
        ContactPostalAddress? HomeAddress,
        ContactPostalAddress? WorkAddress,
        string? Website,
        string? Birthday,
        string? Notes,
        IReadOnlyList<string> Categories,
        byte[]? PhotoData,
        string? PhotoMediaType);
}