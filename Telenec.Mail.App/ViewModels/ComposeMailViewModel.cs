using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.ViewModels;

public sealed class ComposeMailViewModel : BaseViewModel
{
    private readonly IMailSendService _mailSendService;
    private readonly IMailAccountStore _mailAccountStore;

    private string _fromAddress =
        "Wird geladen …";

    private string _recipientAddress =
        string.Empty;

    private string _subject =
        string.Empty;

    private string _body =
        string.Empty;

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

            return;
        }

        if (!string.IsNullOrWhiteSpace(
                account.DisplayName))
        {
            FromAddress =
                $"{account.DisplayName} <{account.EmailAddress}>";

            return;
        }

        FromAddress =
            account.EmailAddress;
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