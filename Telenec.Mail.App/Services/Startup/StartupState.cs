namespace Telenec.Mail.App.Services.Startup;

public enum StartupState
{
    NoAccount,
    AccountReady,
    AuthenticationRequired,
    AccountConfigurationInvalid,
    StartupFailure
}