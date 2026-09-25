using System.Globalization;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.ViewModels;

public sealed class MailMessageItemViewModel : BaseViewModel
{
    private bool _isUnread;
    private bool _emphasizeSender;

    private IReadOnlyList<string> _keywords =
        Array.Empty<string>();

    public MailMessageItemViewModel(
        string sender,
        string senderAddress,
        string recipientAddress,
        string subject,
        string preview,
        string displayTime,
        string displayDateTime,
        string senderInitial,
        string greeting,
        string body,
        string closing,
        string signature,
        bool isUnread = false,
        bool emphasizeSender = false,
        string? highlightTitle = null,
        string? highlightText = null,
        string? htmlBody = null,
        uint uniqueId = 0,
        IReadOnlyList<MailAttachmentData>? attachments = null,
        bool hasSmimeSignature = false,
        string? messageId = null,
        IReadOnlyList<string>? references = null,
        IReadOnlyList<string>? toAddresses = null,
        IReadOnlyList<string>? ccAddresses = null,
        IReadOnlyList<string>? replyToAddresses = null,
        MailImportanceLevel importance = MailImportanceLevel.Normal,
        MailReadReceiptData? readReceipt = null,
        IReadOnlyList<MailReadReceiptData>? receivedReadReceipts = null,
        IReadOnlyList<string>? keywords = null,
        MailSecurityData? securityData = null,
        MailSmimeVerificationData? smimeVerification = null)
    {
        Sender =
            sender;

        SenderAddress =
            senderAddress;

        RecipientAddress =
            recipientAddress;

        Subject =
            subject;

        Preview =
            preview;

        DisplayTime =
            displayTime;

        DisplayDateTime =
            displayDateTime;

        SenderInitial =
            senderInitial;

        Greeting =
            greeting;

        Body =
            body;

        Closing =
            closing;

        Signature =
            signature;

        _isUnread =
            isUnread;

        _emphasizeSender =
            emphasizeSender;

        HighlightTitle =
            highlightTitle;

        HighlightText =
            highlightText;

        HtmlBody =
            htmlBody;

        UniqueId =
            uniqueId;

        Attachments =
            attachments
            ?? Array.Empty<MailAttachmentData>();

        SecurityData =
            securityData
            ?? CreateLegacySecurityData(
                hasSmimeSignature);

        SmimeVerification =
            smimeVerification
            ?? MailSmimeVerificationData
                .NotChecked;

        MessageId =
            messageId;

        References =
            references?.ToArray()
            ?? Array.Empty<string>();

        ToAddresses =
            CreateAddressSnapshot(
                toAddresses,
                recipientAddress);

        CcAddresses =
            CreateAddressSnapshot(
                ccAddresses);

        ReplyToAddresses =
            CreateAddressSnapshot(
                replyToAddresses);

        Importance =
            importance;

        ReadReceipt =
            readReceipt;

        ReadReceiptDetail =
            CreateReadReceiptDetail(
                ReadReceipt);

        ReceivedReadReceipts =
            receivedReadReceipts?.ToArray()
            ?? Array.Empty<MailReadReceiptData>();

        ReceivedReadReceiptDetails =
            CreateReceivedReadReceiptDetails(
                ReceivedReadReceipts);

        _keywords =
            CreateKeywordSnapshot(
                keywords);
    }

    public string Sender { get; }

    public string SenderAddress { get; }

    public string RecipientAddress { get; }

    public string AddressLine =>
        $"{SenderAddress} → {RecipientAddress}";

    public string Subject { get; }

    public string Preview { get; }

    public string DisplayTime { get; }

    public string DisplayDateTime { get; }

    public string SenderInitial { get; }

    public string Greeting { get; }

    public string Body { get; }

    public string Closing { get; }

    public string Signature { get; }

