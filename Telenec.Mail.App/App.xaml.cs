using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Startup;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class App : Application
{
    private static readonly TimeSpan MinimumSplashDuration =
        TimeSpan.FromMilliseconds(1200);

    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
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

                services.AddSingleton<AppDataPaths>();
                services.AddSingleton<DatabaseInitializer>();

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
            })
            .Build();
    }

    protected override async void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode =
            ShutdownMode.OnExplicitShutdown;

        var splashWindow =
            _host.Services
                .GetRequiredService<SplashWindow>();

        splashWindow.Show();

        var minimumSplashDelay =
            Task.Delay(
                MinimumSplashDuration);

        await _host.StartAsync();

        var startupService =
            _host.Services
                .GetRequiredService<ApplicationStartupService>();

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
        _host.StopAsync()
            .GetAwaiter()
            .GetResult();

        _host.Dispose();

        base.OnExit(e);
    }
}