using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.ViewModels;

public sealed class ComposeMailViewModel : BaseViewModel
{
    private readonly IMailSendService _mailSendService;
    private readonly IMailAccountStore _mailAccountStore;

    private MailMessageItemViewModel? _replySourceMessage;

    private bool _isReplyAll;

    private string _windowTitle =
        "Neue E-Mail";

    private string _headerTitle =
        "Neue E-Mail";

    private string _fromAddress =
        "Wird geladen …";

    private string _recipientAddress =
        string.Empty;

    private string _ccAddress =
        string.Empty;

    private string _subject =
        string.Empty;

    private string _body =
        string.Empty;

    private bool _showCcField;
    private bool _focusBodyOnLoad;
    private bool _isSending;

    public ComposeMailViewModel(
        IMailSendService mailSendService,
        IMailAccountStore mailAccountStore)
    {
        ArgumentNullException.ThrowIfNull(
            mailSendService);

        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        _mailSendService =
            mailSendService;

        _mailAccountStore =
            mailAccountStore;
    }

    public string WindowTitle
    {
        get =>
            _windowTitle;

        private set
        {
            if (_windowTitle == value)
            {
                return;
            }

            _windowTitle =
                value;

            OnPropertyChanged();
        }
    }

    public string HeaderTitle
    {
        get =>
            _headerTitle;

        private set
        {
            if (_headerTitle == value)
            {
                return;
            }

            _headerTitle =
                value;

            OnPropertyChanged();
        }
    }

    public string FromAddress
    {
        get => _fromAddress;

        private set
        {
            if (_fromAddress == value)
            {
                return;
            }

            _fromAddress =
                value;

            OnPropertyChanged();
        }
    }

