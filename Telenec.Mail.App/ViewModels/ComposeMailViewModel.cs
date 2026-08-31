using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.ViewModels;

public sealed class ComposeMailViewModel : BaseViewModel
{
    private readonly IMailSendService _mailSendService;
    private readonly IMailAccountStore _mailAccountStore;

    private MailMessageItemViewModel? _replySourceMessage;

    private string _windowTitle =
        "Neue E-Mail";

    private string _headerTitle =
        "Neue E-Mail";

    private string _fromAddress =
        "Wird geladen …";

    private string _recipientAddress =
        string.Empty;

    private string _subject =
        string.Empty;

    private string _body =
        string.Empty;

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
        ArgumentNullException.ThrowIfNull(
            message);

        _replySourceMessage =
            message;

        WindowTitle =
            "Antworten";

        HeaderTitle =
            "Antworten";

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

            ApplyReplyRecipient(
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

        ApplyReplyRecipient(
            account.EmailAddress);
    }

    private void ApplyReplyRecipient(
        string? activeAccountAddress)
    {
        var replySource =
            _replySourceMessage;

        if (replySource is null)
        {
            return;
        }

        var senderAddress =
            replySource
                .SenderAddress
                .Trim();

        var recipientAddress =
            replySource
                .RecipientAddress
                .Trim();

        /*
         * Wenn die ausgewählte Nachricht vom eigenen Konto
         * stammt, darf "Antworten" nicht einfach wieder an
         * die eigene Absenderadresse adressieren.
         *
         * In diesem Fall verwenden wir den bisherigen
         * Empfänger der Nachricht.
         */
        if (!string.IsNullOrWhiteSpace(
                activeAccountAddress) &&
            string.Equals(
                senderAddress,
                activeAccountAddress.Trim(),
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(
                recipientAddress))
        {
            RecipientAddress =
                recipientAddress;

            return;
        }

        RecipientAddress =
            senderAddress;
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
                        Body);

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
