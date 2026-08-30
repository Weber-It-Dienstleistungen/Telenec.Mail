namespace Telenec.Mail.App.Services.Mail;

public sealed class MailAuthenticationResult
{
    private MailAuthenticationResult(
        MailAuthenticationStatus status,
        string? capabilities = null)
    {
        Status = status;
        Capabilities = capabilities;
    }

    public MailAuthenticationStatus Status { get; }

    public string? Capabilities { get; }

    public static MailAuthenticationResult Success(
        string capabilities)
    {
        return new MailAuthenticationResult(
            MailAuthenticationStatus.Success,
            capabilities);
    }

    public static MailAuthenticationResult FromStatus(
        MailAuthenticationStatus status)
    {
        return new MailAuthenticationResult(
            status);
    }
}