    public string RecipientAddress
    {
        get => _recipientAddress;

        set
        {
            if (_recipientAddress == value)
            {
                return;
            }

            _recipientAddress =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSend));
        }
    }

    public string CcAddress
    {
        get =>
            _ccAddress;

        set
        {
            if (_ccAddress == value)
            {
                return;
            }

            _ccAddress =
                value;

            OnPropertyChanged();
        }
    }

    public string Subject
    {
        get => _subject;

        set
        {
            if (_subject == value)
            {
                return;
            }

            _subject =
                value;

            OnPropertyChanged();
        }
    }

    public string Body
    {
        get => _body;

        set
        {
            if (_body == value)
            {
                return;
            }

            _body =
                value;

            OnPropertyChanged();
        }
    }

    public bool ShowCcField
    {
        get =>
            _showCcField;

        private set
        {
            if (_showCcField == value)
            {
                return;
            }

            _showCcField =
                value;

            OnPropertyChanged();
        }
    }

    public bool FocusBodyOnLoad
    {
        get =>
            _focusBodyOnLoad;

        private set
        {
            if (_focusBodyOnLoad == value)
            {
                return;
            }

            _focusBodyOnLoad =
                value;

            OnPropertyChanged();
        }
    }

    public bool IsSending
    {
        get => _isSending;

        private set
        {
            if (_isSending == value)
            {
                return;
            }

            _isSending =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSend));
        }
    }

    public bool CanSend =>
        !IsSending &&
        !string.IsNullOrWhiteSpace(
            RecipientAddress);

    public void PrepareReply(
        MailMessageItemViewModel message)
    {
        PrepareReplyCore(
            message,
            replyAll: false);
    }

    public void PrepareReplyAll(
        MailMessageItemViewModel message)
    {
        PrepareReplyCore(
            message,
            replyAll: true);
    }

    private void PrepareReplyCore(
        MailMessageItemViewModel message,
        bool replyAll)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        _replySourceMessage =
            message;

        _isReplyAll =
            replyAll;

        WindowTitle =
            replyAll
                ? "Allen antworten"
                : "Antworten";

        HeaderTitle =
            WindowTitle;

        ShowCcField =
            replyAll;

        CcAddress =
            string.Empty;

        Subject =
            CreateReplySubject(
                message.Subject);

        Body =
            CreateReplyBody(
                message);

        FocusBodyOnLoad =
            true;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            FromAddress =
                "Kein aktives Mailkonto";

            ApplyReplyRecipients(
                activeAccountAddress: null);

            return;
        }

        if (!string.IsNullOrWhiteSpace(
                account.DisplayName))
        {
            FromAddress =
                $"{account.DisplayName} <{account.EmailAddress}>";
        }
        else
        {
            FromAddress =
                account.EmailAddress;
        }

        ApplyReplyRecipients(
            account.EmailAddress);
    }

    private void ApplyReplyRecipients(
        string? activeAccountAddress)
    {
        var replySource =
            _replySourceMessage;

        if (replySource is null)
        {
            return;
        }

        if (_isReplyAll)
        {
            ApplyReplyAllRecipients(
                replySource,
                activeAccountAddress);

            return;
        }

        ApplySingleReplyRecipients(
            replySource,
            activeAccountAddress);
    }

    private void ApplySingleReplyRecipients(
        MailMessageItemViewModel replySource,
        string? activeAccountAddress)
    {
        var senderIsOwnAccount =
            IsSameAddress(
                replySource.SenderAddress,
                activeAccountAddress);

        /*
         * Antworten auf eine selbst gesendete Nachricht:
         *
         * Wir antworten weiterhin an den ursprünglichen
         * Empfänger und nicht an uns selbst.
         */
        if (senderIsOwnAccount)
        {
            var originalRecipient =
                replySource
                    .ToAddresses
                    .FirstOrDefault(
                        address =>
                            !IsSameAddress(
                                address,
                                activeAccountAddress));

            originalRecipient ??=
                !IsSameAddress(
                    replySource.RecipientAddress,
                    activeAccountAddress)
                    ? replySource.RecipientAddress
                    : null;

            RecipientAddress =
                originalRecipient
                ?? string.Empty;

            CcAddress =
                string.Empty;

            return;
        }

        /*
         * RFC-konformes Antworten:
         *
         * Wenn Reply-To vorhanden ist, hat es Vorrang vor
         * der From-Adresse.
         */
        var replyTargets =
            replySource.ReplyToAddresses.Count > 0
                ? replySource.ReplyToAddresses
                : new[]
                {
                    replySource.SenderAddress
                };

        RecipientAddress =
            JoinAddresses(
                NormalizeAddresses(
                    replyTargets,
                    activeAccountAddress));

        CcAddress =
            string.Empty;
    }

    private void ApplyReplyAllRecipients(
        MailMessageItemViewModel replySource,
        string? activeAccountAddress)
    {
        var toAddresses =
            new List<string>();

        var ccAddresses =
            new List<string>();

        var usedAddresses =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var senderIsOwnAccount =
            IsSameAddress(
                replySource.SenderAddress,
                activeAccountAddress);

        /*
         * Bei einer empfangenen Nachricht kommt zuerst das
         * Reply-To-Ziel bzw. der Absender in das An-Feld.
         *
         * Bei einer selbst gesendeten Nachricht überspringen
         * wir diesen Schritt, weil wir sonst uns selbst als
         * Empfänger aufnehmen würden.
         */
        if (!senderIsOwnAccount)
        {
            var replyTargets =
                replySource.ReplyToAddresses.Count > 0
                    ? replySource.ReplyToAddresses
                    : new[]
                    {
                        replySource.SenderAddress
                    };

            foreach (var address in replyTargets)
            {
                AddReplyAddress(
                    toAddresses,
                    usedAddresses,
                    address,
                    activeAccountAddress);
            }
        }

        /*
         * Alle ursprünglichen To-Empfänger bleiben To,
         * außer dem eigenen Konto und bereits vorhandenen
         * Empfängern.
         */
        foreach (var address in
                 replySource.ToAddresses)
        {
            AddReplyAddress(
                toAddresses,
                usedAddresses,
                address,
                activeAccountAddress);
        }

        /*
         * Fallback für ältere oder ungewöhnliche Nachrichten,
         * bei denen nur RecipientAddress vorhanden ist.
         */
        if (toAddresses.Count == 0)
        {
            AddReplyAddress(
                toAddresses,
                usedAddresses,
                replySource.RecipientAddress,
                activeAccountAddress);
        }

        /*
         * Ursprüngliche Cc-Empfänger bleiben Cc.
         *
         * Adressen, die bereits in To gelandet sind, werden
         * nicht noch einmal aufgenommen.
         */
        foreach (var address in
                 replySource.CcAddresses)
        {
            AddReplyAddress(
                ccAddresses,
                usedAddresses,
                address,
                activeAccountAddress);
        }

        /*
         * Sollte nach allen Filtern noch kein To-Empfänger
         * vorhanden sein, versuchen wir als letzten sicheren
         * Fallback den Absender.
         */
        if (toAddresses.Count == 0)
        {
            AddReplyAddress(
                toAddresses,
                usedAddresses,
                replySource.SenderAddress,
                activeAccountAddress);
        }

        RecipientAddress =
            JoinAddresses(
                toAddresses);

        CcAddress =
            JoinAddresses(
                ccAddresses);
    }

    private static IReadOnlyList<string>
        NormalizeAddresses(
            IEnumerable<string> addresses,
            string? excludedAddress)
    {
        var result =
            new List<string>();

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var address in addresses)
        {
            AddReplyAddress(
                result,
                seen,
                address,
                excludedAddress);
        }

        return result;
    }

    private static void AddReplyAddress(
        ICollection<string> result,
        ISet<string> usedAddresses,
        string? address,
        string? excludedAddress)
    {
        if (string.IsNullOrWhiteSpace(
                address))
        {
            return;
        }

        var normalized =
            address.Trim();

        if (IsSameAddress(
                normalized,
                excludedAddress))
        {
            return;
        }

        if (!usedAddresses.Add(
                normalized))
        {
            return;
        }

        result.Add(
            normalized);
    }

    private static bool IsSameAddress(
        string? firstAddress,
        string? secondAddress)
    {
        if (string.IsNullOrWhiteSpace(
                firstAddress) ||
            string.IsNullOrWhiteSpace(
                secondAddress))
        {
            return false;
        }

        return string.Equals(
            firstAddress.Trim(),
            secondAddress.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string JoinAddresses(
        IEnumerable<string> addresses)
    {
        return string.Join(
            "; ",
            addresses);
    }

    private static string CreateReplySubject(
        string? originalSubject)
    {
        var subject =
            originalSubject?
                .Trim()
            ?? string.Empty;

        if (subject.StartsWith(
                "Re:",
                StringComparison.OrdinalIgnoreCase) ||
            subject.StartsWith(
                "AW:",
                StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }

        return string.IsNullOrWhiteSpace(
                subject)
            ? "Re:"
            : $"Re: {subject}";
    }

    private static string CreateReplyBody(
        MailMessageItemViewModel message)
    {
        var senderDescription =
            CreateReplySenderDescription(
                message);

        var originalBody =
            string.IsNullOrWhiteSpace(
                message.Body)
                ? "(Kein darstellbarer Nachrichtentext.)"
                : message.Body.TrimEnd();

        var quotedBody =
            QuoteText(
                originalBody);

        return
            Environment.NewLine +
            Environment.NewLine +
            $"Am {message.DisplayDateTime} schrieb {senderDescription}:" +
            Environment.NewLine +
            quotedBody;
    }

    private static string CreateReplySenderDescription(
        MailMessageItemViewModel message)
    {
        var senderName =
            message.Sender.Trim();

        var senderAddress =
            message.SenderAddress.Trim();

        if (string.IsNullOrWhiteSpace(
                senderAddress))
        {
            return string.IsNullOrWhiteSpace(
                    senderName)
                ? "Unbekannter Absender"
                : senderName;
        }

        if (string.IsNullOrWhiteSpace(
                senderName) ||
            string.Equals(
                senderName,
                senderAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            return senderAddress;
        }

        return
            $"{senderName} <{senderAddress}>";
    }

    private static string QuoteText(
        string text)
    {
        var normalized =
            text.Replace(
                    "\r\n",
                    "\n")
                .Replace(
                    '\r',
                    '\n');

        return string.Join(
            Environment.NewLine,
            normalized
                .Split('\n')
                .Select(
                    line =>
                        string.IsNullOrEmpty(
                            line)
                            ? ">"
                            : $"> {line}"));
    }

    public async Task<MailSendResult> SendAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsSending)
        {
            throw new InvalidOperationException(
                "Es läuft bereits ein Versandvorgang.");
        }

        if (string.IsNullOrWhiteSpace(
                RecipientAddress))
        {
            throw new ArgumentException(
                "Bitte geben Sie einen Empfänger an.");
        }

        IsSending =
            true;

        try
        {
            var request =
                new MailSendRequest(
                    RecipientAddress:
                        RecipientAddress.Trim(),

                    Subject:
                        Subject,

                    Body:
                        Body,

                    CcAddress:
                        string.IsNullOrWhiteSpace(
                            CcAddress)
                            ? null
                            : CcAddress.Trim(),

                    ParentMessageId:
                        _replySourceMessage?.MessageId,

                    ParentReferences:
                        _replySourceMessage?.References);

            return await _mailSendService
                .SendAsync(
                    request,
                    cancellationToken);
        }
        finally
        {
            IsSending =
                false;
        }
    }
}