using System.Threading;

namespace Telenec.Mail.App.Services.Mail;

public static class MailboxConnectivityState
{
    private static int _offlineCacheActive;

    public static bool IsOfflineCacheActive =>
        Volatile.Read(
            ref _offlineCacheActive) != 0;

    public static void MarkOnline()
    {
        Volatile.Write(
            ref _offlineCacheActive,
            0);
    }

    public static void MarkOfflineCacheActive()
    {
        Volatile.Write(
            ref _offlineCacheActive,
            1);
    }
}