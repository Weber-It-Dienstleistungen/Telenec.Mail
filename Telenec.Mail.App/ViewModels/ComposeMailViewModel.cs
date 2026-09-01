using System.Collections.ObjectModel;
using System.IO;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.ViewModels;

public sealed class ComposeMailViewModel : BaseViewModel
{
    private readonly IMailSendService _mailSendService;
    private readonly IMailDraftEditService _mailDraftEditService;
    private readonly IMailDraftCleanupService _mailDraftCleanupService;
    private readonly IMailAccountStore _mailAccountStore;

    private MailMessageItemViewModel? _replySourceMessage;

    private bool _isReplyAll;

    /*
     * Reply-Threading wird bewusst unabhängig vom
     * aktuell sichtbaren MailMessageItemViewModel gehalten.
     *
     * Das ist für gespeicherte Antwort-Entwürfe notwendig:
     * Dort kennen wir In-Reply-To und References, besitzen
     * aber nicht zwingend noch das ursprüngliche
     * MailMessageItemViewModel.
     */
    private string? _parentMessageId;

    private IReadOnlyList<string> _parentReferences =
        Array.Empty<string>();

    private string? _editingDraftSourceFolderId;
    private uint _editingDraftSourceUniqueId;
    private string? _editingDraftSourceMessageId;

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

    private string _bccAddress =
        string.Empty;

    private string _subject =
        string.Empty;

    private string _body =
        string.Empty;

    private bool _showCcField;
    private bool _showBccField;
    private bool _focusBodyOnLoad;
    private bool _isSending;
    private bool _isSavingDraft;

    public ComposeMailViewModel(
        IMailSendService mailSendService,
        IMailDraftEditService mailDraftEditService,
        IMailDraftCleanupService mailDraftCleanupService,
        IMailAccountStore mailAccountStore)
    {
        ArgumentNullException.ThrowIfNull(
            mailSendService);

        ArgumentNullException.ThrowIfNull(
            mailDraftEditService);

        ArgumentNullException.ThrowIfNull(
            mailDraftCleanupService);

        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        _mailSendService =
            mailSendService;

        _mailDraftEditService =
            mailDraftEditService;

        _mailDraftCleanupService =
            mailDraftCleanupService;

        _mailAccountStore =
            mailAccountStore;

        Attachments =
            new ObservableCollection<
                MailSendAttachmentData>();
    }

    public ObservableCollection<
        MailSendAttachmentData> Attachments
    { get; }

    public string WindowTitle
    {
        get => _windowTitle;

        private set
        {
            if (_windowTitle == value)
            {
                return;
            }

            _windowTitle = value;
            OnPropertyChanged();
        }
    }

