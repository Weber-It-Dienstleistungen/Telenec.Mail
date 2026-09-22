using System.Windows;
using Telenec.Mail.App.Services.Migration;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MailboxMigrationWindow :
    Window
{
    private readonly LegacyMailboxConnectionService
        _legacyMailboxConnectionService;

    private readonly LegacyRoundcubeContactService
        _legacyRoundcubeContactService;

    private readonly ContactsViewModel
        _targetContactsViewModel;

    private readonly LegacyContactMigrationService
        _contactMigrationService;

    private readonly LegacyMailProofOfWorkService
        _mailProofOfWorkService;

    private CancellationTokenSource?
        _operationCancellationSource;

    private LegacyRoundcubeContactInventoryResult?
        _contactInventoryResult;

    private LegacyContactMigrationPlan?
        _contactMigrationPlan;

    private bool
        _isOperationRunning;

    private bool
        _connectionVerified;

    private bool
        _contactsMigrated;

    private bool
        _mailProofPrerequisitesMet;

    private bool
        _mailProofCompleted;

    public MailboxMigrationWindow(
        string targetEmailAddress,
        ContactsViewModel targetContactsViewModel,
        LegacyContactMigrationService contactMigrationService,
        LegacyMailProofOfWorkService mailProofOfWorkService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            targetEmailAddress);

        ArgumentNullException.ThrowIfNull(
            targetContactsViewModel);

        ArgumentNullException.ThrowIfNull(
            contactMigrationService);

        ArgumentNullException.ThrowIfNull(
            mailProofOfWorkService);

        InitializeComponent();

        _legacyMailboxConnectionService =
            new LegacyMailboxConnectionService();

        _legacyRoundcubeContactService =
            new LegacyRoundcubeContactService();

        _targetContactsViewModel =
            targetContactsViewModel;

        _contactMigrationService =
            contactMigrationService;

        _mailProofOfWorkService =
            mailProofOfWorkService;

        TargetEmailTextBox.Text =
            targetEmailAddress.Trim();

        LegacyEmailTextBox.Text =
            targetEmailAddress.Trim();

        Loaded +=
            MailboxMigrationWindow_OnLoaded;

        Closed +=
            MailboxMigrationWindow_OnClosed;
    }

    private void MailboxMigrationWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        LegacyPasswordBox.Focus();
    }

    private void LegacyCredentials_OnChanged(
        object sender,
        RoutedEventArgs e)
    {
        _connectionVerified =
            false;

        _contactInventoryResult =
            null;

        _contactMigrationPlan =
            null;

        _contactsMigrated =
            false;

        _mailProofPrerequisitesMet =
            false;

        _mailProofCompleted =
            false;

        if (AnalyzeMailboxButton is not null)
        {
            AnalyzeMailboxButton.IsEnabled =
                false;
        }

        if (MailProofOfWorkButton is not null)
        {
            MailProofOfWorkButton.IsEnabled =
                false;
        }
    }

    private async void TestConnectionButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isOperationRunning)
        {
            return;
        }

        var credentials =
            GetLegacyCredentials();

        if (credentials is null)
        {
            return;
        }

        SetOperationRunning(
            true);

        ConnectionStatusText.Text =
            "Sichere Verbindung zum alten Mailserver wird geprüft …";

        var cancellationSource =
            BeginOperation();

        try
        {
            var result =
                await _legacyMailboxConnectionService
                    .TestConnectionAsync(
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        cancellationSource.Token);

            if (!result.Success)
            {
                _connectionVerified =
                    false;

                ConnectionStatusText.Text =
                    result.Message;

                return;
            }

            _connectionVerified =
                true;

            var encryptionInformation =
                result.UsesTls
                    ? "Transportverschlüsselung: STARTTLS aktiv."
                    : "Achtung: Der Server verwendet keine Transportverschlüsselung.";

            ConnectionStatusText.Text =
                $"Verbindung erfolgreich. " +
                $"Im Posteingang befinden sich " +
                $"{result.InboxMessageCount:N0} Nachrichten.\n" +
                encryptionInformation;
        }
        catch (OperationCanceledException)
        {
            ConnectionStatusText.Text =
                "Der Verbindungstest wurde abgebrochen.";
        }
        finally
        {
            EndOperation(
                cancellationSource);

            SetOperationRunning(
                false);

            AnalyzeMailboxButton.IsEnabled =
                _connectionVerified;
        }
    }

    private async void AnalyzeMailboxButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isOperationRunning ||
            !_connectionVerified)
        {
            return;
        }

        var credentials =
            GetLegacyCredentials();

        if (credentials is null)
        {
            return;
        }

        SetOperationRunning(
            true);

        ConnectionStatusText.Text =
            "Ordnerstruktur und Nachrichtenbestand werden eingelesen …";

        var cancellationSource =
            BeginOperation();

        try
        {
            var mailResult =
                await _legacyMailboxConnectionService
                    .GetInventoryAsync(
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        cancellationSource.Token);

            if (!mailResult.Success)
            {
                ConnectionStatusText.Text =
                    mailResult.Message;

                return;
            }

            cancellationSource.Token
                .ThrowIfCancellationRequested();

            ConnectionStatusText.Text =
                "Kontakte aus dem alten Roundcube werden eingelesen …";

            var contactResult =
                await _legacyRoundcubeContactService
                    .GetInventoryAsync(
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        cancellationSource.Token);

            _contactInventoryResult =
                contactResult;

            cancellationSource.Token
                .ThrowIfCancellationRequested();

            string contactMigrationSummary;

            if (contactResult.Success)
            {
                ConnectionStatusText.Text =
                    "Neues Telenec-Adressbuch wird geprüft …";

                try
                {
                    await _targetContactsViewModel
                        .InitializeAsync(
                            cancellationSource.Token);

                    var targetContacts =
                        _targetContactsViewModel
                            .VisibleContacts
                            .ToArray();

                    _contactMigrationPlan =
                        _contactMigrationService
                            .CreatePlan(
                                contactResult.Contacts,
                                targetContacts);

                    var plan =
                        _contactMigrationPlan;

                    contactMigrationSummary =
                        plan.SourceContactCount == 1
                            ? "Im alten Roundcube wurde 1 Kontakt gefunden.\n"
                            : $"Im alten Roundcube wurden {plan.SourceContactCount:N0} Kontakte gefunden.\n";

                    contactMigrationSummary +=
                        targetContacts.Length == 1
                            ? "Im neuen Telenec-Adressbuch befindet sich bereits 1 Kontakt.\n"
                            : $"Im neuen Telenec-Adressbuch befinden sich bereits {targetContacts.Length:N0} Kontakte.\n";

                    if (plan.ExistingContactCount > 0)
                    {
                        contactMigrationSummary +=
                            plan.ExistingContactCount == 1
                                ? "1 alter Kontakt ist anhand von UID oder E-Mail-Adresse bereits vorhanden.\n"
                                : $"{plan.ExistingContactCount:N0} alte Kontakte sind anhand von UID oder E-Mail-Adresse bereits vorhanden.\n";
                    }

                    contactMigrationSummary +=
                        plan.ContactsToImportCount == 1
                            ? "1 Kontakt ist zur Übernahme vorgesehen."
                            : $"{plan.ContactsToImportCount:N0} Kontakte sind zur Übernahme vorgesehen.";

                    ConfigureContactMigrationButton(
                        plan);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _contactMigrationPlan =
                        null;

                    ContactMigrationButton.IsEnabled =
                        false;

                    contactMigrationSummary =
                        contactResult.ContactCount == 1
                            ? "Im alten Roundcube wurde 1 Kontakt gefunden.\n"
                            : $"Im alten Roundcube wurden {contactResult.ContactCount:N0} Kontakte gefunden.\n";

                    contactMigrationSummary +=
                        "Das neue Telenec-Adressbuch konnte für den Dublettenabgleich noch nicht gelesen werden.\n" +
                        exception.Message;
                }
            }
            else
            {
                _contactMigrationPlan =
                    null;

                ContactMigrationButton.IsEnabled =
                    false;

                contactMigrationSummary =
                    "Der Kontaktbestand konnte noch nicht automatisch eingelesen werden.\n" +
                    contactResult.Message;
            }

            InventoryFolderList.ItemsSource =
                mailResult.Folders;

            var foldersWithMessages =
                mailResult
                    .Folders
                    .Count(
                        folder =>
                            folder.IsSelectable &&
                            folder.MessageCount > 0);

            InventorySummaryText.Text =
                $"{mailResult.FolderCount:N0} Ordner gefunden, " +
                $"davon {foldersWithMessages:N0} mit Nachrichtenbestand.\n" +
                $"Insgesamt wurden {mailResult.TotalMessageCount:N0} Nachrichten ermittelt.\n\n" +
                contactMigrationSummary;

            ConfigureMailProofOfWork(
                mailResult);

            ConnectionStepPanel.Visibility =
                Visibility.Collapsed;

            InventoryStepPanel.Visibility =
                Visibility.Visible;

            StepStatusText.Text =
                "Schritt 2 von 4 · Bestandsaufnahme";
        }
        catch (OperationCanceledException)
        {
            ConnectionStatusText.Text =
                "Die Bestandsaufnahme wurde abgebrochen.";
        }
        catch (Exception exception)
        {
            ConnectionStatusText.Text =
                "Die Bestandsaufnahme konnte nicht abgeschlossen werden.\n\n" +
                exception.Message;
        }
        finally
        {
            EndOperation(
                cancellationSource);

            SetOperationRunning(
                false);
        }
    }

    private async void ContactMigrationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isOperationRunning ||
            _contactsMigrated ||
            _contactMigrationPlan is null ||
            _contactInventoryResult is null ||
            _contactMigrationPlan.ContactsToImportCount == 0)
        {
            return;
        }

        var plan =
            _contactMigrationPlan;

        var confirmationText =
            plan.ContactsToImportCount == 1
                ? "Es wird 1 Kontakt in das neue Telenec-Adressbuch übertragen."
                : $"Es werden {plan.ContactsToImportCount:N0} Kontakte in das neue Telenec-Adressbuch übertragen.";

        var confirmation =
            MessageBox.Show(
                this,
                confirmationText +
                "\n\n" +
                "Die Kontakte im alten Roundcube bleiben unverändert erhalten.\n" +
                "Bereits erkannte Dubletten werden nicht überschrieben.\n\n" +
                "Möchten Sie die Kontakte jetzt übernehmen?",
                "Kontakte migrieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetOperationRunning(
            true);

        ContactMigrationStatusText.Text =
            "Kontakte werden übertragen …";

        var cancellationSource =
            BeginOperation();

        try
        {
            var result =
                await _contactMigrationService
                    .ImportAsync(
                        plan,
                        cancellationSource.Token);

            ContactMigrationStatusText.Text =
                "Kontakte wurden geschrieben. Neues Adressbuch wird vollständig geprüft …";

            await _targetContactsViewModel
                .ReloadAsync(
                    cancellationSource.Token);

            var targetContactsAfterImport =
                _targetContactsViewModel
                    .VisibleContacts
                    .ToArray();

            var verificationPlan =
                _contactMigrationService
                    .CreatePlan(
                        _contactInventoryResult.Contacts,
                        targetContactsAfterImport);

            _contactMigrationPlan =
                verificationPlan;

            if (verificationPlan.ContactsToImportCount >
                0)
            {
                var missingCount =
                    verificationPlan
                        .ContactsToImportCount;

                ContactMigrationStatusText.Text =
                    missingCount == 1
                        ? "Nach dem vollständigen CardDAV-Abgleich fehlt noch 1 Kontakt im neuen Adressbuch."
                        : $"Nach dem vollständigen CardDAV-Abgleich fehlen noch {missingCount:N0} Kontakte im neuen Adressbuch.";

                if (result.Failures.Count > 0)
                {
                    ContactMigrationStatusText.Text +=
                        "\n" +
                        string.Join(
                            "\n",
                            result.Failures.Take(3));
                }

                ConfigureContactMigrationButton(
                    verificationPlan);

                return;
            }

            _contactsMigrated =
                true;

            ContactMigrationButton.IsEnabled =
                false;

            ContactMigrationButton.Content =
                "Kontakte vollständig übernommen";

            var migratedCount =
                plan.ContactsToImportCount;

            ContactMigrationStatusText.Text =
                migratedCount == 1
                    ? "1 Kontakt wurde erfolgreich übernommen. Der vollständige alte Kontaktbestand wurde anschließend im neuen CardDAV bestätigt."
                    : $"{migratedCount:N0} Kontakte wurden erfolgreich übernommen. Der vollständige alte Kontaktbestand wurde anschließend im neuen CardDAV bestätigt.";

            MigrationNoticeText.Text =
                "Die Kontakte wurden in das neue Telenec-Adressbuch übernommen und vollständig geprüft. " +
                "Der alte Roundcube-Kontaktbestand wurde nicht verändert. " +
                "E-Mails und Ordner wurden weiterhin noch nicht kopiert oder gelöscht.";

            var newTargetCount =
                targetContactsAfterImport
                    .Length;

            InventorySummaryText.Text +=
                $"\n\nKontaktmigration abgeschlossen: " +
                $"{_contactInventoryResult.ContactCount:N0} von " +
                $"{_contactInventoryResult.ContactCount:N0} alten Kontakten im Ziel bestätigt. " +
                $"Neues Adressbuch: {newTargetCount:N0} Kontakte.";

            UpdateMailProofButtonState();
        }
        catch (OperationCanceledException)
        {
            ContactMigrationStatusText.Text =
                "Die Kontaktmigration wurde abgebrochen. " +
                "Bereits vollständig übertragene Kontakte bleiben erhalten und werden bei einer erneuten Bestandsaufnahme erkannt.";
        }
        catch (Exception exception)
        {
            ContactMigrationStatusText.Text =
                "Die Kontaktmigration konnte nicht abgeschlossen werden.\n" +
                exception.Message;
        }
        finally
        {
            EndOperation(
                cancellationSource);

            SetOperationRunning(
                false);

            if (!_contactsMigrated &&
                _contactMigrationPlan is not null)
            {
                ConfigureContactMigrationButton(
                    _contactMigrationPlan);
            }
        }
    }

    private async void MailProofOfWorkButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isOperationRunning ||
            _mailProofCompleted ||
            !_mailProofPrerequisitesMet ||
            !ContactsReadyForMailMigration())
        {
            return;
        }

        var credentials =
            GetLegacyCredentials();

        if (credentials is null)
        {
            return;
        }

        var confirmation =
            MessageBox.Show(
                this,
                $"Im alten Ordner „{LegacyMailProofOfWorkService.TestFolderName}“ liegt genau eine Testmail.\n\n" +
                "Der Test führt folgende Schritte aus:\n" +
                "• Rohdaten der Quellmail lesen\n" +
                "• byteidentisch auf den neuen Server übertragen\n" +
                "• SHA-256, Rohbytes, Flags und Empfangsdatum verifizieren\n" +
                "• erst danach die Quellmail dauerhaft und selektiv löschen\n" +
                "• Quelle und Ziel abschließend erneut prüfen\n\n" +
                "Die Testmail wird bei erfolgreicher Verifikation wirklich vom alten Server gelöscht.\n\n" +
                "Proof-of-Work jetzt starten?",
                "Mailmigration — Proof-of-Work",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetOperationRunning(
            true);

        MailProofOfWorkStatusText.Text =
            "Proof-of-Work läuft. Die Quelle wird nur nach vollständiger Zielverifikation gelöscht …";

        var cancellationSource =
            BeginOperation();

        try
        {
            var result =
                await _mailProofOfWorkService
                    .RunAsync(
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        cancellationSource.Token);

            if (!result.Success)
            {
                MailProofOfWorkStatusText.Text =
                    result.Message;

                if (result.SourceDeleted)
                {
                    MessageBox.Show(
                        this,
                        result.Message,
                        "Mailmigration — Prüfung erforderlich",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            _mailProofCompleted =
                true;

            _mailProofPrerequisitesMet =
                false;

            MailProofOfWorkButton.IsEnabled =
                false;

            MailProofOfWorkButton.Content =
                "Proof-of-Work erfolgreich";

            var abbreviatedHash =
                result.SourceSha256 is
                { Length: >= 16 }
                    ? result.SourceSha256[..16] +
                      "…"
                    : result.SourceSha256
                      ?? "-";

            MailProofOfWorkStatusText.Text =
                $"✓ „{result.Subject}“ wurde byteidentisch migriert.\n" +
                $"SHA-256 Quelle = Ziel: {abbreviatedHash}\n" +
                "✓ Ziel vor der Löschung verifiziert\n" +
                "✓ Quell-UID selektiv gelöscht\n" +
                "✓ Löschung auf dem Altserver bestätigt\n" +
                "✓ Ziel nach der Löschung erneut verifiziert\n" +
                "Endzustand: Quelle 0 · Ziel 1";

            MigrationNoticeText.Text =
                "Proof-of-Work abgeschlossen: Die einzelne Testmail wurde erst nach vollständiger Byte- und Metadatenprüfung vom alten Server gelöscht. " +
                "Der übrige Mailbestand wurde nicht verändert.";

            InventorySummaryText.Text +=
                "\n\nProof-of-Work erfolgreich: " +
                "1 Testmail 1:1 übertragen, verifiziert und anschließend auf dem Altserver gelöscht.";

            StepStatusText.Text =
                "Schritt 3 von 4 · Proof-of-Work";
        }
        catch (OperationCanceledException)
        {
            MailProofOfWorkStatusText.Text =
                "Der Proof-of-Work wurde abgebrochen. Bei einem Abbruch wird die Quelle nur gelöscht, wenn die vollständige Verifikationskette bereits abgeschlossen war.";
        }
        catch (Exception exception)
        {
            MailProofOfWorkStatusText.Text =
                "Der Proof-of-Work konnte nicht abgeschlossen werden.\n" +
                exception.Message;
        }
        finally
        {
            EndOperation(
                cancellationSource);

            SetOperationRunning(
                false);
        }
    }

    private void ConfigureContactMigrationButton(
        LegacyContactMigrationPlan plan)
    {
        if (plan.ContactsToImportCount <= 0)
        {
            ContactMigrationButton.Content =
                "Alle Kontakte bereits vorhanden";

            ContactMigrationButton.IsEnabled =
                false;

            ContactMigrationStatusText.Text =
                "Für die Kontakte ist keine Übertragung erforderlich.";

            UpdateMailProofButtonState();

            return;
        }

        ContactMigrationButton.Content =
            plan.ContactsToImportCount == 1
                ? "1 Kontakt übernehmen"
                : $"{plan.ContactsToImportCount:N0} Kontakte übernehmen";

        ContactMigrationButton.IsEnabled =
            !_isOperationRunning;

        UpdateMailProofButtonState();
    }

    private void ConfigureMailProofOfWork(
        LegacyMailboxInventoryResult mailResult)
    {
        var testFolder =
            mailResult
                .Folders
                .FirstOrDefault(
                    folder =>
                        string.Equals(
                            folder.FullName,
                            LegacyMailProofOfWorkService.TestFolderName,
                            StringComparison.OrdinalIgnoreCase));

        _mailProofCompleted =
            false;

        if (testFolder is null)
        {
            _mailProofPrerequisitesMet =
                false;

            MailProofOfWorkStatusText.Text =
                $"Proof-of-Work: Auf dem alten Server fehlt der Ordner „{LegacyMailProofOfWorkService.TestFolderName}“.";

            UpdateMailProofButtonState();

            return;
        }

        if (!testFolder.IsSelectable)
        {
            _mailProofPrerequisitesMet =
                false;

            MailProofOfWorkStatusText.Text =
                $"Proof-of-Work: Der Ordner „{LegacyMailProofOfWorkService.TestFolderName}“ ist nicht als Nachrichtenordner auswählbar.";

            UpdateMailProofButtonState();

            return;
        }

        if (testFolder.MessageCount != 1)
        {
            _mailProofPrerequisitesMet =
                false;

            MailProofOfWorkStatusText.Text =
                testFolder.MessageCount == 0
                    ? $"Proof-of-Work: Bitte genau eine Testmail in „{LegacyMailProofOfWorkService.TestFolderName}“ ablegen."
                    : $"Proof-of-Work: Im Testordner liegen {testFolder.MessageCount:N0} Nachrichten. Bitte auf genau eine reduzieren.";

            UpdateMailProofButtonState();

            return;
        }

        _mailProofPrerequisitesMet =
            true;

        MailProofOfWorkStatusText.Text =
            $"Proof-of-Work bereit: „{LegacyMailProofOfWorkService.TestFolderName}“ enthält genau eine Testmail.";

        UpdateMailProofButtonState();
    }

    private bool ContactsReadyForMailMigration()
    {
        return
            _contactsMigrated ||
            (_contactMigrationPlan is not null &&
             _contactMigrationPlan
                 .ContactsToImportCount == 0);
    }

    private void UpdateMailProofButtonState()
    {
        if (MailProofOfWorkButton is null)
        {
            return;
        }

        var contactsReady =
            ContactsReadyForMailMigration();

        MailProofOfWorkButton.IsEnabled =
            !_isOperationRunning &&
            !_mailProofCompleted &&
            _mailProofPrerequisitesMet &&
            contactsReady;

        if (_mailProofPrerequisitesMet &&
            !contactsReady)
        {
            MailProofOfWorkStatusText.Text =
                "Die Testmail ist bereit. Vor dem Mail-Proof müssen jedoch zunächst die noch fehlenden Kontakte übernommen werden.";
        }
    }

    private void BackToConnectionButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        InventoryStepPanel.Visibility =
            Visibility.Collapsed;

        ConnectionStepPanel.Visibility =
            Visibility.Visible;

        StepStatusText.Text =
            "Schritt 1 von 4 · Verbindung";

        AnalyzeMailboxButton.IsEnabled =
            _connectionVerified;
    }

    private (
        string UserName,
        string Password)?
        GetLegacyCredentials()
    {
        var legacyEmailAddress =
            LegacyEmailTextBox.Text
                .Trim();

        var legacyPassword =
            LegacyPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(
                legacyEmailAddress))
        {
            ConnectionStatusText.Text =
                "Bitte geben Sie die E-Mail-Adresse des alten Postfachs ein.";

            LegacyEmailTextBox.Focus();

            return null;
        }

        if (string.IsNullOrEmpty(
                legacyPassword))
        {
            ConnectionStatusText.Text =
                "Bitte geben Sie das Passwort des alten Postfachs ein.";

            LegacyPasswordBox.Focus();

            return null;
        }

        return (
            legacyEmailAddress,
            legacyPassword);
    }

    private CancellationTokenSource
        BeginOperation()
    {
        var cancellationSource =
            new CancellationTokenSource();

        _operationCancellationSource =
            cancellationSource;

        return cancellationSource;
    }

    private void EndOperation(
        CancellationTokenSource cancellationSource)
    {
        if (ReferenceEquals(
                _operationCancellationSource,
                cancellationSource))
        {
            _operationCancellationSource =
                null;
        }

        cancellationSource.Dispose();
    }

    private void SetOperationRunning(
        bool isRunning)
    {
        _isOperationRunning =
            isRunning;

        TestConnectionButton.IsEnabled =
            !isRunning;

        LegacyEmailTextBox.IsEnabled =
            !isRunning;

        LegacyPasswordBox.IsEnabled =
            !isRunning;

        AnalyzeMailboxButton.IsEnabled =
            !isRunning &&
            _connectionVerified;

        if (ContactMigrationButton is not null)
        {
            ContactMigrationButton.IsEnabled =
                !isRunning &&
                !_contactsMigrated &&
                _contactMigrationPlan is not null &&
                _contactMigrationPlan
                    .ContactsToImportCount > 0;
        }

        UpdateMailProofButtonState();
    }

    private void MailboxMigrationWindow_OnClosed(
        object? sender,
        EventArgs e)
    {
        try
        {
            _operationCancellationSource?
                .Cancel();
        }
        catch
        {
        }

        LegacyPasswordBox.Clear();

        _contactInventoryResult =
            null;

        _contactMigrationPlan =
            null;

        Loaded -=
            MailboxMigrationWindow_OnLoaded;

        Closed -=
            MailboxMigrationWindow_OnClosed;
    }
}