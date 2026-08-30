namespace Telenec.Mail.App.Models;

public sealed class MailAccount
{
    public Guid AccountId { get; init; }

    public required string EmailAddress { get; init; }

    public string? DisplayName { get; init; }

    public bool IsActive { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}