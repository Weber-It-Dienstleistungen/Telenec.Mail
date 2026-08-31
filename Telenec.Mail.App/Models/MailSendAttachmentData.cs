namespace Telenec.Mail.App.Models;

public sealed record MailSendAttachmentData(
    string FilePath,
    string FileName,
    long SizeBytes,
    string? SourceFolderId = null,
    uint SourceUniqueId = 0,
    string? SourcePartSpecifier = null,
    string? SourceMessageId = null)
{
    public bool IsLocalFile =>
        !string.IsNullOrWhiteSpace(
            FilePath);

    public bool IsServerAttachment =>
        string.IsNullOrWhiteSpace(
            FilePath) &&
        !string.IsNullOrWhiteSpace(
            SourceFolderId) &&
        SourceUniqueId > 0 &&
        !string.IsNullOrWhiteSpace(
            SourcePartSpecifier);

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