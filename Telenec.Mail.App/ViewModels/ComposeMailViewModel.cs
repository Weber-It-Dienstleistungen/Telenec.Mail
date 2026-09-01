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

    /*
     * Identität eines bereits vorhandenen Server-Entwurfs.
     *
     * Diese Werte werden beim sicheren Replace benötigt:
     *
     * 1. neue Version speichern bzw. Mail versenden
     * 2. Erfolg abwarten
     * 3. alten Draft anhand UID + Message-ID verifizieren
     * 4. erst dann alten Draft entfernen
     */
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

    private string _subject =
        string.Empty;

    private string _body =
        string.Empty;

    private bool _showCcField;
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
        get =>
            _fromAddress;

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
        get =>
            _recipientAddress;

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

            OnPropertyChanged(
                nameof(CanSaveDraft));
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

            OnPropertyChanged(
                nameof(CanSaveDraft));
        }
    }

    public string Subject
    {
        get =>
            _subject;

        set
        {
            if (_subject == value)
            {
                return;
            }

            _subject =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSaveDraft));
        }
    }

    public string Body
    {
        get =>
            _body;

        set
        {
            if (_body == value)
            {
                return;
            }

            _body =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSaveDraft));
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
        get =>
            _isSending;

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
                nameof(IsBusy));

            OnPropertyChanged(
                nameof(CanSend));

            OnPropertyChanged(
                nameof(CanSaveDraft));

            OnPropertyChanged(
                nameof(CanModifyAttachments));
        }
    }

    public bool IsSavingDraft
    {
        get =>
            _isSavingDraft;

        private set
        {
            if (_isSavingDraft == value)
            {
                return;
            }

            _isSavingDraft =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(IsBusy));

            OnPropertyChanged(
                nameof(CanSend));

            OnPropertyChanged(
                nameof(CanSaveDraft));

            OnPropertyChanged(
                nameof(CanModifyAttachments));
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
            Subject) ||
        !string.IsNullOrWhiteSpace(
            Body) ||
        Attachments.Count > 0;

    public string AttachmentSummary =>
        Attachments.Count switch
        {
            0 =>
                string.Empty,

            1 =>
                "1 Anhang",

            _ =>
                $"{Attachments.Count} Anhänge"
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

        /*
         * Server-Anhänge besitzen absichtlich keinen
         * lokalen Dateipfad.
         *
         * Für die Dublettenprüfung lokaler Dateien werden
         * deshalb ausschließlich lokale Anhänge betrachtet.
         */
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
            new List<
                MailSendAttachmentData>();

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

        /*
         * Erst die komplette Liste validieren und erzeugen.
         *
         * Damit entsteht auch hier kein halbfertiger Zustand,
         * wenn ein einzelner MIME-Part unerwartet ungültig
         * sein sollte.
         */
        var forwardedAttachments =
            new List<
                MailSendAttachmentData>();

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
                    FilePath:
                        string.Empty,

                    FileName:
                        attachment.FileName,

                    SizeBytes:
                        attachment.EncodedSizeBytes,

                    SourceFolderId:
                        sourceFolderId.Trim(),

                    SourceUniqueId:
                        message.UniqueId,

                    SourcePartSpecifier:
                        attachment.PartSpecifier,

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

        /*
         * Bereits beim Hinzufügen prüfen wir, ob die Datei
         * tatsächlich lesbar ist.
         *
         * Der Stream wird sofort wieder geschlossen.
         * Der eigentliche Versand oder die Draft-Speicherung
         * öffnet die Datei später erneut.
         */
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
            FilePath:
                fullPath,

            FileName:
                fileName,

            SizeBytes:
                sizeBytes);
    }

    private void NotifyAttachmentStateChanged()
    {
        OnPropertyChanged(
            nameof(HasAttachments));

        OnPropertyChanged(
            nameof(AttachmentSummary));

        OnPropertyChanged(
            nameof(CanSaveDraft));
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

        /*
         * Weiterleiten ist ausdrücklich KEIN Reply.
         *
         * Deshalb darf beim späteren Versand weder
         * In-Reply-To noch References aus der
         * Ursprungsnachricht übernommen werden.
         */
        _replySourceMessage =
            null;

        _isReplyAll =
            false;

        _parentMessageId =
            null;

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

        ShowCcField =
            false;

        Subject =
            CreateForwardSubject(
                message.Subject);

        Body =
            CreateForwardBody(
                message);

        /*
         * Beim Weiterleiten muss der Benutzer zuerst einen
         * neuen Empfänger bestimmen.
         */
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

        /*
         * Ab hier wurde der Entwurf bereits frisch vom
         * Server geladen und durch MailKitDraftEditService
         * gegen die erwartete Message-ID geprüft.
         */

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

        ShowCcField =
            draft.CcAddresses.Count > 0;

        Subject =
            draft.Subject;

        Body =
            draft.Body;

        FocusBodyOnLoad =
            true;

        Attachments.Clear();

        foreach (var attachment in
                 draft.Attachments)
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

        OnPropertyChanged(
            nameof(IsEditingDraft));

        OnPropertyChanged(
            nameof(EditingDraftSourceFolderId));

        OnPropertyChanged(
            nameof(EditingDraftSourceUniqueId));

        OnPropertyChanged(
            nameof(EditingDraftSourceMessageId));
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

        OnPropertyChanged(
            nameof(IsEditingDraft));

        OnPropertyChanged(
            nameof(EditingDraftSourceFolderId));

        OnPropertyChanged(
            nameof(EditingDraftSourceUniqueId));

        OnPropertyChanged(
            nameof(EditingDraftSourceMessageId));
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

        /*
         * Draft-Identität VOR dem Versand einfrieren.
         *
         * Nach erfolgreichem SMTP-Versand ist dieser Zustand
         * irreversibel. Erst danach darf der alte Entwurf
         * entfernt werden.
         */
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

            /*
             * Ab hier wurde die Nachricht bereits per SMTP
             * versendet.
             *
             * Deshalb verwenden wir für den Cleanup bewusst
             * NICHT mehr den ursprünglichen CancellationToken.
             *
             * Eine nachträgliche Cancellation darf nicht dazu
             * führen, dass der bereits versendete Draft
             * unnötig im Entwürfe-Ordner liegen bleibt.
             *
             * Der Cleanup-Dienst besitzt selbst einen
             * begrenzten Timeout.
             */
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

        /*
         * Identität der bisherigen Draft-Version vor dem
         * Append sichern.
         *
         * Die neue Version wird IMMER zuerst vollständig
         * gespeichert.
         */
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

            /*
             * Erst die neue Version sicher auf den Server
             * schreiben.
             *
             * Schlägt das fehl, wird die alte Version nicht
             * angefasst.
             */
            await _mailSendService
                .SaveDraftAsync(
                    request,
                    cancellationToken);

            /*
             * Bei einer komplett neuen Nachricht existiert
             * kein alter Draft, den wir entfernen müssten.
             */
            if (!wasEditingDraft ||
                string.IsNullOrWhiteSpace(
                    sourceFolderId) ||
                sourceUniqueId == 0 ||
                string.IsNullOrWhiteSpace(
                    sourceMessageId))
            {
                return new MailDraftSaveResult(
                    WasSaved:
                        true,

                    PreviousDraftRemoved:
                        true);
            }

            /*
             * Die neue Version ist jetzt bereits sicher auf
             * dem Server vorhanden.
             *
             * Auch hier verwenden wir für den Cleanup
             * CancellationToken.None.
             *
             * Eine Cancellation NACH erfolgreichem Append
             * darf nicht unnötig eine doppelte Draft-Version
             * erzeugen.
             *
             * MailKitDraftCleanupService besitzt selbst einen
             * festen Cleanup-Timeout.
             */
            var previousDraftRemoved =
                await _mailDraftCleanupService
                    .TryDeleteDraftAsync(
                        sourceFolderId,
                        sourceUniqueId,
                        sourceMessageId,
                        CancellationToken.None);

            if (previousDraftRemoved)
            {
                /*
                 * Die alte Draft-Quelle existiert jetzt nicht
                 * mehr.
                 *
                 * Für unseren aktuellen Workflow ist das
                 * korrekt, weil das Compose-Fenster nach
                 * erfolgreichem Speichern geschlossen wird.
                 *
                 * Ein späteres Autosave benötigt dafür einen
                 * erweiterten Workflow, der anschließend die
                 * neue UID/Message-ID übernimmt.
                 */
                ClearEditingDraftSource();
            }

            return new MailDraftSaveResult(
                WasSaved:
                    true,

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

            ParentMessageId:
                _parentMessageId,

            ParentReferences:
                _parentReferences,

            Attachments:
                Attachments.ToArray());
    }
}