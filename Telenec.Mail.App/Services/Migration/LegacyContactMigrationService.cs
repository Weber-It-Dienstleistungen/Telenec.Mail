using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Migration;

public sealed class LegacyContactMigrationService
{
    private static readonly Uri BaseUri =
        new(
            "https://dav.necnet.de/");

    private const string DefaultCollectionHref =
        "contacts";

    private static readonly HttpClient HttpClient =
        new();

    private static readonly Regex UidLineRegex =
        new(
            @"(?im)^UID(?:;[^:]*)?:(?<uid>[^\r\n]+)",
            RegexOptions.CultureInvariant);

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    public LegacyContactMigrationService(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;
    }

    public LegacyContactMigrationPlan
        CreatePlan(
            IReadOnlyList<
                LegacyRoundcubeContactInventoryItem>
                sourceContacts,
            IReadOnlyList<ContactData>
                targetContacts)
    {
        ArgumentNullException.ThrowIfNull(
            sourceContacts);

        ArgumentNullException.ThrowIfNull(
            targetContacts);

        var knownUids =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var knownEmailAddresses =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var targetContact in
                 targetContacts)
        {
            if (!string.IsNullOrWhiteSpace(
                    targetContact.Uid))
            {
                knownUids.Add(
                    targetContact.Uid.Trim());
            }

            AddContactEmailAddresses(
                targetContact,
                knownEmailAddresses);
        }

        var contactsToImport =
            new List<
                LegacyContactMigrationCandidate>();

        var existingContactCount =
            0;

        foreach (var sourceContact in
                 sourceContacts)
        {
            var effectiveUid =
                GetEffectiveUid(
                    sourceContact);

            var normalizedEmails =
                sourceContact
                    .EmailAddresses
                    .Where(
                        email =>
                            !string.IsNullOrWhiteSpace(
                                email))
                    .Select(
                        email =>
                            email.Trim())
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var uidAlreadyExists =
                knownUids.Contains(
                    effectiveUid);

            var emailAlreadyExists =
                normalizedEmails.Any(
                    knownEmailAddresses.Contains);

            if (uidAlreadyExists ||
                emailAlreadyExists)
            {
                existingContactCount++;

                continue;
            }

            contactsToImport.Add(
                new LegacyContactMigrationCandidate(
                    SourceContact:
                        sourceContact,

                    EffectiveUid:
                        effectiveUid));

            /*
             * Auch innerhalb des alten Roundcube-Bestands
             * werden identische UID/E-Mail-Kombinationen
             * nur einmal eingeplant.
             */
            knownUids.Add(
                effectiveUid);

            foreach (var emailAddress in
                     normalizedEmails)
            {
                knownEmailAddresses.Add(
                    emailAddress);
            }
        }

