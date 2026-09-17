namespace Telenec.Mail.App.Services.Storage;

public interface IExternalImagePermissionStore
{
    Task<bool> IsAllowedAsync(
        Guid accountId,
        string messageKey,
        CancellationToken cancellationToken = default);

    Task AllowAsync(
        Guid accountId,
        string messageKey,
        CancellationToken cancellationToken = default);
}