using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Telenec.Mail.App.Services.Updates;
using Velopack;

namespace Telenec.Mail.App;

internal static class Program
{
    private const byte ActivateRequest = 1;

    private static readonly TimeSpan ActivationTimeout =
        TimeSpan.FromSeconds(5);

    [STAThread]
    private static void Main(
        string[] args)
    {
        // Velopack-Lifecycle-Callbacks müssen vor der Instanzprüfung
        // ausgeführt werden, auch bei einem Update oder Neustart.
        VelopackApp
            .Build()
            .OnAfterUpdateFastCallback(
                version =>
                    ReleaseNotesUpdateMarker
                        .MarkPending(
                            version.ToString()))
            .Run();

        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();

        var userSid = identity.User?.Value
            ?? throw new InvalidOperationException(
                "Die Windows-Benutzerkennung konnte nicht ermittelt werden.");

        // Ein sichtbares Fenster kann nur in derselben interaktiven
        // Windows-Sitzung wiederhergestellt werden.
        var instanceName =
            $"Telenec.Mail.{userSid}.{process.SessionId}";

        using var mutex = new Mutex(
            initiallyOwned: false,
            name: $@"Local\{instanceName}");

        var deadline = Stopwatch.StartNew();

        while (!TryAcquireInstance(mutex))
        {
            if (TryRequestActivation(instanceName))
            {
                return;
            }

            // Falls die erste Instanz gerade startet oder beendet wird,
            // warten wir begrenzt auf den Aktivierungskanal bzw. die Sperre.
            if (deadline.Elapsed >= ActivationTimeout)
            {
                MessageBox.Show(
                    "Telenec Mail läuft bereits, konnte aber nicht wieder geöffnet werden.\n\n" +
                    "Bitte öffnen Sie das vorhandene Programm über die Taskleiste " +
                    "oder den Infobereich.",
                    "Telenec Mail",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            Thread.Sleep(100);
        }

        // Der Mutex bleibt bis zum Ende des STA-Hauptthreads im Besitz
        // dieser Instanz. Erst jetzt werden App, DI und Dienste aufgebaut.
        try
        {
            var application = new App();
            application.InitializeComponent();

            using var cancellation = new CancellationTokenSource();

            var listener = Task.Run(
                () => ListenForActivationAsync(
                    instanceName,
                    application.Dispatcher,
                    () => ActivateExistingWindow(application),
                    cancellation.Token));

            try
            {
                application.Run();
            }
            finally
            {
                cancellation.Cancel();
                listener.GetAwaiter().GetResult();
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool TryAcquireInstance(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Nach einem Absturz wird der Mutex vom nächsten Start
            // übernommen; es bleibt keine dauerhafte Sperre zurück.
            return true;
        }
    }

    private static bool TryRequestActivation(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.Out,
                PipeOptions.CurrentUserOnly);

            client.Connect(500);

            // Windows erlaubt nicht jedem Hintergrundprozess, den
            // Vordergrund zu übernehmen. Der neu gestartete Prozess
            // überträgt seine Berechtigung gezielt an die erste Instanz.
            if (GetNamedPipeServerProcessId(
                    client.SafePipeHandle,
                    out var serverProcessId))
            {
                AllowSetForegroundWindow(serverProcessId);
            }

            client.WriteByte(ActivateRequest);
            client.Flush();

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task ListenForActivationAsync(
        string pipeName,
        Dispatcher dispatcher,
        Action activate,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(cancellationToken)
                        .ConfigureAwait(false);

                    // Ein verbundener Client darf den Kanal nicht
                    // unbegrenzt blockieren, wenn er nichts sendet.
                    using var requestTimeout =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));

                    var buffer = new byte[1];
                    var received = await server.ReadAsync(
                            buffer.AsMemory(),
                            requestTimeout.Token)
                        .ConfigureAwait(false);

                    if (received == 1 &&
                        buffer[0] == ActivateRequest &&
                        !dispatcher.HasShutdownStarted)
                    {
                        dispatcher.BeginInvoke(
                            DispatcherPriority.Normal,
                            activate);
                    }
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    // Unvollständige Anfrage: Kanal erneut bereitstellen.
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    Trace.WriteLine(
                        $"Telenec Mail activation channel: {exception.GetType().Name}");

                    await Task.Delay(100, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normales Beenden des Aktivierungskanals.
        }
    }

    private static void ActivateExistingWindow(App application)
    {
        if (application.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        // Während des Starts kann dies der Splashscreen oder die
        // Anmeldung sein. Später ist es das aktuelle Hauptfenster.
        var window = application.MainWindow;

        if (window is null)
        {
            // Die erste Instanz befindet sich noch vor dem Fensteraufbau
            // und zeigt ihr Startfenster ohnehin selbst an.
            return;
        }

        try
        {
            if (window is MainWindow)
            {
                window.ShowInTaskbar = true;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            var handle = new WindowInteropHelper(window).Handle;

            if (handle != IntPtr.Zero && !IsWindowEnabled(handle))
            {
                // Ein modaler Dialog hat Vorrang vor seinem Besitzer.
                var popup = GetLastActivePopup(handle);

                if (popup != IntPtr.Zero &&
                    popup != handle &&
                    IsWindowVisible(popup))
                {
                    SetForegroundWindow(popup);
                    return;
                }
            }

            window.Activate();

            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
            }
        }
        catch (InvalidOperationException exception)
        {
            // Das Fenster kann während eines Anmeldewechsels oder
            // beim Beenden bereits geschlossen worden sein.
            Trace.WriteLine(
                $"Telenec Mail window activation: {exception.GetType().Name}");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint serverProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr window);
}
