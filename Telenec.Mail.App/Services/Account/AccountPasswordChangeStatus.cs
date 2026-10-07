namespace Telenec.Mail.App.Services.Account;

public enum AccountPasswordChangeStatus
{
    Success,
    InvalidCredentials,
    PasswordRejected,
    RateLimited,
    ServiceUnavailable,
    Failed
}