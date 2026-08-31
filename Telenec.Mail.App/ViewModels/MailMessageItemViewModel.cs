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
        IReadOnlyList<string>? references = null)
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
}