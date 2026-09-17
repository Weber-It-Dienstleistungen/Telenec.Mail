namespace Telenec.Mail.App.Services.Contacts;

public interface IContactProvisioningService
{
    Task<bool> EnsureDefaultAddressBookAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);
}