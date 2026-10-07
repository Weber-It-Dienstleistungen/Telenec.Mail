namespace Telenec.Mail.App.Services.Account;

public sealed record AccountPasswordChangeResult(
    AccountPasswordChangeStatus Status)
{
    public bool IsSuccess =>
        Status == AccountPasswordChangeStatus.Success;

    public static AccountPasswordChangeResult Success()
        => new(AccountPasswordChangeStatus.Success);

    public static AccountPasswordChangeResult InvalidCredentials()
        => new(AccountPasswordChangeStatus.InvalidCredentials);

    public static AccountPasswordChangeResult PasswordRejected()
        => new(AccountPasswordChangeStatus.PasswordRejected);

    public static AccountPasswordChangeResult RateLimited()
        => new(AccountPasswordChangeStatus.RateLimited);

    public static AccountPasswordChangeResult ServiceUnavailable()
        => new(AccountPasswordChangeStatus.ServiceUnavailable);

    public static AccountPasswordChangeResult Failed()
        => new(AccountPasswordChangeStatus.Failed);
}