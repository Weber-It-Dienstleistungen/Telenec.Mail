namespace Telenec.Mail.App.Services.Account;

public interface IAccountPasswordChangeService
{
    Task<AccountPasswordChangeResult> ChangePasswordAsync(
        string userName,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}