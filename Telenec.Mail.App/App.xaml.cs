using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Startup;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.Services.Updates;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class App : Application
{
    private static readonly TimeSpan MinimumSplashDuration =
        TimeSpan.FromMilliseconds(1200);

    private const long MaximumLogFileSizeBytes =
        5L * 1024L * 1024L;

    private const int RetainedLogFileCount =
        14;

    private readonly IHost _host;

    private ILogger<App>? _logger;

    public App()
    {
        _host =
            Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    /*
                     * AppDataPaths wird bewusst vor dem Logging
                     * registriert.
                     *
                     * Dadurch verwendet auch das Logging exakt
                     * denselben lokalen Anwendungspfad wie die
                     * restlichen Programmdaten.
                     */
                    services.AddSingleton<AppDataPaths>();

                    services.AddSerilog(
                        (
                            serviceProvider,
                            loggerConfiguration) =>
                        {
                            ConfigureLogging(
                                serviceProvider,
                                loggerConfiguration);
                        });

                    services.AddSingleton<
                        IMailDataSource,
                        ImapMailDataSource>();

                    services.AddSingleton<
                        IMailMessageStateSource,
                        ImapMailMessageStateSource>();

                    services.AddSingleton<
                        IMailPermanentDeleteService,
                        MailKitPermanentDeleteService>();

                    services.AddSingleton<
                        IMailSendService,
                        MailKitSendService>();

                    services.AddSingleton<
                        IMailDraftEditService,
                        MailKitDraftEditService>();

                    services.AddSingleton<
                        IMailDraftCleanupService,
                        MailKitDraftCleanupService>();

                    services.AddSingleton<
                        IMailAuthenticationService,
                        MailKitAuthenticationService>();

                    services.AddSingleton<
                        IApplicationUpdateService,
                        VelopackApplicationUpdateService>();

                    services.AddSingleton<
                        ReleaseNotesService>();

                    services.AddSingleton<
                        DatabaseInitializer>();

                    services.AddSingleton<
                        IMailAccountStore,
                        SqliteMailAccountStore>();

                    services.AddSingleton<
                        ICredentialStore,
                        WindowsCredentialStore>();

                    services.AddSingleton<
                        ApplicationStartupService>();

                    services.AddTransient<
                        MainViewModel>();

                    services.AddTransient<
                        LoginViewModel>();

                    services.AddTransient<
                        ComposeMailViewModel>();

                    services.AddSingleton<
                        SplashWindow>();

                    services.AddTransient<
                        LoginWindow>();

                    services.AddTransient<
                        MainWindow>();

                    services.AddTransient<
                        ComposeWindow>();

                    services.AddTransient<
                        WhatsNewWindow>();
                })
                .Build();
    }

    protected override async void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode =
            ShutdownMode.OnExplicitShutdown;

        InitializeApplicationLogger();

        var splashWindow =
            _host.Services
                .GetRequiredService<SplashWindow>();

        splashWindow.Show();

        var minimumSplashDelay =
            Task.Delay(
                MinimumSplashDuration);

        var updateService =
            _host.Services
                .GetRequiredService<
                    IApplicationUpdateService>();

        var updateRestartInitiated =
            await updateService
                .TryApplyAvailableUpdateAsync(
                    splashWindow.SetStatus);

        /*
         * ApplyUpdatesAndRestart beendet den Prozess normalerweise
         * bereits selbst. Dieser Rücksprung verhindert defensiv,
         * dass parallel noch der normale Mail-Startup beginnt.
         */
        if (updateRestartInitiated)
        {
            return;
        }

        await _host.StartAsync();

        var startupService =
            _host.Services
                .GetRequiredService<
                    ApplicationStartupService>();

        var startupResult =
            await startupService
                .DetermineStartupStateAsync();

        await minimumSplashDelay;

        switch (startupResult.State)
        {
            case StartupState.NoAccount:
            case StartupState.AuthenticationRequired:
            case StartupState.AccountConfigurationInvalid:
                await ShowLoginWindowAsync(
                    splashWindow,
                    startupResult);
                break;

            case StartupState.AccountReady:
                await ShowMainWindowAsync(
                    splashWindow);
                break;

            case StartupState.StartupFailure:
                await HandleStartupFailureAsync(
                    splashWindow);
                break;

            default:
                await HandleStartupFailureAsync(
                    splashWindow);
                break;
        }
    }

    private static void ConfigureLogging(
        IServiceProvider serviceProvider,
        LoggerConfiguration loggerConfiguration)
    {
        /*
         * Standardmäßig protokollieren wir unsere eigenen
         * technischen Ereignisse ab Information.
         *
         * Framework-interne Meldungen von Microsoft/System
         * werden auf Warning begrenzt, damit die Feldtestlogs
         * nicht mit wenig hilfreichem Frameworkrauschen
         * überfüllt werden.
         */
        loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override(
                "Microsoft",
                LogEventLevel.Warning)
            .MinimumLevel.Override(
                "System",
                LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty(
                "Application",
                "Telenec Mail");

        try
        {
            var appDataPaths =
                serviceProvider
                    .GetRequiredService<
                        AppDataPaths>();

            Directory.CreateDirectory(
                appDataPaths.LogDirectory);

            loggerConfiguration
                .WriteTo.File(
                    path:
                        appDataPaths
                            .LogFilePathPattern,

                    rollingInterval:
                        RollingInterval.Day,

                    retainedFileCountLimit:
                        RetainedLogFileCount,

                    fileSizeLimitBytes:
                        MaximumLogFileSizeBytes,

                    rollOnFileSizeLimit:
                        true,

                    buffered:
                        false,

                    shared:
                        false,

                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                        "[{Level:u3}] " +
                        "{SourceContext} - " +
                        "{Message:lj}" +
                        "{NewLine}{Exception}");
        }
        catch (Exception exception)
        {
            /*
             * Logging ist für Diagnose und Support wichtig,
             * darf aber niemals verhindern, dass der Benutzer
             * seine E-Mails erreicht.
             *
             * Falls beispielsweise das lokale Profil oder der
             * Logordner nicht beschreibbar ist, läuft Telenec
             * Mail deshalb trotzdem weiter.
             */
            Trace.WriteLine(
                $"Could not initialize persistent logging: {exception}");
        }
    }

    private void InitializeApplicationLogger()
    {
        try
        {
            _logger =
                _host.Services
                    .GetRequiredService<
                        ILogger<App>>();

            _logger.LogInformation(
                "Application started. Version {ApplicationVersion}.",
                GetApplicationVersion());
        }
        catch (Exception exception)
        {
            /*
             * Auch das Erstellen bzw. Abrufen des Loggers
             * darf den normalen Programmstart nicht verhindern.
             */
            Trace.WriteLine(
                $"Could not initialize application logger: {exception}");
        }
    }

    private async Task ShowLoginWindowAsync(
        SplashWindow splashWindow,
        StartupResult startupResult)
    {
        var loginWindow =
            _host.Services
                .GetRequiredService<LoginWindow>();

        if (startupResult.State ==
            StartupState.AuthenticationRequired)
        {
            loginWindow.PrepareKnownAccount(
                startupResult.Account?.EmailAddress);
        }
        else
        {
            loginWindow.PrepareKnownAccount(
                null);
        }

        MainWindow =
            loginWindow;

        loginWindow.Show();

        await splashWindow.FadeOutAsync();

        splashWindow.Close();

        ShutdownMode =
            ShutdownMode.OnMainWindowClose;
    }

    private async Task ShowMainWindowAsync(
        SplashWindow splashWindow)
    {
        var mainWindow =
            _host.Services
                .GetRequiredService<MainWindow>();

        MainWindow =
            mainWindow;

        mainWindow.Show();

        await splashWindow.FadeOutAsync();

        splashWindow.Close();

        ShutdownMode =
            ShutdownMode.OnMainWindowClose;

        ShowPendingReleaseNotes(
            mainWindow);
    }

    private void ShowPendingReleaseNotes(
        Window owner)
    {
        var releaseNotesService =
            _host.Services
                .GetRequiredService<
                    ReleaseNotesService>();

        var releaseNotes =
            releaseNotesService
                .TryGetPendingReleaseNotes();

        if (releaseNotes is null)
        {
            return;
        }

        try
        {
            var whatsNewWindow =
                _host.Services
                    .GetRequiredService<
                        WhatsNewWindow>();

            whatsNewWindow.Owner =
                owner;

            whatsNewWindow
                .ShowReleaseNotes(
                    releaseNotes);

            whatsNewWindow.ShowDialog();

            /*
             * Erst nachdem das Fenster tatsächlich angezeigt
             * und geschlossen wurde, gilt diese Version als gesehen.
             */
            releaseNotesService
                .MarkAsShown();
        }
        catch (Exception exception)
        {
            /*
             * Ein Fehler in der Komfortfunktion
             * darf Telenec Mail nicht beeinträchtigen.
             *
             * Der Marker bleibt erhalten, damit beim nächsten
             * Programmstart erneut versucht werden kann,
             * die Hinweise anzuzeigen.
             */
            Trace.WriteLine(
                $"Could not show release notes: {exception}");
        }
    }

    private async Task HandleStartupFailureAsync(
        SplashWindow splashWindow)
    {
        await splashWindow.FadeOutAsync();

        splashWindow.Close();

        MessageBox.Show(
            "Telenec Mail konnte nicht gestartet werden.\n\n" +
            "Die lokalen Programmdaten konnten nicht geladen werden.",
            "Telenec Mail",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown();
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        try
        {
            _logger?.LogInformation(
                "Application stopping.");
        }
        catch (Exception exception)
        {
            Trace.WriteLine(
                $"Could not write application shutdown log: {exception}");
        }

        try
        {
            _host.StopAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            try
            {
                _logger?.LogError(
                    exception,
                    "An error occurred while stopping the application host.");
            }
            catch
            {
            }

            Trace.WriteLine(
                $"Could not stop application host cleanly: {exception}");
        }
        finally
        {
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static string GetApplicationVersion()
    {
        var assembly =
            typeof(App).Assembly;

        var informationalVersion =
            assembly
                .GetCustomAttribute<
                    AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(
                informationalVersion))
        {
            var metadataSeparatorIndex =
                informationalVersion.IndexOf(
                    '+');

            if (metadataSeparatorIndex >= 0)
            {
                informationalVersion =
                    informationalVersion[
                        ..metadataSeparatorIndex];
            }

            return informationalVersion;
        }

        return assembly
                   .GetName()
                   .Version?
                   .ToString()
               ?? "unbekannt";
    }
}