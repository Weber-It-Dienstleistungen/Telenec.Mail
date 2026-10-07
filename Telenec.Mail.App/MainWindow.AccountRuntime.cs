using System.Windows;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private async Task<bool> PauseAccountRuntimeAsync(
        bool resetVisualState = true)
    {
        if (_searchIsRunning ||
            _searchResultIsLoading ||
            _searchAttachmentDownloadRunning ||
            _searchMutationIsRunning ||
            _searchDragInProgress)
        {
            MessageBox.Show(
                this,
                "Das E-Mail-Konto kann momentan nicht gewechselt werden.\n\n" +
                "Bitte warten Sie, bis die aktuelle Aktion abgeschlossen ist.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return false;
        }

        StopAutomaticSynchronization();

        var synchronizationTask =
            _automaticSynchronizationTask;

        if (synchronizationTask is not null)
        {
            try
            {
                await synchronizationTask;
            }
            catch
            {
            }
        }

        var desktopNotificationTask =
            _desktopNotificationLoopTask;

        if (_desktopNotificationInfrastructureInitialized)
        {
            MainWindowDesktopNotifications_OnClosed(
                this,
                EventArgs.Empty);
        }

        if (desktopNotificationTask is not null)
        {
            try
            {
                await desktopNotificationTask;
            }
            catch
            {
            }
        }

        if (resetVisualState)
        {
            ResetAccountScopedVisualState();
        }

        return true;
    }

    private void ResetAccountScopedVisualState()
    {
        if (_searchUiInitialized)
        {
            ClearSearchView(
                clearText:
                    true,
                resetScope:
                    true);
        }

        _rememberedExternalImageMessageKeys.Clear();

        _externalImagePermissionLookupsInProgress.Clear();

        _allowExternalImagesForCurrentMessage =
            false;

        _renderVersion++;

        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        ShowPlainTextView();

        if (TaskbarItemInfo is not null)
        {
            TaskbarItemInfo.Overlay =
                null;

            TaskbarItemInfo.Description =
                "Telenec Mail";
        }

        ShowMailboxQuotaLoadingState();
    }

    private async Task ResumeAccountRuntimeAsync()
    {
        /*
         * Der wesentliche Postfachinhalt ist zu diesem
         * Zeitpunkt bereits geladen.
         *
         * Alles Weitere darf parallel nachziehen.
         */
        RebindTaskbarInboxFolder();

        /*
         * Die normale Synchronisierung kann sofort wieder
         * bereitstehen. Der erste automatische Durchlauf
         * erfolgt ohnehin erst nach dem bekannten Intervall.
         */
        StartAutomaticSynchronization();

        /*
         * Lesebereich, Quota sowie Notification-/Regel-
         * Infrastruktur sind voneinander unabhängig.
         *
         * Früher wurden diese Arbeiten nacheinander erledigt.
         * Jetzt laufen sie parallel.
         */
        var renderTask =
            RenderSelectedMessageAsync();

        var quotaTask =
            RefreshMailboxQuotaAsync();

        var notificationTask =
            InitializeDesktopNotificationsAsync();

        await Task.WhenAll(
            renderTask,
            quotaTask,
            notificationTask);
    }
}