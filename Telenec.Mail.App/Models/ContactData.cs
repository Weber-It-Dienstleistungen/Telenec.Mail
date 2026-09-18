namespace Telenec.Mail.App.Models;

/*
 * Zentrales Kontaktmodell von Telenec Mail.
 *
 * Die bisherigen einfachen Eigenschaften
 *
 * - FirstName
 * - LastName
 * - DisplayName
 * - EmailAddress
 * - PhoneNumber
 *
 * bleiben bewusst bestehen.
 *
 * Dadurch funktionieren die aktuelle Kontaktoberfläche und
 * bereits vorhandener Code unverändert weiter, während wir
 * das Modell jetzt auf ein vollständigeres CardDAV-/vCard-
 * Niveau anheben.
 */
public sealed class ContactData
{
    /*
     * CardDAV-Metadaten.
     */
    public required string ResourcePath { get; init; }

    public string? ETag { get; init; }

    public string? Uid { get; init; }

    /*
     * Person / Name.
     */
    public string? Salutation { get; init; }

    public string? AcademicTitle { get; init; }

    public string? FirstName { get; init; }

    public string? MiddleName { get; init; }

    public string? LastName { get; init; }

    public string? NameSuffix { get; init; }

    public string? Nickname { get; init; }

    public required string DisplayName { get; init; }

    /*
     * E-Mail.
     *
     * EmailAddress bleibt die bevorzugte bzw. primäre
     * Adresse und wird weiterhin von der bestehenden UI
     * verwendet.
     *
     * Die typisierten Felder bilden zusätzliche vCard-
     * Informationen ab.
     */
    public string? EmailAddress { get; init; }

    public string? BusinessEmailAddress { get; init; }

    public string? PrivateEmailAddress { get; init; }

    public IReadOnlyList<string>
        AdditionalEmailAddresses
    { get; init; } =
        Array.Empty<string>();

    /*
     * Telefon.
     *
     * PhoneNumber bleibt die bevorzugte bzw. primäre Nummer
     * für die bestehende Oberfläche.
     */
    public string? PhoneNumber { get; init; }

    public string? MobilePhoneNumber { get; init; }

    public string? BusinessPhoneNumber { get; init; }

    public string? PrivatePhoneNumber { get; init; }

    public string? FaxNumber { get; init; }

    public IReadOnlyList<string>
        AdditionalPhoneNumbers
    { get; init; } =
        Array.Empty<string>();

    /*
     * Beruf.
     */
    public string? Company { get; init; }

    public string? Department { get; init; }

    public string? JobTitle { get; init; }

    /*
     * Anschriften.
     */
    public ContactPostalAddress?
        HomeAddress
    { get; init; }

    public ContactPostalAddress?
        WorkAddress
    { get; init; }

    /*
     * Weitere Angaben.
     *
     * Birthday bleibt zunächst als Zeichenfolge erhalten.
     * Dadurch verlieren wir keine CardDAV-Daten, falls ein
     * anderer Client ein Datumsformat verwendet, das unsere
     * spätere Oberfläche nicht unmittelbar darstellen kann.
     */
    public string? Website { get; init; }

    public string? Birthday { get; init; }

    public string? Notes { get; init; }

    /*
     * CardDAV-Gruppen.
     *
     * Diese werden später über die standardisierte
     * vCard-Eigenschaft CATEGORIES mit Roundcube geteilt.
     */
    public IReadOnlyList<string>
        Categories
    { get; init; } =
        Array.Empty<string>();

    /*
     * Kontaktfoto.
     *
     * Die Binärdaten bleiben unabhängig von WPF.
     * Erst die Oberfläche entscheidet später, wie daraus
     * ein BitmapImage erzeugt wird.
     */
    public byte[]? PhotoData { get; init; }

    public string? PhotoMediaType { get; init; }

    /*
     * Unbekannte bzw. von anderen CardDAV-Clients ergänzte
     * vCard-Eigenschaften.
     *
     * Diese Reserve ist wichtig:
     *
     * Bearbeitet Telenec Mail später beispielsweise einen
     * Kontakt, den Roundcube um zusätzliche Eigenschaften
     * ergänzt hat, sollen unbekannte Daten nicht einfach
     * verschwinden.
     */
    public IReadOnlyList<string>
        PreservedVCardLines
    { get; init; } =
        Array.Empty<string>();
}