    public string HeaderTitle
    {
        get => _headerTitle;

        private set
        {
            if (_headerTitle == value)
            {
                return;
            }

            _headerTitle = value;
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

            _fromAddress = value;
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

            _recipientAddress = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSaveDraft));
        }
    }

    public string CcAddress
    {
        get => _ccAddress;

        set
        {
            if (_ccAddress == value)
            {
                return;
            }

            _ccAddress = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveDraft));
        }
    }

    public string BccAddress
    {
        get => _bccAddress;

        set
        {
            if (_bccAddress == value)
            {
                return;
            }

            _bccAddress = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveDraft));
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

            _subject = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveDraft));
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

            _body = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveDraft));
        }
    }

    public bool ShowCcField
    {
        get => _showCcField;

        private set
        {
            if (_showCcField == value)
            {
                return;
            }

            _showCcField = value;
            OnPropertyChanged();
        }
    }

    public bool ShowBccField
    {
        get => _showBccField;

        private set
        {
            if (_showBccField == value)
            {
                return;
            }

            _showBccField = value;
            OnPropertyChanged();
        }
    }

    public void ShowCc()
    {
        if (IsBusy ||
            ShowCcField)
        {
            return;
        }

        ShowCcField = true;
    }

    public void ShowBcc()
    {
        if (IsBusy ||
            ShowBccField)
        {
            return;
        }

        ShowBccField = true;
    }

    public bool FocusBodyOnLoad
    {
        get => _focusBodyOnLoad;

        private set
        {
            if (_focusBodyOnLoad == value)
            {
                return;
            }

            _focusBodyOnLoad = value;
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

            _isSending = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSaveDraft));
            OnPropertyChanged(nameof(CanModifyAttachments));
        }
    }

    public bool IsSavingDraft
    {
        get => _isSavingDraft;

        private set
        {
            if (_isSavingDraft == value)
            {
                return;
            }

            _isSavingDraft = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSaveDraft));
            OnPropertyChanged(nameof(CanModifyAttachments));
        }
    }

    public bool IsBusy =>
        IsSending ||
        IsSavingDraft;

    public bool CanSend =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(
            RecipientAddress);

    public bool CanSaveDraft =>
        !IsBusy &&
        HasDraftContent;

    public bool CanModifyAttachments =>
        !IsBusy;

    public bool HasAttachments =>
        Attachments.Count > 0;

    public bool IsEditingDraft =>
        !string.IsNullOrWhiteSpace(
            _editingDraftSourceFolderId) &&
        _editingDraftSourceUniqueId > 0 &&
        !string.IsNullOrWhiteSpace(
            _editingDraftSourceMessageId);

    public string? EditingDraftSourceFolderId =>
        _editingDraftSourceFolderId;

    public uint EditingDraftSourceUniqueId =>
        _editingDraftSourceUniqueId;

    public string? EditingDraftSourceMessageId =>
        _editingDraftSourceMessageId;

    private bool HasDraftContent =>
        !string.IsNullOrWhiteSpace(
            RecipientAddress) ||
        !string.IsNullOrWhiteSpace(
            CcAddress) ||
        !string.IsNullOrWhiteSpace(
            BccAddress) ||
        !string.IsNullOrWhiteSpace(
            Subject) ||
        !string.IsNullOrWhiteSpace(
            Body) ||
        Attachments.Count > 0;

    public string AttachmentSummary =>
        Attachments.Count switch
        {
            0 => string.Empty,
            1 => "1 Anhang",
            _ => $"{Attachments.Count} Anhänge"
        };

    public void AddAttachmentFiles(
        IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(
            filePaths);

        if (IsBusy)
        {
            return;
        }

        var knownPaths =
            Attachments
                .Where(
                    attachment =>
                        attachment.IsLocalFile)
                .Select(
                    attachment =>
                        attachment.FilePath)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var newAttachments =
            new List<MailSendAttachmentData>();

        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(
                    filePath))
            {
                continue;
            }

            var attachment =
                CreateAttachmentData(
                    filePath);

            if (!knownPaths.Add(
                    attachment.FilePath))
            {
                continue;
            }

            newAttachments.Add(
                attachment);
        }

        if (newAttachments.Count == 0)
        {
            return;
        }

        foreach (var attachment in
                 newAttachments)
        {
            Attachments.Add(
                attachment);
        }

        NotifyAttachmentStateChanged();
    }

    public void AddForwardedAttachments(
        MailMessageItemViewModel message,
        string sourceFolderId)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        if (IsBusy ||
            message.Attachments.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                sourceFolderId))
        {
            throw new InvalidOperationException(
                "Der Quellordner der weitergeleiteten Nachricht konnte nicht ermittelt werden.");
        }

        if (message.UniqueId == 0)
        {
            throw new InvalidOperationException(
                "Die Server-ID der weitergeleiteten Nachricht ist ungültig.");
        }

        var forwardedAttachments =
            new List<MailSendAttachmentData>();

        foreach (var attachment in
                 message.Attachments)
        {
            if (string.IsNullOrWhiteSpace(
                    attachment.PartSpecifier))
            {
                throw new InvalidOperationException(
                    "Ein Originalanhang besitzt keinen gültigen MIME-Part.");
            }

            if (string.IsNullOrWhiteSpace(
                    attachment.FileName))
            {
                throw new InvalidOperationException(
                    "Ein Originalanhang besitzt keinen gültigen Dateinamen.");
            }

            forwardedAttachments.Add(
                new MailSendAttachmentData(
                    FilePath: string.Empty,
                    FileName: attachment.FileName,
                    SizeBytes: attachment.EncodedSizeBytes,
                    SourceFolderId: sourceFolderId.Trim(),
                    SourceUniqueId: message.UniqueId,
                    SourcePartSpecifier: attachment.PartSpecifier,
                    SourceMessageId:
                        string.IsNullOrWhiteSpace(
                            message.MessageId)
                            ? null
                            : message.MessageId.Trim()));
        }

        foreach (var attachment in
                 forwardedAttachments)
        {
            Attachments.Add(
                attachment);
        }

        NotifyAttachmentStateChanged();
    }

    public void RemoveAttachment(
        MailSendAttachmentData attachment)
    {
        ArgumentNullException.ThrowIfNull(
            attachment);

        if (IsBusy)
        {
            return;
        }

        if (!Attachments.Remove(
                attachment))
        {
            return;
        }

        NotifyAttachmentStateChanged();
    }

    private static MailSendAttachmentData
        CreateAttachmentData(
            string filePath)
    {
        var fullPath =
            Path.GetFullPath(
                filePath);

        var fileName =
            Path.GetFileName(
                fullPath);

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            throw new ArgumentException(
                "Der ausgewählte Dateiname ist ungültig.",
                nameof(filePath));
        }

        if (!File.Exists(
                fullPath))
        {
            throw new FileNotFoundException(
                "Die ausgewählte Datei wurde nicht gefunden.",
                fullPath);
        }

        using var validationStream =
            new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                FileShare.Delete);

        var sizeBytes =
            validationStream.Length;

        return new MailSendAttachmentData(
            FilePath: fullPath,
            FileName: fileName,
            SizeBytes: sizeBytes);
    }

    private void NotifyAttachmentStateChanged()
    {
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(AttachmentSummary));
        OnPropertyChanged(nameof(CanSaveDraft));
    }

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

    public void PrepareForward(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        ClearEditingDraftSource();

        _replySourceMessage = null;
        _isReplyAll = false;
        _parentMessageId = null;
        _parentReferences =
            Array.Empty<string>();

        WindowTitle =
            "Weiterleiten";

        HeaderTitle =
            "Weiterleiten";

        RecipientAddress =
            string.Empty;

        CcAddress =
            string.Empty;

        BccAddress =
            string.Empty;

        ShowCcField =
            false;

        ShowBccField =
            false;

        Subject =
            CreateForwardSubject(
                message.Subject);

        Body =
            CreateForwardBody(
                message);

        FocusBodyOnLoad =
            false;
    }

    private void PrepareReplyCore(
        MailMessageItemViewModel message,
        bool replyAll)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        ClearEditingDraftSource();

        _replySourceMessage =
            message;

        _isReplyAll =
            replyAll;

        _parentMessageId =
            string.IsNullOrWhiteSpace(
                message.MessageId)
                ? null
                : message.MessageId.Trim();

        _parentReferences =
            message.References
                .Where(
                    reference =>
                        !string.IsNullOrWhiteSpace(
                            reference))
                .Select(
                    reference =>
                        reference.Trim())
                .Distinct(
                    StringComparer.Ordinal)
                .ToArray();

        WindowTitle =
            replyAll
                ? "Allen antworten"
                : "Antworten";

        HeaderTitle =
            WindowTitle;

        ShowCcField =
            replyAll;

        ShowBccField =
            false;

        CcAddress =
            string.Empty;

        /*
         * Bcc wird bei Antworten niemals aus der
         * Ursprungsnachricht rekonstruiert.
         */
        BccAddress =
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

    public async Task PrepareDraftEditAsync(
        string sourceFolderId,
        uint sourceUniqueId,
        string? expectedMessageId,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException(
                "Es läuft bereits ein Vorgang.");
        }

        var draft =
            await _mailDraftEditService
                .LoadDraftAsync(
                    sourceFolderId,
                    sourceUniqueId,
                    expectedMessageId,
                    cancellationToken);

        _replySourceMessage =
            null;

        _isReplyAll =
            false;

        _parentMessageId =
            string.IsNullOrWhiteSpace(
                draft.ParentMessageId)
                ? null
                : draft.ParentMessageId.Trim();

        _parentReferences =
            draft.ParentReferences
                .Where(
                    reference =>
                        !string.IsNullOrWhiteSpace(
                            reference))
                .Select(
                    reference =>
                        reference.Trim())
                .Distinct(
                    StringComparer.Ordinal)
                .ToArray();

        SetEditingDraftSource(
            draft.SourceFolderId,
            draft.SourceUniqueId,
            draft.SourceMessageId);

        WindowTitle =
            "Entwurf bearbeiten";

        HeaderTitle =
            "Entwurf bearbeiten";

        RecipientAddress =
            JoinAddresses(
                draft.ToAddresses);

        CcAddress =
            JoinAddresses(
                draft.CcAddresses);

        BccAddress =
            JoinAddresses(
                draft.BccAddresses);

        ShowCcField =
            draft.CcAddresses.Count > 0;

        ShowBccField =
            draft.BccAddresses.Count > 0;

        Subject =
            draft.Subject;

        Body =
            draft.Body;

        FocusBodyOnLoad =
            true;

        ReplaceAttachments(
            draft.Attachments);
    }

    private void AdoptSavedDraft(
        MailDraftEditData draft)
    {
        ArgumentNullException.ThrowIfNull(
            draft);

        SetEditingDraftSource(
            draft.SourceFolderId,
            draft.SourceUniqueId,
            draft.SourceMessageId);

        ReplaceAttachments(
            draft.Attachments);
    }

    private void ReplaceAttachments(
        IEnumerable<MailSendAttachmentData> attachments)
    {
        ArgumentNullException.ThrowIfNull(
            attachments);

        Attachments.Clear();

        foreach (var attachment in attachments)
        {
            Attachments.Add(
                attachment);
        }

        NotifyAttachmentStateChanged();
    }

    private void SetEditingDraftSource(
        string sourceFolderId,
        uint sourceUniqueId,
        string sourceMessageId)
    {
        if (string.IsNullOrWhiteSpace(
                sourceFolderId))
        {
            throw new ArgumentException(
                "Der Entwürfe-Ordner darf nicht leer sein.",
                nameof(sourceFolderId));
        }

        if (sourceUniqueId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceUniqueId));
        }

        if (string.IsNullOrWhiteSpace(
                sourceMessageId))
        {
            throw new ArgumentException(
                "Die Message-ID des Entwurfs darf nicht leer sein.",
                nameof(sourceMessageId));
        }

        _editingDraftSourceFolderId =
            sourceFolderId.Trim();

        _editingDraftSourceUniqueId =
            sourceUniqueId;

        _editingDraftSourceMessageId =
            sourceMessageId.Trim();

        OnPropertyChanged(nameof(IsEditingDraft));
        OnPropertyChanged(nameof(EditingDraftSourceFolderId));
        OnPropertyChanged(nameof(EditingDraftSourceUniqueId));
        OnPropertyChanged(nameof(EditingDraftSourceMessageId));
    }

    private void ClearEditingDraftSource()
    {
        var hadEditingDraft =
            IsEditingDraft;

        _editingDraftSourceFolderId =
            null;

        _editingDraftSourceUniqueId =
            0;

        _editingDraftSourceMessageId =
            null;

        if (!hadEditingDraft)
        {
            return;
        }

        OnPropertyChanged(nameof(IsEditingDraft));
        OnPropertyChanged(nameof(EditingDraftSourceFolderId));
        OnPropertyChanged(nameof(EditingDraftSourceUniqueId));
        OnPropertyChanged(nameof(EditingDraftSourceMessageId));
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

        foreach (var address in
                 replySource.ToAddresses)
        {
            AddReplyAddress(
                toAddresses,
                usedAddresses,
                address,
                activeAccountAddress);
        }

        if (toAddresses.Count == 0)
        {
            AddReplyAddress(
                toAddresses,
                usedAddresses,
                replySource.RecipientAddress,
                activeAccountAddress);
        }

        foreach (var address in
                 replySource.CcAddresses)
        {
            AddReplyAddress(
                ccAddresses,
                usedAddresses,
                address,
                activeAccountAddress);
        }

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

    private static string CreateForwardSubject(
        string? originalSubject)
    {
        var subject =
            originalSubject?
                .Trim()
            ?? string.Empty;

        if (subject.StartsWith(
                "Fwd:",
                StringComparison.OrdinalIgnoreCase) ||
            subject.StartsWith(
                "Fw:",
                StringComparison.OrdinalIgnoreCase) ||
            subject.StartsWith(
                "WG:",
                StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }

        return string.IsNullOrWhiteSpace(
                subject)
            ? "Fwd:"
            : $"Fwd: {subject}";
    }

    private static string CreateReplyBody(
        MailMessageItemViewModel message)
    {
        var senderDescription =
            CreateSenderDescription(
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

    private static string CreateForwardBody(
        MailMessageItemViewModel message)
    {
        var senderDescription =
            CreateSenderDescription(
                message);

        var toAddresses =
            message.ToAddresses.Count > 0
                ? JoinAddresses(
                    message.ToAddresses)
                : message.RecipientAddress;

        var ccAddresses =
            JoinAddresses(
                message.CcAddresses);

        var originalBody =
            string.IsNullOrWhiteSpace(
                message.Body)
                ? "(Kein darstellbarer Nachrichtentext.)"
                : message.Body.TrimEnd();

        var lines =
            new List<string>
            {
                string.Empty,
                string.Empty,
                "-------- Weitergeleitete Nachricht --------",
                $"Von: {senderDescription}",
                $"Datum: {message.DisplayDateTime}",
                $"Betreff: {message.Subject}",
                $"An: {toAddresses}"
            };

        if (!string.IsNullOrWhiteSpace(
                ccAddresses))
        {
            lines.Add(
                $"Cc: {ccAddresses}");
        }

        lines.Add(
            string.Empty);

        lines.Add(
            originalBody);

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private static string CreateSenderDescription(
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
        if (IsBusy)
        {
            throw new InvalidOperationException(
                "Es läuft bereits ein Vorgang.");
        }

        if (string.IsNullOrWhiteSpace(
                RecipientAddress))
        {
            throw new ArgumentException(
                "Bitte geben Sie einen Empfänger an.");
        }

        var sourceFolderId =
            _editingDraftSourceFolderId;

        var sourceUniqueId =
            _editingDraftSourceUniqueId;

        var sourceMessageId =
            _editingDraftSourceMessageId;

        var wasEditingDraft =
            IsEditingDraft;

        IsSending =
            true;

        try
        {
            var request =
                CreateMailRequest(
                    requireRecipient: true);

            var result =
                await _mailSendService
                    .SendAsync(
                        request,
                        cancellationToken);

            if (!result.WasSent ||
                !wasEditingDraft ||
                string.IsNullOrWhiteSpace(
                    sourceFolderId) ||
                sourceUniqueId == 0 ||
                string.IsNullOrWhiteSpace(
                    sourceMessageId))
            {
                return result;
            }

            var previousDraftRemoved =
                await _mailDraftCleanupService
                    .TryDeleteDraftAsync(
                        sourceFolderId,
                        sourceUniqueId,
                        sourceMessageId,
                        CancellationToken.None);

            if (previousDraftRemoved)
            {
                ClearEditingDraftSource();
            }

            return result with
            {
                PreviousDraftRemoved =
                    previousDraftRemoved
            };
        }
        finally
        {
            IsSending =
                false;
        }
    }

    public async Task<MailDraftSaveResult> SaveDraftAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException(
                "Es läuft bereits ein Vorgang.");
        }

        if (!HasDraftContent)
        {
            throw new InvalidOperationException(
                "Der Entwurf enthält noch keinen Inhalt.");
        }

        var sourceFolderId =
            _editingDraftSourceFolderId;

        var sourceUniqueId =
            _editingDraftSourceUniqueId;

        var sourceMessageId =
            _editingDraftSourceMessageId;

        var wasEditingDraft =
            IsEditingDraft;

        IsSavingDraft =
            true;

        try
        {
            var request =
                CreateMailRequest(
                    requireRecipient: false);

            var savedIdentity =
                await _mailSendService
                    .SaveDraftAsync(
                        request,
                        cancellationToken);

            MailDraftEditData? verifiedSavedDraft =
                null;

            if (savedIdentity is not null)
            {
                try
                {
                    verifiedSavedDraft =
                        await _mailDraftEditService
                            .LoadDraftAsync(
                                savedIdentity.FolderId,
                                savedIdentity.UniqueId,
                                savedIdentity.MessageId,
                                CancellationToken.None);
                }
                catch
                {
                    verifiedSavedDraft =
                        null;
                }
            }

            var hasPreviousDraft =
                wasEditingDraft &&
                !string.IsNullOrWhiteSpace(
                    sourceFolderId) &&
                sourceUniqueId > 0 &&
                !string.IsNullOrWhiteSpace(
                    sourceMessageId);

            if (!hasPreviousDraft)
            {
                if (verifiedSavedDraft is not null)
                {
                    AdoptSavedDraft(
                        verifiedSavedDraft);
                }

                return new MailDraftSaveResult(
                    WasSaved: true,
                    PreviousDraftRemoved: true);
            }

            if (savedIdentity is not null &&
                verifiedSavedDraft is null)
            {
                return new MailDraftSaveResult(
                    WasSaved: true,
                    PreviousDraftRemoved: false);
            }

            var previousDraftRemoved =
                await _mailDraftCleanupService
                    .TryDeleteDraftAsync(
                        sourceFolderId!,
                        sourceUniqueId,
                        sourceMessageId!,
                        CancellationToken.None);

            if (verifiedSavedDraft is not null)
            {
                AdoptSavedDraft(
                    verifiedSavedDraft);
            }
            else if (previousDraftRemoved)
            {
                ClearEditingDraftSource();
            }

            return new MailDraftSaveResult(
                WasSaved: true,
                PreviousDraftRemoved:
                    previousDraftRemoved);
        }
        finally
        {
            IsSavingDraft =
                false;
        }
    }

    private MailSendRequest CreateMailRequest(
        bool requireRecipient)
    {
        var recipientAddress =
            string.IsNullOrWhiteSpace(
                RecipientAddress)
                ? string.Empty
                : RecipientAddress.Trim();

        if (requireRecipient &&
            string.IsNullOrWhiteSpace(
                recipientAddress))
        {
            throw new ArgumentException(
                "Bitte geben Sie einen Empfänger an.");
        }

        return new MailSendRequest(
            RecipientAddress:
                recipientAddress,

            Subject:
                Subject,

            Body:
                Body,

            CcAddress:
                string.IsNullOrWhiteSpace(
                    CcAddress)
                    ? null
                    : CcAddress.Trim(),

            BccAddress:
                string.IsNullOrWhiteSpace(
                    BccAddress)
                    ? null
                    : BccAddress.Trim(),

            ParentMessageId:
                _parentMessageId,

            ParentReferences:
                _parentReferences,

            Attachments:
                Attachments.ToArray());
    }
}