using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const int
        DesktopNotificationPollingIntervalSeconds =
            30;

    private const int
        DesktopNotificationDisplayMilliseconds =
            10000;

    private const int
        DesktopNotificationTemporaryTrayLifetimeMilliseconds =
            12000;

    private const int
        InAppNotificationDisplayMilliseconds =
            7000;

    /*
     * Zusätzliche Shell_NotifyIcon-Konstanten.
     *
     * Der grundlegende Tray-Unterbau selbst bleibt in
     * MainWindow.Tray.cs unverändert.
     */
    private const uint
        NotifyIconModifyForNotification =
            0x00000001;

    private const uint
        NotifyIconInfoFlagForNotification =
            0x00000010;

    private const uint
        NotifyInfoInformationFlag =
            0x00000001;

    /*
     * NIN_BALLOONUSERCLICK = WM_USER + 5
     */
    private const int
        NotificationBalloonUserClick =
            0x0405;

    private static readonly bool
        DesktopNotificationClassHandlerRegistered =
            RegisterDesktopNotificationClassHandler();

    private bool
        _desktopNotificationInfrastructureInitialized;

    private bool
        _desktopNotificationsEnabled;

    private bool
        _desktopNotificationTrayHookAttached;

    private bool
        _desktopNotificationOwnsTemporaryTrayIcon;

    private Guid?
        _desktopNotificationAccountId;

    private ISettingsStore?
        _desktopNotificationSettingsStore;

    private InboxNotificationMonitor?
        _inboxNotificationMonitor;

    private CancellationTokenSource?
        _desktopNotificationLoopCancellationSource;

    private Task?
        _desktopNotificationLoopTask;

    private CancellationTokenSource?
        _desktopNotificationTrayCleanupCancellationSource;

    private Border?
        _inAppNotificationBanner;

    private CancellationTokenSource?
        _inAppNotificationCancellationSource;

    private static bool
        RegisterDesktopNotificationClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnDesktopNotificationLoaded));

        return true;
    }

    private static void MainWindow_OnDesktopNotificationLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MainWindow window ||
            !ReferenceEquals(
                e.OriginalSource,
                window))
        {
            return;
        }

        _ =
            window
                .InitializeDesktopNotificationsAsync();
    }

    private async Task
        InitializeDesktopNotificationsAsync()
    {
        if (_desktopNotificationInfrastructureInitialized)
        {
            return;
        }

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is null)
            {
                return;
            }

            var settingsStore =
                _serviceProvider
                    .GetRequiredService<
                        ISettingsStore>();

            var stateSource =
                _serviceProvider
                    .GetRequiredService<
                        IMailMessageStateSource>();

            var mailDataSource =
                _serviceProvider
                    .GetRequiredService<
                        IMailDataSource>();

            _desktopNotificationAccountId =
                account.AccountId;

            _desktopNotificationSettingsStore =
                settingsStore;

            _inboxNotificationMonitor =
                new InboxNotificationMonitor(
                    stateSource,
                    mailDataSource);

            Closed +=
                MainWindowDesktopNotifications_OnClosed;

            _desktopNotificationInfrastructureInitialized =
                true;

            await RefreshDesktopNotificationStateAsync();

            StartDesktopNotificationLoop();
        }
        catch
        {
            /*
             * Benachrichtigungen sind eine Komfortfunktion.
             *
             * Ein Fehler darf den normalen Mailclient niemals
             * beeinträchtigen.
             */
            _desktopNotificationInfrastructureInitialized =
                false;

            _desktopNotificationsEnabled =
                false;
        }
    }

    private async Task
        RefreshDesktopNotificationStateAsync()
    {
        if (!_desktopNotificationInfrastructureInitialized ||
            _desktopNotificationSettingsStore is null ||
            !_desktopNotificationAccountId.HasValue ||
            _inboxNotificationMonitor is null)
        {
            return;
        }

        try
        {
            var storedValue =
                await _desktopNotificationSettingsStore
                    .GetAccountSettingAsync(
                        _desktopNotificationAccountId.Value,
                        SettingsKeys
                            .NotificationsDesktopEnabled);

            /*
             * Wie im Einstellungsfenster:
             * Fehlt ein gespeicherter Wert, sind
             * Desktop-Benachrichtigungen standardmäßig an.
             */
            var enabled =
                string.IsNullOrWhiteSpace(
                    storedValue)
                ||
                (
                    bool.TryParse(
                        storedValue,
                        out var parsedValue)
                    &&
                    parsedValue
                );

            var wasEnabled =
                _desktopNotificationsEnabled;

            _desktopNotificationsEnabled =
                enabled;

            /*
             * Wird die Funktion neu aktiviert, beginnen wir
             * mit dem JETZT vorhandenen Posteingang als
             * Baseline.
             *
             * Nachrichten, die während ausgeschalteter
             * Benachrichtigungen eingegangen sind, werden
             * dadurch nicht nachträglich gemeldet.
             */
            if (enabled &&
                !wasEnabled)
            {
                try
                {
                    await _inboxNotificationMonitor
                        .EstablishBaselineAsync();
                }
                catch
                {
                    _inboxNotificationMonitor
                        .Reset();
                }
            }

            if (!enabled)
            {
                _inboxNotificationMonitor
                    .Reset();

                CancelTemporaryNotificationTrayCleanup();

                RemoveInAppMailNotification();

                if (_desktopNotificationOwnsTemporaryTrayIcon &&
                    !_closeToTrayEnabled)
                {
                    _desktopNotificationOwnsTemporaryTrayIcon =
                        false;

                    RemoveTrayIcon();
                }
            }

            /*
             * Wurde der normale Tray-Modus zwischenzeitlich
             * aktiviert, gehört ein vorhandenes Icon ab jetzt
             * dem regulären Tray-Unterbau und darf nicht mehr
             * vom Notification-Cleanup entfernt werden.
             */
            if (_closeToTrayEnabled)
            {
                _desktopNotificationOwnsTemporaryTrayIcon =
                    false;

                CancelTemporaryNotificationTrayCleanup();
            }
        }
        catch
        {
            _desktopNotificationsEnabled =
                false;

            _inboxNotificationMonitor
                .Reset();

            RemoveInAppMailNotification();
        }
    }

    private void StartDesktopNotificationLoop()
    {
        if (_desktopNotificationLoopTask
            is { IsCompleted: false })
        {
            return;
        }

        var cancellationSource =
            new CancellationTokenSource();

        _desktopNotificationLoopCancellationSource =
            cancellationSource;

        _desktopNotificationLoopTask =
            RunDesktopNotificationLoopAsync(
                cancellationSource);
    }

    private async Task RunDesktopNotificationLoopAsync(
        CancellationTokenSource cancellationSource)
    {
        var cancellationToken =
            cancellationSource.Token;

        try
        {
            while (true)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        DesktopNotificationPollingIntervalSeconds),
                    cancellationToken);

                cancellationToken
                    .ThrowIfCancellationRequested();

                if (!_desktopNotificationsEnabled ||
                    _inboxNotificationMonitor is null)
                {
                    continue;
                }

                InboxNotificationBatch?
                    notificationBatch;

                try
                {
                    notificationBatch =
                        await _inboxNotificationMonitor
                            .CheckForNewMailAsync(
                                cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    /*
                     * Eine einzelne fehlgeschlagene Abfrage
                     * beendet den Monitor nicht.
                     */
                    continue;
                }

                if (notificationBatch is null ||
                    notificationBatch.NewMessageCount <= 0)
                {
                    continue;
                }

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        DispatchIncomingMailNotification(
                            notificationBatch);
                    });
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(
                    _desktopNotificationLoopCancellationSource,
                    cancellationSource))
            {
                _desktopNotificationLoopCancellationSource =
                    null;

                _desktopNotificationLoopTask =
                    null;
            }

            cancellationSource.Dispose();
        }
    }

    private void DispatchIncomingMailNotification(
        InboxNotificationBatch batch)
    {
        /*
         * Befindet sich Telenec Mail gerade sichtbar und
         * aktiv im Vordergrund, verwenden wir bewusst einen
         * eigenen In-App-Hinweis.
         *
         * Klassische Shell-Benachrichtigungen können von
         * Windows bei der aktiven Anwendung unterdrückt
         * werden.
         *
         * Ist Telenec Mail dagegen minimiert, verdeckt oder
         * im Tray, kommt der normale Windows-Hinweis.
         */
        if (IsVisible &&
            WindowState !=
                WindowState.Minimized &&
            IsActive)
        {
            ShowInAppMailNotification(
                batch);

            return;
        }

        ShowDesktopMailNotification(
            batch);
    }

    private void ShowInAppMailNotification(
        InboxNotificationBatch batch)
    {
        if (!_desktopNotificationsEnabled ||
            batch.NewMessageCount <= 0)
        {
            return;
        }

        if (Content is not Grid rootGrid)
        {
            /*
             * Sollte sich die Hauptfensterstruktur später
             * ändern, bleibt die Windows-Benachrichtigung als
             * funktionaler Fallback erhalten.
             */
            ShowDesktopMailNotification(
                batch);

            return;
        }

        RemoveInAppMailNotification();

        var title =
            new TextBlock
            {
                Text =
                    CreateDesktopNotificationTitle(
                        batch),

                FontSize =
                    13,

                FontWeight =
                    FontWeights.SemiBold,

                TextWrapping =
                    TextWrapping.Wrap
            };

        title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Primary");

        var body =
            new TextBlock
            {
                Text =
                    CreateDesktopNotificationBody(
                        batch),

                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),

                FontSize =
                    12,

                MaxWidth =
                    340,

                MaxHeight =
                    72,

                TextWrapping =
                    TextWrapping.Wrap
            };

        body.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        var content =
            new StackPanel();

        content.Children.Add(
            title);

        content.Children.Add(
            body);

        var banner =
            new Border
            {
                Width =
                    380,

                MaxWidth =
                    420,

                Margin =
                    new Thickness(
                        0,
                        18,
                        18,
                        0),

                Padding =
                    new Thickness(
                        16,
                        13,
                        16,
                        13),

                HorizontalAlignment =
                    HorizontalAlignment.Right,

                VerticalAlignment =
                    VerticalAlignment.Top,

                CornerRadius =
                    new CornerRadius(
                        8),

                BorderThickness =
                    new Thickness(
                        1),

                Child =
                    content,

                Cursor =
                    System.Windows.Input.Cursors.Hand,

                ToolTip =
                    "Klicken zum Schließen"
            };

        banner.SetResourceReference(
            Border.BackgroundProperty,
            "Surface.Card");

        banner.SetResourceReference(
            Border.BorderBrushProperty,
            "Brand.Primary");

        banner.MouseLeftButtonUp +=
            InAppNotificationBanner_OnMouseLeftButtonUp;

        Panel.SetZIndex(
            banner,
            10000);

        Grid.SetColumn(
            banner,
            0);

        Grid.SetColumnSpan(
            banner,
            Math.Max(
                1,
                rootGrid.ColumnDefinitions.Count));

        rootGrid.Children.Add(
            banner);

        _inAppNotificationBanner =
            banner;

        var cancellationSource =
            new CancellationTokenSource();

        _inAppNotificationCancellationSource =
            cancellationSource;

        _ =
            HideInAppMailNotificationAfterDelayAsync(
                banner,
                cancellationSource);
    }

    private void InAppNotificationBanner_OnMouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        RemoveInAppMailNotification();

        e.Handled =
            true;
    }

    private async Task
        HideInAppMailNotificationAfterDelayAsync(
            Border banner,
            CancellationTokenSource cancellationSource)
    {
        var cancellationToken =
            cancellationSource.Token;

        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    InAppNotificationDisplayMilliseconds),
                cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (!ReferenceEquals(
                            _inAppNotificationBanner,
                            banner))
                    {
                        return;
                    }

                    if (Content is Grid rootGrid &&
                        rootGrid.Children.Contains(
                            banner))
                    {
                        rootGrid.Children.Remove(
                            banner);
                    }

                    banner.MouseLeftButtonUp -=
                        InAppNotificationBanner_OnMouseLeftButtonUp;

                    _inAppNotificationBanner =
                        null;
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(
                    _inAppNotificationCancellationSource,
                    cancellationSource))
            {
                _inAppNotificationCancellationSource =
                    null;
            }

            cancellationSource.Dispose();
        }
    }

    private void RemoveInAppMailNotification()
    {
        var cancellationSource =
            _inAppNotificationCancellationSource;

        _inAppNotificationCancellationSource =
            null;

        if (cancellationSource is not null)
        {
            try
            {
                cancellationSource.Cancel();
            }
            catch
            {
            }
        }

        var banner =
            _inAppNotificationBanner;

        _inAppNotificationBanner =
            null;

        if (banner is null)
        {
            return;
        }

        banner.MouseLeftButtonUp -=
            InAppNotificationBanner_OnMouseLeftButtonUp;

        if (Content is Grid rootGrid &&
            rootGrid.Children.Contains(
                banner))
        {
            rootGrid.Children.Remove(
                banner);
        }
    }

    private void ShowDesktopMailNotification(
        InboxNotificationBatch batch)
    {
        if (!_desktopNotificationsEnabled ||
            batch.NewMessageCount <= 0)
        {
            return;
        }

        /*
         * Shell-Benachrichtigungen benötigen ein angemeldetes
         * Notification-Area-Icon.
         *
         * Ist der normale Tray-Modus deaktiviert, wird deshalb
         * vorübergehend dasselbe Telenec-Icon angelegt.
         */
        var trayIconWasAlreadyVisible =
            _trayIconVisible;

        EnsureTrayIcon();

        if (!_trayIconVisible)
        {
            return;
        }

        AttachDesktopNotificationTrayHook();

        if (!trayIconWasAlreadyVisible &&
            !_closeToTrayEnabled)
        {
            _desktopNotificationOwnsTemporaryTrayIcon =
                true;
        }

        var windowHandle =
            new WindowInteropHelper(
                this)
                .Handle;

        if (windowHandle ==
            IntPtr.Zero)
        {
            CleanupTemporaryNotificationTrayIcon();

            return;
        }

        var notificationTitle =
            CreateDesktopNotificationTitle(
                batch);

        var notificationBody =
            CreateDesktopNotificationBody(
                batch);

        var notifyIconData =
            CreateNotifyIconData(
                windowHandle);

        notifyIconData.Flags |=
            NotifyIconInfoFlagForNotification;

        notifyIconData.InfoTitle =
            TruncateDesktopNotificationText(
                notificationTitle,
                63);

        notifyIconData.Info =
            TruncateDesktopNotificationText(
                notificationBody,
                255);

        notifyIconData.TimeoutOrVersion =
            DesktopNotificationDisplayMilliseconds;

        notifyIconData.InfoFlags =
            NotifyInfoInformationFlag;

        var notificationShown =
            ShellNotifyIcon(
                NotifyIconModifyForNotification,
                ref notifyIconData);

        if (!notificationShown)
        {
            CleanupTemporaryNotificationTrayIcon();

            return;
        }

        if (_desktopNotificationOwnsTemporaryTrayIcon &&
            !_closeToTrayEnabled)
        {
            ScheduleTemporaryNotificationTrayCleanup();
        }
    }

    private void AttachDesktopNotificationTrayHook()
    {
        if (_desktopNotificationTrayHookAttached ||
            _trayHwndSource is null)
        {
            return;
        }

        _trayHwndSource.AddHook(
            MainWindowDesktopNotification_WindowProc);

        _desktopNotificationTrayHookAttached =
            true;
    }

    private IntPtr
        MainWindowDesktopNotification_WindowProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
    {
        if (message !=
            TrayCallbackMessage)
        {
            return IntPtr.Zero;
        }

        var trayEvent =
            unchecked(
                (int)lParam.ToInt64());

        if (trayEvent !=
            NotificationBalloonUserClick)
        {
            return IntPtr.Zero;
        }

        RestoreMainWindowFromTray();

        /*
         * War das Icon ausschließlich für diese
         * Benachrichtigung angelegt worden, darf es nach dem
         * Klick wieder verschwinden.
         */
        if (_desktopNotificationOwnsTemporaryTrayIcon &&
            !_closeToTrayEnabled)
        {
            CancelTemporaryNotificationTrayCleanup();

            _desktopNotificationOwnsTemporaryTrayIcon =
                false;

            RemoveTrayIcon();
        }

        handled =
            true;

        return IntPtr.Zero;
    }

    private void ScheduleTemporaryNotificationTrayCleanup()
    {
        CancelTemporaryNotificationTrayCleanup();

        var cancellationSource =
            new CancellationTokenSource();

        _desktopNotificationTrayCleanupCancellationSource =
            cancellationSource;

        _ =
            CleanupTemporaryNotificationTrayIconAsync(
                cancellationSource);
    }

    private async Task
        CleanupTemporaryNotificationTrayIconAsync(
            CancellationTokenSource cancellationSource)
    {
        var cancellationToken =
            cancellationSource.Token;

        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    DesktopNotificationTemporaryTrayLifetimeMilliseconds),
                cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (!ReferenceEquals(
                            _desktopNotificationTrayCleanupCancellationSource,
                            cancellationSource))
                    {
                        return;
                    }

                    CleanupTemporaryNotificationTrayIcon();
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(
                    _desktopNotificationTrayCleanupCancellationSource,
                    cancellationSource))
            {
                _desktopNotificationTrayCleanupCancellationSource =
                    null;
            }

            cancellationSource.Dispose();
        }
    }

    private void CleanupTemporaryNotificationTrayIcon()
    {
        if (!_desktopNotificationOwnsTemporaryTrayIcon ||
            _closeToTrayEnabled)
        {
            return;
        }

        _desktopNotificationOwnsTemporaryTrayIcon =
            false;

        RemoveTrayIcon();
    }

    private void CancelTemporaryNotificationTrayCleanup()
    {
        var cancellationSource =
            _desktopNotificationTrayCleanupCancellationSource;

        _desktopNotificationTrayCleanupCancellationSource =
            null;

        if (cancellationSource is null)
        {
            return;
        }

        try
        {
            cancellationSource.Cancel();
        }
        catch
        {
        }
    }

    private static string
        CreateDesktopNotificationTitle(
            InboxNotificationBatch batch)
    {
        if (batch.NewMessageCount > 1)
        {
            return
                $"{batch.NewMessageCount} neue E-Mails";
        }

        var sender =
            GetNotificationSender(
                batch.LatestMessage);

        return string.IsNullOrWhiteSpace(
                sender)
            ? "Neue E-Mail"
            : $"Neue E-Mail von {sender}";
    }

    private static string
        CreateDesktopNotificationBody(
            InboxNotificationBatch batch)
    {
        var message =
            batch.LatestMessage;

        if (message is null)
        {
            return batch.NewMessageCount == 1
                ? "Eine neue Nachricht ist im Posteingang eingegangen."
                : "Neue Nachrichten sind im Posteingang eingegangen.";
        }

        var subject =
            NormalizeDesktopNotificationText(
                message.Subject);

        if (string.IsNullOrWhiteSpace(
                subject))
        {
            subject =
                "(Kein Betreff)";
        }

        if (batch.NewMessageCount > 1)
        {
            var sender =
                GetNotificationSender(
                    message);

            if (string.IsNullOrWhiteSpace(
                    sender))
            {
                return subject;
            }

            return
                $"{sender}: {subject}";
        }

        var preview =
            NormalizeDesktopNotificationText(
                message.Preview);

        if (string.IsNullOrWhiteSpace(
                preview))
        {
            return subject;
        }

        return
            subject +
            Environment.NewLine +
            preview;
    }

    private static string GetNotificationSender(
        MailMessageData? message)
    {
        if (message is null)
        {
            return string.Empty;
        }

        var sender =
            NormalizeDesktopNotificationText(
                message.Sender);

        if (!string.IsNullOrWhiteSpace(
                sender))
        {
            return sender;
        }

        return NormalizeDesktopNotificationText(
            message.SenderAddress);
    }

    private static string
        NormalizeDesktopNotificationText(
            string? text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            text.Split(
                new[]
                {
                    ' ',
                    '\r',
                    '\n',
                    '\t'
                },
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static string
        TruncateDesktopNotificationText(
            string text,
            int maximumLength)
    {
        if (string.IsNullOrEmpty(
                text) ||
            text.Length <=
                maximumLength)
        {
            return text;
        }

        if (maximumLength <= 1)
        {
            return text[
                ..maximumLength];
        }

        return
            text[
                ..(maximumLength - 1)] +
            "…";
    }

    private void MainWindowDesktopNotifications_OnClosed(
        object? sender,
        EventArgs e)
    {
        Closed -=
            MainWindowDesktopNotifications_OnClosed;

        RemoveInAppMailNotification();

        var loopCancellationSource =
            _desktopNotificationLoopCancellationSource;

        _desktopNotificationLoopCancellationSource =
            null;

        try
        {
            loopCancellationSource?
                .Cancel();
        }
        catch
        {
        }

        CancelTemporaryNotificationTrayCleanup();

        if (_desktopNotificationTrayHookAttached &&
            _trayHwndSource is not null)
        {
            _trayHwndSource.RemoveHook(
                MainWindowDesktopNotification_WindowProc);
        }

        _desktopNotificationTrayHookAttached =
            false;

        _desktopNotificationOwnsTemporaryTrayIcon =
            false;

        _desktopNotificationSettingsStore =
            null;

        _inboxNotificationMonitor =
            null;

        _desktopNotificationAccountId =
            null;

        _desktopNotificationsEnabled =
            false;

        _desktopNotificationInfrastructureInitialized =
            false;
    }
}