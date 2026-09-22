using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Telenec.Mail.App.Services.Migration;

public sealed class LegacyRoundcubeContactDeletionService
{
    private static readonly Uri BaseUri =
        new(
            "https://webmail.telenec.de/");

    private const int DeleteBatchSize =
        100;

    private static readonly Regex HiddenRequestTokenRegex =
        new(
            """
            <input\b
            (?=[^>]*\bname\s*=\s*["']_token["'])
            (?=[^>]*\bvalue\s*=\s*["'](?<token>[^"']+)["'])
            [^>]*>
            """,
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex JavaScriptRequestTokenRegex =
        new(
            """
            ["']request_token["']
            \s*:\s*
            ["'](?<token>[^"']+)["']
            """,
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex AddressBookSourceRegex =
        new(
            """
            ["']source["']
            \s*:\s*
            ["'](?<source>[^"']+)["']
            """,
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex SetEnvironmentSourceRegex =
        new(
            """
            set_env
            \(
            \s*["']source["']
            \s*,\s*
            ["'](?<source>[^"']+)["']
            \s*
            \)
            """,
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex ContactRowRegex =
        new(
            """
            (?:this\.)?
            add_contact_row
            \(
            \s*
            ["']
            (?<id>[a-zA-Z0-9+/=_-]+)
            ["']
            """,
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnorePatternWhitespace);

    public async Task<LegacyRoundcubeContactDeletionResult>
        DeleteAllAsync(
            string userName,
            string password,
            int expectedContactCount,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                userName))
        {
            return Failure(
                "Die E-Mail-Adresse des alten Postfachs fehlt.");
        }

        if (string.IsNullOrEmpty(
                password))
        {
            return Failure(
                "Das Passwort des alten Postfachs fehlt.");
        }

        if (expectedContactCount < 0)
        {
            return Failure(
                "Die erwartete Kontaktanzahl ist ungültig.");
        }

        var cookieContainer =
            new CookieContainer();

        using var handler =
            new HttpClientHandler
            {
                CookieContainer =
                    cookieContainer,

                UseCookies =
                    true,

                AllowAutoRedirect =
                    true,

                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate
            };

        using var client =
            new HttpClient(
                handler)
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        30)
            };

        client.DefaultRequestHeaders
            .UserAgent
            .ParseAdd(
                "Telenec-Mail-Migration/0.1");

        string? requestToken =
            null;

        try
        {
            /*
             * 1. Roundcube-Sitzung aufbauen.
             */
            using var loginPageResponse =
                await client.GetAsync(
                    BaseUri,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);

            if (!loginPageResponse.IsSuccessStatusCode)
            {
                return Failure(
                    $"Das alte Roundcube konnte nicht geöffnet werden. " +
                    $"HTTP {(int)loginPageResponse.StatusCode}.");
            }

            var loginPageHtml =
                await loginPageResponse
                    .Content
                    .ReadAsStringAsync(
                        cancellationToken);

            requestToken =
                ExtractRequestToken(
                    loginPageHtml);

            if (string.IsNullOrWhiteSpace(
                    requestToken))
            {
                return Failure(
                    "Das Sicherheitstoken der alten Roundcube-Anmeldung konnte nicht ermittelt werden.");
            }

            var loginUri =
                new Uri(
                    BaseUri,
                    "?_task=login&_action=login");

            using var loginContent =
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["_token"] =
                            requestToken,

                        ["_task"] =
                            "login",

                        ["_action"] =
                            "login",

                        ["_timezone"] =
                            "Europe/Berlin",

                        ["_url"] =
                            "_task=addressbook",

                        ["_user"] =
                            userName.Trim(),

                        ["_pass"] =
                            password
                    });

            using var loginResponse =
                await client.PostAsync(
                    loginUri,
                    loginContent,
                    cancellationToken);

            if (!loginResponse.IsSuccessStatusCode)
            {
                return Failure(
                    "Die Anmeldung am alten Roundcube wurde abgelehnt.");
            }

