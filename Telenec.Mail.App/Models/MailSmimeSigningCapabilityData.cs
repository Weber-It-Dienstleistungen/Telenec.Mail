namespace Telenec.Mail.App.Models;

public enum MailSmimeSigningCapabilityStatus
{
    Available,
    NoUsableCertificate,
    Error
}

public sealed record MailSmimeSigningCapabilityData(
    MailSmimeSigningCapabilityStatus Status,
    string SignerAddress)
{
    public bool CanSign =>
        Status ==
        MailSmimeSigningCapabilityStatus.Available;

    public bool HasUsableCertificate =>
        CanSign;

    public bool HasError =>
        Status ==
        MailSmimeSigningCapabilityStatus.Error;
}