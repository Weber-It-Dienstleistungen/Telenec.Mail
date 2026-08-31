namespace Telenec.Mail.App.Models;

public sealed record MailSendAttachmentData(
    string FilePath,
    string FileName,
    long SizeBytes)
{
    public string DisplaySize =>
        FormatFileSize(
            SizeBytes);

    private static string FormatFileSize(
        long sizeInBytes)
    {
        const double kilobyte =
            1024d;

        const double megabyte =
            1024d * 1024d;

        const double gigabyte =
            1024d * 1024d * 1024d;

        if (sizeInBytes >=
            gigabyte)
        {
            return
                $"{sizeInBytes / gigabyte:0.##} GB";
        }

        if (sizeInBytes >=
            megabyte)
        {
            return
                $"{sizeInBytes / megabyte:0.##} MB";
        }

        if (sizeInBytes >=
            kilobyte)
        {
            return
                $"{sizeInBytes / kilobyte:0.##} KB";
        }

        return
            $"{sizeInBytes} Bytes";
    }
}