            /*
             * 2. Adressbuch öffnen und aktuelle Sessiondaten
             *    ermitteln.
             */
            var addressBookUri =
                new Uri(
                    BaseUri,
                    "?_task=addressbook");

            using var addressBookResponse =
                await client.GetAsync(
                    addressBookUri,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);

            if (!addressBookResponse.IsSuccessStatusCode)
            {
                return Failure(
                    "Das alte Roundcube-Adressbuch konnte nicht geöffnet werden.");
            }

            var addressBookHtml =
                await addressBookResponse
                    .Content
                    .ReadAsStringAsync(
                        cancellationToken);

            if (LooksLikeLoginPage(
                    addressBookHtml))
            {
                return Failure(
                    "Die Anmeldung am alten Roundcube konnte nicht bestätigt werden.");
            }

            requestToken =
                ExtractRequestToken(
                    addressBookHtml)
                ?? requestToken;

            var sourceId =
                ExtractAddressBookSource(
                    addressBookHtml)
                ?? "0";

            /*
             * 3. Löschbare Roundcube-Datensatz-IDs lesen.
             *
             * Die vCard-UID verwenden wir hierfür bewusst
             * NICHT. Gelöscht wird über die tatsächlichen
             * Roundcube-Kontakt-IDs.
             */
            var contactIds =
                await GetAllContactIdsAsync(
                    client,
                    requestToken,
                    sourceId,
                    cancellationToken);

            if (contactIds.Count !=
                expectedContactCount)
            {
                return Failure(
                    "Der Roundcube-Löschbestand stimmt nicht mit der zuvor verifizierten Bestandsaufnahme überein.\n\n" +
                    $"Verifiziert: {expectedContactCount:N0} Kontakte\n" +
                    $"Aktuell löschbare Roundcube-Datensätze: {contactIds.Count:N0}\n\n" +
                    "Aus Sicherheitsgründen wurde nichts gelöscht.",
                    currentContactCount:
                        contactIds.Count);
            }

            if (contactIds.Count == 0)
            {
                await TryLogoutAsync(
                    client,
                    requestToken);

                return new LegacyRoundcubeContactDeletionResult(
                    Success:
                        true,

                    Message:
                        "Das alte Roundcube-Adressbuch ist bereits leer.",

                    DeletedContactCount:
                        0,

                    RemainingContactCount:
                        0);
            }

            /*
             * 4. Kontakte in überschaubaren Paketen löschen.
             *
             * Nach jedem HTTP-Aufruf verlassen wir uns noch
             * NICHT auf die Erfolgsmeldung von Roundcube.
             * Entscheidend ist die abschließende Inventur.
             */
            foreach (var batch in
                     contactIds.Chunk(
                         DeleteBatchSize))
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var deleteUri =
                    new Uri(
                        BaseUri,
                        "?_task=addressbook" +
                        "&_action=delete" +
                        "&_remote=1" +
                        "&_token=" +
                        Uri.EscapeDataString(
                            requestToken));

                using var deleteRequest =
                    new HttpRequestMessage(
                        HttpMethod.Post,
                        deleteUri);

                deleteRequest.Headers.TryAddWithoutValidation(
                    "X-Requested-With",
                    "XMLHttpRequest");

                deleteRequest.Headers.TryAddWithoutValidation(
                    "Accept",
                    "application/json");

                deleteRequest.Content =
                    new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            ["_token"] =
                                requestToken,

                            ["_task"] =
                                "addressbook",

                            ["_action"] =
                                "delete",

                            ["_remote"] =
                                "1",

                            ["_unlock"] =
                                "0",

                            ["_source"] =
                                sourceId,

