namespace Telenec.Mail.App.Models;

public enum MailSmimeSignatureFormat
{
    None,
    Detached,
    Opaque
}

public enum MailSmimeEncryptionFormat
{
    None,
    EnvelopedData
}

public sealed record MailSecurityData(
    bool HasSmimeSignature,
    bool HasSmimeEncryption,
    bool HasUnclassifiedSmimeContent,
    MailSmimeSignatureFormat SmimeSignatureFormat,
    MailSmimeEncryptionFormat SmimeEncryptionFormat)
{
    public static MailSecurityData None
    { get; } =
        new(
            HasSmimeSignature:
                false,

            HasSmimeEncryption:
                false,

            HasUnclassifiedSmimeContent:
                false,

            SmimeSignatureFormat:
                MailSmimeSignatureFormat.None,

            SmimeEncryptionFormat:
                MailSmimeEncryptionFormat.None);

    public bool HasSmimeProtection =>
        HasSmimeSignature ||
        HasSmimeEncryption;

    public bool HasAnySmimeContent =>
        HasSmimeProtection ||
        HasUnclassifiedSmimeContent;

    public bool IsSignedAndEncrypted =>
        HasSmimeSignature &&
        HasSmimeEncryption;
}