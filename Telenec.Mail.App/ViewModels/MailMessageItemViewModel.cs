using System.Globalization;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.ViewModels;

public sealed class MailMessageItemViewModel : BaseViewModel
{
    private bool _isUnread;
    private bool _emphasizeSender;

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
        IReadOnlyList<MailReadReceiptData>? receivedReadReceipts = null)
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

        HasSmimeSignature =
            hasSmimeSignature;

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

    /*
     * Bedeutet ausschließlich:
     *
     * In der MIME-Struktur wurde ein S/MIME-Signaturpart
     * erkannt.
     *
     * Es bedeutet ausdrücklich noch NICHT:
     *
     * - Signatur kryptografisch gültig
     * - Zertifikat vertrauenswürdig
     * - Zertifikat nicht abgelaufen
     * - Absenderidentität bestätigt
     */
    public bool HasSmimeSignature { get; }

    /*
     * Globale Message-ID der ursprünglichen Nachricht.
     *
     * Sie ist unabhängig von der IMAP-UID und wird für
     * RFC-konformes Reply-Threading verwendet.
     */
    public string? MessageId { get; }

    /*
     * Bereits vorhandene References-Kette der Nachricht.
     *
     * Beim Antworten wird diese Kette übernommen und um
     * die Message-ID der aktuellen Nachricht erweitert.
     */
    public IReadOnlyList<string> References { get; }

    /*
     * Vollständige ursprüngliche Empfängerlisten.
     *
     * RecipientAddress bleibt zusätzlich bestehen, weil die
     * bestehende UI bisher einen einzelnen Hauptempfänger
     * anzeigt.
     */
    public IReadOnlyList<string> ToAddresses { get; }

    public IReadOnlyList<string> CcAddresses { get; }

    /*
     * Reply-To hat beim Antworten Vorrang vor From.
     *
     * Das ist insbesondere für Mailinglisten und Systeme
     * wichtig, die Antworten bewusst an eine andere Adresse
     * lenken.
     */
    public IReadOnlyList<string> ReplyToAddresses { get; }

    /*
     * Vom Absender gesetzte Wichtigkeit der Nachricht.
     *
     * Normal bleibt der sichere Standard für Nachrichten,
     * bei denen kein unterstützter Priority-Header vorhanden
     * ist.
     */
    public MailImportanceLevel Importance { get; }

    /*
     * Enthält ausschließlich dann Daten, wenn die geladene
     * Nachricht selbst als positive Lesebestätigung erkannt
     * wurde.
     *
     * null bedeutet dabei lediglich:
     *
     * "Diese Nachricht wurde nicht als Lesebestätigung
     * erkannt."
     *
     * Es bedeutet ausdrücklich nicht, dass eine zuvor
     * angeforderte Lesebestätigung abgelehnt wurde oder
     * dass die Ursprungsnachricht ungelesen ist.
     */
    public MailReadReceiptData? ReadReceipt { get; }

    public bool IsReadReceipt =>
        ReadReceipt is not null;

    public string ReadReceiptDetail { get; }

    /*
     * Enthält positive Lesebestätigungen, die sich über ihre
     * Original-Message-ID auf genau diese Nachricht beziehen.
     *
     * Bei mehreren Empfängern können deshalb mehrere
     * Bestätigungen vorhanden sein.
     *
     * Eine leere Liste bedeutet ausschließlich:
     *
     * "Für diese Nachricht ist lokal keine positive
     * Lesebestätigung gespeichert."
     *
     * Sie bedeutet ausdrücklich NICHT:
     *
     * - Nachricht wurde nicht gelesen
     * - Empfänger hat die Bestätigung abgelehnt
     * - Empfänger unterstützt keine Lesebestätigungen
     */
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