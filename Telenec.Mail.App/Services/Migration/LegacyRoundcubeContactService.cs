using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Telenec.Mail.App.Services.Migration;

public sealed class LegacyRoundcubeContactService
{
    private static readonly Uri BaseUri =
        new(
            "https://webmail.telenec.de/");

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

    private static readonly Regex
        JavaScriptRequestTokenRegex =
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

    /*
     * Jede vCard wird als eigener Block erfasst.
     *
     * Den vollständigen Originalblock behalten wir im
     * Arbeitsspeicher. Dadurch können wir beim späteren
     * eigentlichen Import mehr Daten erhalten als nur die
     * wenigen Felder, die wir für die Dublettenprüfung
     * benötigen.
     */
    private static readonly Regex VCardBlockRegex =
        new(
            @"BEGIN:VCARD\s*.*?END:VCARD",
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.CultureInvariant);

    private static readonly Regex FoldedLineRegex =
        new(
            @"\r?\n[ \t]",
            RegexOptions.CultureInvariant);

    public async Task<
        LegacyRoundcubeContactInventoryResult>
        GetInventoryAsync(
            string userName,
            string password,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                userName))
        {
            return Failure(
                "Bitte geben Sie die E-Mail-Adresse des alten Postfachs ein.");
        }

        if (string.IsNullOrEmpty(
                password))
        {
            return Failure(
                "Bitte geben Sie das Passwort des alten Postfachs ein.");
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
                    TimeSpan.FromSeconds(30)
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
             * 1. Roundcube-Loginseite öffnen.
             *
             * Dadurch entstehen Session-Cookie und
             * Request-Token.
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

            /*
             * 2. Reguläre Roundcube-Anmeldung.
             */
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
             * 3. Adressbuch öffnen.
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

            /*
             * 4. Offiziellen Roundcube-vCard-Export abrufen.
             */
            var exportUri =
                new Uri(
                    BaseUri,
                    "?_task=addressbook" +
                    "&_action=export" +
                    "&_token=" +
                    Uri.EscapeDataString(
                        requestToken));

            using var exportResponse =
                await client.GetAsync(
                    exportUri,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);

            if (!exportResponse.IsSuccessStatusCode)
            {
                return Failure(
                    $"Der Kontaktbestand konnte nicht exportiert werden. " +
                    $"HTTP {(int)exportResponse.StatusCode}.");
            }

            var vCardData =
                await exportResponse
                    .Content
                    .ReadAsStringAsync(
                        cancellationToken);

            var mediaType =
                exportResponse
                    .Content
                    .Headers
                    .ContentType?
                    .MediaType;

            var vCardBlocks =
                VCardBlockRegex
                    .Matches(
                        vCardData)
                    .Select(
                        match =>
                            match.Value)
                    .ToArray();

            var responseLooksLikeVCard =
                string.Equals(
                    mediaType,
                    "text/vcard",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    mediaType,
                    "text/x-vcard",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    mediaType,
                    "text/directory",
                    StringComparison.OrdinalIgnoreCase) ||
                vCardBlocks.Length > 0;

            if (!responseLooksLikeVCard)
            {
                if (LooksLikeLoginPage(
                        vCardData))
                {
                    return Failure(
                        "Die Roundcube-Sitzung ist beim Zugriff auf das Adressbuch nicht mehr gültig.");
                }

                return Failure(
                    "Roundcube hat beim Kontakt-Export keine gültigen vCard-Daten geliefert.");
            }

            var contacts =
                vCardBlocks
                    .Select(
                        ParseContact)
                    .ToArray();

            await TryLogoutAsync(
                client,
                requestToken);

            return new LegacyRoundcubeContactInventoryResult(
                Success:
                    true,

                Contacts:
                    contacts,

                VCardData:
                    vCardData,

                Message:
                    contacts.Length == 1
                        ? "1 Kontakt wurde im alten Roundcube gefunden."
                        : $"{contacts.Length:N0} Kontakte wurden im alten Roundcube gefunden.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                "Das alte Roundcube konnte nicht zuverlässig erreicht werden.\n\n" +
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
                "Der Kontaktbestand des alten Roundcube konnte nicht eingelesen werden.\n\n" +
                exception.Message);
        }
    }

    private static LegacyRoundcubeContactInventoryItem
        ParseContact(
            string vCard)
    {
        /*
         * RFC-vCard erlaubt umgebrochene Zeilen:
         *
         * Zeilen, deren Folgezeile mit Leerzeichen oder Tab
         * beginnt, gehören logisch zusammen.
         */
        var unfolded =
            FoldedLineRegex.Replace(
                vCard,
                string.Empty);

        var lines =
            unfolded
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries);

        string? uid =
            null;

        string? displayName =
            null;

        string? structuredName =
            null;

        var emailAddresses =
            new List<string>();

        foreach (var rawLine in
                 lines)
        {
            var line =
                rawLine.Trim();

            var colonIndex =
                line.IndexOf(
                    ':');

            if (colonIndex <= 0)
            {
                continue;
            }

            var propertyDefinition =
                line[..colonIndex];

            var value =
                line[
                    (colonIndex + 1)..];

            var parameterIndex =
                propertyDefinition
                    .IndexOf(
                        ';');

            var propertyName =
                parameterIndex >= 0
                    ? propertyDefinition[
                        ..parameterIndex]
                    : propertyDefinition;

            switch (propertyName
                        .Trim()
                        .ToUpperInvariant())
            {
                case "UID":
                    uid =
                        UnescapeVCardText(
                            value)
                        .Trim();

                    break;

                case "FN":
                    displayName =
                        UnescapeVCardText(
                            value)
                        .Trim();

                    break;

                case "N":
                    structuredName =
                        CreateDisplayNameFromStructuredName(
                            value);

                    break;

                case "EMAIL":
                    {
                        var email =
                            UnescapeVCardText(
                                value)
                            .Trim();

                        if (email.StartsWith(
                                "mailto:",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            email =
                                email[
                                    "mailto:".Length..]
                                .Trim();
                        }

                        if (!string.IsNullOrWhiteSpace(
                                email) &&
                            !emailAddresses.Contains(
                                email,
                                StringComparer.OrdinalIgnoreCase))
                        {
                            emailAddresses.Add(
                                email);
                        }

                        break;
                    }
            }
        }

        displayName =
            FirstNonEmpty(
                displayName,
                structuredName,
                emailAddresses.FirstOrDefault(),
                "Kontakt");

        return new LegacyRoundcubeContactInventoryItem(
            Uid:
                uid,

            DisplayName:
                displayName,

            EmailAddresses:
                emailAddresses,

            VCardData:
                vCard);
    }

    private static string?
        CreateDisplayNameFromStructuredName(
            string value)
    {
        var parts =
            value
                .Split(
                    ';')
                .Select(
                    UnescapeVCardText)
                .ToArray();

        /*
         * vCard-N:
         *
         * Nachname;
         * Vorname;
         * weitere Namen;
         * Präfix;
         * Suffix
         */
        var lastName =
            GetPart(
                parts,
                0);

        var firstName =
            GetPart(
                parts,
                1);

        var middleName =
            GetPart(
                parts,
                2);

        var prefix =
            GetPart(
                parts,
                3);

        var suffix =
            GetPart(
                parts,
                4);

        var name =
            string.Join(
                " ",
                new[]
                {
                    prefix,
                    firstName,
                    middleName,
                    lastName,
                    suffix
                }
                .Where(
                    item =>
                        !string.IsNullOrWhiteSpace(
                            item)));

        return string.IsNullOrWhiteSpace(
                name)
            ? null
            : name.Trim();
    }

    private static string?
        GetPart(
            IReadOnlyList<string> parts,
            int index)
    {
        if (index < 0 ||
            index >= parts.Count)
        {
            return null;
        }

        var value =
            parts[index]
                .Trim();

        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value;
    }

    private static string
        UnescapeVCardText(
            string value)
    {
        return value
            .Replace(
                "\\n",
                "\n",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "\\,",
                ",",
                StringComparison.Ordinal)
            .Replace(
                "\\;",
                ";",
                StringComparison.Ordinal)
            .Replace(
                "\\\\",
                "\\",
                StringComparison.Ordinal);
    }

    private static string
        FirstNonEmpty(
            params string?[] values)
    {
        return values
            .FirstOrDefault(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value))?
            .Trim()
            ?? string.Empty;
    }

    private static string?
        ExtractRequestToken(
            string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return null;
        }

        var hiddenMatch =
            HiddenRequestTokenRegex.Match(
                html);

        if (hiddenMatch.Success)
        {
            return WebUtility.HtmlDecode(
                    hiddenMatch
                        .Groups["token"]
                        .Value)
                .Trim();
        }

        var javaScriptMatch =
            JavaScriptRequestTokenRegex.Match(
                html);

        if (javaScriptMatch.Success)
        {
            return WebUtility.HtmlDecode(
                    javaScriptMatch
                        .Groups["token"]
                        .Value)
                .Trim();
        }

        return null;
    }

    private static bool
        LooksLikeLoginPage(
            string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return false;
        }

        return
            html.Contains(
                "name=\"_user\"",
                StringComparison.OrdinalIgnoreCase) &&
            html.Contains(
                "name=\"_pass\"",
                StringComparison.OrdinalIgnoreCase);
    }

    private static async Task
        TryLogoutAsync(
            HttpClient client,
            string requestToken)
    {
        try
        {
            var logoutUri =
                new Uri(
                    BaseUri,
                    "?_task=logout" +
                    "&_token=" +
                    Uri.EscapeDataString(
                        requestToken));

            using var response =
                await client.GetAsync(
                    logoutUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken.None);
        }
        catch
        {
        }
    }

    private static LegacyRoundcubeContactInventoryResult
        Failure(
            string message)
    {
        return new LegacyRoundcubeContactInventoryResult(
            Success:
                false,

            Contacts:
                Array.Empty<
                    LegacyRoundcubeContactInventoryItem>(),

            VCardData:
                null,

            Message:
                message);
    }
}

public sealed record
    LegacyRoundcubeContactInventoryItem(
        string? Uid,
        string DisplayName,
        IReadOnlyList<string> EmailAddresses,
        string VCardData);

public sealed record
    LegacyRoundcubeContactInventoryResult(
        bool Success,
        IReadOnlyList<LegacyRoundcubeContactInventoryItem> Contacts,
        string? VCardData,
        string Message)
{
    public int ContactCount =>
        Contacts.Count;
}