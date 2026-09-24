namespace Telenec.Mail.App.Services.Storage;

public interface ISettingsStore
{
    Task<string?> GetApplicationSettingAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task SetApplicationSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    Task<string?> GetAccountSettingAsync(
        Guid accountId,
        string key,
        CancellationToken cancellationToken = default);

    Task SetAccountSettingAsync(
        Guid accountId,
        string key,
        string value,
        CancellationToken cancellationToken = default);
}