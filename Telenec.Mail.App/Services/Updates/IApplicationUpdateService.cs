namespace Telenec.Mail.App.Services.Updates;

public interface IApplicationUpdateService
{
    Task<bool> TryApplyAvailableUpdateAsync(
        Action<string?>? statusChanged = null,
        CancellationToken cancellationToken = default);
}