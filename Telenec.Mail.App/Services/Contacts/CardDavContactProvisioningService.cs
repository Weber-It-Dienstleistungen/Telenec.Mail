using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Telenec.Mail.App.Services.Contacts;

public sealed class CardDavContactProvisioningService
    : IContactProvisioningService
{
    private static readonly Uri BaseUri =
        new("https://dav.necnet.de/");

    private static readonly HttpMethod PropFindMethod =
        new("PROPFIND");

    private static readonly HttpMethod MkColMethod =
        new("MKCOL");

    private static readonly HttpClient HttpClient =
        CreateHttpClient();

    private const string DefaultCollectionHref =
        "contacts";

    private const string DefaultDisplayName =
        "Kontakte";

    private const string DefaultDescription =
        "Telenec Kontakte";

    private const string AddressBookMkColXml =
        """
        <?xml version="1.0" encoding="UTF-8" ?>
        <create xmlns="DAV:" xmlns:CR="urn:ietf:params:xml:ns:carddav">
          <set>
            <prop>
              <resourcetype>
                <collection />
                <CR:addressbook />
              </resourcetype>
              <displayname>Kontakte</displayname>
              <CR:addressbook-description>Telenec Kontakte</CR:addressbook-description>
            </prop>
          </set>
        </create>
        """;

    private readonly ILogger<CardDavContactProvisioningService>
        _logger;

    public CardDavContactProvisioningService(
        ILogger<CardDavContactProvisioningService> logger)
    {
        _logger =
            logger;
    }

    public async Task<bool> EnsureDefaultAddressBookAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                userName))
        {
            return false;
        }

        if (string.IsNullOrEmpty(
                password))
        {
            return false;
        }

        try
        {
            var addressBookUri =
                BuildDefaultAddressBookUri(
                    userName);

            var exists =
                await AddressBookExistsAsync(
                    addressBookUri,
                    userName,
                    password,
                    cancellationToken);

            if (exists)
            {
                _logger.LogInformation(
                    "Default CardDAV address book is already available.");

                return true;
            }

            await CreateDefaultAddressBookAsync(
                addressBookUri,
                userName,
                password,
                cancellationToken);

            _logger.LogInformation(
                "Default CardDAV address book was created.");

            return true;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "CardDAV provisioning timed out.");

            return false;
        }
        catch (Exception exception)
        {
            /*
             * Kontakte sind eine Komfortfunktion.
             *
             * Ein Fehler des CardDAV-Servers darf deshalb
             * niemals verhindern, dass der Benutzer seine
             * E-Mails erreicht.
             *
             * Zugangsdaten werden selbstverständlich nicht
             * protokolliert.
             */
            _logger.LogWarning(
                exception,
                "CardDAV provisioning could not be completed.");

            return false;
        }
    }

    private static async Task<bool> AddressBookExistsAsync(
        Uri addressBookUri,
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        using var request =
            CreateAuthenticatedRequest(
                PropFindMethod,
                addressBookUri,
                userName,
                password);

        request.Headers.TryAddWithoutValidation(
            "Depth",
            "0");

        using var response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode ==
            HttpStatusCode.NotFound)
        {
            return false;
        }

        throw new HttpRequestException(
            $"CardDAV PROPFIND failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
            null,
            response.StatusCode);
    }

    private static async Task CreateDefaultAddressBookAsync(
        Uri addressBookUri,
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        using var request =
            CreateAuthenticatedRequest(
                MkColMethod,
                addressBookUri,
                userName,
                password);

        request.Content =
            new StringContent(
                AddressBookMkColXml,
                Encoding.UTF8,
                "application/xml");

        using var response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        /*
         * Falls zwischen PROPFIND und MKCOL ein anderer
         * Client dieselbe Collection angelegt hat, kann
         * Radicale mit MethodNotAllowed antworten.
         *
         * Dann prüfen wir noch einmal. Existiert das
         * Adressbuch jetzt, ist unser Ziel erreicht.
         */
        if (response.StatusCode ==
            HttpStatusCode.MethodNotAllowed)
        {
            var exists =
                await AddressBookExistsAsync(
                    addressBookUri,
                    userName,
                    password,
                    cancellationToken);

            if (exists)
            {
                return;
            }
        }

        throw new HttpRequestException(
            $"CardDAV MKCOL failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
            null,
            response.StatusCode);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
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

    private static Uri BuildDefaultAddressBookUri(
        string userName)
    {
        /*
         * Radicale ist auf lc_username=true konfiguriert.
         * Deshalb verwenden wir auch für den Collection-Pfad
         * die normalisierte Kleinschreibung.
         *
         * Das @ wird sauber URL-kodiert.
         */
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

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            /*
             * Ein nicht erreichbarer Kontakte-Server soll
             * den normalen Mail-Login nicht lange verzögern.
             */
            Timeout =
                TimeSpan.FromSeconds(8)
        };
    }
}