    public bool IsUnread
    {
        get =>
            _isUnread;

        private set
        {
            if (_isUnread == value)
            {
                return;
            }

            _isUnread =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanMarkAsUnread));
        }
    }

    public bool EmphasizeSender
    {
        get =>
            _emphasizeSender;

        private set
        {
            if (_emphasizeSender == value)
            {
                return;
            }

            _emphasizeSender =
                value;

            OnPropertyChanged();
        }
    }

    public string? HighlightTitle { get; }

    public string? HighlightText { get; }

    public string? HtmlBody { get; }

    public uint UniqueId { get; }

    public IReadOnlyList<MailAttachmentData>
        Attachments
    { get; }

    public MailSecurityData SecurityData
    { get; }

    public MailSmimeVerificationData
        SmimeVerification
    { get; }

    public bool HasSmimeSignature =>
        SecurityData.HasSmimeSignature;

    public bool HasSmimeEncryption =>
        SecurityData.HasSmimeEncryption;

    public bool HasUnclassifiedSmimeContent =>
        SecurityData.HasUnclassifiedSmimeContent;

    public bool HasAnySmimeContent =>
        SecurityData.HasAnySmimeContent;

    public bool HasSmimeProtection =>
        SecurityData.HasSmimeProtection;

    public bool IsSmimeSignedAndEncrypted =>
        SecurityData.IsSignedAndEncrypted;

    public MailSmimeSignatureFormat
        SmimeSignatureFormat =>
            SecurityData
                .SmimeSignatureFormat;

    public MailSmimeEncryptionFormat
        SmimeEncryptionFormat =>
            SecurityData
                .SmimeEncryptionFormat;

    public MailSmimeVerificationStatus
        SmimeVerificationStatus =>
            SmimeVerification.Status;

    public bool HasSmimeVerificationResult =>
        SmimeVerification.WasChecked;

    public bool IsSmimeSignatureValid =>
        SmimeVerification.Status ==
        MailSmimeVerificationStatus.Valid;

    public bool IsSmimeSignatureInvalid =>
        SmimeVerification.Status ==
        MailSmimeVerificationStatus
            .InvalidSignature;

    public bool HasSmimeCertificateProblem =>
        SmimeVerification.Status ==
            MailSmimeVerificationStatus
                .CertificateNotYetValid
        ||
        SmimeVerification.Status ==
            MailSmimeVerificationStatus
                .CertificateExpired
        ||
        SmimeVerification.Status ==
            MailSmimeVerificationStatus
                .CertificateValidationFailed;

    public bool HasSmimeVerificationError =>
        SmimeVerification.Status ==
        MailSmimeVerificationStatus.Error;

    public string SecurityStatusTitle =>
        CreateSecurityStatusTitle();

    public string SecurityStatusDetail =>
        CreateSecurityStatusDetail();

    public string? MessageId { get; }

    public IReadOnlyList<string> References { get; }

    public IReadOnlyList<string> ToAddresses { get; }

    public IReadOnlyList<string> CcAddresses { get; }

    public IReadOnlyList<string> ReplyToAddresses { get; }

    public MailImportanceLevel Importance { get; }

    public MailReadReceiptData? ReadReceipt { get; }

    public bool IsReadReceipt =>
        ReadReceipt is not null;

    public string ReadReceiptDetail { get; }

    public IReadOnlyList<MailReadReceiptData>
        ReceivedReadReceipts
    { get; }

    public IReadOnlyList<string>
        ReceivedReadReceiptDetails
    { get; }

    public bool HasReceivedReadReceipts =>
        ReceivedReadReceipts.Count > 0;

    public int ReceivedReadReceiptCount =>
        ReceivedReadReceipts.Count;

    public IReadOnlyList<string> Keywords =>
        _keywords;

    public IReadOnlyList<MailCategoryDefinition>
        AssignedCategories =>
            MailCategoryCatalog
                .All
                .Where(
                    HasCategory)
                .ToArray();

    public bool IsHighImportance =>
        Importance ==
        MailImportanceLevel.High;

    public bool IsLowImportance =>
        Importance ==
        MailImportanceLevel.Low;

    public bool HasHtmlBody =>
        !string.IsNullOrWhiteSpace(
            HtmlBody);

    public bool HasHighlight =>
        !string.IsNullOrWhiteSpace(
            HighlightTitle) &&
        !string.IsNullOrWhiteSpace(
            HighlightText);

    public bool HasAttachments =>
        Attachments.Count > 0;

    public int AttachmentCount =>
        Attachments.Count;

    public string AttachmentSummary =>
        AttachmentCount switch
        {
            0 =>
                string.Empty,

            1 =>
                "1 Anhang",

            _ =>
                $"{AttachmentCount} Anhänge"
        };

    public bool CanMarkAsUnread =>
        !IsUnread;

    public void MarkAsRead()
    {
        IsUnread =
            false;

        EmphasizeSender =
            false;
    }

    public void MarkAsUnread()
    {
        IsUnread =
            true;

        EmphasizeSender =
            true;
    }

    public bool HasCategory(
        MailCategoryDefinition category)
    {
        ArgumentNullException.ThrowIfNull(
            category);

        return _keywords.Any(
            keyword =>
                string.Equals(
                    keyword,
                    category.Keyword,
                    StringComparison.OrdinalIgnoreCase));
    }

    public void SetKeywordState(
        string keyword,
        bool isEnabled)
    {
        if (string.IsNullOrWhiteSpace(
                keyword))
        {
            throw new ArgumentException(
                "Das IMAP-Keyword darf nicht leer sein.",
                nameof(keyword));
        }

        var normalizedKeyword =
            keyword.Trim();

        var updatedKeywords =
            new HashSet<string>(
                _keywords,
                StringComparer.OrdinalIgnoreCase);

        var changed =
            isEnabled
                ? updatedKeywords.Add(
                    normalizedKeyword)
                : updatedKeywords.Remove(
                    normalizedKeyword);

        if (!changed)
        {
            return;
        }

        _keywords =
            updatedKeywords
                .ToArray();

        OnPropertyChanged(
            nameof(Keywords));

        OnPropertyChanged(
            nameof(AssignedCategories));
    }

    private string CreateSecurityStatusTitle()
    {
        if (HasSmimeSignature &&
            SmimeVerification.WasChecked)
        {
            return SmimeVerification.Status switch
            {
                MailSmimeVerificationStatus.Valid =>
                    "Signatur gültig (S/MIME)",

                MailSmimeVerificationStatus
                    .InvalidSignature =>
                    "Signatur ungültig (S/MIME)",

                MailSmimeVerificationStatus
                    .CertificateExpired =>
                    "Signatur geprüft · Zertifikat abgelaufen",

                MailSmimeVerificationStatus
                    .CertificateNotYetValid =>
                    "Signatur geprüft · Zertifikat noch nicht gültig",

                MailSmimeVerificationStatus
                    .CertificateValidationFailed =>
                    "Signatur geprüft · Zertifikat nicht vertrauenswürdig",

                MailSmimeVerificationStatus.Error =>
                    "S/MIME-Signatur konnte nicht geprüft werden",

                _ =>
                    "Digital signiert (S/MIME)"
            };
        }

        if (IsSmimeSignedAndEncrypted)
        {
            return
                "Signiert und verschlüsselt (S/MIME)";
        }

        if (HasSmimeEncryption)
        {
            return
                "Verschlüsselt (S/MIME)";
        }

        if (HasSmimeSignature)
        {
            return
                "Digital signiert (S/MIME)";
        }

        if (HasUnclassifiedSmimeContent)
        {
            return
                "S/MIME-Inhalt erkannt";
        }

        return string.Empty;
    }

    private string CreateSecurityStatusDetail()
    {
        if (HasSmimeSignature &&
            SmimeVerification.WasChecked)
        {
            return CreateVerifiedSignatureDetail();
        }

        if (IsSmimeSignedAndEncrypted)
        {
            return
                "Die MIME-Struktur enthält Merkmale einer " +
                "S/MIME-Signatur und einer S/MIME-Verschlüsselung. " +
                "Die Signatur wurde noch nicht kryptografisch geprüft.";
        }

        if (HasSmimeEncryption)
        {
            return
                "Die Nachricht wurde als S/MIME-verschlüsselter Inhalt erkannt. " +
                "Eine Entschlüsselung ist in diesem Entwicklungsschritt noch nicht verfügbar.";
        }

        if (HasSmimeSignature)
        {
            return SmimeSignatureFormat switch
            {
                MailSmimeSignatureFormat.Detached =>
                    "Eine S/MIME-Signatur wurde erkannt. " +
                    "Gültigkeit und Zertifikat wurden noch nicht geprüft.",

                MailSmimeSignatureFormat.Opaque =>
                    "Eine eingebettete S/MIME-Signatur wurde erkannt. " +
                    "Gültigkeit und Zertifikat wurden noch nicht geprüft.",

                _ =>
                    "Eine S/MIME-Signatur wurde erkannt. " +
                    "Gültigkeit und Zertifikat wurden noch nicht geprüft."
            };
        }

        if (HasUnclassifiedSmimeContent)
        {
            return
                "Ein S/MIME-Inhalt wurde erkannt, sein Typ konnte anhand der " +
                "MIME-Struktur jedoch nicht eindeutig bestimmt werden.";
        }

        return string.Empty;
    }

    private string CreateVerifiedSignatureDetail()
    {
        var signerDescription =
            CreateSignerDescription();

        var verificationText =
            SmimeVerification.Status switch
            {
                MailSmimeVerificationStatus.Valid =>
                    "Die Signatur ist kryptografisch korrekt und die " +
                    "Zertifikatskette wurde über Windows erfolgreich validiert.",

                MailSmimeVerificationStatus
                    .InvalidSignature =>
                    "Die kryptografische Signaturprüfung ist fehlgeschlagen. " +
                    "Die Nachricht oder ihre Signatur könnte verändert oder beschädigt sein.",

                MailSmimeVerificationStatus
                    .CertificateExpired =>
                    "Die Signatur ist kryptografisch korrekt. " +
                    "Das verwendete Signaturzertifikat ist jedoch abgelaufen.",

                MailSmimeVerificationStatus
                    .CertificateNotYetValid =>
                    "Die Signatur ist kryptografisch korrekt. " +
                    "Das verwendete Zertifikat ist derzeit noch nicht gültig.",

                MailSmimeVerificationStatus
                    .CertificateValidationFailed =>
                    "Die Signatur ist kryptografisch korrekt, aber die " +
                    "Zertifikatskette konnte auf diesem Windows-System " +
                    "nicht als vertrauenswürdig bestätigt werden.",

                MailSmimeVerificationStatus.Error =>
                    "Die S/MIME-Signatur konnte technisch nicht vollständig geprüft werden.",

                _ =>
                    "Die S/MIME-Signatur wurde noch nicht geprüft."
            };

        if (string.IsNullOrWhiteSpace(
                signerDescription))
        {
            return verificationText;
        }

        return
            verificationText +
            " Signiert von " +
            signerDescription +
            ".";
    }

    private string CreateSignerDescription()
    {
        var signer =
            SmimeVerification
                .Signers
                .FirstOrDefault();

        if (signer is null)
        {
            return string.Empty;
        }

        var name =
            signer.Name?
                .Trim()
            ?? string.Empty;

        var emailAddress =
            signer.EmailAddress?
                .Trim()
            ?? string.Empty;

        /*
         * Manche S/MIME-Zertifikate verwenden die
         * E-Mail-Adresse gleichzeitig als Anzeigenamen.
         *
         * Statt
         *
         * mail@example.de <mail@example.de>
         *
         * zeigen wir in diesem Fall nur die Adresse.
         */
        if (!string.IsNullOrWhiteSpace(
                name) &&
            !string.IsNullOrWhiteSpace(
                emailAddress) &&
            string.Equals(
                name,
                emailAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            return emailAddress;
        }

        if (!string.IsNullOrWhiteSpace(
                name) &&
            !string.IsNullOrWhiteSpace(
                emailAddress))
        {
            return
                $"{name} <{emailAddress}>";
        }

        if (!string.IsNullOrWhiteSpace(
                emailAddress))
        {
            return emailAddress;
        }

        return name;
    }

    private static MailSecurityData
        CreateLegacySecurityData(
            bool hasSmimeSignature)
    {
        if (!hasSmimeSignature)
        {
            return MailSecurityData.None;
        }

        return new MailSecurityData(
            HasSmimeSignature:
                true,

            HasSmimeEncryption:
                false,

            HasUnclassifiedSmimeContent:
                false,

            SmimeSignatureFormat:
                MailSmimeSignatureFormat.Detached,

            SmimeEncryptionFormat:
                MailSmimeEncryptionFormat.None);
    }

    private static IReadOnlyList<string>
        CreateReceivedReadReceiptDetails(
            IReadOnlyList<MailReadReceiptData> readReceipts)
    {
        if (readReceipts.Count == 0)
        {
            return Array.Empty<string>();
        }

        return readReceipts
            .Select(
                CreateReadReceiptDetail)
            .ToArray();
    }

    private static string
        CreateReadReceiptDetail(
            MailReadReceiptData? readReceipt)
    {
        if (readReceipt is null)
        {
            return string.Empty;
        }

        var sender =
            !string.IsNullOrWhiteSpace(
                readReceipt.Sender)
                ? readReceipt.Sender.Trim()
                : readReceipt.SenderAddress.Trim();

        if (string.IsNullOrWhiteSpace(
                sender))
        {
            sender =
                "unbekanntem Empfänger";
        }

        if (!readReceipt.ReceiptDate.HasValue)
        {
            return
                $"Bestätigung von {sender}";
        }

        var localReceiptDate =
            readReceipt
                .ReceiptDate
                .Value
                .ToLocalTime();

        return
            $"Bestätigung von {sender} · " +
            $"{localReceiptDate.ToString(
                "dd.MM.yyyy HH:mm",
                CultureInfo.CurrentCulture)} Uhr";
    }

    private static IReadOnlyList<string>
        CreateKeywordSnapshot(
            IReadOnlyList<string>? keywords)
    {
        if (keywords is null ||
            keywords.Count == 0)
        {
            return Array.Empty<string>();
        }

        return keywords
            .Where(
                keyword =>
                    !string.IsNullOrWhiteSpace(
                        keyword))
            .Select(
                keyword =>
                    keyword.Trim())
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string>
        CreateAddressSnapshot(
            IReadOnlyList<string>? addresses,
            string? fallbackAddress = null)
    {
        var result =
            new List<string>();

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (addresses is not null)
        {
            foreach (var address in addresses)
            {
                AddAddress(
                    result,
                    seen,
                    address);
            }
        }

        if (result.Count == 0)
        {
            AddAddress(
                result,
                seen,
                fallbackAddress);
        }

        return result;
    }

    private static void AddAddress(
        ICollection<string> result,
        ISet<string> seen,
        string? address)
    {
        if (string.IsNullOrWhiteSpace(
                address))
        {
            return;
        }

        var normalized =
            address.Trim();

        if (!seen.Add(
                normalized))
        {
            return;
        }

        result.Add(
            normalized);
    }
}