        return new LegacyContactMigrationPlan(
            SourceContactCount:
                sourceContacts.Count,

            ExistingContactCount:
                existingContactCount,

            ContactsToImport:
                contactsToImport);
    }

    public async Task<LegacyContactMigrationResult>
        ImportAsync(
            LegacyContactMigrationPlan plan,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            plan);

        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            throw new InvalidOperationException(
                "Es ist kein aktives Telenec-Mail-Konto eingerichtet.");
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
                "Für das neue Telenec-Mail-Konto sind keine gültigen Zugangsdaten gespeichert.");
        }

        var addressBookUri =
            BuildDefaultAddressBookUri(
                credential.UserName);

        var importedUids =
            new List<string>();

        var skippedExistingCount =
            plan.ExistingContactCount;

        var failures =
            new List<string>();

        foreach (var candidate in
                 plan.ContactsToImport)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            try
            {
                var normalizedVCard =
                    PrepareVCardForImport(
                        candidate.SourceContact
                            .VCardData,
                        candidate.EffectiveUid);

                var resourceName =
                    CreateDeterministicResourceName(
                        candidate.EffectiveUid);

                var resourceUri =
                    new Uri(
                        addressBookUri,
                        resourceName);

                using var request =
                    CreateAuthenticatedRequest(
                        HttpMethod.Put,
                        resourceUri,
                        credential.UserName,
                        credential.Password);

                /*
                 * Bestehende CardDAV-Ressourcen werden
                 * niemals überschrieben.
                 */
                request.Headers
                    .TryAddWithoutValidation(
                        "If-None-Match",
                        "*");

                request.Content =
                    CreateVCardContent(
                        normalizedVCard);

                using var response =
                    await HttpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                if (response.StatusCode ==
                    HttpStatusCode.PreconditionFailed)
                {
                    /*
                     * Die deterministische Zielressource
                     * existiert bereits.
                     *
                     * Ob der Kontakt tatsächlich vorhanden
                     * ist, wird NICHT hier per Einzel-GET
                     * entschieden.
                     *
                     * Die belastbare Verifikation erfolgt nach
                     * dem gesamten Batch über den normalen
                     * CardDAV-REPORT von Telenec Mail.
                     */
                    skippedExistingCount++;

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    failures.Add(
                        $"{candidate.SourceContact.DisplayName}: " +
                        $"CardDAV antwortete mit HTTP {(int)response.StatusCode} ({response.StatusCode}).");

                    continue;
                }

                /*
                 * Ein erfolgreicher PUT wird zunächst als
                 * geschrieben registriert.
                 *
                 * Ob der Kontakt anschließend wirklich im
                 * vollständigen Zieladressbuch sichtbar ist,
                 * prüft der aufrufende Migrationsdialog nach
                 * Abschluss des gesamten Batches.
                 */
                importedUids.Add(
                    candidate.EffectiveUid);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"{candidate.SourceContact.DisplayName}: " +
                    exception.Message);
            }
        }

        return new LegacyContactMigrationResult(
            ImportedUids:
                importedUids,

            SkippedExistingCount:
                skippedExistingCount,

            Failures:
                failures);
    }

    private static string
        GetEffectiveUid(
            LegacyRoundcubeContactInventoryItem
                sourceContact)
    {
        if (!string.IsNullOrWhiteSpace(
                sourceContact.Uid))
        {
            return sourceContact
                .Uid
                .Trim();
        }

        /*
         * Besitzt die alte vCard keine UID, wird aus ihrem
         * vollständigen Inhalt eine deterministische UID
         * erzeugt.
         *
         * Dadurch bleibt die Migration auch bei einem
         * erneuten Lauf idempotent.
         */
        var sourceBytes =
            Encoding.UTF8.GetBytes(
                NormalizeLineEndings(
                    sourceContact.VCardData));

        var hash =
            SHA256.HashData(
                sourceBytes);

        return
            "telenec-migration-" +
            Convert
                .ToHexString(
                    hash)
                .ToLowerInvariant();
    }

    private static string
        PrepareVCardForImport(
            string vCard,
            string effectiveUid)
    {
        if (string.IsNullOrWhiteSpace(
                vCard))
        {
            throw new InvalidOperationException(
                "Die alte vCard enthält keine Daten.");
        }

        var normalized =
            NormalizeLineEndings(
                vCard)
            .Trim();

        if (!normalized.StartsWith(
                "BEGIN:VCARD",
                StringComparison.OrdinalIgnoreCase) ||
            !normalized.EndsWith(
                "END:VCARD",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die alte Kontaktdatei besitzt kein gültiges vCard-Format.");
        }

        var uidMatch =
            UidLineRegex.Match(
                normalized);

        if (!uidMatch.Success)
        {
            var endIndex =
                normalized.LastIndexOf(
                    "END:VCARD",
                    StringComparison.OrdinalIgnoreCase);

            normalized =
                normalized
                    .Insert(
                        endIndex,
                        $"UID:{effectiveUid}\r\n");
        }

        return normalized +
               "\r\n";
    }

    private static string
        NormalizeLineEndings(
            string value)
    {
        return value
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n')
            .Replace(
                "\n",
                "\r\n",
                StringComparison.Ordinal);
    }

    private static string
        CreateDeterministicResourceName(
            string effectiveUid)
    {
        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    effectiveUid));

        return
            "migration-" +
            Convert
                .ToHexString(
                    hash)
                .ToLowerInvariant() +
            ".vcf";
    }

    private static void
        AddContactEmailAddresses(
            ContactData contact,
            ISet<string> target)
    {
        AddEmailAddress(
            contact.EmailAddress,
            target);

        AddEmailAddress(
            contact.BusinessEmailAddress,
            target);

        AddEmailAddress(
            contact.PrivateEmailAddress,
            target);

        foreach (var emailAddress in
                 contact.AdditionalEmailAddresses)
        {
            AddEmailAddress(
                emailAddress,
                target);
        }
    }

    private static void
        AddEmailAddress(
            string? emailAddress,
            ISet<string> target)
    {
        if (string.IsNullOrWhiteSpace(
                emailAddress))
        {
            return;
        }

        target.Add(
            emailAddress.Trim());
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

    private static HttpContent
        CreateVCardContent(
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

    private static Uri
        BuildDefaultAddressBookUri(
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
}

public sealed record
    LegacyContactMigrationCandidate(
        LegacyRoundcubeContactInventoryItem
            SourceContact,
        string EffectiveUid);

public sealed record
    LegacyContactMigrationPlan(
        int SourceContactCount,
        int ExistingContactCount,
        IReadOnlyList<
            LegacyContactMigrationCandidate>
            ContactsToImport)
{
    public int ContactsToImportCount =>
        ContactsToImport.Count;
}

public sealed record
    LegacyContactMigrationResult(
        IReadOnlyList<string> ImportedUids,
        int SkippedExistingCount,
        IReadOnlyList<string> Failures)
{
    public int ImportedCount =>
        ImportedUids.Count;

    public int FailedCount =>
        Failures.Count;
}