/*
 * Postalische Adresse entsprechend der ADR-Struktur einer
 * vCard.
 *
 * ADR besteht grundsätzlich aus:
 *
 * Postfach;
 * Zusatz;
 * Straße;
 * Ort;
 * Region;
 * Postleitzahl;
 * Land
 */
public sealed class ContactPostalAddress
{
    public string? PostOfficeBox { get; init; }

    public string? ExtendedAddress { get; init; }

    public string? Street { get; init; }

    public string? City { get; init; }

    public string? Region { get; init; }

    public string? PostalCode { get; init; }

    public string? Country { get; init; }
}

/*
 * Daten für einen neuen Kontakt.
 *
 * Die bisherigen Eigenschaften bleiben erhalten, sodass die
 * aktuelle Oberfläche zunächst unverändert weiterarbeiten
 * kann.
 */
public sealed class ContactCreateRequest
{
    public string? Salutation { get; init; }

    public string? AcademicTitle { get; init; }

    public string? FirstName { get; init; }

    public string? MiddleName { get; init; }

    public string? LastName { get; init; }

    public string? NameSuffix { get; init; }

    public string? Nickname { get; init; }

    public string? DisplayName { get; init; }

    public string? EmailAddress { get; init; }

    public string? BusinessEmailAddress { get; init; }

    public string? PrivateEmailAddress { get; init; }

    public IReadOnlyList<string>?
        AdditionalEmailAddresses
    { get; init; }

    public string? PhoneNumber { get; init; }

    public string? MobilePhoneNumber { get; init; }

    public string? BusinessPhoneNumber { get; init; }

    public string? PrivatePhoneNumber { get; init; }

    public string? FaxNumber { get; init; }

    public IReadOnlyList<string>?
        AdditionalPhoneNumbers
    { get; init; }

    public string? Company { get; init; }

    public string? Department { get; init; }

    public string? JobTitle { get; init; }

    public ContactPostalAddress?
        HomeAddress
    { get; init; }

    public ContactPostalAddress?
        WorkAddress
    { get; init; }

    public string? Website { get; init; }

    public string? Birthday { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<string>?
        Categories
    { get; init; }

    public byte[]? PhotoData { get; init; }

    public string? PhotoMediaType { get; init; }
}

/*
 * Daten für die Bearbeitung eines bestehenden Kontakts.
 *
 * Noch verwendet die alte UI nur einen Teil davon.
 * Beim anschließenden Umbau des CardDAV-Dienstes sorgen wir
 * dafür, dass zusätzliche vorhandene Werte trotzdem nicht
 * verloren gehen.
 */
public sealed class ContactUpdateRequest
{
    public string? Salutation { get; init; }

    public string? AcademicTitle { get; init; }

    public string? FirstName { get; init; }

    public string? MiddleName { get; init; }

    public string? LastName { get; init; }

    public string? NameSuffix { get; init; }

    public string? Nickname { get; init; }

    public string? DisplayName { get; init; }

    public string? EmailAddress { get; init; }

    public string? BusinessEmailAddress { get; init; }

    public string? PrivateEmailAddress { get; init; }

    public IReadOnlyList<string>?
        AdditionalEmailAddresses
    { get; init; }

    public string? PhoneNumber { get; init; }

    public string? MobilePhoneNumber { get; init; }

    public string? BusinessPhoneNumber { get; init; }

    public string? PrivatePhoneNumber { get; init; }

    public string? FaxNumber { get; init; }

    public IReadOnlyList<string>?
        AdditionalPhoneNumbers
    { get; init; }

    public string? Company { get; init; }

    public string? Department { get; init; }

    public string? JobTitle { get; init; }

    public ContactPostalAddress?
        HomeAddress
    { get; init; }

    public ContactPostalAddress?
        WorkAddress
    { get; init; }

    public string? Website { get; init; }

    public string? Birthday { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<string>?
        Categories
    { get; init; }

    public byte[]? PhotoData { get; init; }

    public string? PhotoMediaType { get; init; }
}