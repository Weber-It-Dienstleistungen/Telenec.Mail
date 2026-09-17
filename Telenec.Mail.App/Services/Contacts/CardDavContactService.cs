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

    public async Task<IReadOnlyList<ContactData>> GetAllAsync(
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
                $"CardDAV contact retrieval failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
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
                request.FirstName,
                request.LastName,
                request.DisplayName,
                request.EmailAddress,
                request.PhoneNumber);

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
                normalizedContact);

        using var httpRequest =
            CreateAuthenticatedRequest(
                HttpMethod.Put,
                resourceUri,
                credential.UserName,
                credential.Password);

        /*
         * Create darf niemals unbemerkt einen bereits
         * vorhandenen Kontakt überschreiben.
         */
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
                $"CardDAV contact creation failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        _logger.LogInformation(
            "CardDAV contact creation completed successfully.");

        return new ContactData
        {
            ResourcePath =
                resourceUri.AbsolutePath,

            ETag =
                response.Headers.ETag?.Tag,

            Uid =
                uid,

            FirstName =
                normalizedContact.FirstName,

            LastName =
                normalizedContact.LastName,

            DisplayName =
                normalizedContact.DisplayName,

            EmailAddress =
                normalizedContact.EmailAddress,

            PhoneNumber =
                normalizedContact.PhoneNumber
        };
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

        var normalizedContact =
            NormalizeContactValues(
                request.FirstName,
                request.LastName,
                request.DisplayName,
                request.EmailAddress,
                request.PhoneNumber);

        /*
         * Vorhandene UID unbedingt bewahren.
         *
         * Falls ein importierter Altbestand wider Erwarten
         * keine UID besitzt, bekommt er bei der ersten
         * Bearbeitung eine stabile neue UID.
         */
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
                normalizedContact);

        using var httpRequest =
            CreateAuthenticatedRequest(
                HttpMethod.Put,
                resourceUri,
                credential.UserName,
                credential.Password);

        /*
         * Ist ein ETag bekannt, verwenden wir If-Match.
         *
         * Damit überschreiben wir nicht stillschweigend
         * Änderungen, die inzwischen durch einen anderen
         * CardDAV-Client erfolgt sind.
         */
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
                $"CardDAV contact update failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        _logger.LogInformation(
            "CardDAV contact update completed successfully.");

        return new ContactData
        {
            ResourcePath =
                resourceUri.AbsolutePath,

            ETag =
                response.Headers.ETag?.Tag
                ?? contact.ETag,

            Uid =
                uid,

            FirstName =
                normalizedContact.FirstName,

            LastName =
                normalizedContact.LastName,

            DisplayName =
                normalizedContact.DisplayName,

            EmailAddress =
                normalizedContact.EmailAddress,

            PhoneNumber =
                normalizedContact.PhoneNumber
        };
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
                $"CardDAV contact deletion failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
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
            resourcePath
                .TrimStart('/'));
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

        string? firstName =
            null;

        string? lastName =
            null;

        string? displayName =
            null;

        string? emailAddress =
            null;

        string? phoneNumber =
            null;

        foreach (var line in lines)
        {
            var separatorIndex =
                line.IndexOf(
                    ':');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var propertyPart =
                line[..separatorIndex];

            var valuePart =
                line[(separatorIndex + 1)..];

            var propertyName =
                propertyPart
                    .Split(
                        ';',
                        2)[0]
                    .Trim()
                    .ToUpperInvariant();

            switch (propertyName)
            {
                case "UID":
                    uid =
                        UnescapeVCardValue(
                            valuePart);
                    break;

                case "FN":
                    displayName =
                        UnescapeVCardValue(
                            valuePart);
                    break;

                case "N":
                    {
                        var parts =
                            SplitEscaped(
                                valuePart,
                                ';');

                        if (parts.Count > 0)
                        {
                            lastName =
                                UnescapeVCardValue(
                                    parts[0]);
                        }

                        if (parts.Count > 1)
                        {
                            firstName =
                                UnescapeVCardValue(
                                    parts[1]);
                        }

                        break;
                    }

                case "EMAIL":
                    if (string.IsNullOrWhiteSpace(
                            emailAddress))
                    {
                        emailAddress =
                            UnescapeVCardValue(
                                valuePart);
                    }

                    break;

                case "TEL":
                    if (string.IsNullOrWhiteSpace(
                            phoneNumber))
                    {
                        phoneNumber =
                            UnescapeVCardValue(
                                valuePart);
                    }

                    break;
            }
        }

        displayName =
            BuildDisplayName(
                displayName,
                firstName,
                lastName,
                emailAddress);

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

            FirstName =
                NullIfWhiteSpace(
                    firstName),

            LastName =
                NullIfWhiteSpace(
                    lastName),

            DisplayName =
                displayName,

            EmailAddress =
                NullIfWhiteSpace(
                    emailAddress),

            PhoneNumber =
                NullIfWhiteSpace(
                    phoneNumber)
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

        foreach (var sourceLine in sourceLines)
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

        foreach (var character in value)
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
            string? firstName,
            string? lastName,
            string? displayName,
            string? emailAddress,
            string? phoneNumber)
    {
        firstName =
            NullIfWhiteSpace(
                firstName);

        lastName =
            NullIfWhiteSpace(
                lastName);

        emailAddress =
            NullIfWhiteSpace(
                emailAddress);

        phoneNumber =
            NullIfWhiteSpace(
                phoneNumber);

        displayName =
            BuildDisplayName(
                displayName,
                firstName,
                lastName,
                emailAddress);

        if (string.IsNullOrWhiteSpace(
                displayName))
        {
            throw new ArgumentException(
                "Der Kontakt benötigt mindestens einen Namen oder eine E-Mail-Adresse.");
        }

        return new NormalizedContact(
            firstName,
            lastName,
            displayName,
            emailAddress,
            phoneNumber);
    }

    private static string CreateVCard(
        string uid,
        NormalizedContact contact)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            "BEGIN:VCARD");

        builder.AppendLine(
            "VERSION:3.0");

        builder.Append(
            "UID:")
            .AppendLine(
                EscapeVCardValue(
                    uid));

        builder.Append(
            "N:")
            .Append(
                EscapeVCardValue(
                    contact.LastName))
            .Append(';')
            .Append(
                EscapeVCardValue(
                    contact.FirstName))
            .AppendLine(
                ";;;");

        builder.Append(
            "FN:")
            .AppendLine(
                EscapeVCardValue(
                    contact.DisplayName));

        if (!string.IsNullOrWhiteSpace(
                contact.EmailAddress))
        {
            builder.Append(
                "EMAIL;TYPE=INTERNET:")
                .AppendLine(
                    EscapeVCardValue(
                        contact.EmailAddress));
        }

        if (!string.IsNullOrWhiteSpace(
                contact.PhoneNumber))
        {
            builder.Append(
                "TEL:")
                .AppendLine(
                    EscapeVCardValue(
                        contact.PhoneNumber));
        }

        builder.Append(
            "REV:")
            .AppendLine(
                DateTime.UtcNow
                    .ToString(
                        "yyyyMMdd'T'HHmmss'Z'"));

        builder.AppendLine(
            "END:VCARD");

        return builder.ToString();
    }

    private static string BuildDisplayName(
        string? explicitDisplayName,
        string? firstName,
        string? lastName,
        string? emailAddress)
    {
        if (!string.IsNullOrWhiteSpace(
                explicitDisplayName))
        {
            return explicitDisplayName.Trim();
        }

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
                            value)))
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

        foreach (var character in value)
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

    private sealed record NormalizedContact(
        string? FirstName,
        string? LastName,
        string DisplayName,
        string? EmailAddress,
        string? PhoneNumber);
}