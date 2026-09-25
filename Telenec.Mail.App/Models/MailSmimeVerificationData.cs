namespace Telenec.Mail.App.Models;

public enum MailSmimeVerificationStatus
{
    NotChecked,
    Valid,
    InvalidSignature,
    CertificateNotYetValid,
    CertificateExpired,
    CertificateValidationFailed,
    Error
}

public sealed record MailSmimeSignerData(
    string Name,
    string EmailAddress,
    string Fingerprint,
    DateTime? CertificateValidFrom,
    DateTime? CertificateValidUntil,
    bool SignatureIntegrityValid,
    bool CertificateValidationSuccessful);

public sealed record MailSmimeVerificationData(
    MailSmimeVerificationStatus Status,
    IReadOnlyList<MailSmimeSignerData> Signers)
{
    public static MailSmimeVerificationData NotChecked
    { get; } =
        new(
            Status:
                MailSmimeVerificationStatus.NotChecked,

            Signers:
                Array.Empty<MailSmimeSignerData>());

    public bool WasChecked =>
        Status !=
        MailSmimeVerificationStatus.NotChecked;

    public bool IsValid =>
        Status ==
        MailSmimeVerificationStatus.Valid;
}