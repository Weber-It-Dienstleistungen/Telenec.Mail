using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const uint TrayIconId =
        1;

    private const int TrayCallbackMessage =
        0x8001;

    private const int WmLeftButtonUp =
        0x0202;

    private const int WmRightButtonUp =
        0x0205;

    private const int WmNull =
        0x0000;

    private const uint NotifyIconAdd =
        0x00000000;

    private const uint NotifyIconDelete =
        0x00000002;

    private const uint NotifyIconMessageFlag =
        0x00000001;

    private const uint NotifyIconIconFlag =
        0x00000002;

    private const uint NotifyIconTipFlag =
        0x00000004;

    private const uint MenuString =
        0x00000000;

    private const uint MenuSeparator =
        0x00000800;

    private const uint TrackPopupReturnCommand =
        0x00000100;

    private const uint TrackPopupRightButton =
        0x00000002;

    private const uint TrayMenuOpenCommand =
        1;

    private const uint TrayMenuExitCommand =
        2;

    private bool
        _trayInfrastructureInitialized;

    private bool
        _trayIconVisible;

    private bool
        _trayExitRequested;

    private bool
        _closeToTrayEnabled;

    private int
        _taskbarCreatedMessage;

    private IntPtr
        _trayIconHandle;

    private HwndSource?
        _trayHwndSource;

    private ISettingsStore?
        _traySettingsStore;

    private async void MainWindowTray_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -=
            MainWindowTray_OnLoaded;

        if (_trayInfrastructureInitialized)
        {
            return;
        }

        try
        {
            _traySettingsStore =
                _serviceProvider
                    .GetRequiredService<
                        ISettingsStore>();

            var windowHandle =
                new WindowInteropHelper(
                    this)
                    .Handle;

            if (windowHandle ==
                IntPtr.Zero)
            {
                return;
            }

            _trayHwndSource =
                HwndSource.FromHwnd(
                    windowHandle);

            if (_trayHwndSource is null)
            {
                return;
            }

            _trayHwndSource.AddHook(
                MainWindowTray_WindowProc);

            _taskbarCreatedMessage =
                RegisterWindowMessage(
                    "TaskbarCreated");

            Closing +=
                MainWindowTray_OnClosing;

            Closed +=
                MainWindowTray_OnClosed;

            _trayInfrastructureInitialized =
                true;

            await RefreshTrayStateAsync();
        }
        catch
        {
            /*
             * Ein Fehler beim Tray-Unterbau darf niemals
             * verhindern, dass Telenec Mail normal benutzt
             * und beendet werden kann.
             */
            _trayInfrastructureInitialized =
                false;

            _closeToTrayEnabled =
                false;

            RemoveTrayIcon();
        }
    }

    private async Task RefreshTrayStateAsync()
    {
        if (!_trayInfrastructureInitialized ||
            _traySettingsStore is null)
        {
            return;
        }

        try
        {
            var storedValue =
                await _traySettingsStore
                    .GetApplicationSettingAsync(
                        SettingsKeys
                            .ApplicationCloseToTrayEnabled);

            _closeToTrayEnabled =
                bool.TryParse(
                    storedValue,
                    out var enabled) &&
                enabled;

            if (_closeToTrayEnabled)
            {
                EnsureTrayIcon();
            }
            else
            {
                RemoveTrayIcon();
            }
        }
        catch
        {
            /*
             * Können die Einstellungen nicht gelesen werden,
             * verwenden wir aus Sicherheitsgründen das
             * bisherige Standardverhalten:
             *
             * X beendet die Anwendung vollständig.
             */
            _closeToTrayEnabled =
                false;

            RemoveTrayIcon();
        }
    }

    private void MainWindowTray_OnClosing(
        object? sender,
        CancelEventArgs e)
    {
        /*
         * Explizites Beenden über das Tray und das bestehende
         * Abmelden müssen das Fenster wirklich schließen
         * dürfen.
         */
        if (_trayExitRequested ||
            _isLoggingOut ||
            !_trayInfrastructureInitialized ||
            !_closeToTrayEnabled)
        {
            return;
        }

        /*
         * Bevor wir das Fenster unsichtbar machen, stellen
         * wir sicher, dass tatsächlich ein funktionierendes
         * Tray-Icon vorhanden ist.
         *
         * Andernfalls würde der Benutzer ein unsichtbares
         * Programm ohne Rückweg erhalten.
         */
        EnsureTrayIcon();

        if (!_trayIconVisible)
        {
            /*
             * Tray konnte nicht initialisiert werden.
             *
             * Das normale Schließen wird deshalb NICHT
             * verhindert.
             */
            return;
        }

        /*
         * Ganz wichtig:
         *
         * Während WPF gerade das Closing-Ereignis verarbeitet,
         * dürfen Show(), Hide(), Close() und EnsureHandle()
         * nicht erneut in den Window-Lifecycle eingreifen.
         *
         * Wir brechen deshalb ausschließlich das Schließen ab.
         */
        e.Cancel =
            true;

        /*
         * Erst nachdem der aktuelle Closing-Durchlauf
         * vollständig beendet und von WPF abgebrochen wurde,
         * darf das Fenster versteckt werden.
         */
        Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    if (_trayExitRequested ||
                        _isLoggingOut ||
                        !_closeToTrayEnabled ||
                        !_trayIconVisible)
                    {
                        return;
                    }

                    HideMainWindowToTray();
                }));
    }

    private void EnsureTrayIcon()
    {
        if (_trayIconVisible)
        {
            return;
        }

        var windowHandle =
            new WindowInteropHelper(
                this)
                .Handle;

        if (windowHandle ==
            IntPtr.Zero)
        {
            return;
        }

        if (_trayIconHandle ==
            IntPtr.Zero)
        {
            _trayIconHandle =
                ExtractApplicationIcon();

            if (_trayIconHandle ==
                IntPtr.Zero)
            {
                return;
            }
        }

        var notifyIconData =
            CreateNotifyIconData(
                windowHandle);

        if (!ShellNotifyIcon(
                NotifyIconAdd,
                ref notifyIconData))
        {
            DestroyCurrentTrayIconHandle();

            return;
        }

        _trayIconVisible =
            true;
    }

    private NotifyIconData CreateNotifyIconData(
        IntPtr windowHandle)
    {
        return new NotifyIconData
        {
            Size =
                (uint)Marshal.SizeOf<
                    NotifyIconData>(),

            WindowHandle =
                windowHandle,

            Id =
                TrayIconId,

            Flags =
                NotifyIconMessageFlag |
                NotifyIconIconFlag |
                NotifyIconTipFlag,

            CallbackMessage =
                TrayCallbackMessage,

            IconHandle =
                _trayIconHandle,

            Tip =
                "Telenec Mail",

            State =
                0,

            StateMask =
                0,

            Info =
                string.Empty,

            TimeoutOrVersion =
                0,

            InfoTitle =
                string.Empty,

            InfoFlags =
                0,

            GuidItem =
                Guid.Empty,

            BalloonIconHandle =
                IntPtr.Zero
        };
    }

    private IntPtr ExtractApplicationIcon()
    {
        var executablePath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                executablePath))
        {
            return IntPtr.Zero;
        }

        var largeIcons =
            new IntPtr[1];

        var smallIcons =
            new IntPtr[1];

        var extractedCount =
            ExtractIconEx(
                executablePath,
                0,
                largeIcons,
                smallIcons,
                1);

        if (extractedCount == 0)
        {
            return IntPtr.Zero;
        }

        if (smallIcons[0] !=
            IntPtr.Zero)
        {
            if (largeIcons[0] !=
                IntPtr.Zero)
            {
                DestroyIcon(
                    largeIcons[0]);
            }

            return smallIcons[0];
        }

        return largeIcons[0];
    }

    private void RemoveTrayIcon()
    {
        if (_trayIconVisible)
        {
            var windowHandle =
                new WindowInteropHelper(
                    this)
                    .Handle;

            if (windowHandle !=
                IntPtr.Zero)
            {
                var notifyIconData =
                    CreateNotifyIconData(
                        windowHandle);

                ShellNotifyIcon(
                    NotifyIconDelete,
                    ref notifyIconData);
            }

            _trayIconVisible =
                false;
        }

        DestroyCurrentTrayIconHandle();
    }

    private void DestroyCurrentTrayIconHandle()
    {
        if (_trayIconHandle ==
            IntPtr.Zero)
        {
            return;
        }

        DestroyIcon(
            _trayIconHandle);

        _trayIconHandle =
            IntPtr.Zero;
    }

    private void HideMainWindowToTray()
    {
        /*
         * Dieser Aufruf findet bewusst NICHT innerhalb des
         * Closing-Ereignisses statt.
         */
        ShowInTaskbar =
            false;

        Hide();
    }

    private void RestoreMainWindowFromTray()
    {
        /*
         * Falls das Fenster bereits sichtbar ist, reicht es,
         * es nach vorne zu holen.
         */
        if (!IsVisible)
        {
            ShowInTaskbar =
                true;

            Show();
        }
        else
        {
            ShowInTaskbar =
                true;
        }

        if (WindowState ==
            WindowState.Minimized)
        {
            WindowState =
                WindowState.Normal;
        }

        Activate();

        var windowHandle =
            new WindowInteropHelper(
                this)
                .Handle;

        if (windowHandle !=
            IntPtr.Zero)
        {
            SetForegroundWindow(
                windowHandle);
        }
    }

    private void ShowTrayContextMenu()
    {
        var windowHandle =
            new WindowInteropHelper(
                this)
                .Handle;

        if (windowHandle ==
            IntPtr.Zero)
        {
            return;
        }

        var menuHandle =
            CreatePopupMenu();

        if (menuHandle ==
            IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(
                menuHandle,
                MenuString,
                TrayMenuOpenCommand,
                "Telenec Mail öffnen");

            AppendMenu(
                menuHandle,
                MenuSeparator,
                0,
                null);

            AppendMenu(
                menuHandle,
                MenuString,
                TrayMenuExitCommand,
                "Beenden");

            if (!GetCursorPosition(
                    out var cursorPosition))
            {
                return;
            }

            SetForegroundWindow(
                windowHandle);

            var selectedCommand =
                TrackPopupMenuEx(
                    menuHandle,
                    TrackPopupReturnCommand |
                    TrackPopupRightButton,
                    cursorPosition.X,
                    cursorPosition.Y,
                    windowHandle,
                    IntPtr.Zero);

            switch (selectedCommand)
            {
                case TrayMenuOpenCommand:
                    RestoreMainWindowFromTray();
                    break;

                case TrayMenuExitCommand:
                    ExitApplicationFromTray();
                    break;
            }

            /*
             * Laut Win32-Verhalten verhindert diese Nachricht,
             * dass das Kontextmenü anschließend in einem
             * ungewöhnlichen Fokuszustand hängen bleibt.
             */
            PostMessage(
                windowHandle,
                WmNull,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(
                menuHandle);
        }
    }

    private void ExitApplicationFromTray()
    {
        /*
         * Kein RestoreMainWindowFromTray() mehr.
         *
         * Ein verstecktes WPF-Fenster kann problemlos
         * geschlossen werden. Dadurch vermeiden wir sowohl
         * unnötiges Aufblitzen als auch jeden weiteren
         * Visibility-Wechsel während des Beendens.
         */
        _trayExitRequested =
            true;

        Close();
    }

    private IntPtr MainWindowTray_WindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        /*
         * Nach einem Neustart von explorer.exe gehen
         * klassische Tray-Icons verloren.
         *
         * Windows sendet anschließend TaskbarCreated.
         */
        if (_taskbarCreatedMessage != 0 &&
            message ==
                _taskbarCreatedMessage)
        {
            if (_closeToTrayEnabled &&
                _trayIconHandle !=
                    IntPtr.Zero)
            {
                _trayIconVisible =
                    false;

                EnsureTrayIcon();
            }

            return IntPtr.Zero;
        }

        if (message !=
            TrayCallbackMessage)
        {
            return IntPtr.Zero;
        }

        var trayEvent =
            unchecked(
                (int)lParam.ToInt64());

        switch (trayEvent)
        {
            case WmLeftButtonUp:
                RestoreMainWindowFromTray();

                handled =
                    true;

                break;

            case WmRightButtonUp:
                ShowTrayContextMenu();

                handled =
                    true;

                break;
        }

        return IntPtr.Zero;
    }

    private void MainWindowTray_OnClosed(
        object? sender,
        EventArgs e)
    {
        Closing -=
            MainWindowTray_OnClosing;

        Closed -=
            MainWindowTray_OnClosed;

        RemoveTrayIcon();

        if (_trayHwndSource is not null)
        {
            _trayHwndSource.RemoveHook(
                MainWindowTray_WindowProc);

            _trayHwndSource =
                null;
        }

        _traySettingsStore =
            null;

        _trayInfrastructureInitialized =
            false;

        _closeToTrayEnabled =
            false;
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet =
            CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;

        public IntPtr WindowHandle;

        public uint Id;

        public uint Flags;

        public uint CallbackMessage;

        public IntPtr IconHandle;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst =
                128)]
        public string Tip;

        public uint State;

        public uint StateMask;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst =
                256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst =
                64)]
        public string InfoTitle;

        public uint InfoFlags;

        public Guid GuidItem;

        public IntPtr BalloonIconHandle;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }

    [DllImport(
        "shell32.dll",
        CharSet =
            CharSet.Unicode,
        EntryPoint =
            "Shell_NotifyIconW")]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(
        uint message,
        ref NotifyIconData data);

    [DllImport(
        "shell32.dll",
        CharSet =
            CharSet.Unicode,
        EntryPoint =
            "ExtractIconExW")]
    private static extern uint ExtractIconEx(
        string fileName,
        int iconIndex,
        IntPtr[] largeIcons,
        IntPtr[] smallIcons,
        uint iconCount);

    [DllImport(
        "user32.dll")]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool DestroyIcon(
        IntPtr iconHandle);

    [DllImport(
        "user32.dll",
        CharSet =
            CharSet.Unicode,
        EntryPoint =
            "RegisterWindowMessageW")]
    private static extern int RegisterWindowMessage(
        string message);

    [DllImport(
        "user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport(
        "user32.dll",
        CharSet =
            CharSet.Unicode,
        EntryPoint =
            "AppendMenuW")]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool AppendMenu(
        IntPtr menuHandle,
        uint flags,
        uint menuItemId,
        string? text);

    [DllImport(
        "user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr menuHandle,
        uint flags,
        int x,
        int y,
        IntPtr windowHandle,
        IntPtr reserved);

    [DllImport(
        "user32.dll")]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool DestroyMenu(
        IntPtr menuHandle);

    [DllImport(
        "user32.dll",
        EntryPoint =
            "GetCursorPos")]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool GetCursorPosition(
        out NativePoint point);

    [DllImport(
        "user32.dll")]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(
        IntPtr windowHandle);

    [DllImport(
        "user32.dll",
        EntryPoint =
            "PostMessageW")]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);
}