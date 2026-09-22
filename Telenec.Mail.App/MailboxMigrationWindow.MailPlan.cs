using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Migration;

namespace Telenec.Mail.App;

public partial class MailboxMigrationWindow
{
    private readonly LegacyMailMigrationPlanningService
        _mailMigrationPlanningService =
            new();

    private LegacyMailMigrationPlan?
        _mailMigrationPlan;

    internal IReadOnlyList<MailFolderData>
        TargetMailFolders
    {
        get;
        set;
    } =
        Array.Empty<MailFolderData>();

    private async void BuildMigrationPlanButton_OnClick(
        object sender,
        System.Windows.RoutedEventArgs e)
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

        BuildMigrationPlanButton.IsEnabled =
            false;

        SetOperationRunning(
            true);

        var cancellationSource =
            BeginOperation();

        try
        {
            MigrationPlanStatusText.Text =
                "Quellbestand wird für den Migrationsplan erneut geprüft …";

            var sourceInventory =
                await _legacyMailboxConnectionService
                    .GetInventoryAsync(
                        credentials.Value.UserName,
                        credentials.Value.Password,
                        cancellationSource.Token);

            if (!sourceInventory.Success)
            {
                MigrationPlanStatusText.Text =
                    sourceInventory.Message;

                return;
            }

            cancellationSource.Token
                .ThrowIfCancellationRequested();

            var plan =
                _mailMigrationPlanningService
                    .CreatePlan(
                        sourceInventory,
                        TargetMailFolders);

            _mailMigrationPlan =
                plan;

            MigrationPlanList.ItemsSource =
                plan.Folders;

            var readyText =
                plan.IsReady
                    ? "Der Plan ist vollständig und technisch migrationsfähig."
                    : "Der Plan enthält mindestens eine Zuordnung, die vor der Migration geklärt werden muss.";

            MigrationPlanSummaryText.Text =
                $"{plan.SourceFolderCount:N0} Quellordner im produktiven Migrationsplan.\n" +
                $"{plan.TotalMessageCount:N0} Nachrichten werden berücksichtigt.\n" +
                $"{plan.NewTargetFolderCount:N0} neue Zielordner müssen angelegt werden.\n" +
                $"{plan.MergeTargetCount:N0} Zielordner führen mehrere historische Altordner zusammen.\n" +
                $"{plan.ExcludedFolderCount:N0} Proof-of-Work-Testordner wurde aus der Produktivmigration ausgeschlossen.\n\n" +
                readyText;

            if (plan.BlockedMessageCount > 0)
            {
                MigrationPlanSummaryText.Text +=
                    $"\n{plan.BlockedMessageCount:N0} Nachrichten sind derzeit noch blockiert.";
            }

            MigrationPlanStatusText.Text =
                plan.IsReady
                    ? "Noch wurde keine Nachricht kopiert oder gelöscht. Dies ist ausschließlich der verbindliche Migrationsplan."
                    : "Noch wurde keine Nachricht kopiert oder gelöscht. Bitte zuerst die blockierten Zielzuordnungen klären.";

            InventoryStepPanel.Visibility =
                System.Windows.Visibility.Collapsed;

            MigrationPlanStepPanel.Visibility =
                System.Windows.Visibility.Visible;

            StepStatusText.Text =
                "Schritt 3 von 4 · Migrationsplan";

            ConfigureProductionMigrationButton();
        }
        catch (OperationCanceledException)
        {
            MigrationPlanStatusText.Text =
                "Die Erstellung des Migrationsplans wurde abgebrochen.";
        }
        catch (Exception exception)
        {
            MigrationPlanStatusText.Text =
                "Der Migrationsplan konnte nicht erstellt werden.\n" +
                exception.Message;
        }
        finally
        {
            EndOperation(
                cancellationSource);

            SetOperationRunning(
                false);

            BuildMigrationPlanButton.IsEnabled =
                true;

            ConfigureProductionMigrationButton();
        }
    }

    private void BackToInventoryButton_OnClick(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        MigrationPlanStepPanel.Visibility =
            System.Windows.Visibility.Collapsed;

        InventoryStepPanel.Visibility =
            System.Windows.Visibility.Visible;

        StepStatusText.Text =
            "Schritt 2 von 4 · Bestandsaufnahme";
    }
}