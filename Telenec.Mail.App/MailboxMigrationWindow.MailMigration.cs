using System.Windows;
using Telenec.Mail.App.Services.Migration;

namespace Telenec.Mail.App;

public partial class MailboxMigrationWindow
{
    internal LegacyMailboxMigrationService?
        MailMigrationService
    {
        get;
        set;
    }

    private void ConfigureProductionMigrationButton()
    {
        if (StartMailMigrationButton is null)
        {
            return;
        }

        var ready =
            _mailMigrationPlan is
            {
                IsReady: true
            } &&
            ContactsReadyForMailMigration() &&
            MailMigrationService is not null;

        StartMailMigrationButton.IsEnabled =
            ready &&
            !_isOperationRunning;
    }

    private async void StartMailMigrationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isOperationRunning ||
            _mailMigrationPlan is null ||
            !_mailMigrationPlan.IsReady ||
            MailMigrationService is null ||
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

        var plan =
            _mailMigrationPlan;

        var confirmation =
            MessageBox.Show(
                this,
                $"Es werden bis zu {plan.PlannedMessageCount:N0} Nachrichten migriert.\n\n" +
                "Jede einzelne Mail wird:\n" +
                "• roh vom alten Server gelesen\n" +
                "• byteidentisch auf den neuen Server übertragen\n" +
                "• anhand von Rohdaten, SHA-256, Flags und Empfangsdatum verifiziert\n" +
                "• erst danach selektiv vom alten Server gelöscht\n" +
                "• nach der Löschung auf dem Ziel erneut geprüft\n\n" +
                "Beim ersten nicht eindeutig verifizierbaren Fall stoppt die Migration automatisch.\n\n" +
                "Dieser Vorgang löscht erfolgreich verifizierte Nachrichten dauerhaft vom alten Server.\n\n" +
                "Migration jetzt starten?",
                "E-Mail-Migration starten",
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

        StartMailMigrationButton.IsEnabled =
            false;

        MigrationPlanStepPanel.Visibility =
            Visibility.Collapsed;

        MigrationRunStepPanel.Visibility =
            Visibility.Visible;

        StepStatusText.Text =
            "Schritt 4 von 4 · Migration";

        MigrationProgressBar.Minimum =
            0;

        MigrationProgressBar.Maximum =
            100;

        MigrationProgressBar.Value =
            0;

        MigrationProgressSummaryText.Text =
            $"0 von {plan.PlannedMessageCount:N0} Nachrichten verarbeitet.";

        MigrationCurrentFolderText.Text =
            "Verbindungen werden aufgebaut …";

        MigrationCurrentMessageText.Text =
            string.Empty;

        MigrationOperationStatusText.Text =
            "Noch wurde in diesem Lauf keine Nachricht gelöscht.";

        var cancellationSource =
            BeginOperation();

        var progress =
            new Progress<
                LegacyMailboxMigrationProgress>(
                migrationProgress =>
                {
                    MigrationProgressBar.Value =
                        migrationProgress.Percentage;

                    MigrationProgressSummaryText.Text =
                        $"{migrationProgress.ProcessedMessageCount:N0} von " +
                        $"{migrationProgress.TotalMessageCount:N0} Nachrichten verarbeitet " +
                        $"({migrationProgress.Percentage:0.0} %).";

                    MigrationCurrentFolderText.Text =
                        $"{migrationProgress.CurrentSourceFolder}  →  " +
                        $"{migrationProgress.CurrentTargetFolder}";

                    MigrationCurrentMessageText.Text =
                        string.IsNullOrWhiteSpace(
                            migrationProgress.CurrentSubject)
                            ? string.Empty
                            : migrationProgress.CurrentSubject;

                    MigrationOperationStatusText.Text =
                        migrationProgress.StatusText;
                });

        try
        {
            var result =
                await MailMigrationService
                    .RunAsync(
                        plan,
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        progress,
                        cancellationSource.Token);

            if (!result.Success)
            {
                MigrationOperationStatusText.Text =
                    result.Message;

                MigrationResultText.Text =
                    $"Bereits vollständig verarbeitet: " +
                    $"{result.MigratedMessageCount + result.AlreadyPresentMessageCount:N0}\n" +
                    $"Neu übertragen: {result.MigratedMessageCount:N0}\n" +
                    $"Bereits byteidentisch vorhanden: {result.AlreadyPresentMessageCount:N0}";

                if (!string.IsNullOrWhiteSpace(
                        result.FailedFolder))
                {
                    MigrationResultText.Text +=
                        $"\nFehlerordner: {result.FailedFolder}";
                }

                if (!string.IsNullOrWhiteSpace(
                        result.FailedSubject))
                {
                    MigrationResultText.Text +=
                        $"\nBetreff: {result.FailedSubject}";
                }

                if (result.SourceDeletedBeforeFailure)
                {
                    MessageBox.Show(
                        this,
                        result.Message +
                        "\n\n" +
                        "Bei diesem Fehler wurde die betreffende Quellmail bereits gelöscht. " +
                        "Bitte den Zielbestand prüfen, bevor die Migration fortgesetzt wird.",
                        "Migration gestoppt — Prüfung erforderlich",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                MigrationBackButton.IsEnabled =
                    true;

                return;
            }

            MigrationProgressBar.Value =
                100;

            MigrationProgressSummaryText.Text =
                $"{plan.PlannedMessageCount:N0} von {plan.PlannedMessageCount:N0} Nachrichten verarbeitet (100 %).";

            MigrationCurrentFolderText.Text =
                "Migration abgeschlossen";

            MigrationCurrentMessageText.Text =
                string.Empty;

            MigrationOperationStatusText.Text =
                result.Message;

            MigrationResultText.Text =
                $"Neu übertragen: {result.MigratedMessageCount:N0}\n" +
                $"Bereits byteidentisch vorhanden: {result.AlreadyPresentMessageCount:N0}\n" +
                "Verbleibende Nachrichten in den geplanten Quellordnern: 0";

            MigrationBackButton.IsEnabled =
                false;

            StepStatusText.Text =
                "Schritt 4 von 4 · Migration abgeschlossen";

            MessageBox.Show(
                this,
                "Die E-Mail-Migration wurde vollständig abgeschlossen.\n\n" +
                $"{result.MigratedMessageCount:N0} Nachrichten wurden neu übertragen.\n" +
                $"{result.AlreadyPresentMessageCount:N0} bereits vorhandene byteidentische Nachrichten wurden bestätigt.\n\n" +
                "In den geplanten Ordnern des alten Servers befinden sich keine Nachrichten mehr.",
                "E-Mail-Migration abgeschlossen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            MigrationOperationStatusText.Text =
                "Die Migration wurde abgebrochen. " +
                "Bereits vollständig verifizierte und gelöschte Nachrichten bleiben migriert. " +
                "Beim nächsten Lauf werden vorhandene Zielkopien erkannt.";

            MigrationBackButton.IsEnabled =
                true;
        }
        catch (Exception exception)
        {
            MigrationOperationStatusText.Text =
                "Die Migration wurde gestoppt.\n" +
                exception.Message;

            MigrationBackButton.IsEnabled =
                true;
        }
        finally
        {
            EndOperation(
                cancellationSource);

            SetOperationRunning(
                false);
        }
    }

    private void MigrationBackButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        MigrationRunStepPanel.Visibility =
            Visibility.Collapsed;

        InventoryStepPanel.Visibility =
            Visibility.Visible;

        /*
         * Nach einem abgebrochenen oder teilweise
         * erfolgreichen Lauf muss zwingend erneut
         * inventarisiert und geplant werden.
         */
        _mailMigrationPlan =
            null;

        StepStatusText.Text =
            "Schritt 2 von 4 · Bestandsaufnahme";

        BuildMigrationPlanButton.IsEnabled =
            true;
    }
}