namespace Telenec.Mail.App.Services.Security;

public sealed class StoredCredential
{
    public required string UserName { get; init; }

    public required string Password { get; init; }
}