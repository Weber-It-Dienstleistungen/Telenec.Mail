using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Startup;

public sealed class StartupResult
{
    private StartupResult(
        StartupState state,
        MailAccount? account = null,
        string? errorMessage = null)
    {
        State = state;
        Account = account;
        ErrorMessage = errorMessage;
    }

    public StartupState State { get; }

    public MailAccount? Account { get; }

    public string? ErrorMessage { get; }

    public static StartupResult NoAccount()
    {
        return new StartupResult(
            StartupState.NoAccount);
    }

    public static StartupResult AccountReady(
        MailAccount account)
    {
        return new StartupResult(
            StartupState.AccountReady,
            account);
    }

    public static StartupResult AuthenticationRequired(
        MailAccount account)
    {
        return new StartupResult(
            StartupState.AuthenticationRequired,
            account);
    }

    public static StartupResult AccountConfigurationInvalid(
        MailAccount? account = null)
    {
        return new StartupResult(
            StartupState.AccountConfigurationInvalid,
            account);
    }

    public static StartupResult StartupFailure(
        string errorMessage)
    {
        return new StartupResult(
            StartupState.StartupFailure,
            errorMessage: errorMessage);
    }
}