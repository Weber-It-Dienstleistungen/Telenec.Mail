namespace Telenec.Mail.App.Services.Security;

public interface ICredentialStore
{
    Task<bool> ExistsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<StoredCredential?> ReadAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Guid accountId,
        string userName,
        string password,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}