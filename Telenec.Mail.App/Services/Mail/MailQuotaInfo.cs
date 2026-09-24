namespace Telenec.Mail.App.Services.Mail;

public sealed record MailQuotaInfo(
    long? UsedBytes,
    long? LimitBytes)
{
    public bool IsUnlimited =>
        !LimitBytes.HasValue ||
        LimitBytes.Value <= 0;

    public double? UsagePercentage
    {
        get
        {
            if (IsUnlimited ||
                !UsedBytes.HasValue)
            {
                return null;
            }

            return
                UsedBytes.Value /
                (double)LimitBytes!.Value *
                100.0;
        }
    }
}