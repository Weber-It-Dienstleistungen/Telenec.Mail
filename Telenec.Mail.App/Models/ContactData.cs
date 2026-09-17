namespace Telenec.Mail.App.Models;

public sealed class ContactData
{
    public required string ResourcePath { get; init; }

    public string? ETag { get; init; }

    public string? Uid { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public required string DisplayName { get; init; }

    public string? EmailAddress { get; init; }

    public string? PhoneNumber { get; init; }
}

public sealed class ContactCreateRequest
{
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? DisplayName { get; init; }

    public string? EmailAddress { get; init; }

    public string? PhoneNumber { get; init; }
}

public sealed class ContactUpdateRequest
{
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? DisplayName { get; init; }

    public string? EmailAddress { get; init; }

    public string? PhoneNumber { get; init; }
}