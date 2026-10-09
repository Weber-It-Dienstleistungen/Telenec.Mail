using System.Diagnostics;
using Velopack;

namespace Telenec.Mail.App.Services.Updates;

public sealed class VelopackApplicationUpdateService
    : IApplicationUpdateService
{
    private const string UpdateBaseUrl =
        "https://dav.necnet.de/updates/telenec-mail";

    private const string ProductionChannel =
        "win";

    private static readonly TimeSpan UpdateCheckTimeout =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan UpdateDownloadTimeout =
        TimeSpan.FromMinutes(10);

    public async Task<bool> TryApplyAvailableUpdateAsync(
        Action<string?>? statusChanged = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            /*
             * Produktivversionen von Telenec Mail beziehen
             * Programmupdates ausschließlich über die
             * Telenec-Updateinfrastruktur.
             *
             * Der explizite Kanal "win" stellt sicher,
             * dass nur für den Produktivbetrieb veröffentlichte
             * Releases angeboten werden.
             */
            var options =
                new UpdateOptions
                {
                    ExplicitChannel =
                        ProductionChannel
                };

            var updateManager =
                new UpdateManager(
                    UpdateBaseUrl,
                    options);

            /*
             * Ein normaler Debug-/Publish-Start außerhalb einer
             * Velopack-Installation darf niemals versuchen,
             * Updates einzuspielen.
             */
            if (!updateManager.IsInstalled)
            {
                return false;
            }

            statusChanged?.Invoke(
                "Suche nach Updates …");

            var updateInfo =
                await updateManager
                    .CheckForUpdatesAsync()
                    .WaitAsync(
                        UpdateCheckTimeout,
                        cancellationToken);

            if (updateInfo is null)
            {
                statusChanged?.Invoke(null);

                return false;
            }

            statusChanged?.Invoke(
                $"Update {updateInfo.TargetFullRelease.Version} " +
                "wird heruntergeladen …");

            using var downloadCancellation =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            downloadCancellation.CancelAfter(
                UpdateDownloadTimeout);

            await updateManager
                .DownloadUpdatesAsync(
                    updateInfo,
                    progress =>
                    {
                        statusChanged?.Invoke(
                            $"Update wird heruntergeladen … {progress}%");
                    },
                    downloadCancellation.Token);

            statusChanged?.Invoke(
                "Update wird installiert …");

            /*
             * Velopack beendet die aktuelle Anwendung,
             * installiert die heruntergeladene Version und
             * startet Telenec Mail anschließend erneut.
             */
            updateManager.ApplyUpdatesAndRestart(
                updateInfo);

            /*
             * Normalerweise wird diese Stelle nicht mehr erreicht,
             * weil Velopack den Prozess beendet.
             *
             * Der Rückgabewert bleibt als defensive Absicherung
             * erhalten.
             */
            return true;
        }
        catch (OperationCanceledException exception)
        {
            Trace.WriteLine(
                $"Telenec Mail update cancelled: {exception}");

            statusChanged?.Invoke(null);

            return false;
        }
        catch (TimeoutException exception)
        {
            Trace.WriteLine(
                $"Telenec Mail update check timed out: {exception}");

            statusChanged?.Invoke(null);

            return false;
        }
        catch (Exception exception)
        {
            /*
             * Ein Updateproblem darf niemals verhindern,
             * dass der Benutzer Telenec Mail normal verwenden kann.
             */
            Trace.WriteLine(
                $"Telenec Mail update failed: {exception}");

            statusChanged?.Invoke(null);

            return false;
        }
    }
}