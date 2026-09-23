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
        IReadOnlyList<string>? keywords = null)
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

    public bool HasSmimeSignature { get; }

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