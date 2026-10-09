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
             * Ab 0.1.0-test.9 werden Updates nicht mehr direkt
             * über GitHub bezogen.
             *
             * Die Testversion 0.1.0-test.9 wird noch einmal über
             * den bisherigen GitHub-Testkanal verteilt.
             *
             * Nach ihrer Installation sucht Telenec Mail jedoch
             * ausschließlich im neuen produktiven Updatepfad
             * nach dem Velopack-Kanal "win".
             *
             * Dadurch können bestehende Testinstallationen ohne
             * Neuinstallation auf die Produktivversion 1.0.0
             * migriert werden.
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
             * dass der Benutzer seine E-Mails erreicht.
             *
             * Das ist insbesondere für die Brückenversion
             * wichtig: Solange noch keine releases.win.json
             * auf dem neuen Updatehost liegt, darf Telenec Mail
             * trotzdem ganz normal starten.
             */
            Trace.WriteLine(
                $"Telenec Mail update failed: {exception}");

            statusChanged?.Invoke(null);

            return false;
        }
    }
}