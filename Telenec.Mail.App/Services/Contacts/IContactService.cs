using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Contacts;

public interface IContactService
{
    Task<IReadOnlyList<ContactData>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<ContactData> CreateAsync(
        ContactCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<ContactData> UpdateAsync(
        ContactData contact,
        ContactUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        ContactData contact,
        CancellationToken cancellationToken = default);
}