                            ["_cid"] =
                                string.Join(
                                    ",",
                                    batch)
                        });

                using var deleteResponse =
                    await client.SendAsync(
                        deleteRequest,
                        HttpCompletionOption.ResponseContentRead,
                        cancellationToken);

                if (!deleteResponse.IsSuccessStatusCode)
                {
                    return Failure(
                        "Roundcube hat das Löschen der alten Kontakte nicht bestätigt.\n\n" +
                        $"HTTP {(int)deleteResponse.StatusCode}.",
                        currentContactCount:
                            contactIds.Count);
                }

                var deleteResponseBody =
                    await deleteResponse
                        .Content
                        .ReadAsStringAsync(
                            cancellationToken);

                if (LooksLikeLoginPage(
                        deleteResponseBody))
                {
                    return Failure(
                        "Die Roundcube-Sitzung ist während des Löschvorgangs abgelaufen.",
                        currentContactCount:
                            contactIds.Count);
                }
            }

            /*
             * 5. Roundcube selbst unmittelbar erneut
             *    inventarisieren.
             */
            var remainingIds =
                await GetAllContactIdsAsync(
                    client,
                    requestToken,
                    sourceId,
                    cancellationToken);

            var deletedCount =
                Math.Max(
                    contactIds.Count -
                    remainingIds.Count,
                    0);

            if (remainingIds.Count != 0)
            {
                return new LegacyRoundcubeContactDeletionResult(
                    Success:
                        false,

                    Message:
                        $"Roundcube enthält nach dem Löschvorgang noch {remainingIds.Count:N0} Kontakte. " +
                        "Der Vorgang gilt deshalb NICHT als vollständig abgeschlossen.",

                    DeletedContactCount:
                        deletedCount,

                    RemainingContactCount:
                        remainingIds.Count);
            }

            await TryLogoutAsync(
                client,
                requestToken);

            return new LegacyRoundcubeContactDeletionResult(
                Success:
                    true,

                Message:
                    $"Alle {deletedCount:N0} alten Roundcube-Kontakte wurden gelöscht und der leere Quellbestand anschließend bestätigt.",

                DeletedContactCount:
                    deletedCount,

                RemainingContactCount:
                    0);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                "Das alte Roundcube konnte während der Kontaktlöschung nicht zuverlässig erreicht werden.\n\n" +
                exception.Message);
        }
        catch (TaskCanceledException)
        {
            return Failure(
                "Die Verbindung zum alten Roundcube hat das Zeitlimit überschritten.");
        }
        catch (Exception exception)
        {
            return Failure(
                "Die alten Roundcube-Kontakte konnten nicht vollständig gelöscht werden.\n\n" +
                exception.Message);
        }
    }

    private static async Task<IReadOnlyList<string>>
        GetAllContactIdsAsync(
            HttpClient client,
            string requestToken,
            string sourceId,
            CancellationToken cancellationToken)
    {
        var contactIds =
            new List<string>();

        var page =
            1;

        var pageCount =
            1;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var listUri =
                new Uri(
                    BaseUri,
                    "?_task=addressbook" +
                    "&_action=list" +
                    "&_remote=1" +
                    "&_source=" +
                    Uri.EscapeDataString(
                        sourceId) +
                    "&_page=" +
                    page +
                    "&_token=" +
                    Uri.EscapeDataString(
                        requestToken));

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    listUri);

            request.Headers.TryAddWithoutValidation(
                "X-Requested-With",
                "XMLHttpRequest");

            request.Headers.TryAddWithoutValidation(
                "Accept",
                "application/json");

            using var response =
                await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new IOException(
                    $"Die Roundcube-Kontaktliste konnte nicht gelesen werden. " +
                    $"HTTP {(int)response.StatusCode}.");
            }

            var responseText =
                await response
                    .Content
                    .ReadAsStringAsync(
                        cancellationToken);

            if (LooksLikeLoginPage(
                    responseText))
            {
                throw new IOException(
                    "Die Roundcube-Sitzung ist beim Lesen der Kontaktliste nicht mehr gültig.");
            }

            using var document =
                JsonDocument.Parse(
                    responseText);

            var root =
                document.RootElement;

            if (root.TryGetProperty(
                    "exec",
                    out var execElement) &&
                execElement.ValueKind ==
                JsonValueKind.String)
            {
                var executionScript =
                    execElement.GetString()
                    ?? string.Empty;

                foreach (Match match in
                         ContactRowRegex.Matches(
                             executionScript))
                {
                    var id =
                        match.Groups["id"]
                            .Value;

                    if (!string.IsNullOrWhiteSpace(
                            id) &&
                        !contactIds.Contains(
                            id,
                            StringComparer.Ordinal))
                    {
                        contactIds.Add(
                            id);
                    }
                }
            }

            pageCount =
                ReadPageCount(
                    root);

            page++;

            if (page > 10000)
            {
                throw new IOException(
                    "Roundcube meldet eine unplausibel große Anzahl Kontaktseiten.");
            }
        }
        while (page <=
               pageCount);

        return contactIds;
    }

    private static int
        ReadPageCount(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "env",
                out var environment) ||
            environment.ValueKind !=
            JsonValueKind.Object ||
            !environment.TryGetProperty(
                "pagecount",
                out var pageCountElement))
        {
            return 1;
        }

        if (pageCountElement.ValueKind ==
                JsonValueKind.Number &&
            pageCountElement.TryGetInt32(
                out var numericPageCount))
        {
            return Math.Max(
                numericPageCount,
                1);
        }

        if (pageCountElement.ValueKind ==
                JsonValueKind.String &&
            int.TryParse(
                pageCountElement.GetString(),
                out var stringPageCount))
        {
            return Math.Max(
                stringPageCount,
                1);
        }

        return 1;
    }

    private static string?
        ExtractAddressBookSource(
            string html)
    {
        var setEnvironmentMatch =
            SetEnvironmentSourceRegex.Match(
                html);

        if (setEnvironmentMatch.Success)
        {
            return WebUtility.HtmlDecode(
                setEnvironmentMatch
                    .Groups["source"]
                    .Value);
        }

        var environmentMatch =
            AddressBookSourceRegex.Match(
                html);

        if (environmentMatch.Success)
        {
            return WebUtility.HtmlDecode(
                environmentMatch
                    .Groups["source"]
                    .Value);
        }

        return null;
    }

    private static string?
        ExtractRequestToken(
            string html)
    {
        var hiddenMatch =
            HiddenRequestTokenRegex.Match(
                html);

        if (hiddenMatch.Success)
        {
            return WebUtility.HtmlDecode(
                hiddenMatch
                    .Groups["token"]
                    .Value);
        }

        var javaScriptMatch =
            JavaScriptRequestTokenRegex.Match(
                html);

        if (javaScriptMatch.Success)
        {
            return WebUtility.HtmlDecode(
                javaScriptMatch
                    .Groups["token"]
                    .Value);
        }

        return null;
    }

    private static bool
        LooksLikeLoginPage(
            string content)
    {
        if (string.IsNullOrWhiteSpace(
                content))
        {
            return false;
        }

        return
            content.Contains(
                "name=\"_user\"",
                StringComparison.OrdinalIgnoreCase) &&
            content.Contains(
                "name=\"_pass\"",
                StringComparison.OrdinalIgnoreCase);
    }

    private static async Task
        TryLogoutAsync(
            HttpClient client,
            string? requestToken)
    {
        try
        {
            var logoutUri =
                string.IsNullOrWhiteSpace(
                    requestToken)
                    ? new Uri(
                        BaseUri,
                        "?_task=logout")
                    : new Uri(
                        BaseUri,
                        "?_task=logout&_token=" +
                        Uri.EscapeDataString(
                            requestToken));

            using var cancellationSource =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(
                        5));

            using var response =
                await client.GetAsync(
                    logoutUri,
                    cancellationSource.Token);
        }
        catch
        {
        }
    }

    private static LegacyRoundcubeContactDeletionResult
        Failure(
            string message,
            int? currentContactCount = null)
    {
        return new LegacyRoundcubeContactDeletionResult(
            Success:
                false,

            Message:
                message,

            DeletedContactCount:
                0,

            RemainingContactCount:
                currentContactCount);
    }
}

public sealed record LegacyRoundcubeContactDeletionResult(
    bool Success,
    string Message,
    int DeletedContactCount,
    int? RemainingContactCount);