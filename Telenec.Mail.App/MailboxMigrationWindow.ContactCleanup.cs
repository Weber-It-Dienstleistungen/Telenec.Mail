using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Telenec.Mail.App.Services.Migration;

namespace Telenec.Mail.App;

public partial class MailboxMigrationWindow
{
    private static readonly bool
        ContactCleanupClassHandlerRegistered =
            RegisterContactCleanupClassHandler();

    private readonly LegacyRoundcubeContactDeletionService
        _legacyRoundcubeContactDeletionService =
            new();

    private Button?
        _deleteLegacyContactsButton;

    private TextBlock?
        _deleteLegacyContactsStatusText;

    private bool
        _legacyContactsDeleted;

    private static bool
        RegisterContactCleanupClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MailboxMigrationWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MailboxMigrationWindow_OnContactCleanupLoaded));

        return true;
    }

    private static void
        MailboxMigrationWindow_OnContactCleanupLoaded(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not
            MailboxMigrationWindow window)
        {
            return;
        }

        if (!ReferenceEquals(
                e.OriginalSource,
                window))
        {
            return;
        }

        window.InstallContactCleanupUi();
    }

    private void
        InstallContactCleanupUi()
    {
        if (_deleteLegacyContactsButton is not null)
        {
            return;
        }

        var button =
            new Button
            {
                Height =
                    42,

                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        0),

                Content =
                    "Alte Roundcube-Kontakte löschen",

                Cursor =
                    Cursors.Hand,

                IsEnabled =
                    true
            };

        button.Click +=
            DeleteLegacyContactsButton_OnClick;

        var statusText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        8,
                        0,
                        0),

                FontSize =
                    11,

                TextAlignment =
                    TextAlignment.Center,

                TextWrapping =
                    TextWrapping.Wrap,

                Opacity =
                    0.72,

                Text =
                    "Die alten Kontakte werden nur nach erneuter vollständiger Zielprüfung gelöscht."
            };

        var insertIndex =
            InventoryStepPanel
                .Children
                .IndexOf(
                    ContactMigrationStatusText);

        if (insertIndex < 0)
        {
            insertIndex =
                InventoryStepPanel
                    .Children
                    .Count -
                1;
        }

        InventoryStepPanel
            .Children
            .Insert(
                insertIndex + 1,
                button);

        InventoryStepPanel
            .Children
            .Insert(
                insertIndex + 2,
                statusText);

        _deleteLegacyContactsButton =
            button;

        _deleteLegacyContactsStatusText =
            statusText;
    }

    private async void
        DeleteLegacyContactsButton_OnClick(
            object sender,
            RoutedEventArgs e)
    {
        if (_isOperationRunning ||
            _legacyContactsDeleted ||
            _deleteLegacyContactsButton is null ||
            _deleteLegacyContactsStatusText is null)
        {
            return;
        }

        var credentials =
            GetLegacyCredentials();

        if (credentials is null)
        {
            return;
        }

        _deleteLegacyContactsButton.IsEnabled =
            false;

        SetOperationRunning(
            true);

        var cancellationSource =
            BeginOperation();

        try
        {
            /*
             * 1. Quelle unmittelbar vor der Löschung
             *    vollständig neu lesen.
             */
            _deleteLegacyContactsStatusText.Text =
                "Alter Roundcube-Kontaktbestand wird erneut gelesen …";

            var sourceInventory =
                await _legacyRoundcubeContactService
                    .GetInventoryAsync(
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        cancellationSource.Token);

            if (!sourceInventory.Success)
            {
                _deleteLegacyContactsStatusText.Text =
                    sourceInventory.Message;

                return;
            }

            if (sourceInventory.ContactCount == 0)
            {
                _legacyContactsDeleted =
                    true;

                _deleteLegacyContactsButton.Content =
                    "Alte Kontakte bereits gelöscht";

                _deleteLegacyContactsStatusText.Text =
                    "Das alte Roundcube-Adressbuch ist bereits leer.";

                return;
            }

            /*
             * 2. Ziel unmittelbar vor der destruktiven
             *    Operation nochmals vollständig aus CardDAV
             *    lesen.
             */
            _deleteLegacyContactsStatusText.Text =
                "Neues Telenec-Adressbuch wird vor der Löschung erneut vollständig geprüft …";

            await _targetContactsViewModel
                .ReloadAsync(
                    cancellationSource.Token);

            var targetContacts =
                _targetContactsViewModel
                    .VisibleContacts
                    .ToArray();

            var verificationPlan =
                _contactMigrationService
                    .CreatePlan(
                        sourceInventory.Contacts,
                        targetContacts);

            if (verificationPlan.ContactsToImportCount >
                0)
            {
                var missingCount =
                    verificationPlan
                        .ContactsToImportCount;

                _deleteLegacyContactsStatusText.Text =
                    missingCount == 1
                        ? "Löschung gesperrt: 1 alter Kontakt konnte im neuen CardDAV nicht bestätigt werden."
                        : $"Löschung gesperrt: {missingCount:N0} alte Kontakte konnten im neuen CardDAV nicht bestätigt werden.";

                MessageBox.Show(
                    this,
                    _deleteLegacyContactsStatusText.Text +
                    "\n\n" +
                    "Im alten Roundcube wurde nichts gelöscht.",
                    "Kontaktlöschung gesperrt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            /*
             * 3. Explizite destruktive Freigabe.
             */
            var confirmation =
                MessageBox.Show(
                    this,
                    $"Alle {sourceInventory.ContactCount:N0} alten Roundcube-Kontakte wurden unmittelbar zuvor im neuen Telenec-CardDAV bestätigt.\n\n" +
                    "Wenn Sie fortfahren, werden diese Kontakte jetzt aus dem alten Roundcube gelöscht.\n\n" +
                    "Anschließend wird sowohl der leere Quellbestand als auch der vollständige Zielbestand nochmals geprüft.\n\n" +
                    "Alte Roundcube-Kontakte jetzt löschen?",
                    "Alte Kontakte löschen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

            if (confirmation !=
                MessageBoxResult.Yes)
            {
                _deleteLegacyContactsStatusText.Text =
                    "Die Kontaktlöschung wurde nicht gestartet.";

                return;
            }

            /*
             * 4. Roundcube-Datensätze löschen.
             */
            _deleteLegacyContactsStatusText.Text =
                $"{sourceInventory.ContactCount:N0} alte Roundcube-Kontakte werden gelöscht …";

            var deletionResult =
                await _legacyRoundcubeContactDeletionService
                    .DeleteAllAsync(
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        sourceInventory.ContactCount,
                        cancellationSource.Token);

            if (!deletionResult.Success)
            {
                _deleteLegacyContactsStatusText.Text =
                    deletionResult.Message;

                return;
            }

            /*
             * 5. Unabhängige Quellverifikation über unseren
             *    bereits erprobten vCard-Exportweg.
             */
            _deleteLegacyContactsStatusText.Text =
                "Löschung durchgeführt. Alter Roundcube-Bestand wird unabhängig erneut geprüft …";

            var sourceAfterDeletion =
                await _legacyRoundcubeContactService
                    .GetInventoryAsync(
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        cancellationSource.Token);

            if (!sourceAfterDeletion.Success)
            {
                _deleteLegacyContactsStatusText.Text =
                    "Die Löschung wurde durchgeführt, der abschließende Roundcube-vCard-Export konnte jedoch nicht verifiziert werden.\n" +
                    sourceAfterDeletion.Message;

                MessageBox.Show(
                    this,
                    _deleteLegacyContactsStatusText.Text,
                    "Kontaktlöschung — Prüfung erforderlich",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (sourceAfterDeletion.ContactCount !=
                0)
            {
                _deleteLegacyContactsStatusText.Text =
                    $"Die Löschung ist noch nicht vollständig: Im alten Roundcube werden weiterhin {sourceAfterDeletion.ContactCount:N0} Kontakte gefunden.";

                MessageBox.Show(
                    this,
                    _deleteLegacyContactsStatusText.Text,
                    "Kontaktlöschung unvollständig",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            /*
             * 6. Ziel NACH der Quelllöschung nochmals gegen
             *    den unmittelbar vor der Löschung gesicherten
             *    Quellbestand prüfen.
             */
            _deleteLegacyContactsStatusText.Text =
                "Alter Kontaktbestand ist leer. Zielbestand wird abschließend nochmals geprüft …";

            await _targetContactsViewModel
                .ReloadAsync(
                    cancellationSource.Token);

            var targetContactsAfterDeletion =
                _targetContactsViewModel
                    .VisibleContacts
                    .ToArray();

            var finalVerificationPlan =
                _contactMigrationService
                    .CreatePlan(
                        sourceInventory.Contacts,
                        targetContactsAfterDeletion);

            if (finalVerificationPlan
                    .ContactsToImportCount >
                0)
            {
                var missingCount =
                    finalVerificationPlan
                        .ContactsToImportCount;

                _deleteLegacyContactsStatusText.Text =
                    missingCount == 1
                        ? "Der alte Roundcube-Bestand ist leer, aber 1 zuvor bestätigter Kontakt fehlt bei der abschließenden Zielprüfung."
                        : $"Der alte Roundcube-Bestand ist leer, aber {missingCount:N0} zuvor bestätigte Kontakte fehlen bei der abschließenden Zielprüfung.";

                MessageBox.Show(
                    this,
                    _deleteLegacyContactsStatusText.Text,
                    "Kontaktmigration — Prüfung erforderlich",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            /*
             * 7. Vollständig bestätigter Endzustand.
             */
            _legacyContactsDeleted =
                true;

            _deleteLegacyContactsButton.Content =
                "Alte Kontakte vollständig gelöscht";

            _deleteLegacyContactsStatusText.Text =
                $"✓ {sourceInventory.ContactCount:N0} alte Kontakte im neuen CardDAV bestätigt\n" +
                $"✓ {deletionResult.DeletedContactCount:N0} Roundcube-Kontakte gelöscht\n" +
                "✓ Roundcube anschließend mit 0 Kontakten bestätigt\n" +
                $"✓ Zielbestand abschließend erneut bestätigt ({targetContactsAfterDeletion.Length:N0} Kontakte)";

            MigrationNoticeText.Text =
                "Die alten Roundcube-Kontakte wurden erst nach vollständiger CardDAV-Verifikation gelöscht. " +
                "Der leere Roundcube-Bestand und der Zielbestand wurden anschließend erneut bestätigt.";

            InventorySummaryText.Text +=
                "\n\nKontaktbereinigung abgeschlossen: " +
                $"{sourceInventory.ContactCount:N0} alte Kontakte gelöscht, " +
                "Roundcube-Endbestand 0 Kontakte.";

            MessageBox.Show(
                this,
                "Die Kontaktmigration ist vollständig abgeschlossen.\n\n" +
                $"{sourceInventory.ContactCount:N0} alte Roundcube-Kontakte wurden im neuen CardDAV bestätigt und anschließend aus dem alten Roundcube gelöscht.\n\n" +
                "Endzustand alter Kontaktbestand: 0.",
                "Kontaktmigration abgeschlossen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            _deleteLegacyContactsStatusText.Text =
                "Die Kontaktbereinigung wurde abgebrochen. Bitte den Bestand erneut analysieren, bevor ein weiterer Löschversuch erfolgt.";
        }
        catch (Exception exception)
        {
            _deleteLegacyContactsStatusText.Text =
                "Die Kontaktbereinigung konnte nicht abgeschlossen werden.\n" +
                exception.Message;
        }
        finally
        {
            EndOperation(
                cancellationSource);

            SetOperationRunning(
                false);

            if (_deleteLegacyContactsButton is not null)
            {
                _deleteLegacyContactsButton.IsEnabled =
                    !_legacyContactsDeleted;
            }
        }
    }
}